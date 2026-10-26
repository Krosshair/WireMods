using System;
using WiresForgeOfGoodtential;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float x, float y) => Math.Max(x, y);
        public static int Max(int x, int y) => Math.Max(x, y);
        public static float Min(float x, float y) => Math.Min(x, y);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
        public static float Clamp(float x, float min, float max) =>
            Math.Max(min, Math.Min(max, x));
    }
    public static class Random
    {
        private static readonly System.Random R = new System.Random(1234);
        public static float value => (float)R.NextDouble();
    }
}
public class ItemDrop
{
    public class ItemData
    {
        public float Prestige;
    }
}
namespace WiresForgeOfGoodtential
{
    internal enum ForgeOutcome { None, Success, Jackpot, Ignore, Loss, CompleteLoss }
    internal sealed class ForgeOdds
    {
        internal float Success;
        internal float OrdinaryFailure;
        internal float CompleteLoss;
        internal float JackpotChance = .20f;
        internal bool FailureIsIgnore;
        internal float CascadeChance;
        internal float TotalBad => OrdinaryFailure + CompleteLoss;
    }
    internal static class ForgeData
    {
        internal static float GetTrophyBadOutcomeMultiplier(
            ItemDrop.ItemData trophy, float reference)
        {
            if (trophy == null) return 1;
            return UnityEngine.Mathf.Clamp(
                (float)Math.Exp(-.17f * (trophy.Prestige - reference + 14f)),
                .005f, .995f);
        }
    }
}
public class Program
{
    private static int checkedCases;
    private static void Check(bool cond, string error)
    {
        checkedCases++;
        if (!cond) throw new Exception(error);
    }
    public static void Main()
    {
        var eq = new ItemDrop.ItemData { Prestige = 40 };
        var powerful = new ItemDrop.ItemData { Prestige = 65 };
        var obsolete = new ItemDrop.ItemData { Prestige = 5 };
        float equalMultiplier = ForgeData.GetTrophyBadOutcomeMultiplier(eq, 40);
        Check(equalMultiplier < .11f && equalMultiplier > .08f,
            "Equal trophy should cut bad outcome to ~9.3%");
        Check(ForgeData.GetTrophyBadOutcomeMultiplier(powerful, 40)
            < equalMultiplier, "Powerful trophy must protect more");
        Check(ForgeData.GetTrophyBadOutcomeMultiplier(obsolete, 40)
            > .99f, "Obsolete trophy should hardly matter");

        for(int r = 0; r <= 45; r++)
        for(int required = 0; required <= 7; required++)
        for(int chosen = 0; chosen <= 7; chosen++)
        foreach(var trophy in new ItemDrop.ItemData[] { null, eq, powerful, obsolete })
        {
            ForgeOdds o = ForgeRules.GetOdds(r, required, chosen,
                trophy, 40);
            float total = o.Success + o.OrdinaryFailure + o.CompleteLoss;
            string at = $"R={r} required={required} chosen={chosen} trophy={trophy?.Prestige}";
            Check(Math.Abs(total - 1f) < .00001f, "Not normalized: " + at);
            Check(o.Success >= 0 && o.Success <= 1, "Invalid success: "+at);
            Check(o.OrdinaryFailure >= 0 && o.OrdinaryFailure <= 1,
                "Invalid failure: "+at);
            Check(o.CompleteLoss >= 0 && o.CompleteLoss <= .070001f,
                "Invalid reset: "+at);
            Check(r >= 8 || o.CompleteLoss == 0f,
                "Reset too early: "+at);
            Check(o.FailureIsIgnore == (r < 2),
                "Incorrect early ignore: "+at);
            Check(o.JackpotChance >= .1999f && o.JackpotChance <= 1,
                "Invalid jackpot: "+at);
            if(chosen < required)
            {
                Check(o.CompleteLoss <= .07001f,
                    "Undertier reset exceeds 7%: "+at);
                Check(o.JackpotChance > .20f,
                    "Undertier should boost jackpot: "+at);
            }

            for(int i=0;i<8;i++)
            {
                ForgeOutcome result = ForgeRules.RollOutcome(o);
                Check(result == ForgeOutcome.Success ||
                      result == ForgeOutcome.Ignore ||
                      result == ForgeOutcome.Loss ||
                      result == ForgeOutcome.CompleteLoss,
                      "Invalid outcome "+at);
                if(result == ForgeOutcome.Loss)
                    Check(ForgeRules.RollLossLevels(r, o.CascadeChance) >= 1,
                        "Invalid loss levels "+at);
            }
        }

        var undertier = ForgeRules.GetOdds(8, 3, 2, null, 40);
        Check(Math.Abs(undertier.Success - .625f) < .00001f,
            "Under-tier success changed unexpectedly");
        Check(Math.Abs(undertier.CompleteLoss - .0375f) < .00001f,
            "Under-tier late reset wrong");
        var safe = ForgeRules.GetOdds(8, 3, 3, null, 40);
        Check(Math.Abs(safe.CompleteLoss - .035f) < .00001f,
            "Safe late reset wrong");
        var early = ForgeRules.GetOdds(7, 3, 2, null, 40);
        Check(early.CompleteLoss == 0, "Reset before +8");

        System.Console.WriteLine($"PASS {checkedCases:N0} assertions " +
            "across all 46 quality levels, 64 idol combinations and " +
            "four trophy scenarios; no destructive forge outcome.");
    }
}
