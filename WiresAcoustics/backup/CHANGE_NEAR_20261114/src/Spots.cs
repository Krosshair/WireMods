using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Where a sound is made. The game often creates a sound at whatever position it has at hand: a
    // character's root (its feet) for weapon swings (3,387 of ~3,400 attacks have no origin joint),
    // holstering, picking up, dropping and eating; a piece's pivot (a door's base, a smelter's or
    // fermenter's base) for its own sounds. Those are moved, before anything else uses their position,
    // to where the thing actually happens:
    //  - weapon swing, trigger and trail sounds: the weapon hand, attached to it (they follow the swing);
    //  - a creature's idle and alert calls: its head (m_eye). An attack's sounds each by what they are
    //    (the game's names for them): voices, bites and spits at the head, swings, swipes and claws at
    //    the weapon or right hand (no hand: the head), stomps, slams and impacts left at the feet;
    //  - holstering: the back slot the item goes to (armour: the chest); dropping: the right hand;
    //    eating and drinking: the head; picking up: where the item was;
    //  - a piece's own sounds: a door's hinge, a chest's lid, a fire's flame, otherwise the middle of the piece; a
    //    crafting station's crafting sounds (played at the player): the station.
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
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightShown =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftShown =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly Collider[] Near = new Collider[16];
        private static int s_mask;
        private static int Mask => s_mask != 0 ? s_mask
            : (s_mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "Default_small", "static_solid", "vehicle"));

        internal static void Clear() { Owner = Bone = null; HasPoint = false; }

        // EffectList.Create postfix (Follow), before the sounds are followed or measured. May set the owner.
        internal static void Place(EffectList list, GameObject[] made, Vector3 basePos, Transform baseParent, ref Transform given)
        {
            Transform root = baseParent != null ? baseParent : given;
            // made at a character's pivot with no owner given (pets, animals jumping, a player's tool):
            // that character is its owner, so it follows it
            if (root == null && (root = CharacterAt(basePos)) != null) given = root;
            if (root != null && (basePos - root.position).sqrMagnitude < 1e-4f)
            {
                Character ch = root.GetComponent<Character>();
                if (ch == null) return;
                // crafting: the game plays the station's own crafting sounds at the player
                if (ch is global::Player pl && pl.GetCurrentCraftingStation() is CraftingStation st &&
                    (list == st.m_craftItemEffects || list == st.m_craftItemDoneEffects || list == st.m_craftItemDoneFailEffects))
                {
                    given = st.transform;
                    if (Area.CenterOf(st.transform, out Vector3 mid)) { Move(made, root, mid, null); Note(st.name + " middle", made); }
                    return;
                }
                BaseAI ai = root.GetComponent<BaseAI>();
                MonsterAI mai = ai as MonsterAI;
                bool call = ai != null && (list == ai.m_idleSound || list == ai.m_alertedEffects ||
                    (mai != null && (list == mai.m_wakeupEffects || list == mai.m_sleepEffects)));
                Humanoid h = ch as Humanoid;
                if (call || (h != null && (Attacking(h, list) || ItemSound(h, list) != null)))
                {
                    // one attack often plays a voice and a swing together: each goes where it's made
                    Transform hand = h != null ? WeaponHand(h, list) ?? ItemSound(h, list) : null;
                    Transform limb = hand ?? h?.GetComponent<VisEquipment>()?.m_rightHand;
                    Transform head = ch.m_eye;
                    foreach (GameObject go in made)
                    {
                        ZSFX z = go != null ? go.GetComponent<ZSFX>() : null;
                        if (z == null) continue;
                        Kind k = call ? Kind.Voice : KindOf(z);
                        Transform to = k == Kind.Voice ? head
                            : k == Kind.Limb ? (limb ?? head)
                            : k == Kind.Ground ? null
                            : hand ?? (h is global::Player ? limb : head); // anything else: the weapon, a player's fist, a creature's head
                        if (to == null) continue;
                        MoveOne(go, root, to.position, to);
                        Note(go, k + " -> " + to.name);
                    }
                    return;
                }
                if (h == null || Owner != root) return;
                if (Bone != null) { Move(made, root, Bone.position, Bone); Note(Bone.name, made); }
                else if (HasPoint) { Move(made, root, Point, null); Note("item spot", made); }
                return;
            }
            if (given != null || baseParent != null) return;
            Transform piece = PieceAt(basePos);
            if (piece == null) return;
            given = piece;
            Vector3 at;
            bool door = piece.GetComponent<Door>() != null;
            bool known = door ? Hinge(piece, out at)
                : piece.GetComponent<Container>() != null ? Area.TopOf(piece, out at)
                : piece.GetComponent<Fireplace>() != null ? Area.FlameOf(piece, out at)
                : Area.CenterOf(piece, out at);
            if (!known) return;
            Move(made, null, at, null);
            Note(piece.name + (door ? " hinge" : piece.GetComponent<Container>() != null ? " lid" : piece.GetComponent<Fireplace>() != null ? " flame" : " middle"), made);
        }

        private static int s_noted;
        private static void Note(string where, GameObject[] made)
        {
            foreach (GameObject go in made)
                if (go != null && go.GetComponent<ZSFX>() != null) { Note(go, where); return; }
        }

        private static void Note(GameObject go, string where)
        {
            if (!Plugin.DebugLog.Value || s_noted >= 60) return;
            s_noted++;
            Plugin.Log.LogInfo("[spots] " + go.name + " -> " + where);
        }

        // True if this is the start, trigger or trail sound of the character's current attack.
        private static bool Attacking(Humanoid h, EffectList list)
        {
            Attack a = CurrentAttack(h);
            ItemDrop.ItemData w = a?.GetWeapon();
            if (w == null) return false;
            ItemDrop.ItemData.SharedData s = w.m_shared;
            return list == a.m_startEffect || list == a.m_triggerEffect || list == a.m_trailStartEffect ||
                   list == s.m_startEffect || list == s.m_triggerEffect || list == s.m_trailStartEffect;
        }

        // The hand holding the weapon whose attack sound this is, or null (not an attack sound, or a
        // creature's built-in attack: no weapon in a hand).
        private static Transform WeaponHand(Humanoid h, EffectList list)
        {
            if (!Attacking(h, list)) return null;
            ItemDrop.ItemData w = CurrentAttack(h).GetWeapon();
            if (w != h.RightItem && w != h.LeftItem) return null;
            VisEquipment vis = h.GetComponent<VisEquipment>();
            if (vis == null) return null;
            // a creature's natural weapon (a bite, claws) has nothing shown in the hand
            bool left = w == h.LeftItem;
            if ((left ? LeftShown(vis) : RightShown(vis)) == null) return null;
            return left ? vis.m_leftHand : vis.m_rightHand;
        }

        // The hand of the item whose drawing (a bow) or building (a hammer) sound this is, or null.
        private static Transform ItemSound(Humanoid h, EffectList list)
        {
            ItemDrop.ItemData r = h.RightItem, l = h.LeftItem;
            bool right = r != null && (list == r.m_shared.m_holdStartEffect || list == r.m_shared.m_buildEffect);
            bool left = !right && l != null && (list == l.m_shared.m_holdStartEffect || list == l.m_shared.m_buildEffect);
            if (!right && !left) return null;
            VisEquipment vis = h.GetComponent<VisEquipment>();
            return vis == null ? null : right ? vis.m_rightHand : vis.m_leftHand;
        }

        private static Transform CharacterAt(Vector3 p)
        {
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && (c.transform.position - p).sqrMagnitude < 1e-4f) return c.transform;
            return null;
        }

        private enum Kind { Other, Voice, Limb, Ground }
        private static readonly string[] VoiceWords = { "vocal", "voice", "_vo_", "growl", "roar", "snarl", "scream", "shout", "grunt", "bite", "spit", "attacking", "taunt", "breath" };
        private static readonly string[] LimbWords = { "swing", "whoosh", "whsh", "swipe", "swish", "slash", "claw", "trail", "stab", "chain", "lift", "flurry", "whip" };
        private static readonly string[] GroundWords = { "stomp", "slam", "impact", "land", "jump" };

        // What a sound is, from the game's own names for it (the effect and its first clip).
        private static Kind KindOf(ZSFX z)
        {
            string n = z.name.ToLowerInvariant();
            if (z.m_audioClips != null && z.m_audioClips.Length > 0 && z.m_audioClips[0] != null) n += " " + z.m_audioClips[0].name.ToLowerInvariant();
            foreach (string w in VoiceWords) if (n.Contains(w)) return Kind.Voice;
            foreach (string w in LimbWords) if (n.Contains(w)) return Kind.Limb;
            foreach (string w in GroundWords) if (n.Contains(w)) return Kind.Ground;
            return Kind.Other;
        }

        // Sound objects made at the root (unparented, or attached to the root): moved, and attached to bone if given.
        private static void Move(GameObject[] made, Transform root, Vector3 at, Transform bone)
        {
            foreach (GameObject go in made)
                if (go != null && go.GetComponent<ZSFX>() != null) MoveOne(go, root, at, bone);
        }

        private static void MoveOne(GameObject go, Transform root, Vector3 at, Transform bone)
        {
            Transform t = go.transform;
            if (t.parent != null && t.parent != root) return;
            t.position = at;
            if (bone == null) return;
            if (Follow.Networked(go)) Follow.Track(t, bone); // never attach a networked object
            else t.SetParent(bone, true);
            // attached (or following) now, so Follow skips it: an unplaced (2D) player sound is placed here instead
            PlayerSounds.Place(go.GetComponent<ZSFX>(), go.GetComponent<AudioSource>());
        }

        // A door's sounds come from its hinge, half way up: the leaf turns there, so it's in the doorway
        // whether the door is open or shut, and the leaf swinging never ends up between you and its own
        // sound (from the middle of the door's box it could: the box of a door first seen open is out in
        // the room beside the doorway). The hinge is where the game turns the leaf (its "door" part),
        // brought 15 cm into the doorway so the frame beside it doesn't hide it (a judgement value). A
        // door whose moving part turns round the middle (a gate, a grate) keeps its middle.
        private static bool Hinge(Transform piece, out Vector3 at)
        {
            bool mid = Area.CenterOf(piece, out at);
            float y = mid ? at.y : piece.position.y + 1f;
            Transform leaf = null;
            foreach (Transform t in piece.GetComponentsInChildren<Transform>(true))
                if (t != piece && t.name == "door") { leaf = t; break; }
            if (leaf == null) return mid;
            Vector3 flat = leaf.position - piece.position; flat.y = 0f;
            if (flat.sqrMagnitude < .05f * .05f) return mid;
            Vector3 hinge = leaf.position - flat.normalized * Mathf.Min(.15f, flat.magnitude);
            at = new Vector3(hinge.x, y, hinge.z);
            return true;
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
