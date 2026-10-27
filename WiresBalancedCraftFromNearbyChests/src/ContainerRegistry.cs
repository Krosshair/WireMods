using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class ContainerRegistry
    {
        private const float CacheSeconds = 0.25f;
        // One chest re-checked at a time (another player renaming it).
        private const float ReconcileIntervalSeconds = 0.25f;

        private static readonly HashSet<Container> Known =
            new HashSet<Container>();

        private static readonly List<Container> Reconcile =
            new List<Container>();

        private static readonly Queue<Container> PendingIndex =
            new Queue<Container>();

        private static readonly Dictionary<string, HashSet<Container>>
            ByRoute =
                new Dictionary<string, HashSet<Container>>(
                    StringComparer.Ordinal);

        private static readonly Dictionary<int, HashSet<string>> RoutesByContainer =
            new Dictionary<int, HashSet<string>>();

        private static readonly Dictionary<int, string> TitleByContainer =
            new Dictionary<int, string>();

        private static readonly Dictionary<ZDOID, Container> ByZdo =
            new Dictionary<ZDOID, Container>();

        private static readonly List<Container> Matching =
            new List<Container>();

        // Container.CheckAccess is private: called through a delegate, not
        // reflection, since it runs for every nearby container on refresh.
        private static readonly Func<Container, long, bool> CheckAccess =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(
                AccessTools.Method(
                    typeof(Container),
                    "CheckAccess",
                    new[] { typeof(long) }));

        // Bumped whenever the matching list is rebuilt, so callers caching
        // per-list results (InventoryBridge) know to start over.
        internal static int MatchingVersion;

        private static readonly Dictionary<Container, string> SortTitles =
            new Dictionary<Container, string>();

        private static float _nextRefresh;
        private static float _nextReconcile;
        private static int _reconcileCursor;
        private static bool _fullReindexPending;
        private static int _fullReindexCursor;
        private static string _cachedRouteId;
        private static Vector3 _cachedOrigin;
        private static float _cachedRange;

        internal static void Register(Container container)
        {
            if (container == null || !Known.Add(container))
                return;

            Reconcile.Add(container);
            PendingIndex.Enqueue(container);
            Plugin.Wake();

            ZNetView tracked = GetNetView(container);
            if (tracked == null)
                tracked = container.GetComponentInParent<ZNetView>();
            if (tracked != null)
                TrackedViews.Add(tracked.GetInstanceID());

            ZNetView nview = GetNetView(container);
            if (nview != null &&
                nview.IsValid() &&
                nview.GetZDO() != null)
                ByZdo[nview.GetZDO().m_uid] = container;
        }

        internal static void Unregister(Container container)
        {
            if (container == null)
                return;

            Known.Remove(container);
            Reconcile.Remove(container);
            RemoveIndex(container);
            Invalidate();
            ConnectionVisuals.NotifyGraphChanged();
        }

        // Objects holding a chest or a refill target: everything else that
        // gets destroyed (trees, rocks, items, creatures...) is skipped at
        // once instead of being searched.
        internal static readonly HashSet<int> TrackedViews = new HashSet<int>();

        internal static void Clear()
        {
            TrackedViews.Clear();
            Known.Clear();
            Reconcile.Clear();
            PendingIndex.Clear();
            ByRoute.Clear();
            RoutesByContainer.Clear();
            TitleByContainer.Clear();
            ByZdo.Clear();
            Matching.Clear();
            _nextRefresh = 0f;
            _nextReconcile = 0f;
            _reconcileCursor = 0;
            _fullReindexPending = false;
            _fullReindexCursor = 0;
            _cachedRouteId = null;
            _cachedOrigin = Vector3.zero;
            _cachedRange = 0f;
        }

        internal static void Invalidate()
        {
            _nextRefresh = 0f;
            _cachedRouteId = null;
        }

        internal static void ReindexAll()
        {
            // Never rescan every loaded chest in one frame. Clear the
            // derived route index and rebuild it lazily, one chest per frame.
            ByRoute.Clear();
            RoutesByContainer.Clear();
            TitleByContainer.Clear();

            _fullReindexPending = Reconcile.Count > 0;
            _fullReindexCursor = 0;
            if (_fullReindexPending)
                Plugin.Wake();

            Invalidate();
            ConnectionVisuals.NotifyGraphChanged();
        }

        // New chests are indexed one per frame, only while there are any.
        internal static bool Busy =>
            PendingIndex.Count > 0 || _fullReindexPending;

        internal static void TickPending()
        {
            while (PendingIndex.Count > 0)
            {
                Container pending = PendingIndex.Dequeue();
                if (pending == null)
                    continue;

                Reindex(pending, false);
                return;
            }

            if (_fullReindexPending)
            {
                while (_fullReindexCursor < Reconcile.Count)
                {
                    Container container =
                        Reconcile[_fullReindexCursor++];

                    if (container == null)
                        continue;

                    Reindex(container, true);
                    return;
                }

                _fullReindexPending = false;
                _fullReindexCursor = 0;
            }
        }

        // A chest renamed by another player arrives without a call we can
        // catch: one known chest is re-checked now and then.
        internal static void TickReconcile()
        {
            if (Busy ||
                Reconcile.Count == 0 ||
                Time.time < _nextReconcile)
                return;

            _nextReconcile =
                Time.time + ReconcileIntervalSeconds;

            if (_reconcileCursor >= Reconcile.Count)
                _reconcileCursor = 0;

            int index = _reconcileCursor;
            Container current = Reconcile[index];

            if (current == null)
            {
                Reconcile.RemoveAt(index);
                if (_reconcileCursor >= Reconcile.Count)
                    _reconcileCursor = 0;
                return;
            }

            _reconcileCursor++;
            Reindex(current, false);
        }

        internal static void NotifyTitleChanged(Container container)
        {
            if (container == null)
                return;

            int id = container.GetInstanceID();
            string oldTitle;
            TitleByContainer.TryGetValue(id, out oldTitle);

            string currentTitle =
                GetCustomTitle(container) ?? "";

            if (string.Equals(
                    oldTitle,
                    currentTitle,
                    StringComparison.Ordinal))
                return;

            Reindex(container, true);
            Invalidate();
            ConnectionVisuals.NotifyChestChanged(container);
        }

        internal static void NotifyTitleChanged(ZDO zdo)
        {
            if (zdo == null)
                return;

            Container container;
            if (ByZdo.TryGetValue(zdo.m_uid, out container) &&
                container != null)
            {
                NotifyTitleChanged(container);
            }
        }

        internal static string GetAssignedRouteId(Container container)
        {
            HashSet<string> assigned =
                GetAssignedRouteIds(container);

            if (assigned == null || assigned.Count == 0)
                return null;

            string title = GetCustomTitle(container);
            string primary = RouteRegistry.ClassifyTitle(title);

            if (!string.IsNullOrEmpty(primary) &&
                assigned.Contains(primary))
                return primary;

            foreach (RouteDefinition route in RouteRegistry.All)
            {
                if (route != null &&
                    assigned.Contains(route.Id))
                    return route.Id;
            }

            return null;
        }

        internal static HashSet<string> GetAssignedRouteIds(
            Container container)
        {
            if (container == null)
                return null;

            int id = container.GetInstanceID();
            if (!TitleByContainer.ContainsKey(id))
                Reindex(container, true);
            else
                Reindex(container, false);

            HashSet<string> routeIds;
            return RoutesByContainer.TryGetValue(
                    id,
                    out routeIds)
                ? routeIds
                : null;
        }

        internal static List<Container> GetMatching(RouteContext context)
        {
            if (context == null ||
                context.Definition == null)
            {
                Matching.Clear();
                return Matching;
            }

            // Walking around with the hammer moves the origin every frame:
            // that list follows on its short timer instead of being rebuilt
            // every frame (taking items asks for a fresh one). A different
            // station or smelter of the same kind is a different place, so
            // it gets its own list right away.
            bool sameContext =
                _cachedRouteId == context.Definition.Id &&
                Mathf.Abs(_cachedRange - context.Range) < 0.01f &&
                (context.Tool != null ||
                 (_cachedOrigin - context.Origin).sqrMagnitude < 0.0625f);

            if (sameContext && Time.time < _nextRefresh)
                return Matching;

            _cachedRouteId = context.Definition.Id;
            _cachedOrigin = context.Origin;
            _cachedRange = context.Range;
            _nextRefresh = Time.time + CacheSeconds;

            Refresh(context);
            return Matching;
        }

        internal static bool IsAssignedTo(
            Container container,
            RouteDefinition route)
        {
            if (container == null || route == null)
                return false;

            ZNetView nview = GetNetView(container);
            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null)
                return false;

            HashSet<string> assigned =
                GetAssignedRouteIds(container);

            return assigned != null &&
                   assigned.Contains(route.Id);
        }

        internal static bool IsUsable(Container container)
        {
            if (Player.m_localPlayer == null || Game.instance == null)
                return false;

            return IsUsable(
                container,
                Game.instance.GetPlayerProfile().GetPlayerID());
        }

        internal static bool IsUsable(Container container, long playerId)
        {
            if (container == null ||
                Player.m_localPlayer == null ||
                container.GetInventory() == null)
                return false;

            ZNetView nview = GetNetView(container);
            if (nview == null || !nview.IsValid())
                return false;

            if (CheckAccess != null &&
                !CheckAccess(container, playerId))
                return false;

            if (container.m_checkGuardStone &&
                !PrivateArea.CheckAccess(
                    container.transform.position,
                    0f,
                    false,
                    false))
                return false;

            ZDO zdo = nview.GetZDO();
            if (!MultiUserChestCompat.Active &&
                zdo != null &&
                !nview.IsOwner() &&
                zdo.GetInt(ZDOVars.s_inUse) == 1)
                return false;

            return true;
        }

        internal static bool IsMoving(Container container)
        {
            return container != null &&
                   container.m_rootObjectOverride != null;
        }

        internal static bool HasMovingForRoute(string routeId)
        {
            if (string.IsNullOrEmpty(routeId))
                return false;

            HashSet<Container> containers;
            if (!ByRoute.TryGetValue(routeId, out containers))
                return false;

            foreach (Container container in containers)
            {
                if (IsMoving(container))
                    return true;
            }

            return false;
        }

        internal static ZNetView GetNetView(Container container)
        {
            if (container == null)
                return null;

            return container.m_rootObjectOverride != null
                ? container.m_rootObjectOverride
                : container.GetComponent<ZNetView>();
        }

        internal static bool TryTakeOwnership(Container container)
        {
            ZNetView nview = GetNetView(container);
            if (nview == null || !nview.IsValid())
                return false;

            if (!nview.IsOwner())
                nview.ClaimOwnership();

            return nview.IsOwner();
        }

        private static void Reindex(
            Container container,
            bool force)
        {
            Prof.Begin(Prof.Reindex);
            try { ReindexTimed(container, force); }
            finally { Prof.End(Prof.Reindex); }
        }

        private static void ReindexTimed(
            Container container,
            bool force)
        {
            if (container == null)
                return;

            ZNetView nview = GetNetView(container);
            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null)
                return;

            ZDO zdo = nview.GetZDO();
            ByZdo[zdo.m_uid] = container;

            string title =
                zdo.GetString(ZDOVars.s_text, "") ?? "";

            int id = container.GetInstanceID();
            string oldTitle;
            if (!force &&
                TitleByContainer.TryGetValue(id, out oldTitle) &&
                string.Equals(
                    oldTitle,
                    title,
                    StringComparison.Ordinal))
                return;

            HashSet<string> oldRoutes;
            RoutesByContainer.TryGetValue(id, out oldRoutes);

            var newRoutes =
                new HashSet<string>(StringComparer.Ordinal);

            string primaryRoute =
                RouteRegistry.ClassifyTitle(title);

            if (!string.IsNullOrEmpty(primaryRoute))
                newRoutes.Add(primaryRoute);

            SharedRouteRegistry.AddMatchingRoutes(
                title,
                newRoutes);

            bool changed =
                oldRoutes == null
                    ? newRoutes.Count > 0
                    : !oldRoutes.SetEquals(newRoutes);

            if (oldRoutes != null)
            {
                foreach (string oldRoute in oldRoutes)
                {
                    HashSet<Container> oldSet;
                    if (ByRoute.TryGetValue(oldRoute, out oldSet))
                    {
                        oldSet.Remove(container);
                        if (oldSet.Count == 0)
                            ByRoute.Remove(oldRoute);
                    }
                }
            }

            if (newRoutes.Count > 0)
            {
                foreach (string newRoute in newRoutes)
                {
                    HashSet<Container> set;
                    if (!ByRoute.TryGetValue(newRoute, out set))
                    {
                        set = new HashSet<Container>();
                        ByRoute.Add(newRoute, set);
                    }

                    set.Add(container);
                }

                RoutesByContainer[id] = newRoutes;
            }
            else
            {
                RoutesByContainer.Remove(id);
            }

            TitleByContainer[id] = title;

            if (changed)
            {
                Invalidate();
                ConnectionVisuals.NotifyGraphChanged();
            }
        }

        private static void RemoveIndex(Container container)
        {
            if (container == null)
                return;

            int id = container.GetInstanceID();

            HashSet<string> routeIds;
            if (RoutesByContainer.TryGetValue(id, out routeIds))
            {
                foreach (string routeId in routeIds)
                {
                    HashSet<Container> set;
                    if (ByRoute.TryGetValue(routeId, out set))
                    {
                        set.Remove(container);
                        if (set.Count == 0)
                            ByRoute.Remove(routeId);
                    }
                }
            }

            RoutesByContainer.Remove(id);
            TitleByContainer.Remove(id);

            ZNetView nview = GetNetView(container);
            if (nview != null && nview.GetZDO() != null)
                ByZdo.Remove(nview.GetZDO().m_uid);
        }

        private static void Refresh(RouteContext context)
        {
            Prof.Begin(Prof.ListRefresh);
            try { RefreshTimed(context); }
            finally { Prof.End(Prof.ListRefresh); }
        }

        private static void RefreshTimed(RouteContext context)
        {
            Matching.Clear();
            MatchingVersion++;

            float rangeSqr = context.Range * context.Range;

            HashSet<Container> candidates;
            if (!ByRoute.TryGetValue(
                    context.Definition.Id,
                    out candidates))
                return;

            if (Player.m_localPlayer == null || Game.instance == null)
                return;

            long playerId =
                Game.instance.GetPlayerProfile().GetPlayerID();

            foreach (Container container in candidates)
            {
                if (container == null)
                    continue;

                Vector3 delta =
                    container.transform.position - context.Origin;

                // Match CraftingStation's practical horizontal range behavior.
                delta.y = 0f;

                if (delta.sqrMagnitude > rangeSqr)
                    continue;

                if (!IsUsable(container, playerId))
                    continue;

                Matching.Add(container);
            }

            // Each title read once, not on every comparison.
            SortTitles.Clear();
            for (int i = 0; i < Matching.Count; i++)
                SortTitles[Matching[i]] = GetCustomTitle(Matching[i]);

            Matching.Sort(
                delegate(Container a, Container b)
                {
                    string titleA = SortTitles[a];
                    string titleB = SortTitles[b];

                    int byTitle =
                        WindowsLogicalCompare(
                            titleA,
                            titleB);

                    if (byTitle != 0)
                        return byTitle;

                    // Deterministic tie-breaker for identically named chests.
                    float da =
                        (a.transform.position - context.Origin)
                        .sqrMagnitude;
                    float db =
                        (b.transform.position - context.Origin)
                        .sqrMagnitude;

                    return da.CompareTo(db);
                });
        }

        internal static string GetCustomTitle(
            Container container)
        {
            ZNetView nview =
                GetNetView(container);

            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null)
                return "";

            return nview.GetZDO().GetString(
                ZDOVars.s_text,
                "");
        }

        private static int WindowsLogicalCompare(
            string left,
            string right)
        {
            left = left ?? "";
            right = right ?? "";

            // Requested priority rule:
            // "AAA" sorts before "AA" when one title is an exact prefix.
            if (left.Length != right.Length)
            {
                if (left.StartsWith(
                        right,
                        StringComparison.OrdinalIgnoreCase))
                    return -1;

                if (right.StartsWith(
                        left,
                        StringComparison.OrdinalIgnoreCase))
                    return 1;
            }

            int i = 0;
            int j = 0;

            while (i < left.Length &&
                   j < right.Length)
            {
                char a = left[i];
                char b = right[j];

                if (char.IsDigit(a) &&
                    char.IsDigit(b))
                {
                    int aStart = i;
                    int bStart = j;

                    while (i < left.Length &&
                           left[i] == '0')
                        i++;

                    while (j < right.Length &&
                           right[j] == '0')
                        j++;

                    int aDigits = i;
                    int bDigits = j;

                    while (i < left.Length &&
                           char.IsDigit(left[i]))
                        i++;

                    while (j < right.Length &&
                           char.IsDigit(right[j]))
                        j++;

                    int aSignificant = i - aDigits;
                    int bSignificant = j - bDigits;

                    if (aSignificant != bSignificant)
                        return aSignificant.CompareTo(
                            bSignificant);

                    for (int k = 0;
                         k < aSignificant;
                         k++)
                    {
                        int cmp =
                            left[aDigits + k].CompareTo(
                                right[bDigits + k]);

                        if (cmp != 0)
                            return cmp;
                    }

                    int aRun = i - aStart;
                    int bRun = j - bStart;

                    if (aRun != bRun)
                        return aRun.CompareTo(bRun);

                    continue;
                }

                char upperA =
                    char.ToUpperInvariant(a);
                char upperB =
                    char.ToUpperInvariant(b);

                if (upperA != upperB)
                    return upperA.CompareTo(upperB);

                i++;
                j++;
            }

            return (left.Length - i)
                .CompareTo(right.Length - j);
        }

    }
}
