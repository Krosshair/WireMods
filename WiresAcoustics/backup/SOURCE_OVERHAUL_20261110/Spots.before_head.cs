using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Where a sound is made. The game often creates a sound at whatever position it has at hand: a
    // character's root (its feet) for weapon swings (3,387 of ~3,400 attacks have no origin joint),
    // holstering, picking up, dropping and eating; a piece's pivot (a door's hinge, a smelter's or
    // fermenter's base) for its own sounds. Those are moved, before anything else uses their position,
    // to where the thing actually happens:
    //  - weapon swing, trigger and trail sounds: the weapon hand, attached to it (they follow the swing);
    //  - holstering: the back slot the item goes to (armour: the chest); dropping: the right hand;
    //    eating and drinking: the head; picking up: where the item was;
    //  - a piece's own sounds: a chest's lid, a fire's flame, otherwise the middle of the piece.
    // Sounds the game already places (footsteps at the foot, hits at the hit point, equipping at the
    // hand, projectiles at their spawn point) are left alone.
    internal static class Spots
    {
        // Set around the game's item calls (Equip), for sounds made at that character's root.
        internal static Transform Owner, Bone;
        internal static Vector3 Point;
        internal static bool HasPoint;

        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack =
            AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly Collider[] Near = new Collider[16];
        private static int s_mask;
        private static int Mask => s_mask != 0 ? s_mask
            : (s_mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "Default_small", "static_solid", "vehicle"));

        internal static void Clear() { Owner = Bone = null; HasPoint = false; }

        // EffectList.Create postfix (Follow), before the sounds are followed or measured. May set the owner.
        internal static void Place(EffectList list, GameObject[] made, Vector3 basePos, Transform baseParent, ref Transform given)
        {
            Transform root = baseParent != null ? baseParent : given;
            if (root != null && (basePos - root.position).sqrMagnitude < 1e-4f)
            {
                Humanoid h = root.GetComponent<Humanoid>();
                if (h == null) return;
                Transform hand = WeaponHand(h, list);
                if (hand != null) { Move(made, root, hand.position, hand); Note("weapon hand " + hand.name, made); return; }
                if (Owner != root) return;
                if (Bone != null) { Move(made, root, Bone.position, Bone); Note(Bone.name, made); }
                else if (HasPoint) { Move(made, root, Point, null); Note("item spot", made); }
                return;
            }
            if (given != null || baseParent != null) return;
            Transform piece = PieceAt(basePos);
            if (piece == null) return;
            given = piece;
            Vector3 at;
            bool known = piece.GetComponent<Container>() != null ? Area.TopOf(piece, out at)
                : piece.GetComponent<Fireplace>() != null ? Area.FlameOf(piece, out at)
                : Area.CenterOf(piece, out at);
            if (!known) return;
            Move(made, null, at, null);
            Note(piece.name + (piece.GetComponent<Container>() != null ? " lid" : piece.GetComponent<Fireplace>() != null ? " flame" : " middle"), made);
        }

        private static int s_noted;
        private static void Note(string where, GameObject[] made)
        {
            if (!Plugin.DebugLog.Value || s_noted >= 40) return;
            foreach (GameObject go in made)
                if (go != null && go.GetComponent<ZSFX>() != null)
                {
                    s_noted++;
                    Plugin.Log.LogInfo("[spots] " + go.name + " -> " + where);
                    return;
                }
        }

        // The hand holding the weapon whose swing, trigger or trail sound this is, or null.
        private static Transform WeaponHand(Humanoid h, EffectList list)
        {
            Attack a = CurrentAttack(h);
            ItemDrop.ItemData w = a?.GetWeapon();
            if (w == null) return null;
            ItemDrop.ItemData.SharedData s = w.m_shared;
            if (list != a.m_startEffect && list != a.m_triggerEffect && list != a.m_trailStartEffect &&
                list != s.m_startEffect && list != s.m_triggerEffect && list != s.m_trailStartEffect) return null;
            VisEquipment vis = h.GetComponent<VisEquipment>();
            if (vis == null) return null;
            return w == h.LeftItem ? vis.m_leftHand : vis.m_rightHand;
        }

        // A sound object made at the root (unparented, or attached to the root): moved, and attached to bone if given.
        private static void Move(GameObject[] made, Transform root, Vector3 at, Transform bone)
        {
            foreach (GameObject go in made)
            {
                if (go == null || go.GetComponent<ZSFX>() == null) continue;
                Transform t = go.transform;
                if (t.parent != null && t.parent != root) continue;
                t.position = at;
                if (bone == null) continue;
                t.SetParent(bone, true);
                // attached now, so Follow skips it: an unplaced (2D) player sound is placed here instead
                PlayerSounds.Place(go.GetComponent<ZSFX>(), go.GetComponent<AudioSource>());
            }
        }

        // The piece (or other networked object) whose pivot is exactly here.
        private static Transform PieceAt(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p, .5f, Near, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = Near[i];
                if (c == null) continue;
                ZNetView nv = c.GetComponentInParent<ZNetView>();
                Transform r = nv != null ? nv.transform : c.transform.root;
                if ((r.position - p).sqrMagnitude < 1e-4f && r.GetComponent<Character>() == null) return r;
            }
            return null;
        }

        // The back slot an item goes to when put away (VisEquipment.AttachBackItem), or null.
        internal static Transform BackSlot(VisEquipment vis, ItemDrop.ItemData item)
        {
            if (vis == null || item == null) return null;
            ItemDrop.ItemData.SharedData s = item.m_shared;
            switch (s.m_attachOverride != ItemDrop.ItemData.ItemType.None ? s.m_attachOverride : s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Torch: return vis.m_backTool;
                case ItemDrop.ItemData.ItemType.Bow: return vis.m_backBow;
                case ItemDrop.ItemData.ItemType.Tool: return vis.m_backTool;
                case ItemDrop.ItemData.ItemType.Attach_Atgeir: return vis.m_backAtgeir;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: return vis.m_backMelee;
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return vis.m_backTwohandedMelee;
                case ItemDrop.ItemData.ItemType.Shield: return vis.m_backShield;
                default: return null;
            }
        }
    }
}
