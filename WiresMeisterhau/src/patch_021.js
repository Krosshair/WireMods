import fs from 'fs';
const p = 'C:\\Users\\kajet\\WiresMeisterhauBuild\\src\\WiresMeisterhauPlugin.cs';
const backup = 'C:\\Users\\kajet\\WiresMeisterhauBuild\\src\\WiresMeisterhauPlugin.0.2.0.cs.bak';
let s = fs.readFileSync(backup, 'utf8').replace(/^\uFEFF/, '');
s = s.replace('public const string ModVersion = "0.2.0";', 'public const string ModVersion = "0.2.1";');
s = s.replace('CounterSoundPitch = Config.Bind("Feedback", "CounterSoundPitch", 1.25f,', 'CounterSoundPitch = Config.Bind("Feedback", "CounterSoundPitch", 1.50f,');
s = s.replace(
'internal static readonly Dictionary<int, HitRecord> RecentHits = new Dictionary<int, HitRecord>();',
`internal static readonly Dictionary<int, HitRecord> RecentHits = new Dictionary<int, HitRecord>();
        internal static readonly Dictionary<int, RangedWindupState> RangedWindups = new Dictionary<int, RangedWindupState>();`);
s = s.replace(
'private static readonly FieldInfo CurrentChainField = AccessTools.Field(typeof(Attack), "m_currentAttackCainLevel");',
`private static readonly FieldInfo CurrentChainField = AccessTools.Field(typeof(Attack), "m_currentAttackCainLevel");
        private static readonly FieldInfo CurrentAttackField = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
        private static readonly FieldInfo ProjectileAttackStartedField = AccessTools.Field(typeof(Attack), "m_projectileAttackStarted");`);
s = s.replace(
'private static readonly MethodInfo GetStaggerThresholdMethod = AccessTools.Method(typeof(Character), "GetStaggerTreshold");',
`private static readonly MethodInfo GetStaggerThresholdMethod = AccessTools.Method(typeof(Character), "GetStaggerTreshold");
        private static readonly MethodInfo ModifyDamageMethod = AccessTools.Method(typeof(Attack), "ModifyDamage", new[] { typeof(HitData), typeof(float) });`);
const weaponNeedle = `        internal static bool IsUsableCounterWeapon(ItemDrop.ItemData weapon)
        {
            return weapon != null && weapon.m_shared != null && weapon.m_shared.m_timedBlockBonus > 1f;
        }
`;
const weaponInsert = weaponNeedle + `
        internal static bool IsRangedAttack(Attack attack)
        {
            if (attack == null) return false;
            return attack.m_attackType == Attack.AttackType.Projectile
                || attack.m_attackType == Attack.AttackType.TriggerProjectile
                || attack.m_attackProjectile != null;
        }

        internal static RangedWindupState GetActiveRangedWindup(Character target)
        {
            Humanoid humanoid = target as Humanoid;
            if (humanoid == null || CurrentAttackField == null) return null;
            RangedWindupState state;
            if (!RangedWindups.TryGetValue(target.GetInstanceID(), out state) || state == null) return null;
            Attack current = CurrentAttackField.GetValue(humanoid) as Attack;
            if (current != state.Attack || current == null || !target.InAttack())
            {
                RangedWindups.Remove(target.GetInstanceID());
                return null;
            }
`;
if (!s.includes(weaponNeedle)) throw new Error('weapon marker missing');
s = s.replace(weaponNeedle, weaponInsert);
const windupTail = `            bool projectileStarted = false;
            if (ProjectileAttackStartedField != null)
            {
                object value = ProjectileAttackStartedField.GetValue(current);
                if (value is bool) projectileStarted = (bool)value;
            }
            if (projectileStarted)
            {
                RangedWindups.Remove(target.GetInstanceID());
                return null;
            }
            return state;
        }

        internal static HitData BuildRangedWindupHit(RangedWindupState state, Player player)
        {
            if (state == null || state.Attack == null || state.Owner == null || player == null) return null;
            ItemDrop.ItemData enemyWeapon = state.Attack.GetWeapon();
            if (enemyWeapon == null || enemyWeapon.m_shared == null || !enemyWeapon.m_shared.m_blockable) return null;

            HitData hit = new HitData();
            hit.m_damage = enemyWeapon.GetDamage();
            hit.m_blockable = enemyWeapon.m_shared.m_blockable;
            hit.m_dodgeable = enemyWeapon.m_shared.m_dodgeable;
            hit.m_staggerMultiplier = state.Attack.m_staggerMultiplier;
            hit.m_skill = enemyWeapon.m_shared.m_skillType;
            hit.SetAttacker(state.Owner);
`;
const marker1 = '        internal static bool WouldPerfectParrySucceed(Player player, HitData incoming, HitRecord record)';
let pos = s.indexOf(marker1);
if (pos < 0) throw new Error('marker1 missing');
s = s.slice(0, pos) + windupTail + '\n' + s.slice(pos);
const windupMethods = `            float skillFactor = state.Owner.GetRandomSkillFactor(enemyWeapon.m_shared.m_skillType);
            if (ModifyDamageMethod != null)
                ModifyDamageMethod.Invoke(state.Attack, new object[] { hit, skillFactor });

            Vector3 dir = player.transform.position - state.Owner.transform.position;
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
            HitData incoming = BuildRangedWindupHit(state, player);
            if (incoming == null) return false;

            bool success = TryOffensiveParry(player, incoming, target, record, true, incoming.m_dir);
            if (success) RangedWindups.Remove(target.GetInstanceID());
            return success;
        }

`;
pos = s.indexOf(marker1);
if (pos < 0) throw new Error('marker1 missing 2');
s = s.slice(0, pos) + windupMethods + s.slice(pos);
const regOld = `            RecentHits[target.GetInstanceID()] = record;
            source.Records.Add(record);
        }
`;
const regNew = `            RecentHits[target.GetInstanceID()] = record;
            source.Records.Add(record);

            TryRangedWindupInterrupt(target, record);
        }
`;
if (!s.includes(regOld)) throw new Error('register block missing');
s = s.replace(regOld, regNew);
const classMarker = `    internal sealed class PendingXp
    {
        internal HitRecord Record;
        internal float ResolveAt;
    }
`;
const classInsert = classMarker + `
    internal sealed class RangedWindupState
    {
        internal Humanoid Owner;
        internal Attack Attack;
        internal float StartedAt;
    }
`;
if (!s.includes(classMarker)) throw new Error('class marker missing');
s = s.replace(classMarker, classInsert);
const oldPatchStart = s.indexOf('    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]');
if (oldPatchStart < 0) throw new Error('old projectile patch missing');
const oldPatchEnd = s.indexOf('\n    }\n}', oldPatchStart);
if (oldPatchEnd < 0) throw new Error('old projectile patch end missing');
const newPatches = `    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class EnemyRangedWindupStartPatch
    {
        private static void Postfix(Attack __instance, Humanoid character, bool __result)
        {
            Player local = Player.m_localPlayer;
            if (!__result || character == null || character == local || !WiresMeisterhauPlugin.IsRangedAttack(__instance)) return;
            if (character.IsBoss()) return;
            WiresMeisterhauPlugin.RangedWindups[character.GetInstanceID()] = new RangedWindupState
            {
                Owner = character,
                Attack = __instance,
                StartedAt = Time.time
            };
        }
    }

`;
const newPatches2 = `    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class EnemyRangedWindupEndPatch
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character)
        {
            if (___m_character == null || ___m_character == Player.m_localPlayer) return;
            RangedWindupState state;
            if (!WiresMeisterhauPlugin.RangedWindups.TryGetValue(___m_character.GetInstanceID(), out state)) return;
            if (state != null && state.Attack == __instance)
                WiresMeisterhauPlugin.RangedWindups.Remove(___m_character.GetInstanceID());
        }
    }
`;
const endIndex = oldPatchEnd + '\n    }'.length;
s = s.slice(0, oldPatchStart) + newPatches + newPatches2 + s.slice(endIndex);
fs.writeFileSync(p, s, 'utf8');
console.log('patched', p);
