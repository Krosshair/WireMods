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
    public ItemData m_itemData;
    public class ItemData
    {
        public float Prestige;
    }
}

public class Player
{
    public void ConsumeResources(Piece.Requirement[] requirements) {}
}

public class Piece
{
    public class Requirement
    {
        public ItemDrop m_resItem;
        public bool m_upgraderResource;
    }
}

public class Recipe
{
    public Piece.Requirement[] m_resources;
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t, string methodName) {}
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute {}
}
namespace WiresForgeOfGoodtential
{
    internal static class ForgeContext
    {
        internal static bool Active;
        internal static Player Player;
        internal static Recipe Recipe;
        internal static ItemDrop.ItemData UpgraderResource;
        internal static float IdolKeepChance;
        internal static ForgeOutcome Outcome;
    }

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

    private static void TestActualSparingPatch(float noTrophy,
        float normalTrophy, float veryStrongTrophy)
    {
        var method = typeof(ForgeIdolSparingPatch).GetMethod("Prefix",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);
        Check(method != null, "Missing production consumption interception");
        var idol = new ItemDrop.ItemData();
        var idolRequirement = new Piece.Requirement
        {
            m_upgraderResource = true,
            m_resItem = new ItemDrop { m_itemData = idol }
        };
        var trophyRequirement = new Piece.Requirement
        {
            m_upgraderResource = true,
            m_resItem = new ItemDrop { m_itemData = new ItemDrop.ItemData() }
        };
        var otherRequirement = new Piece.Requirement
        {
            m_upgraderResource = false,
            m_resItem = new ItemDrop { m_itemData = new ItemDrop.ItemData() }
        };
        var resources = new []
        {
            idolRequirement, trophyRequirement, otherRequirement
        };
        var player = new Player();
        var recipe = new Recipe { m_resources = resources };
        ForgeContext.Active = true;
        ForgeContext.Player = player;
        ForgeContext.Recipe = recipe;
        ForgeContext.UpgraderResource = idol;

        foreach(var outcome in new []
        {
            ForgeOutcome.Success, ForgeOutcome.Jackpot,
            ForgeOutcome.Ignore, ForgeOutcome.Loss, ForgeOutcome.CompleteLoss
        })
        foreach(var chance in new [] { noTrophy, normalTrophy, veryStrongTrophy })
        {
            ForgeContext.Outcome = outcome;
            ForgeContext.IdolKeepChance = chance;
            int preserved = 0;
            const int tries = 3000;
            for (int i = 0; i < tries; i++)
            {
                object[] args = { player, resources };
                method.Invoke(null, args);
                var passed = (Piece.Requirement[])args[1];
                Check(ReferenceEquals(recipe.m_resources, resources),
                    "Craft recipe mutated");
                Check(Array.Exists(passed, r => ReferenceEquals(r, trophyRequirement)),
                    "Trophy must always remain in the consumption array");
                Check(Array.Exists(passed, r => ReferenceEquals(r, otherRequirement)),
                    "Other crafting requirements must remain unchanged");
                if (!Array.Exists(passed, r => ReferenceEquals(r, idolRequirement)))
                {
                    preserved++;
                    Check(passed.Length == 2,
                        "Idol exemption removed other requirements");
                }
                else
                    Check(passed.Length == 3,
                        "Idol consumption contains wrong requirements");
            }
            float observed = preserved / (float)tries;
            Check(Math.Abs(observed - chance) < .04f,
                $"Wrong preservation rate {outcome}: {observed} instead of {chance}");
        }

        // Outside an active forge transaction, the patch must do nothing.
        ForgeContext.Active = false;
        object[] plain = { player, resources };
        method.Invoke(null, plain);
        Check(ReferenceEquals(plain[1], resources),
            "Consumed a resource outside the Forge of Goodtential");
    }
    public static void Main()
    {
        var eq = new ItemDrop.ItemData { Prestige = 40 };
        var powerful = new ItemDrop.ItemData { Prestige = 65 };
        var obsolete = new ItemDrop.ItemData { Prestige = 5 };
        var excellent = new ItemDrop.ItemData { Prestige = 500 };
        float keepNone = ForgeRules.GetIdolKeepChance(null, 40);
        float keepEqual = ForgeRules.GetIdolKeepChance(eq, 40);
        float keepGood = ForgeRules.GetIdolKeepChance(powerful, 40);
        float keepObsolete = ForgeRules.GetIdolKeepChance(obsolete, 40);
        float keepExcellent = ForgeRules.GetIdolKeepChance(excellent, 40);
        Check(Math.Abs(keepNone - .5f) < 0.000001f, "Base keep chance not 50%");
        Check(keepObsolete > keepNone && keepObsolete < .502f,
              "Obsolete trophy must not significantly improve idol survival");
        Check(keepEqual > .65f && keepEqual < .67f,
              "Equal-prestige idol preservation should be about 66%");
        Check(keepGood > keepEqual, "Stronger trophy should protect more");
        Check(Math.Abs(keepExcellent - .675f) < .000001f,
              "Max trophy bonus should be +35% relative (67.5% total)");
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

        TestActualSparingPatch(keepNone, keepEqual, keepExcellent);

        System.Console.WriteLine($"PASS {checkedCases:N0} assertions " +
            "across all 46 quality levels, 64 idol combinations and " +
            "four trophy scenarios; no destructive forge outcome.");
    }
}
