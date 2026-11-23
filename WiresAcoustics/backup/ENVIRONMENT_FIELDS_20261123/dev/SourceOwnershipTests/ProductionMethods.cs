using UnityEngine;
namespace WiresAcoustics {
internal static class Spots {
 private static readonly Collider[] RootNear=new Collider[64];
 private static int Mask=>1;
private static Transform PieceAt(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p, .5f, RootNear, Mask,
                                                 QueryTriggerInteraction.Ignore);
            // If the result buffer filled, another same-pivot owner may have
            // been omitted. Leave the source at its authored position.
            if (n == RootNear.Length) return null;
            Transform found = null;
            for (int i = 0; i < n; i++)
            {
                Collider c = RootNear[i];
                if (c == null) continue;
                ZNetView nv = c.GetComponentInParent<ZNetView>();
                Transform root = nv != null ? nv.transform : c.transform.root;
                if ((root.position - p).sqrMagnitude >= 1e-4f ||
                    root.GetComponent<Character>() != null) continue;
                // Do not silently attach world ambience, creatures or
                // arbitrary effects to a nearby unrelated networked prop.
                if (root.GetComponent<Piece>() == null &&
                    root.GetComponent<Container>() == null &&
                    root.GetComponent<Fireplace>() == null &&
                    root.GetComponent<Door>() == null &&
                    root.GetComponent<CraftingStation>() == null) continue;
                if (found != null && found != root) return null;
                found = root;
            }
            return found;
        }
 internal static Transform Choose(Vector3 at)=>PieceAt(at);
}}