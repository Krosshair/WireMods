using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WiresMeisterhau
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public sealed class WiresMeisterhauPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "wire.meisterhau";
        public const string ModName = "Wire's Meisterhau";
        public const string ModVersion = "0.2.4";
        internal const float VanillaPerfectParryWindow = 0.25f;
        internal const string RangedWindupRpc = "wire.meisterhau.rangedwindup";

        internal static readonly ServerSync.ConfigSync ServerConfig = new ServerSync.ConfigSync(ModGuid)
        {
            DisplayName = ModName,
            CurrentVersion = ModVersion,
            MinimumRequiredVersion = ModVersion,
            ModRequired = true
        };

        internal static WiresMeisterhauPlugin Instance;
        internal static ConfigEntry<bool> LockConfiguration;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> OffensiveBlockPower;
        internal static ConfigEntry<float> RangedInterruptDamageFactor;
        internal static ConfigEntry<float> WeaponXpMultiplier;
        internal static ConfigEntry<float> CounterSoundPitch;

        internal static readonly Dictionary<int, HitRecord> RecentHits = new Dictionary<int, HitRecord>();
        internal static readonly Dictionary<int, RangedWindupState> RangedWindups = new Dictionary<int, RangedWindupState>();
        internal static readonly Dictionary<ZDOID, RangedWindupState> NetworkRangedWindups = new Dictionary<ZDOID, RangedWindupState>();
        internal static readonly List<PendingXp> PendingXpAwards = new List<PendingXp>();
        internal static readonly List<int> PruneKeys = new List<int>();
        internal static float NextPruneTime;
        internal static AttackSourceContext CurrentSource;
        internal static bool InOffensiveBlock;
        internal static bool GrantingBonusXp;
        internal static Character ActiveParryTarget;
        internal static Skills.SkillType ActiveCounterSkill = Skills.SkillType.None;
        internal static bool ActiveCounterIsRanged;
        internal static bool OffensiveParryStaggerSeen;

        private static readonly MethodInfo BlockAttackMethod = AccessTools.Method(typeof(Humanoid), "BlockAttack", new[] { typeof(HitData), typeof(Character) });
        private static readonly FieldInfo LeftItemField = AccessTools.Field(typeof(Humanoid), "m_leftItem");
        private static readonly FieldInfo RightItemField = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo BlockTimerField = AccessTools.Field(typeof(Humanoid), "m_blockTimer");
        private static readonly FieldInfo NextChainField = AccessTools.Field(typeof(Attack), "m_nextAttackChainLevel");
        private static readonly FieldInfo CurrentChainField = AccessTools.Field(typeof(Attack), "m_currentAttackCainLevel");
        private static readonly FieldInfo CurrentAttackField = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
        private static readonly FieldInfo ProjectileAttackStartedField = AccessTools.Field(typeof(Attack), "m_projectileAttackStarted");
        private static readonly FieldInfo StaggerDamageField = AccessTools.Field(typeof(Character), "m_staggerDamage");
        private static readonly MethodInfo GetStaggerThresholdMethod = AccessTools.Method(typeof(Character), "GetStaggerTreshold");
        private static readonly MethodInfo ModifyDamageMethod = AccessTools.Method(typeof(Attack), "ModifyDamage", new[] { typeof(HitData), typeof(float) });

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            LockConfiguration = Config.Bind("Server", "LockConfiguration", true, "Server controls and locks all Meisterhau gameplay settings for connected clients.");
            ServerConfig.AddLockingConfigEntry(LockConfiguration);
            Enabled = BindSynced("General", "Enabled", true, "Enable Wire's Meisterhau.");
            OffensiveBlockPower = BindSynced("Balance", "OffensiveParryPower", 1.5f, "Multiplier on the attacking melee weapon's vanilla parry power.");
            RangedInterruptDamageFactor = BindSynced("Balance", "RangedInterruptDamageFactor", 0.5f, "Fraction of ranged weapon damage used as base interrupt power.");
            WeaponXpMultiplier = BindSynced("Balance", "WeaponSkillXP", 3f, "Total weapon-skill XP for a successful Meisterhau versus a normal connected hit.");
            CounterSoundPitch = BindSynced("Feedback", "CounterSoundPitch", 1.50f, "Pitch/speed multiplier for the vanilla parry sound during Meisterhau.");

            _harmony = new Harmony(ModGuid);
            PatchOwnHarmonyClasses();
            Logger.LogInfo($"{ModName} {ModVersion} loaded. Multiplayer enforcement and server config sync enabled.");
        }

        private ConfigEntry<T> BindSynced<T>(string section, string key, T value, string description)
        {
            ConfigEntry<T> entry = Config.Bind(section, key, value, description);
            ServerConfig.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        private void PatchOwnHarmonyClasses()
        {
            foreach (Type type in typeof(WiresMeisterhauPlugin).Assembly.GetTypes())
            {
                if (type.Namespace == typeof(WiresMeisterhauPlugin).Namespace &&
                    type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                    _harmony.CreateClassProcessor(type).Patch();
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            ResolvePendingXp();
            PruneOldHits();
        }

        internal static void PruneOldHits()
        {
            if (Time.time < NextPruneTime) return;
            NextPruneTime = Time.time + 0.5f;
            if (RecentHits.Count == 0) return;

            float cutoff = Time.time - 0.75f;
            PruneKeys.Clear();
            foreach (var pair in RecentHits)
                if (pair.Value == null || pair.Value.Time < cutoff || pair.Value.Consumed)
                    PruneKeys.Add(pair.Key);
            foreach (int key in PruneKeys) RecentHits.Remove(key);
        }

        internal static HitRecord GetRecentHit(Character attacker)
        {
            if (attacker == null) return null;
            HitRecord record;
            if (!RecentHits.TryGetValue(attacker.GetInstanceID(), out record) || record == null || record.Consumed)
                return null;
            float age = Time.time - record.Time;
            if (age < 0f || age > VanillaPerfectParryWindow) return null;
            return record;
        }

        internal static bool IsUsableCounterWeapon(ItemDrop.ItemData weapon)
        {
            return weapon != null && weapon.m_shared != null && weapon.m_shared.m_timedBlockBonus > 1f;
        }

        internal static Skills.SkillType GetCounterSkill(HitRecord record)
        {
            if (record == null) return Skills.SkillType.None;
            if (record.Skill != Skills.SkillType.None) return record.Skill;
            return record.Weapon != null && record.Weapon.m_shared != null
                ? record.Weapon.m_shared.m_skillType
                : Skills.SkillType.None;
        }

        internal static bool IsMagicSkill(Skills.SkillType skill)
        {
            return skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic;
        }

        internal static float ApplyVanillaBlockSkillScaling(float basePower, float skillFactor)
        {
            return basePower * (1f + Mathf.Clamp01(skillFactor) * 0.5f);
        }

        internal static float GetRangedInterruptBasePower(HitRecord record)
        {
            if (record == null || record.Weapon == null) return 0f;
            return Mathf.Max(0f, record.Weapon.GetDamage().GetTotalDamage())
                * Mathf.Max(0f, RangedInterruptDamageFactor.Value);
        }

        internal static bool IsRangedAttack(Attack attack)
        {
            if (attack == null) return false;
            return attack.m_attackType == Attack.AttackType.Projectile
                || attack.m_attackType == Attack.AttackType.TriggerProjectile
                || attack.m_attackProjectile != null;
        }

        internal static ZDOID GetCharacterZdoId(Character character)
        {
            if (character == null) return ZDOID.None;
            ZNetView view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return ZDOID.None;
            ZDO zdo = view.GetZDO();
            return zdo != null ? zdo.m_uid : ZDOID.None;
        }

        internal static HitData BuildEnemyRangedAttackHit(Humanoid owner, Attack attack)
        {
            if (owner == null || attack == null) return null;
            ItemDrop.ItemData enemyWeapon = attack.GetWeapon();
            if (enemyWeapon == null || enemyWeapon.m_shared == null || !enemyWeapon.m_shared.m_blockable) return null;

            HitData hit = new HitData();
            hit.m_damage = enemyWeapon.GetDamage();
            hit.m_blockable = enemyWeapon.m_shared.m_blockable;
            hit.m_dodgeable = enemyWeapon.m_shared.m_dodgeable;
            hit.m_staggerMultiplier = attack.m_staggerMultiplier;
            hit.m_skill = enemyWeapon.m_shared.m_skillType;
            hit.SetAttacker(owner);

            float skillFactor = owner.GetRandomSkillFactor(enemyWeapon.m_shared.m_skillType);
            if (ModifyDamageMethod != null)
                ModifyDamageMethod.Invoke(attack, new object[] { hit, skillFactor });
            return hit;
        }

        internal static void BroadcastRangedWindup(Humanoid owner, Attack attack, bool active)
        {
            if (owner == null || ZRoutedRpc.instance == null) return;
            ZNetView view = owner.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            ZDO zdo = view.GetZDO();
            if (zdo == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(active);
            pkg.Write(zdo.m_uid);
            if (active)
            {
                HitData hit = BuildEnemyRangedAttackHit(owner, attack);
                if (hit == null) return;
                hit.Serialize(ref pkg);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RangedWindupRpc, pkg);
        }

        internal static void EndRangedWindup(Humanoid owner, Attack attack)
        {
            if (owner == null || attack == null || owner.IsPlayer()) return;
            RangedWindupState state;
            if (!RangedWindups.TryGetValue(owner.GetInstanceID(), out state) || state == null || state.Attack != attack) return;
            RangedWindups.Remove(owner.GetInstanceID());
            ZDOID id = GetCharacterZdoId(owner);
            if (!id.IsNone()) NetworkRangedWindups.Remove(id);
            BroadcastRangedWindup(owner, attack, false);
        }

        internal static void RPC_RangedWindup(long sender, ZPackage pkg)
        {
            try
            {
                bool active = pkg.ReadBool();
                ZDOID id = pkg.ReadZDOID();
                if (!active)
                {
                    NetworkRangedWindups.Remove(id);
                    return;
                }

                HitData hit = new HitData();
                hit.Deserialize(ref pkg);
                NetworkRangedWindups[id] = new RangedWindupState
                {
                    IncomingHit = hit,
                    NetworkId = id,
                    StartedAt = Time.time,
                    IsNetworkState = true
                };
            }
            catch (Exception ex)
            {
                Instance?.Logger.LogWarning("Meisterhau ranged-windup RPC failed: " + ex.Message);
            }
        }

        internal static RangedWindupState GetActiveRangedWindup(Character target)
        {
            if (target == null) return null;

            RangedWindupState localState;
            if (RangedWindups.TryGetValue(target.GetInstanceID(), out localState) && localState != null)
            {
                Humanoid humanoid = target as Humanoid;
                Attack current = humanoid != null && CurrentAttackField != null
                    ? CurrentAttackField.GetValue(humanoid) as Attack
                    : null;
                bool projectileStarted = false;
                if (current != null && ProjectileAttackStartedField != null)
                {
                    object value = ProjectileAttackStartedField.GetValue(current);
                    if (value is bool) projectileStarted = (bool)value;
                }
                if (current == localState.Attack && current != null && target.InAttack() && !projectileStarted)
                    return localState;
                RangedWindups.Remove(target.GetInstanceID());
            }

            ZDOID id = GetCharacterZdoId(target);
            if (id.IsNone()) return null;
            RangedWindupState networkState;
            if (!NetworkRangedWindups.TryGetValue(id, out networkState) || networkState == null) return null;
            if (Time.time - networkState.StartedAt > 10f)
            {
                NetworkRangedWindups.Remove(id);
                return null;
            }
            return networkState;
        }

        internal static HitData BuildRangedWindupHit(RangedWindupState state, Character target, Player player)
        {
            if (state == null || target == null || player == null) return null;
            HitData hit = state.IncomingHit != null
                ? state.IncomingHit.Clone()
                : BuildEnemyRangedAttackHit(state.Owner, state.Attack);
            if (hit == null || !hit.m_blockable) return null;
            hit.SetAttacker(target);

            Vector3 dir = player.transform.position - target.transform.position;
            if (dir.sqrMagnitude < 0.0001f) dir = -player.transform.forward;
            hit.m_dir = dir.normalized;
            return hit;
        }

        internal static bool TryRangedWindupInterrupt(Character target, HitRecord record)
        {
            Player player = Player.m_localPlayer;
            if (player == null || target == null || record == null) return false;
            if (target.IsBoss() || target.IsDead() || target.IsStaggering()) return false;

            RangedWindupState state = GetActiveRangedWindup(target);
            if (state == null) return false;
            HitData incoming = BuildRangedWindupHit(state, target, player);
            if (incoming == null) return false;

            bool success = TryOffensiveParry(player, incoming, target, record, true, incoming.m_dir, true);
            if (success)
            {
                RangedWindups.Remove(target.GetInstanceID());
                ZDOID id = GetCharacterZdoId(target);
                if (!id.IsNone()) NetworkRangedWindups.Remove(id);
            }
            return success;
        }

        internal static bool WouldPerfectParrySucceed(Player player, HitData incoming, HitRecord record)
        {
            if (player == null || incoming == null || record == null || record.Weapon == null) return false;
            if (!incoming.m_blockable || !player.HaveStamina(0f)) return false;

            Skills.SkillType skill = GetCounterSkill(record);
            if (skill == Skills.SkillType.None || IsMagicSkill(skill)) return false;

            var weapon = record.Weapon;
            float skillFactor = player.GetSkillFactor(skill);
            float blockPower;

            if (record.IsProjectile)
            {
                float basePower = GetRangedInterruptBasePower(record);
                if (basePower <= 0f) return false;
                blockPower = ApplyVanillaBlockSkillScaling(basePower, skillFactor);
            }
            else
            {
                if (!IsUsableCounterWeapon(weapon)) return false;
                blockPower = weapon.GetBlockPower(skillFactor) * Mathf.Max(0f, OffensiveBlockPower.Value);
                blockPower *= weapon.m_shared.m_timedBlockBonus;
                player.GetSEMan().ModifyTimedBlockBonus(ref blockPower);
            }

            HitData test = incoming.Clone();
            if (weapon.m_shared.m_damageModifiers != null && weapon.m_shared.m_damageModifiers.Count > 0)
            {
                HitData.DamageModifiers modifiers = new HitData.DamageModifiers();
                modifiers.Apply(weapon.m_shared.m_damageModifiers);
                HitData.DamageModifier ignored;
                test.ApplyResistance(modifiers, out ignored);
            }

            HitData.DamageTypes damage = test.m_damage.Clone();
            damage.ApplyArmor(blockPower);
            float stagger = damage.GetTotalStaggerDamage();
            float modifiedStagger = stagger;
            player.GetSEMan().ModifyStagger(stagger, ref modifiedStagger);

            float threshold = GetStaggerThreshold(player);
            if (threshold <= 0f) return true;
            float current = player.GetStaggerPercentage() * threshold;
            return current + modifiedStagger < threshold;
        }

        internal static bool TryOffensiveParry(Player player, HitData incoming, Character attacker, HitRecord record, bool useClone, Vector3? forcedDirection = null, bool preservePlayerStagger = false)
        {
            if (!Enabled.Value || player == null || incoming == null || attacker == null || record == null) return false;
            if (record.Consumed || player.IsDead() || player.IsStaggering() || player.IsBlocking()) return false;
            if (attacker.IsDead() || attacker.IsBoss() || attacker.IsStaggering() || !attacker.m_staggerWhenBlocked) return false;
            if (!incoming.m_blockable || record.Weapon == null) return false;

            Skills.SkillType skill = GetCounterSkill(record);
            if (skill == Skills.SkillType.None || IsMagicSkill(skill)) return false;
            if (!record.IsProjectile && !IsUsableCounterWeapon(record.Weapon)) return false;
            if (record.IsProjectile && GetRangedInterruptBasePower(record) <= 0f) return false;

            HitData parryHit = useClone ? incoming.Clone() : incoming;
            Vector3 originalPoint = parryHit.m_point;
            Vector3 originalDirection = parryHit.m_dir;
            if (forcedDirection.HasValue && forcedDirection.Value.sqrMagnitude > 0.0001f)
                parryHit.m_dir = forcedDirection.Value.normalized;
            parryHit.m_point = record.HitPoint;
            parryHit.SetAttacker(attacker);

            if (!WouldPerfectParrySucceed(player, parryHit, record))
            {
                if (!useClone) { parryHit.m_point = originalPoint; parryHit.m_dir = originalDirection; }
                return false;
            }

            bool success = RunVanillaPerfectParry(player, parryHit, attacker, record, preservePlayerStagger);
            if (!useClone) { parryHit.m_point = originalPoint; parryHit.m_dir = originalDirection; }
            if (!success) return false;

            record.Consumed = true;
            OnCounterSuccess(record);
            return true;
        }

        internal static bool RunVanillaPerfectParry(Player player, HitData hit, Character attacker, HitRecord record, bool preservePlayerStagger)
        {
            if (BlockAttackMethod == null || LeftItemField == null || RightItemField == null || BlockTimerField == null)
                return false;

            var weapon = record.Weapon;
            Skills.SkillType skill = GetCounterSkill(record);
            bool rangedCounter = record.IsProjectile;
            object oldLeft = LeftItemField.GetValue(player);
            object oldRight = RightItemField.GetValue(player);
            float oldTimer = (float)BlockTimerField.GetValue(player);
            float oldPerfectDrain = player.m_perfectBlockStaminaDrain;
            float oldBlockPower = weapon.m_shared.m_blockPower;
            float oldBlockPerLevel = weapon.m_shared.m_blockPowerPerLevel;
            float oldTimedBonus = weapon.m_shared.m_timedBlockBonus;
            float oldStaggerDamage = 0f;
            if (preservePlayerStagger && StaggerDamageField != null)
                oldStaggerDamage = (float)StaggerDamageField.GetValue(player);

            bool returned = false;
            OffensiveParryStaggerSeen = false;
            ActiveParryTarget = attacker;
            ActiveCounterSkill = skill;
            ActiveCounterIsRanged = rangedCounter;
            InOffensiveBlock = true;

            try
            {
                LeftItemField.SetValue(player, null);
                RightItemField.SetValue(player, weapon);
                BlockTimerField.SetValue(player, 0f);
                player.m_perfectBlockStaminaDrain = 0f;

                if (rangedCounter)
                {
                    const float syntheticTimedBonus = 1.0001f;
                    float basePower = GetRangedInterruptBasePower(record);
                    weapon.m_shared.m_blockPower = basePower / syntheticTimedBonus;
                    weapon.m_shared.m_blockPowerPerLevel = 0f;
                    weapon.m_shared.m_timedBlockBonus = syntheticTimedBonus;
                }
                else
                {
                    float multiplier = Mathf.Max(0f, OffensiveBlockPower.Value);
                    weapon.m_shared.m_blockPower *= multiplier;
                    weapon.m_shared.m_blockPowerPerLevel *= multiplier;
                }

                object result = BlockAttackMethod.Invoke(player, new object[] { hit, attacker });
                if (result is bool) returned = (bool)result;
            }
            catch (Exception ex)
            {
                Instance?.Logger.LogError("Meisterhau vanilla parry call failed: " + ex);
            }
            finally
            {
                LeftItemField.SetValue(player, oldLeft);
                RightItemField.SetValue(player, oldRight);
                BlockTimerField.SetValue(player, oldTimer);
                player.m_perfectBlockStaminaDrain = oldPerfectDrain;
                weapon.m_shared.m_blockPower = oldBlockPower;
                weapon.m_shared.m_blockPowerPerLevel = oldBlockPerLevel;
                weapon.m_shared.m_timedBlockBonus = oldTimedBonus;
                if (preservePlayerStagger && StaggerDamageField != null)
                    StaggerDamageField.SetValue(player, oldStaggerDamage);
                InOffensiveBlock = false;
                ActiveParryTarget = null;
                ActiveCounterSkill = Skills.SkillType.None;
                ActiveCounterIsRanged = false;
            }

            return returned && OffensiveParryStaggerSeen;
        }

        internal static void OnCounterSuccess(HitRecord record)
        {
            if (record == null) return;

            SkipToFinalComboAttack(record);
            PendingXpAwards.Add(new PendingXp
            {
                Record = record,
                ResolveAt = Time.time + 0.05f
            });

            Instance?.Logger.LogDebug($"Meisterhau: {record.Weapon?.m_shared?.m_name ?? "weapon"} -> {record.Target?.GetHoverName() ?? "enemy"}");
        }

        internal static void SkipToFinalComboAttack(HitRecord record)
        {
            Attack attack = record.SourceAttack;
            if (attack == null || NextChainField == null || CurrentChainField == null) return;
            int levels = attack.m_attackChainLevels;
            if (levels <= 1) return;

            int finalIndex = levels - 1;
            int currentIndex = (int)CurrentChainField.GetValue(attack);
            if (currentIndex >= finalIndex) return;

            NextChainField.SetValue(attack, finalIndex);
        }

        internal static void ResolvePendingXp()
        {
            if (PendingXpAwards.Count == 0) return;
            for (int i = PendingXpAwards.Count - 1; i >= 0; --i)
            {
                PendingXp pending = PendingXpAwards[i];
                if (Time.time < pending.ResolveAt) continue;
                PendingXpAwards.RemoveAt(i);
                AwardBonusXp(pending.Record);
            }
        }
        internal static void AwardBonusXp(HitRecord record)
        {
            Player player = Player.m_localPlayer;
            if (record == null || player == null || record.Skill == Skills.SkillType.None) return;

            float normalXp = record.NormalXp;
            if (normalXp <= 0f && record.BaseSkillRaiseAmount > 0f)
            {
                if (record.IsProjectile || (record.SourceAttack != null && record.SourceAttack.m_attackType == Attack.AttackType.Area))
                    normalXp = record.BaseSkillRaiseAmount;
                else
                    normalXp = record.BaseSkillRaiseAmount * 1.5f;
            }

            float extra = normalXp * Mathf.Max(0f, WeaponXpMultiplier.Value - 1f);
            if (extra <= 0f) return;

            GrantingBonusXp = true;
            try
            {
                player.RaiseSkill(record.Skill, extra);
            }
            finally
            {
                GrantingBonusXp = false;
            }
        }

        internal static void RegisterActualPlayerHit(Character target, HitData hit, float damageDone)
        {
            AttackSourceContext source = CurrentSource;
            Player player = Player.m_localPlayer;
            if (source == null || player == null || target == null || hit == null || damageDone <= 0f) return;
            if (hit.GetAttacker() != player || source.Weapon == null) return;

            HitRecord record = new HitRecord
            {
                Target = target,
                Time = Time.time,
                HitPoint = hit.m_point,
                Weapon = source.Weapon,
                SourceAttack = source.Attack,
                IsProjectile = source.IsProjectile,
                Skill = hit.m_skill,
                BaseSkillRaiseAmount = hit.m_skillRaiseAmount
            };

            RecentHits[target.GetInstanceID()] = record;
            source.Records.Add(record);

            TryRangedWindupInterrupt(target, record);
        }

        internal static float GetStaggerThreshold(Character character)
        {
            if (character == null || GetStaggerThresholdMethod == null) return 0f;
            object value = GetStaggerThresholdMethod.Invoke(character, null);
            return value is float ? (float)value : 0f;
        }

        internal static void LogWarn(string message)
        {
            Instance?.Logger.LogWarning(message);
        }    }

    internal sealed class AttackSourceContext
    {
        internal ItemDrop.ItemData Weapon;
        internal Attack Attack;
        internal bool IsProjectile;
        internal readonly List<HitRecord> Records = new List<HitRecord>();
    }

    internal sealed class HitRecord
    {
        internal Character Target;
        internal float Time;
        internal Vector3 HitPoint;
        internal ItemDrop.ItemData Weapon;
        internal Attack SourceAttack;
        internal bool IsProjectile;
        internal Skills.SkillType Skill;
        internal float BaseSkillRaiseAmount;
        internal float NormalXp;
        internal bool Consumed;
    }

    internal sealed class PendingXp
    {
        internal HitRecord Record;
        internal float ResolveAt;
    }

    internal sealed class RangedWindupState
    {
        internal Humanoid Owner;
        internal Attack Attack;
        internal HitData IncomingHit;
        internal ZDOID NetworkId;
        internal bool IsNetworkState;
        internal float StartedAt;
    }
    [HarmonyPatch(typeof(Attack), "OnAttackTrigger")]
    internal static class PlayerAttackSourcePatch
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character, ItemDrop.ItemData ___m_weapon, ref AttackSourceContext __state)
        {
            __state = WiresMeisterhauPlugin.CurrentSource;
            Player player = ___m_character as Player;
            if (player == null || player != Player.m_localPlayer || ___m_weapon == null) return;

            WiresMeisterhauPlugin.CurrentSource = new AttackSourceContext
            {
                Weapon = ___m_weapon,
                Attack = __instance,
                IsProjectile = false
            };
        }

        private static void Postfix(AttackSourceContext __state)
        {
            WiresMeisterhauPlugin.CurrentSource = __state;
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class PlayerProjectileSourcePatch
    {
        private static void Prefix(Character ___m_owner, ItemDrop.ItemData ___m_weapon, ref AttackSourceContext __state)
        {
            __state = WiresMeisterhauPlugin.CurrentSource;
            if (___m_owner == null || ___m_owner != Player.m_localPlayer || ___m_weapon == null) return;
            WiresMeisterhauPlugin.CurrentSource = new AttackSourceContext
            {
                Weapon = ___m_weapon,
                Attack = null,
                IsProjectile = true
            };
        }

        private static void Postfix(AttackSourceContext __state)
        {
            WiresMeisterhauPlugin.CurrentSource = __state;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class RemoteConnectedHitPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player local = Player.m_localPlayer;
            if (local == null || __instance == null || __instance == local || hit == null || hit.GetAttacker() != local) return;
            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            // On a remote-owned character ApplyDamage runs on its owner, not on this client.
            // Character.Damage is reached only after the player's real melee/projectile collision connected.
            WiresMeisterhauPlugin.RegisterActualPlayerHit(__instance, hit, Mathf.Max(0.001f, hit.GetTotalDamage()));
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class ActualHitPatch
    {
        private static void Prefix(Character __instance, ref float __state)
        {
            __state = __instance.GetHealth();
        }

        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            Player player = Player.m_localPlayer;
            if (player == null || __instance == player || hit == null) return;
            float damageDone = Mathf.Max(0f, __state - __instance.GetHealth());
            if (damageDone <= 0f) return;
            WiresMeisterhauPlugin.RegisterActualPlayerHit(__instance, hit, damageDone);
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
    internal static class OffensiveCounterSkillPatch
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, ref float __result)
        {
            Player local = Player.m_localPlayer;
            Skills.SkillType active = WiresMeisterhauPlugin.ActiveCounterSkill;
            if (!WiresMeisterhauPlugin.InOffensiveBlock || local == null || __instance != local ||
                skill != Skills.SkillType.Blocking || active == Skills.SkillType.None || active == Skills.SkillType.Blocking)
                return true;

            __result = __instance.GetSkillFactor(active);
            return false;
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyTimedBlockBonus))]
    internal static class RangedInterruptTimedBonusPatch
    {
        private static bool Prefix()
        {
            return !(WiresMeisterhauPlugin.InOffensiveBlock && WiresMeisterhauPlugin.ActiveCounterIsRanged);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class SkillXpPatch
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, float value)
        {
            Player local = Player.m_localPlayer;
            if (local == null || __instance != local) return true;

            if (WiresMeisterhauPlugin.InOffensiveBlock && skill == Skills.SkillType.Blocking)
                return false;

            if (!WiresMeisterhauPlugin.GrantingBonusXp)
            {
                AttackSourceContext source = WiresMeisterhauPlugin.CurrentSource;
                if (source != null)
                {
                    foreach (HitRecord record in source.Records)
                        if (record != null && record.Skill == skill && record.NormalXp <= 0f)
                            record.NormalXp = value;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Stagger))]
    internal static class OffensiveStaggerPatch
    {
        private static void Prefix(Character __instance)
        {
            if (WiresMeisterhauPlugin.InOffensiveBlock && __instance == WiresMeisterhauPlugin.ActiveParryTarget)
                WiresMeisterhauPlugin.OffensiveParryStaggerSeen = true;
        }
    }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class CounterEffectPitchPatch
    {
        private static void Postfix(GameObject[] __result)
        {
            if (!WiresMeisterhauPlugin.InOffensiveBlock || __result == null) return;
            float pitch = Mathf.Max(0.1f, WiresMeisterhauPlugin.CounterSoundPitch.Value);

            foreach (GameObject go in __result)
            {
                if (go == null) continue;
                AudioSource[] sources = go.GetComponentsInChildren<AudioSource>(true);
                foreach (AudioSource source in sources)
                    if (source != null) source.pitch *= pitch;
            }
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class IncomingMeleeCounterPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = __instance as Player;
            if (player == null || player != Player.m_localPlayer || hit == null) return;
            if (!WiresMeisterhauPlugin.Enabled.Value || hit.m_ranged || player.IsBlocking()) return;

            Character attacker = hit.GetAttacker();
            if (attacker == null) return;
            HitRecord record = WiresMeisterhauPlugin.GetRecentHit(attacker);
            if (record == null) return;

            WiresMeisterhauPlugin.TryOffensiveParry(player, hit, attacker, record, false);
        }
    }
    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class MeisterhauNetworkRpcPatch
    {
        private static void Postfix()
        {
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.Register<ZPackage>(WiresMeisterhauPlugin.RangedWindupRpc, WiresMeisterhauPlugin.RPC_RangedWindup);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class EnemyRangedWindupStartPatch
    {
        private static void Postfix(Attack __instance, Humanoid character, bool __result)
        {
            if (!__result || character == null || character.IsPlayer() || !WiresMeisterhauPlugin.IsRangedAttack(__instance)) return;
            if (character.IsBoss()) return;

            HitData hit = WiresMeisterhauPlugin.BuildEnemyRangedAttackHit(character, __instance);
            if (hit == null) return;
            WiresMeisterhauPlugin.RangedWindups[character.GetInstanceID()] = new RangedWindupState
            {
                Owner = character,
                Attack = __instance,
                IncomingHit = hit,
                NetworkId = WiresMeisterhauPlugin.GetCharacterZdoId(character),
                StartedAt = Time.time
            };
            WiresMeisterhauPlugin.BroadcastRangedWindup(character, __instance, true);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class EnemyRangedWindupEndPatch
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character)
        {
            WiresMeisterhauPlugin.EndRangedWindup(___m_character, __instance);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
    internal static class EnemyRangedWindupStopPatch
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character)
        {
            WiresMeisterhauPlugin.EndRangedWindup(___m_character, __instance);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Abort))]
    internal static class EnemyRangedWindupAbortPatch
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character)
        {
            WiresMeisterhauPlugin.EndRangedWindup(___m_character, __instance);
        }
    }

}

