using System;

namespace UnityEngine
{
    internal sealed class GameObject { internal string name = ""; }
}
namespace WiresAcoustics
{
    internal sealed class Piece
    {
        internal readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        internal Piece(string name) { gameObject.name = name; }
    }
}
internal static class Test
{
    private static int checks;
    private static void Check(string prefab, float expected)
    {
        float actual = WiresAcoustics.OpenPanelArea.FromPrefab(new WiresAcoustics.Piece(prefab));
        if (Math.Abs(actual - expected) > 0.00001f)
            throw new Exception(prefab + " expected " + expected + " got " + actual);
        checks++;
    }

    private static void Main()
    {
        Check("iron_grate", .70f);
        Check("wood_fence_gate", .55f);
        Check("wood_window", .55f);
        Check("darkwood_decowall", .50f);
        Check("ashwood_decowall_divider", .55f);
        Check("ashwood_decowall_2x2", .55f);
        Check("piece_dvergr_metal_wall_2x2", .50f);
        Check("iron_grate(Clone)", .70f);
        Check("unknown_modded_panel", 0f);
        Check("stone_wall_2x2", 0f);
        if (WiresAcoustics.OpenPanelArea.FromPrefab(null) != 0f)
            throw new Exception("Null prefab should yield no open area");
        checks++;
        Console.WriteLine("STATIC VALHEIM ACOUSTIC PREFAB PROPERTIES PASS: " + checks);
    }
}
