using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresEnemyVariation
{
    [BepInPlugin(Guid, "Wire's Enemy Variation", "0.1.0")]
    public sealed partial class EnemyVariationPlugin : BaseUnityPlugin
    {
        public const string Guid = "kajetan.valheim.enemyvariation";
        internal static EnemyVariationPlugin Instance;
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled, _debug, _individuals, _bosses, _groups;
        private static readonly ConditionalWeakTable<Character, Physical> Phys = new ConditionalWeakTable<Character, Physical>();
        private static readonly ConditionalWeakTable<MonsterAI, Behavior> Brains = new ConditionalWeakTable<MonsterAI, Behavior>();
        private static readonly ConditionalWeakTable<Character, Scene> Scenes = new ConditionalWeakTable<Character, Scene>();
        private static readonly AccessTools.FieldRef<MonsterAI, Character> Target =
            AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");

        private const string SeedKey = "wev_seed_v1";
        private const float ScanRadius = 28f;
        private const float ScanRadiusSq = ScanRadius * ScanRadius;

        private sealed class Physical
        {
            internal Individual Individual;
            internal bool Boss, Scaled;
            internal float OriginalWalk, OriginalRun, OriginalTurn;
            internal Vector3 OriginalScale;
            internal Physical(Character c, Individual i, bool boss)
            {
                Individual = i; Boss = boss;
                OriginalWalk = c.m_walkSpeed; OriginalRun = c.m_runSpeed;
                OriginalTurn = c.m_turnSpeed; OriginalScale = c.transform.localScale;
            }
        }

        private sealed class Behavior
        {
            internal readonly Character Character;
            internal readonly ZNetView View;
            internal readonly float CircleInterval, CircleDuration, AttackInterval;
            internal float Rage, Fear, Confidence, StateTime;
            internal Posture Action; internal float NextDecisionAt; internal uint Decisions;
            internal string DecisionReason; internal RetreatLease Lease;
            internal float FleeThreshold, FleeTime;
            internal int Allies = -1, Enemies = -1, Phase = -1;
            internal bool HasEncounter;
            internal float LastLogTime = -10000f;
            internal Behavior(MonsterAI ai)
            {
                Character = ai.GetComponent<Character>(); View = ai.GetComponent<ZNetView>();
                CircleInterval = ai.m_circleTargetInterval;
                CircleDuration = ai.m_circleTargetDuration;
                AttackInterval = ai.m_minAttackInterval;
                FleeThreshold = ai.m_fleeIfLowHealth; FleeTime = ai.m_fleeTimeSinceHurt;
                StateTime = Time.time;
            }
            internal void Decay(float now)
            {
                float dt = Mathf.Max(0f, now - StateTime);
                Rage *= Mathf.Exp(-.1155f * dt); // 6-second half-life
                Fear *= Mathf.Exp(-.0866f * dt); // 8-second half-life
                Confidence *= Mathf.Exp(-.05776f * dt); // 12s half-life
                StateTime = now;
            }
        }

        private sealed class Scene
        {
            internal float Expires;
            internal Vector3 Position;
            internal readonly List<Character> Actors = new List<Character>(32);
        }

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Enable Wire's Enemy Variation.");
            _individuals = Config.Bind("General", "Individual physiology and psychology", true,
                "Persistent individual physical attributes and tendencies for ordinary hostile creatures. Bosses excluded.");
            _groups = Config.Bind("General", "Faction-aware encounters", true,
                "Only the exact same faction supports an enemy. Players and other hostile factions are adversaries.");
            _bosses = Config.Bind("General", "Boss escalating aggression", true,
                "Bosses gain offensive pressure as their health falls, without random stats, speed, personality, or fear.");
            _debug = Config.Bind("Debug", "Log spawn and encounter", true,
                "Log every locally owned spawn, first main encounter, group changes, and boss health phases. No log polling.");
            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll(typeof(EnemyVariationPlugin).Assembly);
                Logger.LogInfo("Wire's Enemy Variation 0.1.0 active: deterministic individuals, faction-aware WBA, boss offensive escalation; event hooks only.");
            }
            catch (Exception e)
            {
                Logger.LogError("Enemy Variation patches FAILED: " + e);
                _harmony.UnpatchSelf();
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
            Instance = null;
        }

        private bool Active => _enabled.Value;

        // Different from GetInstanceID(): ZDO IDs are persistent across unload/reload and host migration.
        private static uint SeedFromZdo(ZDO zdo)
        {
            ZDOID uid = zdo.m_uid;
            ulong user = unchecked((ulong)uid.UserID);
            uint x = unchecked((uint)user) ^ unchecked((uint)(user >> 32)) ^ uid.ID ^ 0xB5E6F703u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15;
            x *= 0x846CA68Bu; x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        private static string SpeciesOf(Character c)
        {
            string name = c.gameObject.name;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            if (name.IndexOf("greydwarf", StringComparison.OrdinalIgnoreCase) >= 0) return "Greydwarf";
            if (name.IndexOf("trollfrost", StringComparison.OrdinalIgnoreCase) >= 0) return "TrollFrost";
            if (name.IndexOf("troll", StringComparison.OrdinalIgnoreCase) >= 0) return "Troll";
            if (name.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0) return "Wolf";
            if (name.IndexOf("skeleton", StringComparison.OrdinalIgnoreCase) >= 0) return "Skeleton";
            if (name.IndexOf("seeker", StringComparison.OrdinalIgnoreCase) >= 0) return "Seeker";
            // Fuling prefabs are named Goblin*
            if (name.IndexOf("fuling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("goblin", StringComparison.OrdinalIgnoreCase) >= 0) return "Fuling";
            return name;
        }

        internal void OnSpawn(Character c)
        {
            long spawnTicks = _debug.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            if (!Active || c == null || c is Player || c.GetBaseAI() is not MonsterAI) return;
            // Mistiles are Dvergr attacks, not individuals
            if (c.gameObject.name.IndexOf("mistile", StringComparison.OrdinalIgnoreCase) >= 0) return;
            ZNetView v = c.GetComponent<ZNetView>();
            if (v == null || !v.IsValid() || v.GetZDO() == null) return;
            if (Phys.TryGetValue(c, out _)) return;

            bool boss = c.IsBoss();
            ZDO zdo = v.GetZDO();
            int oldSeed = zdo.GetInt(SeedKey);
            uint seed = oldSeed != 0 ? unchecked((uint)oldSeed) : SeedFromZdo(zdo);
            if (oldSeed == 0 && v.IsOwner()) zdo.Set(SeedKey, unchecked((int)seed));
            Individual traits = !boss && _individuals.Value
                ? new Individual(seed, SpeciesOf(c), Mathf.Max(0, c.GetLevel()-1)) : null;
            var phys = new Physical(c, traits, boss);
            Phys.Add(c, phys);
            RegisterHitRpc(c.GetBaseAI() as MonsterAI, v);
            if (traits != null)
            {
                // Scaling the *root* scales the capsule, physical hitboxes,
                // bone-based attack origins and relative attack reach as well as visuals.
                // Size is calculated AFTER stats, not used as an input into any stat.
                c.m_walkSpeed *= traits.Axis[3];
                c.m_runSpeed *= traits.Axis[3];
                c.m_turnSpeed *= traits.Axis[4];
                c.transform.localScale = phys.OriginalScale * traits.Size;
                phys.Scaled = true;
                ApplyHealth(c, phys, v);
            }
            if (_debug.Value && v.IsOwner())
                Logger.LogInfo("[WEV spawn] initCpuMs=" +
                    ((System.Diagnostics.Stopwatch.GetTimestamp()-spawnTicks)*1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture) +
                    " id=" + zdo.m_uid + " prefab=" + SpeciesOf(c) +
                    " stars=" + Mathf.Max(0,c.GetLevel()-1) + (boss
                    ? " BOSS: randomized physiology/personality=OFF; offensive phase=ON"
                    : traits == null ? " individual traits=OFF"
                    : " seed=" + seed + " " + traits.Summary() +
                      " physicalColliderReach=scaled"));
        }

        private void ApplyHealth(Character c, Physical phys, ZNetView v, float previousFraction = -1f)
        {
            if (phys.Individual == null || !v.IsValid() || !v.IsOwner()) return;
            ZDO zdo = v.GetZDO();
            if (zdo == null) return;
            // Vanilla Awake resets max HP on every load at full health, so check the value, not a flag.
            float modified = c.GetMaxHealthBase() * c.GetLevel() * phys.Individual.Axis[0];
            float hp = c.GetMaxHealth();
            if (hp <= 0f || Mathf.Abs(hp - modified) < 0.01f) return;
            float before = c.GetHealth();
            float ratio = previousFraction >= 0f ? Mathf.Clamp01(previousFraction) : Mathf.Clamp01(before / hp);
            c.SetMaxHealth(modified);
            c.SetHealth(modified * ratio);
        }

        internal void OnLevel(Character c, float previousFraction)
        {
            if (c == null || !Phys.TryGetValue(c, out Physical p)) return;
            int stars = Mathf.Max(0, c.GetLevel()-1);
            if (p.Individual != null && p.Individual.Stars != stars)
            {
                // Star levels are often assigned AFTER Character.Awake.
                // Regenerate from exactly the SAME persistent seed.
                var revised = new Individual(p.Individual.Seed, p.Individual.Species, stars);
                p.Individual = revised;
                c.m_walkSpeed = p.OriginalWalk * revised.Axis[3];
                c.m_runSpeed = p.OriginalRun * revised.Axis[3];
                c.m_turnSpeed = p.OriginalTurn * revised.Axis[4];
                c.transform.localScale = p.OriginalScale * revised.Size;
                ZNetView z = c.GetComponent<ZNetView>();
                if (_debug.Value && z != null && z.IsValid() && z.IsOwner())
                    Logger.LogInfo("[WEV level] id=" + z.GetZDO().m_uid +
                        " stars=" + stars + " seed=" + revised.Seed +
                        " " + revised.Summary());
            }
            ApplyHealth(c, p, c.GetComponent<ZNetView>(),previousFraction);
        }

        private static Behavior Brain(MonsterAI ai)
        {
            if (!Brains.TryGetValue(ai, out Behavior brain))
            {
                brain = new Behavior(ai);
                Brains.Add(ai, brain);
            }
            return brain;
        }

        private static Scene Observe(Character target)
        {
            if (!Scenes.TryGetValue(target, out Scene scene))
            {
                scene = new Scene(); Scenes.Add(target, scene);
            }
            Vector3 pos = target.transform.position;
            float now = Time.time;
            if (scene.Expires > now && (scene.Position-pos).sqrMagnitude < 9f)
                return scene;
            scene.Expires = now + 1.25f;
            scene.Position = pos;
            scene.Actors.Clear();
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c != null && !c.IsDead() &&
                    (c.transform.position - pos).sqrMagnitude < ScanRadiusSq)
                    scene.Actors.Add(c);
            }
            return scene;
        }

        private static float Power(Character c, Character self, float distanceSq)
        {
            // Rough relative fighting potential, not a fixed "equal power budget".
            float level = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1, c.GetLevel()) /
                (float)Mathf.Max(1, self.GetLevel())), .5f, 2.2f);
            float health = .30f + .70f * Mathf.Clamp01(c.GetHealthPercentage());
            return level * health / (1f + distanceSq / 324f);
        }

        private static void Restore(MonsterAI ai, Behavior b)
        {
            if (ai == null) return;
            ai.m_circleTargetInterval = b.CircleInterval;
            ai.m_circleTargetDuration = b.CircleDuration;
            ai.m_minAttackInterval = b.AttackInterval;
            StopRetreat(ai,b);
            b.Action=Posture.Vanilla; b.NextDecisionAt=0f;
            b.HasEncounter = false;
            b.Allies = b.Enemies = -1;
        }

        internal void Refresh(MonsterAI ai, Character candidate, float incomingDamage)
        {
            if (ai == null || !Active) return;
            Behavior b = Brain(ai);
            Character self = b.Character;
            ZNetView v = b.View;
            if (self == null || v == null || !v.IsValid() || !v.IsOwner() ||
                (self.IsDead() || self.GetHealth() <= 0f) || self.IsTamed() || (!self.IsBoss() &&
                (self.IsFlying() || ai.m_randomFly || ai.m_serpentMovement)))
            {
                Restore(ai,b);
                return;
            }

            // Picks up HP for enemies that spawned while another player owned them.
            if (!self.IsBoss() && Phys.TryGetValue(self, out Physical owned)) ApplyHealth(self, owned, v);

            Character enemy = candidate != null && BaseAI.IsEnemy(self,candidate)
                ? candidate : Target(ai);
            if (enemy == null || enemy.IsDead() || !BaseAI.IsEnemy(self,enemy))
            {
                Restore(ai,b);
                return;
            }
            float now = Time.time;
            b.Decay(now);
            if (incomingDamage > 0f && !self.IsBoss())
            {
                float fraction = incomingDamage / Mathf.Max(1f,self.GetMaxHealth());
                b.Rage = Mathf.Clamp01(b.Rage + fraction * 2.0f);
                b.Fear = Mathf.Clamp01(b.Fear + fraction * 1.4f);
                b.Confidence *= .94f;
                b.NextDecisionAt = 0f;
                b.DecisionReason = "hurt-by-enemy";
                if (_debug.Value)
                    Logger.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[WEV reaction] id={0} incomingDamage={1:F1} rage={2:F2} fear={3:F2} confidence={4:F2}",
                        v.GetZDO().m_uid,incomingDamage,b.Rage,b.Fear,b.Confidence));
            }
            int allies=1, enemies=0;
            float allyPower = 1f, enemyPower=0f;
            if (_groups.Value)
            {
                Scene scene = Observe(enemy);
                for (int i=0;i<scene.Actors.Count;i++)
                {
                    Character actor = scene.Actors[i];
                    // Cached scene actors may have died/despawned after the snapshot.
                    // Unity destroyed objects throw on actor.transform; skip them.
                    if (actor == null || actor == self || actor.IsDead() || actor.GetHealth() <= 0f) continue;
                    float sq = (actor.transform.position - self.transform.position).sqrMagnitude;
                    if (sq > ScanRadiusSq) continue;
                    if (!actor.IsTamed() && actor.GetFaction() == self.GetFaction() &&
                        !BaseAI.IsEnemy(self,actor))
                    {
                        allies++;
                        allyPower += Power(actor,self,sq);
                    }
                    else if (BaseAI.IsEnemy(self, actor))
                    {
                        enemies++;
                        enemyPower += Power(actor,self,sq);
                    }
                }
            }
            else { enemies=1; enemyPower=1f; }
            if (enemies == 0) { enemies=1; enemyPower=1f; }
            // EXACT ZERO at ordinary, healthy, unassisted 1v1.
            float imbalance = (allies == 1 && enemies == 1) ? 0f :
                (float)Math.Tanh((allyPower-enemyPower) / Mathf.Max(1f,allyPower+enemyPower));
            float drive = 0f;
            float interval = b.CircleInterval;
            float duration = b.CircleDuration;
            float attack = b.AttackInterval;
            int phase=-1;
            if (self.IsBoss())
            {
                if (!_bosses.Value) { Restore(ai,b); return; }
                float lost = Mathf.Clamp01(1f-self.GetHealthPercentage());
                phase = lost >= .9f ? 4 : lost >= .75f ? 3 :
                    lost >= .5f ? 2 : lost >= .25f ? 1 : 0;
                // Semiphase: strictly offensive, smooth, no boss stat/scale changes.
                float escalation = Mathf.Pow(lost,1.35f) + .11f*b.Confidence +
                    .16f * Mathf.Clamp01((enemies-1f)/3f);
                interval *= 1f + .95f*escalation;
                duration *= Mathf.Max(.20f,1f-.68f*escalation);
                attack *= Mathf.Max(.55f,1f-.40f*escalation);
            }
            else
            {
                Individual p = null;
                if (Phys.TryGetValue(self,out Physical physical)) p=physical.Individual;
                float agg=p?.Aggression ?? 0f, courage=p?.Courage ?? 0f;
                float tactics=p?.Tactics ?? 0f, retaliation=p?.Retaliation ?? 0f;
                float cohesion=p?.Cohesion ?? 0f;
                float groupFactor = p?.SpeciesGroupFactor ?? 1f;
                float fearSensitivity = p?.FearSensitivity ?? 1f;
                // Independent personality, context and deterministic species biology.
                float groupEffect = Mathf.Clamp(imbalance * (.75f+.22f*cohesion) *
                    groupFactor, -1f, 1f);
                float rage = b.Rage * (.55f+.45f*Mathf.Max(0f,retaliation+.5f));
                float fear = b.Fear * fearSensitivity * (1f-.25f*courage);
                drive = .52f*agg + .24f*tactics + .50f*groupEffect +
                    .35f*rage - .28f*fear;
                // ±20% is the physical attack-interval axis. Situational
                // behavior is additive and bounded, with neutral mean=vanilla.
                float attackSpeed = p == null ? 1f : p.Axis[2];
                // Attack speed is attacks/second (±20%), not +/-20% on cooldown.
                // Inverse rate to derive the Valheim interval. At healthy 1v1,
                // expected attack speed is exactly vanilla despite correlation.
                attack /= Mathf.Clamp(attackSpeed + .16f*drive + .14f*b.Confidence +
                    .09f*(1f-enemy.GetHealthPercentage()),.55f,1.45f);
                interval *= Mathf.Clamp(1f+.42f*drive + .18f*groupEffect,.55f,1.60f);
                duration *= Mathf.Clamp(1f-.33f*drive + .12f*tactics,.58f,1.55f);
            }
            ai.m_minAttackInterval = Mathf.Max(0f,attack);
            ai.m_circleTargetInterval = Mathf.Max(0f,interval);
            ai.m_circleTargetDuration = Mathf.Max(0f,duration);
            if (!self.IsBoss() && _individuals.Value &&
                Phys.TryGetValue(self,out Physical decisionPhys) &&
                decisionPhys.Individual != null)
            {
                ChoosePosture(ai,b,enemy,decisionPhys.Individual,drive,
                    Mathf.Clamp(imbalance,-1f,1f),now);
                ApplyPosture(ai,b);
            }

            if (_debug.Value && (!b.HasEncounter || phase!=b.Phase ||
                ((allies!=b.Allies || enemies!=b.Enemies) && now-b.LastLogTime>8f)))
            {
                Logger.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WEV encounter] {0} id={1} target={2} allies={3} adversaries={4} powerBalance={5:+0.00;-0.00;0} hp={6:0%} bossPhase={7} attackDelay={8:0.000}s (vanilla={9:0.000}) circle={10:0.000}/{11:0.000} rage={12:0.00} fear={13:0.00}",
                    self.name, v.GetZDO().m_uid, enemy.name, allies,enemies,imbalance,
                    self.GetHealthPercentage(),phase,ai.m_minAttackInterval,b.AttackInterval,ai.m_circleTargetInterval,ai.m_circleTargetDuration,
                    b.Rage,b.Fear));
                b.LastLogTime=now;
            }
            b.HasEncounter=true; b.Allies=allies; b.Enemies=enemies; b.Phase=phase;
        }

        internal void OnAllyDeath(Character dead)
        {
            if (!Active || dead == null || dead is Player) return;
            List<Character> actors = Character.GetAllCharacters();
            for (int i=0;i<actors.Count;i++)
            {
                Character c=actors[i];
                if (c==null || c==dead || c.IsDead() || c.IsBoss() ||
                    c.GetFaction()!=dead.GetFaction() || c.IsTamed() ||
                    (c.transform.position-dead.transform.position).sqrMagnitude>ScanRadiusSq)
                    continue;
                if (!(c.GetBaseAI() is MonsterAI ai) ||
                    !Brains.TryGetValue(ai,out Behavior brain) ||
                    !Phys.TryGetValue(c,out Physical physical) ||
                    physical.Individual==null || brain.View == null || !brain.View.IsOwner())
                    continue;
                brain.Decay(Time.time);
                Individual p=physical.Individual;
                // Dead ally can cause fear, hatred, opportunistic rage, or little response.
                brain.Rage=Mathf.Clamp01(brain.Rage+.23f*p.LossRageBias*
                    Mathf.Max(.10f, .75f+.45f*p.Retaliation));
                brain.Fear=Mathf.Clamp01(brain.Fear+.18f*p.FearSensitivity*
                    Mathf.Max(.10f,.85f-.45f*p.Courage));
                brain.NextDecisionAt=0f;
                brain.DecisionReason="ally-killed";
                if (_debug.Value)
                    Logger.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[WEV reaction] id={0} allyKilled={1} rage={2:F2} fear={3:F2} confidence={4:F2}",
                        brain.View.GetZDO().m_uid, dead.name, brain.Rage,brain.Fear,brain.Confidence));
                Refresh(ai,Target(ai),0f);
            }
        }

        internal void ScaleIncomingHit(Character victim, ref HitData hit)
        {
            if (!Active || !_individuals.Value || hit == null ||
                victim == null || victim.GetComponent<ZNetView>() is not ZNetView v ||
                !v.IsValid() || !v.IsOwner()) return;
            Character attacker=hit.GetAttacker();
            if (attacker == null || attacker.IsBoss() || attacker.IsTamed()) return;
            if (!Phys.TryGetValue(attacker,out Physical phys) || phys.Individual==null) return;
            // Only scale a single receiver's copy. No cumulative mutation if one
            // projectile/hit object reaches several victims.
            hit=hit.Clone();
            hit.ApplyModifier(phys.Individual.Axis[1]);
            hit.m_damage.m_damage *= phys.Individual.Axis[1];
        }

        internal void StaggerThreshold(Character c, ref float value)
        {
            if (Active && _individuals.Value && c!=null &&
                Phys.TryGetValue(c,out Physical p) && p.Individual!=null)
                value *= p.Individual.Axis[5] / p.Individual.Axis[0]; // cancel the HP-derived part so stagger only follows its own axis
        }

        [HarmonyPatch(typeof(Character),"Awake")]
        private static class SpawnPatch
        {
            private static void Postfix(Character __instance) => Instance?.OnSpawn(__instance);
        }
        [HarmonyPatch(typeof(Character),"SetLevel")]
        private static class LevelPatch
        {
            private static void Prefix(Character __instance, out float __state)
            {
                float hp=__instance.GetMaxHealth();
                __state=hp>0f ? Mathf.Clamp01(__instance.GetHealth()/hp) : 1f;
            }
            private static void Postfix(Character __instance,float __state) =>
                Instance?.OnLevel(__instance,__state);
        }
        [HarmonyPatch(typeof(Character),"RPC_Damage")]
        private static class HitPatch
        {
            private static void Prefix(Character __instance, ref HitData hit) =>
                Instance?.ScaleIncomingHit(__instance,ref hit);
        }
        [HarmonyPatch(typeof(Attack),"Start")]
        private static class ReachPatch
        {
            // Humanoid.StartAttack clones the template before Start; safe per-attack.
            private static void Postfix(Attack __instance, Humanoid character, bool __result)
            {
                if (!__result || Instance == null || !Instance.Active ||
                    !Instance._individuals.Value || character == null ||
                    !Phys.TryGetValue(character, out Physical p) || p.Individual == null)
                    return;
                float scale = p.Individual.Size;
                __instance.m_attackRange *= scale;
                __instance.m_attackHeight *= scale;
                __instance.m_attackRayWidth *= scale;
            }
        }

        [HarmonyPatch(typeof(Character),"GetHeight")]
        private static class HeightPatch
        {
            private static void Postfix(Character __instance, ref float __result)
            {
                if (Instance != null && Instance.Active && Instance._individuals.Value &&
                    Phys.TryGetValue(__instance, out Physical p) && p.Individual != null)
                    __result *= p.Individual.Size;
            }
        }
        [HarmonyPatch(typeof(Character),"GetStaggerTreshold")]
        private static class StaggerPatch
        {
            private static void Postfix(Character __instance, ref float __result) =>
                Instance?.StaggerThreshold(__instance,ref __result);
        }
        [HarmonyPatch(typeof(BaseAI),"FindEnemy")]
        private static class TargetPatch
        {
            private static void Postfix(BaseAI __instance, Character __result)
            {
                if (__instance is MonsterAI ai) Instance?.Refresh(ai,__result,0f);
            }
        }
        [HarmonyPatch(typeof(MonsterAI),"OnDamaged")]
        private static class DamagePatch
        {
            private static void Postfix(MonsterAI __instance,float damage, Character attacker) =>
                Instance?.Refresh(__instance,attacker,damage);
        }
        [HarmonyPatch(typeof(Character),"OnDeath")]
        private static class DeathPatch
        {
            private static void Prefix(Character __instance) => Instance?.OnAllyDeath(__instance);
        }
    }
}