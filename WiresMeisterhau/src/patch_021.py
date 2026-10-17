from pathlib import Path
p = Path(r'C:\Users\kajet\WiresMeisterhauBuild\src\WiresMeisterhauPlugin.cs')
backup = Path(r'C:\Users\kajet\WiresMeisterhauBuild\src\WiresMeisterhauPlugin.0.2.0.cs.bak')
s = backup.read_text(encoding='utf-8-sig')
s = s.replace('public const string ModVersion = "0.2.0";', 'public const string ModVersion = "0.2.1";')
s = s.replace('CounterSoundPitch = Config.Bind("Feedback", "CounterSoundPitch", 1.25f,', 'CounterSoundPitch = Config.Bind("Feedback", "CounterSoundPitch", 1.50f,')
s = s.replace('internal static readonly Dictionary<int, HitRecord> RecentHits = new Dictionary<int, HitRecord>();',
'''internal static readonly Dictionary<int, HitRecord> RecentHits = new Dictionary<int, HitRecord>();
        internal static readonly Dictionary<int, RangedWindupState> RangedWindups = new Dictionary<int, RangedWindupState>();''')
s = s.replace('private static readonly FieldInfo CurrentChainField = AccessTools.Field(typeof(Attack), "m_currentAttackCainLevel");',
'''private static readonly FieldInfo CurrentChainField = AccessTools.Field(typeof(Attack), "m_currentAttackCainLevel");
        private static readonly FieldInfo CurrentAttackField = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
        private static readonly FieldInfo ProjectileAttackStartedField = AccessTools.Field(typeof(Attack), "m_projectileAttackStarted");''')
s = s.replace('private static readonly MethodInfo GetStaggerThresholdMethod = AccessTools.Method(typeof(Character), "GetStaggerTreshold");',
'''private static readonly MethodInfo GetStaggerThresholdMethod = AccessTools.Method(typeof(Character), "GetStaggerTreshold");
        private static readonly MethodInfo ModifyDamageMethod = AccessTools.Method(typeof(Attack), "ModifyDamage", new[] { typeof(HitData), typeof(float) });''')
needle = '''        internal static bool IsUsableCounterWeapon(ItemDrop.ItemData weapon)
        {
            return weapon != null && weapon.m_shared != null && weapon.m_shared.m_timedBlockBonus > 1f;
        }
'''
insert = needle + '''
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
'''
s = s.replace(needle, insert)
s += '' if s.endswith('\n') else '\n'
extra = '''
            bool projectileStarted = false;
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
'''
marker = '        internal static bool WouldPerfectParrySucceed(Player player, HitData incoming, HitRecord record)'
pos = s.find(marker)
if pos < 0: raise SystemExit('marker 1 missing')
s = s[:pos] + extra + '\n' + s[pos:]
extra2 = '''
            float skillFactor = state.Owner.GetRandomSkillFactor(enemyWeapon.m_shared.m_skillType);
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
'''
marker = '        internal static bool WouldPerfectParrySucceed(Player player, HitData incoming, HitRecord record)'
pos = s.find(marker)
s = s[:pos] + extra2 + '\n' + s[pos:]
old = '''            RecentHits[target.GetInstanceID()] = record;
            source.Records.Add(record);
        }
'''
new = '''            RecentHits[target.GetInstanceID()] = record;
            source.Records.Add(record);

            TryRangedWindupInterrupt(target, record);
        }
'''
if old not in s: raise SystemExit('register block missing')
s = s.replace(old, new, 1)
class_marker = '''    internal sealed class PendingXp
    {
        internal HitRecord Record;
        internal float ResolveAt;
    }
'''
class_insert = class_marker + '''
    internal sealed class RangedWindupState
    {
        internal Humanoid Owner;
        internal Attack Attack;
        internal float StartedAt;
    }
'''
if class_marker not in s: raise SystemExit('class marker missing')
s = s.replace(class_marker, class_insert, 1)
old_patch_start = s.find('    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]')
if old_patch_start < 0: raise SystemExit('old projectile patch missing')
old_patch_end = s.find('\n    }\n}', old_patch_start)
if old_patch_end < 0: raise SystemExit('old projectile patch end missing')
old_patch_end += len('\n    }')
new_patches = '''    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
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

    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
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
'''
s = s[:old_patch_start] + new_patches + s[old_patch_end:]
p.write_text(s, encoding='utf-8')
print('patched', p)
