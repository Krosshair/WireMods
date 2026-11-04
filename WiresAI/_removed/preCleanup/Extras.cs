using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresEnemyVariation
{
    // Packs, leaders, grudges, opportunism, regroup fleeing. All event driven.
    public sealed partial class EnemyVariationPlugin
    {
        private const string GroupKey = "wev_group_v1";
        private const string GrudgePidKey = "wev_grudge_pid_v1";
        private const string GrudgeKey = "wev_grudge_v1";
        private const string GrudgeTimeKey = "wev_grudge_t_v1";

        private static float Decayed(ZDO zdo)
        {
            if (ZNet.instance == null) return zdo.GetFloat(GrudgeKey);
            double days = (ZNet.instance.GetTimeSeconds() - zdo.GetLong(GrudgeTimeKey)) / 1800.0;
            return zdo.GetFloat(GrudgeKey) * (float)Math.Pow(.5, Math.Max(0.0, days) / 3.0);
        }

        // Set around vanilla spawn calls, read by Character.Awake inside Instantiate.
        private static long _pendingGroup;
        private static long _sysGroup;
        private static int _sysFrame = -1;
        private static object _sysData;
        private static int _regroupActive;
        private static Vector3 _pendingHome;
        private static bool _hasPendingHome;
        private const string HomeKey = "wev_home_v1";
        private const string ScarKey = "wev_scar_v1";

        private static long GroupHash(Vector3 p, long salt)
        {
            unchecked
            {
                long h = salt * (long)0x9E3779B97F4A7C15UL;
                h ^= Mathf.RoundToInt(p.x) * 73856093L ^ Mathf.RoundToInt(p.z) * 19349663L;
                return h == 0 ? 1 : h;
            }
        }

        // Spawn group id if it has one, otherwise its species family.
        private static long GroupOf(Character c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            ZDO zdo = v != null && v.IsValid() ? v.GetZDO() : null;
            long g = zdo != null ? zdo.GetLong(GroupKey) : 0L;
            return g != 0 ? g : -(long)(uint)SpeciesOf(c).GetHashCode() - 1;
        }

        private static void TagGroup(ZNetView v)
        {
            if (_pendingGroup == 0 && !_hasPendingHome || v == null || !v.IsOwner()) return;
            ZDO zdo = v.GetZDO();
            if (zdo == null) return;
            if (_pendingGroup != 0 && zdo.GetLong(GroupKey) == 0) zdo.Set(GroupKey, _pendingGroup);
            if (_hasPendingHome && zdo.GetVec3(HomeKey, Vector3.zero) == Vector3.zero) zdo.Set(HomeKey, _pendingHome);
        }

        // +1 right at the nest or camp, 0 around 35 m, -1 far away. 0 if it has no home.
        private static float HomeFactor(Behavior b)
        {
            ZDO zdo = b.View != null ? b.View.GetZDO() : null;
            if (zdo == null || b.Character == null) return 0f;
            Vector3 home = zdo.GetVec3(HomeKey, Vector3.zero);
            if (home == Vector3.zero) return 0f;
            float d = Vector3.Distance(home, b.Character.transform.position);
            return Mathf.Clamp((35f - d) / 25f, -1f, 1f);
        }

        private static bool Predator(Individual p) =>
            p != null && (NightHunter(p) || p.Species.IndexOf("Wolf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Species.IndexOf("Bjorn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Species.IndexOf("Asksvin", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool Caster(Character c)
        {
            string n = c.gameObject.name;
            return n.IndexOf("Shaman", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("JotunWitch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Positive: player looks dangerous (big weapon, tower shield). Negative: unarmed. Local player only.
        private static float Threat(Character target)
        {
            if (!(target is Player pl) || pl != Player.m_localPlayer) return 0f;
            ItemDrop.ItemData r = pl.RightItem, l = pl.LeftItem;
            if (r == null && l == null) return -.6f;
            float t = 0f;
            if (r != null && r.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                l != null && l.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft) t += .5f;
            if (l != null && l.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield &&
                l.m_shared.m_timedBlockBonus <= 1f) t += .4f; // tower shields can't parry
            return t;
        }

        // Smart packmates turn on whoever hits their caster.
        private static void ProtectCaster(Character caster, Character attacker)
        {
            long pack = GroupOf(caster);
            Vector3 pos = caster.transform.position;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == caster || c.IsDead() || c.IsTamed() ||
                    (c.transform.position - pos).sqrMagnitude > 400f ||
                    !(c.GetBaseAI() is MonsterAI ai) || GroupOf(c) != pack ||
                    !Phys.TryGetValue(c, out Physical ph) || ph.Individual == null || ph.Individual.Tactics < .1f) continue;
                ZNetView v = c.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner() || !BaseAI.IsEnemy(c, attacker)) continue;
                Target(ai) = attacker;
                ai.Alert();
            }
        }

        private static float LeaderScore(Individual p) =>
            p == null ? -9f : p.Courage + .5f * p.Aggression + .3f * p.Stars;

        private static bool NightHunter(Individual p) =>
            p != null && (p.Species.IndexOf("Elaking", StringComparison.OrdinalIgnoreCase) >= 0 &&
                p.Species.IndexOf("Mole", StringComparison.OrdinalIgnoreCase) < 0 ||
                p.Species.IndexOf("Fenring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Species.IndexOf("Ulv", StringComparison.OrdinalIgnoreCase) >= 0);

        // How exposed the target is right now. Stamina is only known for the local player.
        private static float Opportunity(Character target)
        {
            float opp = .6f * Mathf.Clamp01(1f - target.GetHealthPercentage());
            if (target.IsStaggering()) opp += .6f;
            if (target is Player pl && pl == Player.m_localPlayer &&
                pl.GetStamina() < .25f * Mathf.Max(1f, pl.GetMaxStamina())) opp += .5f;
            return Mathf.Clamp01(opp);
        }

        private static void ReadGrudge(Behavior b, Character target)
        {
            if (!(target is Player pl) || b.View == null) return;
            ZDO zdo = b.View.GetZDO();
            if (zdo == null || zdo.GetLong(GrudgePidKey) != pl.GetPlayerID()) return;
            float g = Decayed(zdo);
            if (g > 0f) b.Confidence = Mathf.Max(b.Confidence, g);
            else b.Fear = Mathf.Max(b.Fear, -g);
        }

        private static void AddGrudge(Behavior b, Character target, float delta)
        {
            if (target is Player pl) AddGrudge(b, pl.GetPlayerID(), delta);
        }

        private static void AddGrudge(Behavior b, long pid, float delta)
        {
            if (pid == 0 || b.View == null || !b.View.IsOwner()) return;
            ZDO zdo = b.View.GetZDO();
            if (zdo == null) return;
            float g = zdo.GetLong(GrudgePidKey) == pid ? Decayed(zdo) : 0f;
            zdo.Set(GrudgePidKey, pid);
            if (ZNet.instance != null) zdo.Set(GrudgeTimeKey, (long)ZNet.instance.GetTimeSeconds());
            zdo.Set(GrudgeKey, Mathf.Clamp(g + delta, -1f, 1f));
        }

        // Top ~15% of a real spawn pack are born leaders: slightly bigger, preferred as leader.
        private static bool BornLeader(Character c, Individual p)
        {
            if (p == null) return false;
            ZNetView v = c.GetComponent<ZNetView>();
            ZDO zdo = v != null && v.IsValid() ? v.GetZDO() : null;
            return zdo != null && zdo.GetLong(GroupKey) != 0 && LeaderScore(p) > .75f;
        }

        // A retreating packmate wakes up the others nearby.
        private static void AlertPack(Behavior b)
        {
            Character self = b.Character;
            if (self == null) return;
            long pack = GroupOf(self);
            Vector3 pos = self.transform.position;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == self || c.IsDead() || c.IsTamed() ||
                    (c.transform.position - pos).sqrMagnitude > ScanRadiusSq ||
                    !(c.GetBaseAI() is MonsterAI ai) || GroupOf(c) != pack) continue;
                ai.Alert(); // vanilla routes this to the owner
            }
        }

        private static void SetRegroup(Behavior b, Character ally)
        {
            if (b.RegroupAlly == null && ally != null) _regroupActive++;
            else if (b.RegroupAlly != null && ally == null) _regroupActive--;
            b.RegroupAlly = ally;
        }

        [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
        private static class SystemSpawnGroup
        {
            // One spawn pass of the same entry in the same frame is one pack.
            private static void Prefix(SpawnSystem.SpawnData critter, Vector3 spawnPoint)
            {
                if (Time.frameCount != _sysFrame || !ReferenceEquals(critter, _sysData))
                {
                    _sysFrame = Time.frameCount; _sysData = critter;
                    _sysGroup = GroupHash(spawnPoint, Time.frameCount);
                }
                _pendingGroup = _sysGroup;
            }
            private static void Postfix() => _pendingGroup = 0;
        }

        [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
        private static class AreaSpawnGroup
        {
            private static void Prefix(SpawnArea __instance)
            {
                _pendingGroup = GroupHash(__instance.transform.position, 7);
                _pendingHome = __instance.transform.position; _hasPendingHome = true;
            }
            private static void Postfix() { _pendingGroup = 0; _hasPendingHome = false; }
        }

        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        private static class CreatureSpawnGroup
        {
            // Camp spawners near each other share a 24 m cell.
            private static void Prefix(CreatureSpawner __instance)
            {
                _pendingGroup = GroupHash(__instance.transform.position / 24f, 13);
                _pendingHome = __instance.transform.position; _hasPendingHome = true;
            }
            private static void Postfix() { _pendingGroup = 0; _hasPendingHome = false; }
        }

        [HarmonyPatch(typeof(BaseAI), "Flee")]
        private static class RegroupFlee
        {
            // Only does anything while a regroup retreat is running.
            private static void Prefix(BaseAI __instance, ref Vector3 from)
            {
                if (_regroupActive <= 0 || !(__instance is MonsterAI ai) ||
                    !Brains.TryGetValue(ai, out Behavior b) || b.Lease == null) return;
                Character ally = b.RegroupAlly;
                if (ally == null || ally.IsDead()) return;
                Vector3 me = ai.transform.position;
                from = me + (me - ally.transform.position);
            }
        }
    }
}
