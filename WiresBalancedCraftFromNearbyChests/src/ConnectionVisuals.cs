using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class ConnectionVisuals
    {
        private enum VisualMode
        {
            None,
            Station,
            RefillTarget,
            ChestStation,
            ChestRefill,
            Tool
        }

        private struct BeamKey : IEquatable<BeamKey>
        {
            internal int ContainerId;
            internal int TargetId;
            internal bool FromPlayer;

            public bool Equals(BeamKey other)
            {
                return ContainerId == other.ContainerId &&
                       TargetId == other.TargetId &&
                       FromPlayer == other.FromPlayer;
            }

            public override bool Equals(object obj)
            {
                return obj is BeamKey && Equals((BeamKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ContainerId;
                    hash = (hash * 397) ^ TargetId;
                    hash = (hash * 397) ^ (FromPlayer ? 1 : 0);
                    return hash;
                }
            }
        }

        private sealed class Beam
        {
            internal BeamKey Key;
            internal Container Container;
            internal Component Target;
            internal bool FromPlayer;
            internal GameObject Prefab;
            internal GameObject Instance;
        }

        private static readonly Dictionary<BeamKey, Beam> Beams =
            new Dictionary<BeamKey, Beam>();

        // Released beams keep their current particles alive while
        // emission stops, then return to the same bounded object pool.
        private sealed class FadingBeam
        {
            internal GameObject Instance;
            internal GameObject Prefab;
            internal BeamKey Key;
            internal ParticleSystem[] Particles;
            internal float Started;
        }

        private static readonly List<FadingBeam> Fading =
            new List<FadingBeam>();
        private const float MaxFadeSeconds = 6f;

        private static readonly HashSet<BeamKey> Desired =
            new HashSet<BeamKey>();

        private static readonly List<BeamKey> RemoveKeys =
            new List<BeamKey>();

        private static readonly Dictionary<int, Stack<GameObject>> Pool =
            new Dictionary<int, Stack<GameObject>>();

        private static readonly FieldInfo AllExtensionsField =
            AccessTools.Field(typeof(StationExtension), "m_allExtensions");

        private static readonly string[] KnownExtensionPrefabs =
        {
            "piece_workbench_ext1",
            "piece_workbench_ext2",
            "piece_workbench_ext3",
            "piece_workbench_ext4",
            "forge_ext1",
            "forge_ext2",
            "forge_ext3",
            "forge_ext4",
            "forge_ext5",
            "forge_ext6"
        };

        internal const float ContextPollSeconds = 0.08f;
        private const float LazyGraphDelay = 0.12f;
        private const float MovingGraphRefreshSeconds = 0.25f;
        private const int BeamCreatesPerFrame = 1;

        private static GameObject _genericConnectionPrefab;

        private static VisualMode _mode;
        private static int _focusId;
        private static string _routeId;
        private static CraftingStation _station;
        private static Component _refillTarget;
        private static Container _chest;
        private static ItemDrop.ItemData _tool;

        private static bool _dirty = true;
        private static float _rebuildNotBefore;
        private static float _nextMovingGraphRefresh;

        // A few times a second (not every frame): what you're looking at,
        // and whether storage that moves needs its beams redrawn. Looking
        // at the same thing as last time costs nothing.
        private static GameObject _lastHover;
        private static bool _lastInBuildUi;
        private static ItemDrop.ItemData _lastTool;
        private static bool _contextActive;

        internal static void Poll()
        {
            ContainerRegistry.TickReconcile();

            Player player = Player.m_localPlayer;
            if (!VisualsEnabled() || player == null)
            {
                if (_contextActive)
                {
                    _contextActive = false;
                    _lastHover = null;
                    CleanupActive();
                    ResetContext();
                    _dirty = false;
                }
                return;
            }
            _contextActive = true;

            GameObject hover = player.GetHoverObject();
            bool inBuildUi = Hud.InBuildUi();
            ItemDrop.ItemData tool = player.RightItem;
            // Plain reference checks: a destroyed object still counts as
            // something different from nothing.
            if (!ReferenceEquals(hover, _lastHover) || inBuildUi != _lastInBuildUi ||
                !ReferenceEquals(tool, _lastTool))
            {
                _lastHover = hover;
                _lastInBuildUi = inBuildUi;
                _lastTool = tool;
                PollContext(player);
            }

            if (_mode != VisualMode.None &&
                Time.time >= _nextMovingGraphRefresh &&
                ContextUsesMovingContainers())
            {
                _nextMovingGraphRefresh =
                    Time.time + MovingGraphRefreshSeconds;
                _dirty = true;
                _rebuildNotBefore = 0f;
                Plugin.Wake();
            }
        }

        // Every frame only while something is actually happening: a
        // rebuild to do, beams still being created, sparks fading out, or
        // beams that follow you (the build menu) or moving storage.
        internal static bool Busy
        {
            get
            {
                if (Fading.Count > 0 || _dirty)
                    return true;
                if (Beams.Count == 0)
                    return false;
                foreach (Beam beam in Beams.Values)
                    if (beam != null && beam.Instance == null && beam.Prefab != null && beam.Container != null)
                        return true;
                return _mode == VisualMode.Tool || HasMovingBeam();
            }
        }

        internal static void Update()
        {
            // Continue naturally draining particles even if the mouse
            // leaves the station, the hammer is put away, or visuals
            // were disabled in the mod config.
            UpdateFading();

            Player player = Player.m_localPlayer;
            if (!VisualsEnabled() || player == null)
            {
                _dirty = false;
                return;
            }

            if (_dirty &&
                Time.time >= _rebuildNotBefore)
            {
                Rebuild(player);
                _dirty = false;
            }

            ProcessBeamCreationBudget(player);

            // Player-origin beams and beams attached to moving storage
            // need live endpoints. Static station/chest beams stay lazy.
            if (Beams.Count > 0 &&
                (_mode == VisualMode.Tool ||
                 HasMovingBeam()))
                UpdateBeamTransforms(player);
        }

        private static bool VisualsEnabled()
        {
            return ModConfig.Enabled != null &&
                   ModConfig.Enabled.Value &&
                   ModConfig.ShowConnections != null &&
                   ModConfig.ShowConnections.Value;
        }

        private static bool ContextUsesMovingContainers()
        {
            if (_chest != null &&
                ContainerRegistry.IsMoving(_chest))
                return true;

            string routeId = _routeId;

            if (string.IsNullOrEmpty(routeId) &&
                _mode == VisualMode.Station &&
                _station != null)
            {
                RouteDefinition route =
                    RouteRegistry.EnsureStation(_station);

                routeId = route != null
                    ? route.Id
                    : null;
            }

            return ContainerRegistry.HasMovingForRoute(
                routeId);
        }

        private static bool HasMovingBeam()
        {
            foreach (Beam beam in Beams.Values)
            {
                if (beam != null &&
                    ContainerRegistry.IsMoving(
                        beam.Container))
                    return true;
            }

            return false;
        }

        private static void PollContext(Player player)
        {
            GameObject hover = player.GetHoverObject();

            if (hover != null)
            {
                CraftingStation station =
                    hover.GetComponentInParent<CraftingStation>();

                if (station != null)
                {
                    SetContext(
                        VisualMode.Station,
                        station.GetInstanceID(),
                        null,
                        station,
                        null,
                        null,
                        null);
                    return;
                }

                Component refill =
                    RefillTargetRegistry.FindHoveredTarget(hover);

                if (refill != null)
                {
                    RouteDefinition route =
                        RefillRouting.Resolve(refill);

                    if (route != null)
                    {
                        SetContext(
                            VisualMode.RefillTarget,
                            refill.GetInstanceID(),
                            route.Id,
                            null,
                            refill,
                            null,
                            null);
                        return;
                    }
                }

                Container chest =
                    hover.GetComponentInParent<Container>();

                if (chest != null)
                {
                    string id =
                        ContainerRegistry.GetAssignedRouteId(chest);

                    RouteDefinition route =
                        RouteRegistry.Get(id);

                    if (route != null &&
                        route.Kind == RouteKind.Station)
                    {
                        SetContext(
                            VisualMode.ChestStation,
                            chest.GetInstanceID(),
                            route.Id,
                            null,
                            null,
                            chest,
                            null);
                        return;
                    }

                    if (route != null &&
                        route.Kind == RouteKind.Refill)
                    {
                        SetContext(
                            VisualMode.ChestRefill,
                            chest.GetInstanceID(),
                            route.Id,
                            null,
                            null,
                            chest,
                            null);
                        return;
                    }
                }
            }

            ItemDrop.ItemData tool = player.RightItem;
            if (Hud.InBuildUi() &&
                tool != null &&
                tool.m_shared != null &&
                tool.m_shared.m_buildPieces != null)
            {
                RouteDefinition route =
                    RouteRegistry.EnsureTool(tool);

                if (route != null)
                {
                    int id =
                        tool.m_dropPrefab != null
                            ? tool.m_dropPrefab.GetInstanceID()
                            : tool.GetHashCode();

                    SetContext(
                        VisualMode.Tool,
                        id,
                        route.Id,
                        null,
                        null,
                        null,
                        tool);
                    return;
                }
            }

            SetContext(
                VisualMode.None,
                0,
                null,
                null,
                null,
                null,
                null);
        }

        private static void SetContext(
            VisualMode mode,
            int focusId,
            string routeId,
            CraftingStation station,
            Component refillTarget,
            Container chest,
            ItemDrop.ItemData tool)
        {
            bool changed =
                _mode != mode ||
                _focusId != focusId ||
                !string.Equals(
                    _routeId,
                    routeId,
                    StringComparison.Ordinal);

            _mode = mode;
            _focusId = focusId;
            _routeId = routeId;
            _station = station;
            _refillTarget = refillTarget;
            _chest = chest;
            _tool = tool;

            if (changed)
            {
                _dirty = true;
                _rebuildNotBefore = 0f;
                Plugin.Wake();
            }
        }

        private static void Rebuild(Player player)
        {
            Prof.Begin(Prof.Rebuild);
            try { RebuildTimed(player); }
            finally { Prof.End(Prof.Rebuild); }
        }

        private static void RebuildTimed(Player player)
        {
            Desired.Clear();

            switch (_mode)
            {
                case VisualMode.Station:
                    BuildStation(_station);
                    break;

                case VisualMode.RefillTarget:
                    BuildRefillTarget(_refillTarget);
                    break;

                case VisualMode.ChestStation:
                    BuildChestStation(_chest);
                    break;

                case VisualMode.ChestRefill:
                    BuildChestRefill(_chest);
                    break;

                case VisualMode.Tool:
                    BuildTool(player, _tool);
                    break;
            }

            RemoveUndesired();
            UpdateBeamTransforms(player);
        }

        private static void BuildStation(
            CraftingStation station)
        {
            if (station == null)
                return;

            RouteDefinition route =
                RouteRegistry.EnsureStation(station);

            if (route == null)
                return;

            float range = station.GetStationBuildRange();

            var context = new RouteContext
            {
                Definition = route,
                Origin = station.transform.position,
                Range = range,
                Station = station
            };

            List<Container> matching =
                ContainerRegistry.GetMatching(context);

            GameObject prefab =
                FindConnectionPrefab(station);

            for (int i = 0; i < matching.Count; i++)
                WantBeam(
                    matching[i],
                    station,
                    false,
                    prefab);
        }

        private static void BuildRefillTarget(
            Component target)
        {
            if (target == null)
                return;

            RouteDefinition route =
                RefillRouting.Resolve(target);

            if (route == null)
                return;

            float range =
                RangeProvider.GetSharedStationRadius();

            var context = new RouteContext
            {
                Definition = route,
                Origin = target.transform.position,
                Range = range
            };

            List<Container> matching =
                ContainerRegistry.GetMatching(context);

            GameObject prefab =
                FindGenericConnectionPrefab();

            for (int i = 0; i < matching.Count; i++)
                WantBeam(
                    matching[i],
                    target,
                    false,
                    prefab);
        }

        private static void BuildChestStation(
            Container chest)
        {
            if (chest == null ||
                !ContainerRegistry.IsUsable(chest))
                return;

            HashSet<string> routeIds =
                ContainerRegistry.GetAssignedRouteIds(chest);

            if (routeIds == null ||
                routeIds.Count == 0)
                return;

            foreach (CraftingStation station
                     in CraftingStation.Instances)
            {
                if (station == null)
                    continue;

                RouteDefinition stationRoute =
                    RouteRegistry.EnsureStation(station);

                if (stationRoute == null ||
                    !routeIds.Contains(stationRoute.Id))
                    continue;

                float range =
                    station.GetStationBuildRange();

                if (!WithinHorizontalRange(
                        chest.transform.position,
                        station.transform.position,
                        range))
                    continue;

                WantBeam(
                    chest,
                    station,
                    false,
                    FindConnectionPrefab(station));
            }
        }

        private static void BuildChestRefill(
            Container chest)
        {
            if (chest == null ||
                !ContainerRegistry.IsUsable(chest))
                return;

            HashSet<string> routeIds =
                ContainerRegistry.GetAssignedRouteIds(chest);

            if (routeIds == null ||
                routeIds.Count == 0)
                return;

            float range =
                RangeProvider.GetSharedStationRadius();

            foreach (RouteDefinition route in RouteRegistry.All)
            {
                if (route == null ||
                    route.Kind != RouteKind.Refill ||
                    !routeIds.Contains(route.Id))
                    continue;

                if (route.Id == "refill:fermenter")
                    RefillTargetRegistry.EnsureFermenterTargets();

                foreach (Component target
                         in RefillTargetRegistry.GetTargets(route))
                {
                    if (target == null)
                        continue;

                    if (!WithinHorizontalRange(
                            chest.transform.position,
                            target.transform.position,
                            range))
                        continue;

                    WantBeam(
                        chest,
                        target,
                        false,
                        FindGenericConnectionPrefab());
                }
            }
        }

        private static void BuildTool(
            Player player,
            ItemDrop.ItemData tool)
        {
            if (player == null || tool == null)
                return;

            RouteDefinition route =
                RouteRegistry.EnsureTool(tool);

            if (route == null)
                return;

            float range =
                RangeProvider.GetSharedStationRadius();

            var context = new RouteContext
            {
                Definition = route,
                Origin = player.transform.position,
                Range = range,
                Tool = tool
            };

            List<Container> matching =
                ContainerRegistry.GetMatching(context);

            GameObject prefab =
                FindGenericConnectionPrefab();

            for (int i = 0; i < matching.Count; i++)
                WantBeam(
                    matching[i],
                    null,
                    true,
                    prefab);
        }

        private static bool WithinHorizontalRange(
            Vector3 a,
            Vector3 b,
            float range)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.sqrMagnitude <= range * range;
        }

        private static void WantBeam(
            Container chest,
            Component target,
            bool fromPlayer,
            GameObject prefab)
        {
            if (chest == null || prefab == null)
                return;

            BeamKey key = new BeamKey
            {
                ContainerId = chest.GetInstanceID(),
                TargetId =
                    target != null
                        ? target.GetInstanceID()
                        : 0,
                FromPlayer = fromPlayer
            };

            Desired.Add(key);
            EnsureBeam(
                key,
                chest,
                target,
                fromPlayer,
                prefab);
        }

        private static void EnsureBeam(
            BeamKey key,
            Container chest,
            Component target,
            bool fromPlayer,
            GameObject prefab)
        {
            Beam existing;
            if (Beams.TryGetValue(key, out existing))
            {
                if (existing != null &&
                    existing.Prefab == prefab)
                {
                    existing.Container = chest;
                    existing.Target = target;
                    existing.FromPlayer = fromPlayer;
                    return;
                }

                ReleaseBeam(existing);
                Beams.Remove(key);
            }

            // Record the desired beam now, but do not instantiate the
            // particle object here. Creation is budgeted across frames.
            Beams.Add(
                key,
                new Beam
                {
                    Key = key,
                    Container = chest,
                    Target = target,
                    FromPlayer = fromPlayer,
                    Prefab = prefab,
                    Instance = null
                });
        }

        private static GameObject AcquireInstance(
            GameObject prefab, BeamKey requestedKey)
        {
            if (prefab == null)
                return null;

            GameObject instance = null;

            // A beam that's still fading out is left to finish: taking it
            // back would clear its particles on the spot.
            if (instance == null)
            {
                int id = prefab.GetInstanceID();
                Stack<GameObject> stack;

                // Completed, pooled effects may be freely reused.
                if (Pool.TryGetValue(id, out stack))
                {
                    while (stack.Count > 0 && instance == null)
                        instance = stack.Pop();
                }
            }

            if (instance == null)
            {
                instance =
                    UnityEngine.Object.Instantiate(
                        prefab,
                        Vector3.zero,
                        Quaternion.identity);
                if (instance != null)
                {
                    instance.SetActive(false);
                    ConnectionParticleSafety.Prepare(instance, prefab);
                }
            }

            // The caller positions the effect BEFORE activation.
            if (instance != null)
                instance.SetActive(false);

            return instance;
        }
        private static void ActivateBeam(GameObject instance)
        {
            if (instance == null)
                return;

            instance.SetActive(true);

            // Every new hover gets the usual full entrance animation,
            // even when recycling an effect that was mid-fade.
            foreach (ParticleSystem particle in
                     instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle == null ||
                    !particle.gameObject.activeInHierarchy)
                    continue;

                particle.Stop(
                    false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
            }
        }

        private static void ReleaseBeam(Beam beam)
        {
            if (beam == null || beam.Instance == null)
                return;

            GameObject instance = beam.Instance;
            beam.Instance = null;

            if (beam.Prefab == null)
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }

            ParticleSystem[] particles =
                instance.GetComponentsInChildren<ParticleSystem>(true);

            bool alive = false;
            if (instance.activeInHierarchy)
            {
                foreach (ParticleSystem particle in particles)
                {
                    if (particle == null ||
                        !particle.gameObject.activeInHierarchy)
                        continue;

                    // Stop spawning fresh particles; allow the ones
                    // already on screen to reach their natural end.
                    particle.Stop(
                        false, ParticleSystemStopBehavior.StopEmitting);
                    if (particle.IsAlive(false))
                        alive = true;
                }
            }

            if (!alive)
            {
                ReturnToPool(instance, beam.Prefab);
                return;
            }

            Plugin.Wake();
            Fading.Add(new FadingBeam
            {
                Instance = instance,
                Prefab = beam.Prefab,
                Key = beam.Key,
                Particles = particles,
                Started = Time.unscaledTime
            });
        }

        private static void UpdateFading()
        {
            Prof.Begin(Prof.Fading);
            try { UpdateFadingTimed(); }
            finally { Prof.End(Prof.Fading); }
        }

        private static void UpdateFadingTimed()
        {
            for (int i = Fading.Count - 1; i >= 0; i--)
            {
                FadingBeam fade = Fading[i];
                if (fade.Instance == null)
                {
                    Fading.RemoveAt(i);
                    continue;
                }

                bool alive = false;
                if (Time.unscaledTime - fade.Started < MaxFadeSeconds)
                {
                    foreach (ParticleSystem particle in fade.Particles)
                    {
                        if (particle != null &&
                            particle.IsAlive(false))
                        {
                            alive = true;
                            break;
                        }
                    }
                }

                if (alive)
                    continue;

                Fading.RemoveAt(i);
                ReturnToPool(fade.Instance, fade.Prefab);
            }
        }

        private static void ReturnToPool(
            GameObject instance, GameObject prefab)
        {
            if (instance == null)
                return;

            instance.SetActive(false);
            if (prefab == null)
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }

            int id = prefab.GetInstanceID();
            Stack<GameObject> stack;
            if (!Pool.TryGetValue(id, out stack))
            {
                stack = new Stack<GameObject>();
                Pool.Add(id, stack);
            }

            if (stack.Count < 64)
                stack.Push(instance);
            else
                UnityEngine.Object.Destroy(instance);
        }
        private static void RemoveUndesired()
        {
            RemoveKeys.Clear();

            foreach (KeyValuePair<BeamKey, Beam> pair
                     in Beams)
            {
                if (!Desired.Contains(pair.Key) ||
                    pair.Value == null ||
                    pair.Value.Container == null)
                    RemoveKeys.Add(pair.Key);
            }

            for (int i = 0; i < RemoveKeys.Count; i++)
            {
                Beam beam;
                if (Beams.TryGetValue(
                        RemoveKeys[i],
                        out beam))
                    ReleaseBeam(beam);

                Beams.Remove(RemoveKeys[i]);
            }
        }

        private static void ProcessBeamCreationBudget(
            Player player)
        {
            int created = 0;

            foreach (Beam beam in Beams.Values)
            {
                if (created >= BeamCreatesPerFrame)
                    break;

                if (beam == null ||
                    beam.Instance != null ||
                    beam.Prefab == null ||
                    beam.Container == null)
                    continue;

                beam.Instance =
                    AcquireInstance(beam.Prefab, beam.Key);

                if (beam.Instance == null)
                    continue;

                UpdateBeamTransform(beam, player);
                ActivateBeam(beam.Instance);
                created++;
            }
        }

        private static void UpdateBeamTransforms(
            Player player)
        {
            Prof.Begin(Prof.BeamTransforms);
            try { UpdateBeamTransformsTimed(player); }
            finally { Prof.End(Prof.BeamTransforms); }
        }

        private static void UpdateBeamTransformsTimed(
            Player player)
        {
            foreach (Beam beam in Beams.Values)
                UpdateBeamTransform(beam, player);
        }

        private static void UpdateBeamTransform(
            Beam beam,
            Player player)
        {
            if (beam == null ||
                beam.Container == null ||
                beam.Instance == null)
                return;

            Vector3 start;
            Vector3 end;

            if (beam.FromPlayer)
            {
                if (player == null)
                    return;

                start =
                    player.transform.position +
                    Vector3.up;
                end =
                    GetContainerPoint(beam.Container);
            }
            else
            {
                if (beam.Target == null)
                    return;

                start =
                    GetContainerPoint(beam.Container);
                end =
                    GetTargetPoint(beam.Target);
            }

            Vector3 vector = end - start;
            if (vector.sqrMagnitude < 0.0001f)
                return;

            Transform t = beam.Instance.transform;
            t.position = start;
            t.rotation =
                Quaternion.LookRotation(
                    vector.normalized);
            t.localScale =
                new Vector3(
                    1f,
                    1f,
                    vector.magnitude);
        }

        // Beam ends follow you every frame in the build menu: each chest's
        // or station's collider is looked up once, not on every frame.
        private static readonly Dictionary<int, Collider> Colliders =
            new Dictionary<int, Collider>();

        private static Collider ColliderOf(Component owner)
        {
            int id = owner.GetInstanceID();
            Collider collider;
            if (Colliders.TryGetValue(id, out collider) &&
                (collider == null || collider))
                return collider;

            collider = owner.GetComponentInChildren<Collider>();
            if (collider == null && !(owner is Container))
            {
                Piece piece = owner.GetComponentInParent<Piece>();
                if (piece != null)
                    collider = piece.GetComponentInChildren<Collider>();
            }

            if (Colliders.Count > 512)
                Colliders.Clear();
            Colliders[id] = collider;
            return collider;
        }

        private static Vector3 GetTargetPoint(
            Component target)
        {
            CraftingStation station =
                target as CraftingStation;

            if (station != null)
                return station.GetConnectionEffectPoint();

            Collider collider = ColliderOf(target);
            if (collider != null)
                return collider.bounds.center;

            return target.transform.position +
                   Vector3.up * 0.5f;
        }

        private static Vector3 GetContainerPoint(
            Container container)
        {
            Collider collider = ColliderOf(container);
            if (collider != null)
                return collider.bounds.center;

            return container.transform.position +
                   Vector3.up * 0.5f;
        }

        private static GameObject FindConnectionPrefab(
            CraftingStation station)
        {
            if (station != null)
            {
                List<StationExtension> extensions =
                    GetLoadedExtensions();

                if (extensions != null)
                {
                    for (int i = 0; i < extensions.Count; i++)
                    {
                        StationExtension extension =
                            extensions[i];

                        if (extension == null ||
                            extension.m_connectionPrefab == null ||
                            extension.m_craftingStation == null)
                            continue;

                        if (extension.m_craftingStation.m_name ==
                            station.m_name)
                            return extension.m_connectionPrefab;
                    }
                }
            }

            return FindGenericConnectionPrefab();
        }

        private static List<StationExtension> GetLoadedExtensions()
        {
            return AllExtensionsField != null
                ? AllExtensionsField.GetValue(null)
                    as List<StationExtension>
                : null;
        }

        private static GameObject FindGenericConnectionPrefab()
        {
            if (_genericConnectionPrefab != null)
                return _genericConnectionPrefab;

            List<StationExtension> loaded =
                GetLoadedExtensions();

            if (loaded != null)
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    StationExtension extension = loaded[i];
                    if (extension != null &&
                        extension.m_connectionPrefab != null)
                    {
                        _genericConnectionPrefab =
                            extension.m_connectionPrefab;
                        return _genericConnectionPrefab;
                    }
                }
            }
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
                return null;

            for (int i = 0;
                 i < KnownExtensionPrefabs.Length;
                 i++)
            {
                GameObject prefab =
                    scene.GetPrefab(
                        KnownExtensionPrefabs[i]);

                if (prefab == null)
                    continue;

                StationExtension extension =
                    prefab.GetComponentInChildren<StationExtension>(
                        true);

                if (extension == null ||
                    extension.m_connectionPrefab == null)
                    continue;

                _genericConnectionPrefab =
                    extension.m_connectionPrefab;
                return _genericConnectionPrefab;
            }

            return null;
        }
        internal static void NotifyGraphChanged()
        {
            // A rename can change where what you're looking at goes.
            _lastHover = null;
            if (!_contextActive)
                return;
            _dirty = true;
            _rebuildNotBefore =
                Mathf.Max(
                    _rebuildNotBefore,
                    Time.time + LazyGraphDelay);
            Plugin.Wake();
        }

        internal static void NotifyChestChanged(
            Container chest)
        {
            NotifyGraphChanged();

            if (chest == null ||
                string.IsNullOrEmpty(
                    ContainerRegistry.GetAssignedRouteId(chest)))
                return;

            SpawnRenameBurst(chest);
        }

        private static void SpawnRenameBurst(
            Container chest)
        {
            GameObject prefab =
                FindGenericConnectionPrefab();

            if (prefab == null)
                return;

            GameObject instance =
                UnityEngine.Object.Instantiate(
                    prefab,
                    GetContainerPoint(chest),
                    Quaternion.identity);

            if (instance == null)
                return;

            // Burst visual bypasses the pooled beam path.
            instance.SetActive(false);
            ConnectionParticleSafety.Prepare(instance, prefab);
            instance.transform.localScale =
                Vector3.one * 0.3f;
            instance.SetActive(true);
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(EndBurst(instance));
            else
                UnityEngine.Object.Destroy(instance, 3f);
        }

        // The burst emits briefly, then stops emitting and lets the
        // particles already out finish on their own before it's removed.
        private static System.Collections.IEnumerator EndBurst(GameObject instance)
        {
            yield return new WaitForSeconds(0.55f);
            if (instance == null)
                yield break;

            ParticleSystem[] particles =
                instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in particles)
                if (particle != null)
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);

            float until = Time.unscaledTime + MaxFadeSeconds;
            while (instance != null && Time.unscaledTime < until)
            {
                bool alive = false;
                foreach (ParticleSystem particle in particles)
                    if (particle != null && particle.IsAlive(false)) { alive = true; break; }
                if (!alive)
                    break;
                yield return null;
            }

            if (instance != null)
                UnityEngine.Object.Destroy(instance);
        }
        private static void CleanupActive()
        {
            foreach (Beam beam in Beams.Values)
                ReleaseBeam(beam);

            Beams.Clear();
            Desired.Clear();
            RemoveKeys.Clear();
        }

        private static void ResetContext()
        {
            _mode = VisualMode.None;
            _focusId = 0;
            _routeId = null;
            _station = null;
            _refillTarget = null;
            _chest = null;
            _tool = null;
            _dirty = true;
            _rebuildNotBefore = 0f;
            _nextMovingGraphRefresh = 0f;
        }

        internal static void Cleanup()
        {
            CleanupActive();

            foreach (Stack<GameObject> stack in Pool.Values)
            {
                while (stack.Count > 0)
                {
                    GameObject instance = stack.Pop();
                    if (instance != null)
                        UnityEngine.Object.Destroy(instance);
                }
            }

            Pool.Clear();
            // Teardown must immediately destroy any remaining tails.
            // Ordinary hover-off keeps its smooth emission stop.
            foreach (FadingBeam fade in Fading)
            {
                if (fade.Instance != null)
                    UnityEngine.Object.Destroy(fade.Instance);
            }
            Fading.Clear();
            ConnectionParticleSafety.Cleanup();
            _genericConnectionPrefab = null;
            ResetContext();
            _lastHover = null;
            _lastTool = null;
            _contextActive = false;
            Colliders.Clear();
        }
    }
}
