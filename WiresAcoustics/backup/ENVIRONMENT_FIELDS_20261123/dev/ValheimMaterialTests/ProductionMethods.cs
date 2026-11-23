using UnityEngine; namespace WiresAcoustics { internal static class Solids { internal sealed class Info {internal bool Structural,AcousticProxy,Wall;internal float OpenFraction,First,Cut;}internal static bool IsCoarseOpenPanel(Collider c, Piece p)
        {
            if (p == null || !(c is BoxCollider box)) return false;
            string name = p.gameObject.name;
            if (name.EndsWith("(Clone)", System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7);
            // Dividers: full-size box proxies; no usable separate bar
            // colliders in these shipped prefabs.
            switch (name)
            {
                case "darkwood_decowall":
                case "ashwood_decowall_divider":
                case "ashwood_decowall_2x2":
                case "piece_dvergr_metal_wall_2x2":
                    return true;
                // Iron grate: central ~1.88 x 2.78m slab is a gameplay
                // collider. Narrow <0.1m iron frame colliders remain active.
                case "iron_grate":
                    return box.size.x > 1.5f && box.size.y > 2f;
                // Wooden fence gate: central ~1.75 x 1.37m gameplay
                // proxy. Narrow support posts must remain acoustic solids.
                case "wood_fence_gate":
                    return box.size.x > 1.5f && box.size.y > 1.2f;
                // The Valheim wooden window has collision covering the
                // aperture; it is not a full wood wall acoustically.
                case "wood_window":
                    return true;
            }
            return false;
        }} internal static class Occlusion {internal static void TransmissionEnergy(WearNTear.MaterialType material,
            out float low, out float mid)
        {
            switch (material)
            {
                case WearNTear.MaterialType.Stone:
                case WearNTear.MaterialType.Ashstone:
                case WearNTear.MaterialType.Marble:
                    low = .015f; mid = .002f; return; // rock
                case WearNTear.MaterialType.Iron:
                    low = .200f; mid = .025f; return; // metal
                case WearNTear.MaterialType.Ice:
                    low = .060f; mid = .044f; return; // glass proxy
                case WearNTear.MaterialType.Wood:
                case WearNTear.MaterialType.HardWood:
                case WearNTear.MaterialType.Timberwood:
                case WearNTear.MaterialType.Ancient:
                    low = .070f; mid = .014f; return; // wood
                default:
                    low = .100f; mid = .050f; return; // Steam Audio generic
            }
        }private static void Loss(Solids.Info s, WearNTear part)
        {
            if (part == null || !s.Structural) return;
            TransmissionEnergy(part.m_materialType, out float low, out float mid);
            if (s.AcousticProxy)
            {
                // ASHRAE area-weighted composite transmission by ENERGY.
                // Free aperture fraction phi has unit transmission.
                float phi = Mathf.Clamp01(s.OpenFraction);
                low = phi + (1f - phi) * low;
                mid = phi + (1f - phi) * mid;
            }
            SpectralPaths.Decode(low, mid, out s.First, out s.Cut);
            s.Wall = true;
        }internal static void TestLoss(Solids.Info s, WearNTear part) {Loss(s,part);} } }