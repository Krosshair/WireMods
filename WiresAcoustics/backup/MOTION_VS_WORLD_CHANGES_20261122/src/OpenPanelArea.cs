using System;

namespace WiresAcoustics
{
    // Authored Valheim acoustic prefab properties.
    // These are content-tuning values, NOT runtime geometry measurements
    // or published acoustic constants. Author/revise offline after visual
    // inspection and listening tests. Do not scan meshes in gameplay.
    internal static class OpenPanelArea
    {
        internal static float FromPrefab(Piece piece)
        {
            if (piece == null) return 0f;
            string name = piece.gameObject.name;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7);

            switch (name)
            {
                case "iron_grate": return 0.70f;
                case "wood_fence_gate": return 0.55f;
                case "wood_window": return 0.55f;
                case "darkwood_decowall": return 0.50f;
                case "ashwood_decowall_divider": return 0.55f;
                case "ashwood_decowall_2x2": return 0.55f;
                case "piece_dvergr_metal_wall_2x2": return 0.50f;
                default: return 0f;
            }
        }
    }
}
