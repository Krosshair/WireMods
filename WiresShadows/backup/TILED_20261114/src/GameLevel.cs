using System.Collections.Generic;
using UnityEngine;

namespace WiresShadows
{
    // The game's "Point light shadows" setting (GraphicsSettingsManager.GetPointLightShadowLimit
    // puts it in LightLod.m_shadowLimit): Off 0, Low 1, the default 3, the highest -1 (no limit).
    // Vanilla gives that many lights shadows, within each light's m_shadowDistance (20 m) of the
    // player, and redraws all six faces of each every frame.
    //  - Highest: the mod as it is.
    //  - Off: no point-light shadows from the mod either.
    //  - Low and default: cached shadows for every light within vanilla's distance (still things
    //    cost nothing per frame once drawn), and moving shadows get vanilla's own per-frame work:
    //    as many cube faces a frame as vanilla redraws (its limit x 6), nearest lights first.
    internal sealed partial class ShadowCache
    {
        private const float VanillaShadowDistance = 20f; // LightLod.m_shadowDistance's default, for lights without a LightLod

        private static int GameLimit => LightLod.m_shadowLimit;
        private static bool GameOff => GameLimit == 0;
        private static bool GameLimited => GameLimit > 0;

        // Past the distance vanilla gives this light shadows at, from the same point (the player).
        private static bool BeyondGame(Tracked t, Vector3 player, float margin)
        {
            if (!GameLimited || t.Light == null) return false;
            float reach = t.Lod != null ? t.Lod.m_shadowDistance : VanillaShadowDistance;
            return Vector3.Distance(t.Light.transform.position, player) >= reach + margin;
        }

        private static readonly List<int> BudgetOrder = new List<int>(32);
        private static Vector3 s_budgetCam;
        private static Tracked[] s_budgetSlots;
        private static readonly System.Comparison<int> BudgetNearer = (a, b) =>
            (s_budgetSlots[a].Center - s_budgetCam).sqrMagnitude.CompareTo((s_budgetSlots[b].Center - s_budgetCam).sqrMagnitude);

        // This frame's moving-shadow redraws held to vanilla's: limit x 6 faces, nearest lights
        // first. The rest keep what they show and try again next frame.
        private void HoldToGameBudget(List<int> candidates, int[] draw, Vector3 cam)
        {
            if (!GameLimited || candidates.Count == 0) return;
            int budget = GameLimit * 6, used = 0;
            BudgetOrder.Clear();
            BudgetOrder.AddRange(candidates);
            s_budgetCam = cam;
            s_budgetSlots = _slots;
            BudgetOrder.Sort(BudgetNearer);
            candidates.Clear();
            foreach (int sl in BudgetOrder)
            {
                int faces = 0;
                for (int d = draw[sl]; d != 0; d &= d - 1) faces++;
                if (faces > 0 && used + faces > budget) continue;
                used += faces;
                candidates.Add(sl);
            }
        }
    }
}
