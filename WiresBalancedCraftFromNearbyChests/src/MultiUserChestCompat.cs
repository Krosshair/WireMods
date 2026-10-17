using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal sealed class MucPendingException : Exception
    {
        internal readonly MultiUserChestCompat.PendingAction Pending;

        internal MucPendingException(
            MultiUserChestCompat.PendingAction pending)
        {
            Pending = pending;
        }
    }

    internal static class MultiUserChestCompat
    {
        internal const string PluginGuid =
            "com.maxsch.valheim.MultiUserChest";

        internal sealed class Demand
        {
            internal string Name;
            internal int Amount;
            internal int Quality = -1;
            internal bool MatchWorldLevel = true;
            internal string PrefabName;
            internal Container SourceOnly;
            internal bool IgnorePlayer;
        }

        internal sealed class PendingAction
        {
            internal RouteContext Context;
            internal readonly List<Demand> Demands =
                new List<Demand>();
            internal readonly Dictionary<Container, Inventory>
                Reserves =
                    new Dictionary<Container, Inventory>();
            internal readonly HashSet<Container> Concurrent =
                new HashSet<Container>();
            internal readonly List<int> RequestIds =
                new List<int>();

            internal Func<bool> StillValid;
            internal Action Ready;
            internal Action Failed;
            internal float Started;
            internal bool Blocked;
        }

        private sealed class RefundJob
        {
            internal Container Container;
            internal Inventory Reserve;
            internal readonly List<int> RequestIds =
                new List<int>();
            internal float NextTry;
        }

        private static bool _initialized;
        private static bool _available;

        private static MethodInfo _removeItemFromChest;
        private static MethodInfo _addItemToChest;
        private static MethodInfo _getPackageGeneric;
        private static Type _iPackageType;

        private static PendingAction _pending;
        private static PendingAction _active;

        private static readonly List<RefundJob> Refunds =
            new List<RefundJob>();

        internal static bool Executing
        {
            get { return _active != null; }
        }

        internal static bool Busy
        {
            get { return _pending != null; }
        }

        internal static bool Active
        {
            get
            {
                EnsureInitialized();
                return _available;
            }
        }
        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
                return;

            try
            {
                var info = Chainloader.PluginInfos[PluginGuid];
                Assembly assembly =
                    info.Instance.GetType().Assembly;

                Type containerHandler =
                    assembly.GetType(
                        "MultiUserChest.ContainerHandler");

                Type packageHandler =
                    assembly.GetType(
                        "MultiUserChest.PackageHandler");

                _iPackageType =
                    assembly.GetType(
                        "MultiUserChest.IPackage");

                if (containerHandler != null)
                {
                    MethodInfo[] methods =
                        containerHandler.GetMethods(
                            BindingFlags.Public |
                            BindingFlags.Static);

                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo method = methods[i];

                        if (method.Name ==
                                "RemoveItemFromChest" &&
                            method.GetParameters().Length == 7)
                            _removeItemFromChest = method;

                        if (method.Name ==
                                "AddItemToChest" &&
                            method.GetParameters().Length == 6)
                            _addItemToChest = method;
                    }
                }

                if (packageHandler != null)
                {
                    MethodInfo[] methods =
                        packageHandler.GetMethods(
                            BindingFlags.Public |
                            BindingFlags.Static);

                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo method = methods[i];

                        if (method.Name == "GetPackage" &&
                            method.IsGenericMethodDefinition &&
                            method.GetParameters().Length == 1)
                        {
                            _getPackageGeneric = method;
                            break;
                        }
                    }
                }

                _available =
                    _removeItemFromChest != null &&
                    _addItemToChest != null &&
                    _getPackageGeneric != null &&
                    _iPackageType != null;

                if (_available && Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(
                        "MultiUserChest compatibility enabled.");
                }
            }
            catch (Exception ex)
            {
                _available = false;

                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Could not initialize MultiUserChest compatibility: " +
                        ex.Message);
                }
            }
        }

        internal static bool IsConcurrentRemote(
            Container container)
        {
            if (!Active || container == null)
                return false;

            ZNetView nview =
                ContainerRegistry.GetNetView(container);

            if (nview == null ||
                !nview.IsValid() ||
                !nview.HasOwner() ||
                nview.IsOwner())
                return false;

            ZDO zdo = nview.GetZDO();

            return container.IsInUse() ||
                   (zdo != null &&
                    zdo.GetInt(ZDOVars.s_inUse) == 1);
        }

        internal static Inventory GetEffectiveInventory(
            Container container)
        {
            if (container == null)
                return null;

            if (_active != null)
            {
                Inventory reserved;
                if (_active.Reserves.TryGetValue(
                        container,
                        out reserved))
                    return reserved;

                if (_active.Concurrent.Contains(container))
                    return null;
            }

            return container.GetInventory();
        }
        internal static void Update()
        {
            UpdatePending();
            UpdateRefunds();
        }

        private static void UpdatePending()
        {
            PendingAction pending = _pending;

            if (pending == null)
                return;

            if (pending.Blocked)
            {
                if (pending.Ready != null ||
                    pending.Failed != null)
                {
                    _pending = null;
                    FailPending(pending);
                }
                return;
            }

            bool waiting = false;

            for (int i = 0;
                 i < pending.RequestIds.Count;
                 i++)
            {
                if (RequestExists(
                        pending.RequestIds[i]))
                {
                    waiting = true;
                    break;
                }
            }

            if (waiting)
            {
                if (Time.realtimeSinceStartup -
                    pending.Started < 8f)
                    return;

                _pending = null;
                FailPending(pending);
                return;
            }

            if (pending.Ready == null &&
                pending.Failed == null)
                return;

            if (!CanSatisfy(pending) ||
                (pending.StillValid != null &&
                 !pending.StillValid()))
            {
                _pending = null;
                FailPending(pending);
                return;
            }

            _pending = null;
            _active = pending;

            try
            {
                if (pending.Ready != null)
                    pending.Ready();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "MultiUserChest routed action failed: " +
                        ex.Message);
                }

                if (pending.Failed != null)
                    pending.Failed();
            }
            finally
            {
                _active = null;
                RefundLeftovers(pending);
            }
        }

        private static void FailPending(
            PendingAction pending)
        {
            RefundLeftovers(pending);

            if (pending != null &&
                pending.Failed != null)
            {
                try
                {
                    pending.Failed();
                }
                catch
                {
                }
            }
        }

        internal static void Attach(
            PendingAction pending,
            Func<bool> stillValid,
            Action ready,
            Action failed)
        {
            if (pending == null)
                return;

            if (pending.Ready != null ||
                pending.Failed != null)
                return;

            pending.StillValid = stillValid;
            pending.Ready = ready;
            pending.Failed = failed;
        }
        internal static PendingAction PrepareName(
            RouteContext context,
            string name,
            int amount,
            int quality,
            bool matchWorldLevel)
        {
            var demand = new Demand
            {
                Name = name,
                Amount = amount,
                Quality = quality,
                MatchWorldLevel = matchWorldLevel
            };

            return Prepare(
                context,
                new List<Demand> { demand });
        }

        internal static PendingAction PrepareExactItem(
            RouteContext context,
            ItemDrop.ItemData item,
            int amount)
        {
            if (context == null ||
                item == null ||
                item.m_shared == null ||
                amount <= 0)
                return null;

            List<Container> matching =
                ContainerRegistry.GetMatching(context);

            Container source = null;

            for (int i = 0; i < matching.Count; i++)
            {
                Inventory inventory =
                    matching[i] != null
                        ? matching[i].GetInventory()
                        : null;

                if (inventory != null &&
                    inventory.ContainsItem(item))
                {
                    source = matching[i];
                    break;
                }
            }

            if (source == null ||
                !IsConcurrentRemote(source))
                return null;

            var demand = new Demand
            {
                Name = item.m_shared.m_name,
                Amount = amount,
                Quality = item.m_quality,
                MatchWorldLevel = false,
                PrefabName =
                    item.m_dropPrefab != null
                        ? item.m_dropPrefab.name
                        : null,
                SourceOnly = source,
                IgnorePlayer = true
            };

            return Prepare(
                context,
                new List<Demand> { demand });
        }

        internal static PendingAction Prepare(
            RouteContext context,
            List<Demand> demands)
        {
            if (!Active ||
                Executing ||
                context == null ||
                demands == null ||
                demands.Count == 0)
                return null;

            if (_pending != null)
                return _pending;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            if (containers.Count == 0)
                return null;

            var pending = new PendingAction
            {
                Context = context,
                Started = Time.realtimeSinceStartup
            };

            for (int i = 0;
                 i < containers.Count;
                 i++)
            {
                Container container = containers[i];

                if (IsConcurrentRemote(container))
                    pending.Concurrent.Add(container);
            }

            if (pending.Concurrent.Count == 0)
                return null;

            bool usedConcurrent = false;
            bool enough = true;

            for (int i = 0; i < demands.Count; i++)
            {
                Demand demand = demands[i];

                if (demand == null ||
                    string.IsNullOrEmpty(demand.Name) ||
                    demand.Amount <= 0)
                    continue;

                pending.Demands.Add(demand);

                int remaining = demand.Amount;

                if (!demand.IgnorePlayer)
                {
                    Player player = Player.m_localPlayer;
                    Inventory playerInventory =
                        player != null
                            ? player.GetInventory()
                            : null;

                    if (playerInventory != null)
                    {
                        remaining -= Mathf.Min(
                            remaining,
                            Count(
                                playerInventory,
                                demand));
                    }
                }

                if (remaining <= 0)
                    continue;

                for (int c = 0;
                     c < containers.Count &&
                     remaining > 0;
                     c++)
                {
                    Container container = containers[c];

                    if (container == null)
                        continue;

                    if (demand.SourceOnly != null &&
                        container != demand.SourceOnly)
                        continue;

                    Inventory inventory =
                        container.GetInventory();

                    if (inventory == null)
                        continue;

                    if (!pending.Concurrent.Contains(container))
                    {
                        int available =
                            Count(inventory, demand);

                        remaining -= Mathf.Min(
                            remaining,
                            available);
                        continue;
                    }

                    int reserved =
                        RequestFromConcurrent(
                            pending,
                            container,
                            inventory,
                            demand,
                            remaining);

                    if (reserved > 0)
                    {
                        usedConcurrent = true;
                        remaining -= reserved;
                    }
                }

                if (remaining > 0)
                    enough = false;
            }

            if (!usedConcurrent)
                return null;

            if (!enough &&
                pending.RequestIds.Count == 0)
            {
                pending.Blocked = true;
            }

            _pending = pending;
            return pending;
        }
        private static int RequestFromConcurrent(
            PendingAction pending,
            Container container,
            Inventory source,
            Demand demand,
            int amount)
        {
            if (pending == null ||
                container == null ||
                source == null ||
                demand == null ||
                amount <= 0)
                return 0;

            Inventory reserve;
            if (!pending.Reserves.TryGetValue(
                    container,
                    out reserve))
            {
                reserve =
                    new Inventory(
                        "Wire MUC Reservation",
                        null,
                        20,
                        20);

                pending.Reserves.Add(
                    container,
                    reserve);
            }

            int requested = 0;

            List<ItemDrop.ItemData> items =
                RefillSelection.BottomLeftFirst(source);

            for (int i = 0;
                 i < items.Count &&
                 requested < amount;
                 i++)
            {
                ItemDrop.ItemData item = items[i];

                if (!Matches(item, demand))
                    continue;

                int take =
                    Mathf.Min(
                        item.m_stack,
                        amount - requested);

                int requestId;
                int accepted =
                    RequestRemove(
                        container,
                        item,
                        reserve,
                        take,
                        out requestId);

                if (accepted <= 0)
                    continue;

                requested += accepted;

                if (requestId != 0)
                    pending.RequestIds.Add(requestId);
            }

            return requested;
        }

        private static int RequestRemove(
            Container container,
            ItemDrop.ItemData item,
            Inventory reserve,
            int amount,
            out int requestId)
        {
            requestId = 0;

            if (!_available ||
                container == null ||
                item == null ||
                reserve == null ||
                amount <= 0)
                return 0;

            Player player = Player.m_localPlayer;
            if (player == null)
                return 0;

            int before =
                reserve.CountItems(
                    item.m_shared.m_name,
                    item.m_quality,
                    false);

            try
            {
                object request =
                    _removeItemFromChest.Invoke(
                        null,
                        new object[]
                        {
                            container,
                            item,
                            reserve,
                            new Vector2i(-1, -1),
                            player.GetZDOID(),
                            amount,
                            null
                        });

                if (request == null)
                {
                    int after =
                        reserve.CountItems(
                            item.m_shared.m_name,
                            item.m_quality,
                            false);

                    return Mathf.Max(
                        0,
                        after - before);
                }

                Type type = request.GetType();

                PropertyInfo id =
                    type.GetProperty("RequestID");

                FieldInfo drag =
                    type.GetField("dragAmount");

                if (id != null)
                    requestId =
                        (int)id.GetValue(
                            request,
                            null);

                return drag != null
                    ? Mathf.Max(
                        0,
                        (int)drag.GetValue(request))
                    : amount;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "MultiUserChest reservation failed: " +
                        ex.Message);
                }

                return 0;
            }
        }
        private static bool CanSatisfy(
            PendingAction pending)
        {
            if (pending == null ||
                pending.Context == null)
                return false;

            List<Container> containers =
                ContainerRegistry.GetMatching(
                    pending.Context);

            for (int i = 0;
                 i < pending.Demands.Count;
                 i++)
            {
                Demand demand = pending.Demands[i];
                int remaining = demand.Amount;

                if (!demand.IgnorePlayer)
                {
                    Player player = Player.m_localPlayer;
                    Inventory playerInventory =
                        player != null
                            ? player.GetInventory()
                            : null;

                    if (playerInventory != null)
                    {
                        remaining -= Mathf.Min(
                            remaining,
                            Count(
                                playerInventory,
                                demand));
                    }
                }

                for (int c = 0;
                     c < containers.Count &&
                     remaining > 0;
                     c++)
                {
                    Container container = containers[c];

                    if (demand.SourceOnly != null &&
                        container != demand.SourceOnly)
                        continue;

                    Inventory inventory = null;

                    if (!pending.Reserves.TryGetValue(
                            container,
                            out inventory))
                    {
                        if (pending.Concurrent.Contains(
                                container))
                            continue;

                        inventory =
                            container != null
                                ? container.GetInventory()
                                : null;
                    }

                    if (inventory == null)
                        continue;

                    remaining -= Mathf.Min(
                        remaining,
                        Count(
                            inventory,
                            demand));
                }

                if (remaining > 0)
                    return false;
            }

            return true;
        }

        private static int Count(
            Inventory inventory,
            Demand demand)
        {
            if (inventory == null ||
                demand == null)
                return 0;

            int total = 0;
            List<ItemDrop.ItemData> items =
                inventory.GetAllItems();

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];

                if (Matches(item, demand))
                    total += item.m_stack;
            }

            return total;
        }

        private static bool Matches(
            ItemDrop.ItemData item,
            Demand demand)
        {
            if (item == null ||
                item.m_shared == null ||
                demand == null ||
                item.m_shared.m_name != demand.Name)
                return false;

            if (demand.Quality >= 0 &&
                item.m_quality != demand.Quality)
                return false;

            if (demand.MatchWorldLevel &&
                item.m_worldLevel < Game.m_worldLevel)
                return false;

            if (!string.IsNullOrEmpty(
                    demand.PrefabName))
            {
                if (item.m_dropPrefab == null ||
                    item.m_dropPrefab.name !=
                        demand.PrefabName)
                    return false;
            }

            return true;
        }
        private static bool RequestExists(int id)
        {
            if (!_available ||
                id == 0)
                return false;

            try
            {
                MethodInfo closed =
                    _getPackageGeneric.MakeGenericMethod(
                        _iPackageType);

                object package =
                    closed.Invoke(
                        null,
                        new object[] { id });

                return package != null;
            }
            catch
            {
                return false;
            }
        }

        internal static ItemDrop.ItemData FindReservedItem(
            string name,
            int quality,
            string prefabName)
        {
            if (_active == null)
                return null;

            List<Container> containers =
                ContainerRegistry.GetMatching(
                    _active.Context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory reserve;
                if (!_active.Reserves.TryGetValue(
                        containers[i],
                        out reserve))
                    continue;

                List<ItemDrop.ItemData> items =
                    RefillSelection.BottomLeftFirst(
                        reserve);

                for (int j = 0;
                     j < items.Count;
                     j++)
                {
                    ItemDrop.ItemData item = items[j];

                    if (item == null ||
                        item.m_shared == null ||
                        item.m_shared.m_name != name ||
                        (quality >= 0 &&
                         item.m_quality != quality))
                        continue;

                    if (!string.IsNullOrEmpty(prefabName) &&
                        (item.m_dropPrefab == null ||
                         item.m_dropPrefab.name !=
                            prefabName))
                        continue;

                    return item;
                }
            }

            return null;
        }

        internal static object[] RefreshItemArgs(
            object[] args)
        {
            if (args == null ||
                _active == null)
                return args;

            object[] copy =
                (object[])args.Clone();

            for (int i = 0; i < copy.Length; i++)
            {
                ItemDrop.ItemData oldItem =
                    copy[i] as ItemDrop.ItemData;

                if (oldItem == null ||
                    oldItem.m_shared == null)
                    continue;

                Player player = Player.m_localPlayer;

                if (player != null &&
                    player.GetInventory().ContainsItem(
                        oldItem))
                    continue;

                string prefab =
                    oldItem.m_dropPrefab != null
                        ? oldItem.m_dropPrefab.name
                        : null;

                ItemDrop.ItemData replacement =
                    FindReservedItem(
                        oldItem.m_shared.m_name,
                        oldItem.m_quality,
                        prefab);

                if (replacement != null)
                    copy[i] = replacement;
            }

            return copy;
        }
        private static void RefundLeftovers(
            PendingAction pending)
        {
            if (pending == null ||
                pending.Reserves.Count == 0)
                return;

            foreach (KeyValuePair<Container, Inventory> pair
                     in pending.Reserves)
            {
                if (pair.Key == null ||
                    pair.Value == null ||
                    pair.Value.NrOfItems() == 0)
                    continue;

                var job = new RefundJob
                {
                    Container = pair.Key,
                    Reserve = pair.Value,
                    NextTry = Time.realtimeSinceStartup
                };

                Refunds.Add(job);
            }
        }

        private static void UpdateRefunds()
        {
            if (!Active ||
                Refunds.Count == 0)
                return;

            for (int i = Refunds.Count - 1;
                 i >= 0;
                 i--)
            {
                RefundJob job = Refunds[i];

                if (job == null ||
                    job.Reserve == null)
                {
                    Refunds.RemoveAt(i);
                    continue;
                }

                bool waiting = false;

                for (int r = job.RequestIds.Count - 1;
                     r >= 0;
                     r--)
                {
                    int id = job.RequestIds[r];

                    if (RequestExists(id))
                    {
                        waiting = true;
                    }
                    else
                    {
                        job.RequestIds.RemoveAt(r);
                    }
                }

                if (waiting)
                    continue;

                if (job.Reserve.NrOfItems() == 0)
                {
                    Refunds.RemoveAt(i);
                    continue;
                }

                if (job.Container == null)
                {
                    ReturnReserveToPlayer(job.Reserve);
                    Refunds.RemoveAt(i);
                    continue;
                }

                if (Time.realtimeSinceStartup <
                    job.NextTry)
                    continue;

                job.NextTry =
                    Time.realtimeSinceStartup + 0.5f;

                StartRefund(job);
            }
        }

        private static void StartRefund(
            RefundJob job)
        {
            Player player = Player.m_localPlayer;

            if (job == null ||
                job.Container == null ||
                job.Reserve == null ||
                player == null)
                return;

            List<ItemDrop.ItemData> items =
                new List<ItemDrop.ItemData>(
                    job.Reserve.GetAllItems());

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];

                if (item == null ||
                    item.m_stack <= 0)
                    continue;

                try
                {
                    object request =
                        _addItemToChest.Invoke(
                            null,
                            new object[]
                            {
                                job.Container,
                                item,
                                job.Reserve,
                                new Vector2i(-1, -1),
                                player.GetZDOID(),
                                item.m_stack
                            });

                    if (request == null)
                        continue;

                    PropertyInfo id =
                        request.GetType()
                            .GetProperty("RequestID");

                    if (id != null)
                    {
                        int requestId =
                            (int)id.GetValue(
                                request,
                                null);

                        if (requestId != 0)
                            job.RequestIds.Add(
                                requestId);
                    }
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning(
                            "MultiUserChest refund failed: " +
                            ex.Message);
                    }
                }
            }
        }

        private static void ReturnReserveToPlayer(
            Inventory reserve)
        {
            Player player = Player.m_localPlayer;

            if (reserve == null ||
                player == null)
                return;

            Inventory target =
                player.GetInventory();

            List<ItemDrop.ItemData> items =
                new List<ItemDrop.ItemData>(
                    reserve.GetAllItems());

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];

                if (item == null)
                    continue;

                target.MoveItemToThis(
                    reserve,
                    item);
            }
        }

        internal static void Reset()
        {
            if (_pending != null)
                RefundLeftovers(_pending);

            if (_active != null)
                RefundLeftovers(_active);

            _pending = null;
            _active = null;

            _initialized = false;
            _available = false;
            _removeItemFromChest = null;
            _addItemToChest = null;
            _getPackageGeneric = null;
            _iPackageType = null;
        }
    }
}
