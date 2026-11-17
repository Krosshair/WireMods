using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    internal static class ItemAnimationSystem
    {
        private sealed class RefComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly RefComparer<T> Instance = new RefComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private enum PartKind { Other, Icon, Amount, EquipMarker, QueueMarker }

        private sealed class PartState
        {
            internal Transform Transform;
            internal PartKind Kind;
            internal Vector3 BasePosition;
            internal Vector3 BaseScale;
            internal Quaternion BaseRotation;
            internal Vector3 AppliedPosition;
            internal float AppliedScale = 1f;
            internal float AppliedRotation;
            internal bool Applied;
        }

        private sealed class ItemState
        {
            internal InventoryGrid Grid;
            internal GridContext Context;
            internal int ContextIndex = -1;
            internal int OccupiedIndex = -1;
            internal InventoryElement Element;
            internal ItemDrop.ItemData Item;
            internal int Stack;
            internal int StackDirection;
            internal bool Equipped;
            internal bool Queued;
            internal bool HasStatusSnapshot;
            internal int Generation;
            internal readonly List<PartState> Parts = new List<PartState>(8);
            internal float AppearTimer = 999f;
            internal float MoveTimer = 999f;
            internal float PulseTimer = 999f;
            internal float EquipTimer = 999f;
            internal float QueueTimer = 999f;
            internal Vector3 MoveFromWorld;
            internal int ActiveIndex = -1;
        }

        private sealed class GridContext
        {
            internal InventoryGrid Grid;
            internal Inventory Inventory;
            internal Action ChangedHandler;
            internal readonly List<ItemState> States = new List<ItemState>();
            internal readonly List<ItemState> Occupied = new List<ItemState>();
            internal bool Dirty = true;
            internal int Generation;
        }

        // Where an item was last shown: its grid and slot, plus the world
        // position at that time as a fallback. Move animations start from
        // the old slot's live position, so a panel that moved since (the
        // inventory opening or sliding in) can't make items fly in from a
        // stale spot.
        private sealed class PositionRecord
        {
            internal Vector3 World;
            internal float LastSeen;
            internal InventoryGrid Grid;
            internal int Index = -1;
        }

        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private static readonly Dictionary<InventoryElement, ItemState> States =
            new Dictionary<InventoryElement, ItemState>(RefComparer<InventoryElement>.Instance);

        private static readonly Dictionary<InventoryGrid, GridContext> Grids =
            new Dictionary<InventoryGrid, GridContext>(RefComparer<InventoryGrid>.Instance);

        private static readonly Dictionary<ItemDrop.ItemData, PositionRecord> LastPositions =
            new Dictionary<ItemDrop.ItemData, PositionRecord>(RefComparer<ItemDrop.ItemData>.Instance);

        private static readonly Dictionary<ItemDrop.ItemData, ItemState> ItemStates =
            new Dictionary<ItemDrop.ItemData, ItemState>(RefComparer<ItemDrop.ItemData>.Instance);

        private static readonly List<ItemState> Active = new List<ItemState>();
        // Items still gliding when their inventory changes again, with the
        // spot they are drawn at right now.
        private struct DropRecord
        {
            internal Vector3 World;
            internal float Time;
        }

        // Where a dragged item was let go, so it glides in from the cursor.
        private static readonly Dictionary<ItemDrop.ItemData, DropRecord> Dropped =
            new Dictionary<ItemDrop.ItemData, DropRecord>();

        internal static void NotifyDropped(ItemDrop.ItemData item, Vector3 world)
        {
            if (Dropped.Count > 16)
                Dropped.Clear();
            Dropped[item] = new DropRecord { World = world, Time = UnityEngine.Time.unscaledTime };
        }

        private static readonly Dictionary<ItemDrop.ItemData, Vector3> InFlight =
            new Dictionary<ItemDrop.ItemData, Vector3>(RefComparer<ItemDrop.ItemData>.Instance);
        private static readonly HashSet<ItemState> QueuedStates = new HashSet<ItemState>();
        private static readonly List<ItemDrop.ItemData> DeadItems = new List<ItemDrop.ItemData>();

        internal static void BindGrid(InventoryGrid grid, Inventory inventory)
        {
            if (grid == null)
                return;

            GridContext context = GetOrCreateContext(grid);
            if (ReferenceEquals(context.Inventory, inventory))
                return;

            if (context.Inventory != null && context.ChangedHandler != null)
                context.Inventory.m_onChanged -= context.ChangedHandler;

            context.Inventory = inventory;
            context.Dirty = true;

            if (inventory != null)
            {
                context.ChangedHandler = () => MarkDirty(grid);
                inventory.m_onChanged += context.ChangedHandler;
            }
            else
            {
                context.ChangedHandler = null;
            }
        }

        internal static void RegisterElement(InventoryElement element)
        {
            if (element == null || States.ContainsKey(element))
                return;

            InventoryGrid grid = element.GetComponentInParent<InventoryGrid>();
            if (grid == null)
                return;

            GridContext context = GetOrCreateContext(grid);
            ItemState state = CreateState(grid, element);
            state.Context = context;
            state.ContextIndex = context.States.Count;
            States.Add(element, state);
            context.States.Add(state);
            context.Dirty = true;

            SlotAnimationSystem.RegisterElement(element, grid);

            SmoothUIElementLifecycle lifecycle = element.GetComponent<SmoothUIElementLifecycle>();
            if (lifecycle == null)
                lifecycle = element.gameObject.AddComponent<SmoothUIElementLifecycle>();
            lifecycle.Bind(element);
        }

        private static GridContext GetOrCreateContext(InventoryGrid grid)
        {
            if (Grids.TryGetValue(grid, out GridContext context))
                return context;

            context = new GridContext { Grid = grid };
            Grids.Add(grid, context);

            SmoothUIGridLifecycle lifecycle = grid.GetComponent<SmoothUIGridLifecycle>();
            if (lifecycle == null)
                lifecycle = grid.gameObject.AddComponent<SmoothUIGridLifecycle>();
            lifecycle.Bind(grid);

            return context;
        }

        private static void MarkDirty(InventoryGrid grid)
        {
            if (grid != null && Grids.TryGetValue(grid, out GridContext context))
                context.Dirty = true;
        }

        internal static void RestoreGrid(InventoryGrid grid)
        {
            if (grid == null || Active.Count == 0)
                return;

            for (int i = 0; i < Active.Count; i++)
            {
                ItemState state = Active[i];
                if (state.Grid == grid)
                    RestoreState(state);
            }
        }

        internal static void CaptureGrid(InventoryGrid grid)
        {
            if (grid == null || !Grids.TryGetValue(grid, out GridContext context))
                return;

            if (context.Dirty)
                RefreshGrid(context);

            if (Active.Count == 0)
                return;

            for (int i = 0; i < Active.Count; i++)
            {
                ItemState state = Active[i];
                if (state.Grid == grid && state.Element != null)
                    CaptureBaselines(state);
            }
        }

        private static void RefreshGrid(GridContext context)
        {
            context.Dirty = false;
            Inventory inventory = context.Inventory ?? context.Grid.GetInventory();
            if (inventory == null)
                return;

            List<InventoryElement> elements;
            try
            {
                elements = Elements(context.Grid);
            }
            catch
            {
                return;
            }

            if (elements == null)
                return;

            int generation = ++context.Generation;
            // Some inventories change twice in a row (the player inventory
            // does on sort). Items already gliding continue from where they
            // are drawn, instead of jumping to the in-between slot.
            InFlight.Clear();
            float moveDuration = Mathf.Max(0.01f, WiresSmoothUIPlugin.ItemMoveDuration.Value);
            for (int i = 0; i < context.Occupied.Count; i++)
            {
                ItemState moving = context.Occupied[i];
                if (moving == null || moving.Item == null || moving.Element == null || moving.MoveTimer >= moveDuration)
                    continue;
                float t = WiresSmoothUIPlugin.EaseOutCubic(Mathf.Clamp01(moving.MoveTimer / moveDuration));
                Vector3 rest = GetPrimaryWorld(moving);
                InFlight[moving.Item] = rest + (moving.MoveFromWorld - rest) * (1f - t);
            }
            int width = Mathf.Max(1, inventory.GetWidth());
            float now = Time.unscaledTime;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null)
                    continue;

                int index = item.m_gridPos.y * width + item.m_gridPos.x;
                if (index < 0 || index >= elements.Count)
                    continue;

                InventoryElement element = elements[index];
                if (element == null)
                    continue;

                if (!States.TryGetValue(element, out ItemState state))
                {
                    RegisterElement(element);
                    if (!States.TryGetValue(element, out state))
                        continue;
                }

                state.Generation = generation;
                state.Grid = context.Grid;
                UpdateItemState(state, element, item, now, context.Grid, index);
            }

            for (int i = context.Occupied.Count - 1; i >= 0; i--)
            {
                ItemState state = context.Occupied[i];
                if (state == null || state.Element == null || state.Generation != generation)
                    ClearItemState(state);
            }

            PruneOldPositions(now);
        }

        private static void UpdateItemState(
            ItemState state,
            InventoryElement element,
            ItemDrop.ItemData item,
            float now,
            InventoryGrid grid,
            int index)
        {
            int stack = item.m_stack;

            if (!ReferenceEquals(item, state.Item))
            {
                QueuedStates.Remove(state);
                state.StackDirection = 0;
                state.Queued = false;

                ItemDrop.ItemData oldItem = state.Item;
                if (oldItem != null
                    && ItemStates.TryGetValue(oldItem, out ItemState oldState)
                    && ReferenceEquals(oldState, state))
                    ItemStates.Remove(oldItem);

                state.Item = item;
                ItemStates[item] = state;
                EnsureOccupied(state);

                state.Stack = stack;
                state.PulseTimer = 999f;
                state.EquipTimer = 999f;
                state.QueueTimer = 999f;
                state.Equipped = item.m_equipped;
                state.Queued = element.m_queued != null && element.m_queued.enabled;
                if (state.Queued)
                    QueuedStates.Add(state);
                state.HasStatusSnapshot = true;

                Vector3 currentWorld = GetPrimaryWorld(state);
                Vector3 fromWorld = Vector3.zero;
                bool moved = false;
                // No age limit: a grid only refreshes when its inventory
                // changes, so the record always holds where the item was last
                // shown, even if nothing changed for a long time. (A time
                // limit here made items in an untouched player inventory pop
                // in instead of sliding when sorted.)
                if (LastPositions.TryGetValue(item, out PositionRecord previous))
                {
                    fromWorld = previous.World;
                    if (TryGetSlotWorld(previous.Grid, previous.Index, out Vector3 live))
                        fromWorld = live;
                    moved = (fromWorld - currentWorld).sqrMagnitude > 1f;
                }

                if (InFlight.TryGetValue(item, out Vector3 drawn))
                {
                    fromWorld = drawn;
                    moved = (fromWorld - currentWorld).sqrMagnitude > 1f;
                }

                if (Dropped.TryGetValue(item, out DropRecord drop))
                {
                    Dropped.Remove(item);
                    if (Time.unscaledTime - drop.Time < 0.5f)
                    {
                        fromWorld = drop.World;
                        moved = (fromWorld - currentWorld).sqrMagnitude > 1f;
                    }
                }

                if (moved)
                {
                    state.MoveFromWorld = fromWorld;
                    state.MoveTimer = 0f;
                    state.AppearTimer = 999f;
                }
                else
                {
                    state.MoveTimer = 999f;
                    state.AppearTimer = 0f;
                }

                Activate(state);
            }
            else
            {
                if (stack != state.Stack)
                {
                    state.StackDirection = stack > state.Stack ? 1 : -1;
                    state.Stack = stack;
                    state.PulseTimer = 0f;
                    Activate(state);
                }

                bool equipped = item.m_equipped;
                if (state.HasStatusSnapshot && equipped != state.Equipped)
                {
                    state.Equipped = equipped;
                    state.EquipTimer = 0f;
                    Activate(state);
                }

                bool queued = element.m_queued != null && element.m_queued.enabled;
                if (state.HasStatusSnapshot && queued != state.Queued)
                {
                    state.Queued = queued;
                    state.QueueTimer = 0f;
                    Activate(state);

                    if (queued)
                        QueuedStates.Add(state);
                    else
                        QueuedStates.Remove(state);
                }

                state.HasStatusSnapshot = true;
            }

            if (!LastPositions.TryGetValue(item, out PositionRecord record))
            {
                record = new PositionRecord();
                LastPositions.Add(item, record);
            }

            record.World = GetPrimaryWorld(state);
            record.LastSeen = now;
            record.Grid = grid;
            record.Index = index;
        }

        // The current on-screen position of a slot's icon, if that grid and
        // slot still exist and are shown.
        private static bool TryGetSlotWorld(InventoryGrid grid, int index, out Vector3 world)
        {
            world = Vector3.zero;
            if (grid == null || index < 0 || !grid.gameObject.activeInHierarchy)
                return false;

            List<InventoryElement> elements;
            try
            {
                elements = Elements(grid);
            }
            catch
            {
                return false;
            }

            if (elements == null || index >= elements.Count)
                return false;

            InventoryElement element = elements[index];
            if (element == null || !element.gameObject.activeInHierarchy)
                return false;

            // A slot whose icon is mid-animation reports its resting spot.
            if (States.TryGetValue(element, out ItemState other) && other.Parts.Count > 0
                && element.m_icon != null && other.Parts[0].Transform == element.m_icon.transform)
            {
                Transform icon = element.m_icon.transform;
                world = icon.parent != null
                    ? icon.parent.TransformPoint(other.Parts[0].BasePosition)
                    : icon.position;
                return true;
            }

            world = element.m_icon != null ? element.m_icon.transform.position : element.transform.position;
            return true;
        }

        private static void EnsureOccupied(ItemState state)
        {
            if (state.OccupiedIndex >= 0 || state.Context == null)
                return;

            state.OccupiedIndex = state.Context.Occupied.Count;
            state.Context.Occupied.Add(state);
        }

        private static void RemoveOccupied(ItemState state)
        {
            GridContext context = state?.Context;
            if (context == null || state.OccupiedIndex < 0)
                return;

            int index = state.OccupiedIndex;
            int last = context.Occupied.Count - 1;
            if (index >= 0 && index <= last)
            {
                if (index != last)
                {
                    ItemState moved = context.Occupied[last];
                    context.Occupied[index] = moved;
                    moved.OccupiedIndex = index;
                }

                context.Occupied.RemoveAt(last);
            }

            state.OccupiedIndex = -1;
        }

        private static void ClearItemState(ItemState state)
        {
            if (state == null)
                return;

            RestoreState(state);
            Deactivate(state);
            QueuedStates.Remove(state);

            if (state.Item != null
                && ItemStates.TryGetValue(state.Item, out ItemState mapped)
                && ReferenceEquals(mapped, state))
                ItemStates.Remove(state.Item);

            state.Item = null;
            state.Stack = 0;
            state.StackDirection = 0;
            state.Equipped = false;
            state.Queued = false;
            state.HasStatusSnapshot = false;
            state.AppearTimer = 999f;
            state.MoveTimer = 999f;
            state.PulseTimer = 999f;
            state.EquipTimer = 999f;
            state.QueueTimer = 999f;
            RemoveOccupied(state);
        }

        internal static void UnregisterElement(InventoryElement element)
        {
            if (ReferenceEquals(element, null)
                || !States.TryGetValue(element, out ItemState state))
                return;

            RemoveState(state);
        }

        private static void RemoveState(ItemState state)
        {
            if (state == null)
                return;

            ClearItemState(state);

            if (!ReferenceEquals(state.Element, null))
            {
                SlotAnimationSystem.UnregisterElement(state.Element);
                States.Remove(state.Element);
            }

            GridContext context = state.Context;
            if (context != null && state.ContextIndex >= 0)
            {
                int index = state.ContextIndex;
                int last = context.States.Count - 1;
                if (index >= 0 && index <= last)
                {
                    if (index != last)
                    {
                        ItemState moved = context.States[last];
                        context.States[index] = moved;
                        moved.ContextIndex = index;
                    }

                    context.States.RemoveAt(last);
                }
            }

            state.Context = null;
            state.ContextIndex = -1;
            state.Element = null;
        }

        private static ItemState CreateState(InventoryGrid grid, InventoryElement element)
        {
            ItemState state = new ItemState
            {
                Grid = grid,
                Element = element
            };

            AddPart(state, element.m_icon != null ? element.m_icon.transform : null, PartKind.Icon);
            AddPart(state, element.m_amount != null ? element.m_amount.transform : null, PartKind.Amount);
            AddPart(state, element.m_quality != null ? element.m_quality.transform : null, PartKind.Other);
            AddPart(state, element.m_equiped != null ? element.m_equiped.transform : null, PartKind.EquipMarker);
            AddPart(state, element.m_queued != null ? element.m_queued.transform : null, PartKind.QueueMarker);
            AddPart(state, element.m_noteleport != null ? element.m_noteleport.transform : null, PartKind.Other);
            AddPart(state, element.m_food != null ? element.m_food.transform : null, PartKind.Other);
            AddPart(state, element.m_durability != null ? element.m_durability.transform : null, PartKind.Other);
            CaptureBaselines(state);
            return state;
        }

        private static void AddPart(ItemState state, Transform transform, PartKind kind)
        {
            if (transform == null)
                return;

            state.Parts.Add(new PartState
            {
                Transform = transform,
                Kind = kind,
                BasePosition = transform.localPosition,
                BaseScale = transform.localScale,
                BaseRotation = transform.localRotation
            });
        }

        private static void CaptureBaselines(ItemState state)
        {
            for (int i = 0; i < state.Parts.Count; i++)
            {
                PartState part = state.Parts[i];
                if (part.Transform == null)
                    continue;

                part.BasePosition = part.Transform.localPosition;
                part.BaseScale = part.Transform.localScale;
                part.BaseRotation = part.Transform.localRotation;
                part.Applied = false;
                part.AppliedPosition = Vector3.zero;
                part.AppliedScale = 1f;
                part.AppliedRotation = 0f;
            }
        }

        internal static bool Busy => Active.Count > 0;

        internal static void Tick()
        {
            if (!WiresSmoothUIPlugin.ItemAnimations.Value)
            {
                RestoreActive();
                return;
            }

            if (Active.Count == 0)
                return;

            float dt = WiresSmoothUIPlugin.Dt;
            float appearDuration = Mathf.Max(0.01f, WiresSmoothUIPlugin.ItemAppearDuration.Value);
            float moveDuration = Mathf.Max(0.01f, WiresSmoothUIPlugin.ItemMoveDuration.Value);
            float stackPulseScale = Mathf.Max(1f, WiresSmoothUIPlugin.StackPulseScale.Value) - 1f;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                ItemState state = Active[i];
                InventoryElement element = state.Element;

                if (element == null || state.Item == null
                    || element.m_icon == null || !element.m_icon.enabled
                    || !element.gameObject.activeInHierarchy)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                    continue;
                }

                state.AppearTimer += dt;
                state.MoveTimer += dt;
                state.PulseTimer += dt;
                state.EquipTimer += dt;
                state.QueueTimer += dt;

                float scale = 1f;
                float amountScale = 1f;
                float equipMarkerScale = 1f;
                float queueMarkerScale = 1f;
                float iconRotation = 0f;

                if (state.AppearTimer < appearDuration)
                {
                    float t = state.AppearTimer / appearDuration;
                    scale *= WiresSmoothUIPlugin.PopScale(t, 0.78f);
                }

                const float pulseDuration = 0.16f;
                if (state.PulseTimer < pulseDuration)
                {
                    float t = Mathf.Clamp01(state.PulseTimer / pulseDuration);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    scale *= 1f + stackPulseScale * pulse;

                    if (state.StackDirection < 0)
                        amountScale *= 1f - 0.08f * pulse;
                    else
                        amountScale *= 1f + 0.12f * pulse;
                }

                const float statusDuration = 0.18f;
                if (state.EquipTimer < statusDuration)
                {
                    float t = Mathf.Clamp01(state.EquipTimer / statusDuration);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    scale *= 1f + 0.035f * pulse;
                    iconRotation += (state.Equipped ? -2f : 2f) * pulse;
                    if (state.Equipped)
                        equipMarkerScale = WiresSmoothUIPlugin.PopScale(t, 0.65f);
                }

                if (state.QueueTimer < statusDuration)
                {
                    float t = Mathf.Clamp01(state.QueueTimer / statusDuration);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    scale *= 1f + 0.02f * pulse;
                    if (state.Queued)
                        queueMarkerScale = WiresSmoothUIPlugin.PopScale(t, 0.68f);
                }

                Vector3 worldMove = Vector3.zero;
                if (state.MoveTimer < moveDuration)
                {
                    float rawT = Mathf.Clamp01(state.MoveTimer / moveDuration);
                    float t = WiresSmoothUIPlugin.EaseOutCubic(rawT);
                    Vector3 currentWorld = GetPrimaryWorld(state);
                    worldMove = Vector3.LerpUnclamped(
                        state.MoveFromWorld - currentWorld,
                        Vector3.zero,
                        t);

                    if (rawT > 0.55f)
                    {
                        float landT = (rawT - 0.55f) / 0.45f;
                        scale *= 1f + Mathf.Sin(landT * Mathf.PI) * 0.04f;
                    }
                }

                ApplyState(
                    state,
                    scale,
                    amountScale,
                    equipMarkerScale,
                    queueMarkerScale,
                    iconRotation,
                    worldMove);

                bool timersDone = state.AppearTimer >= appearDuration
                    && state.MoveTimer >= moveDuration
                    && state.PulseTimer >= pulseDuration
                    && state.EquipTimer >= statusDuration
                    && state.QueueTimer >= statusDuration;

                if (timersDone)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                }
            }
        }

        private static void ApplyState(
            ItemState state,
            float scale,
            float amountScale,
            float equipMarkerScale,
            float queueMarkerScale,
            float iconRotation,
            Vector3 worldMove)
        {
            bool hasMove = worldMove.sqrMagnitude > 0f;
            Transform lastParent = null;
            Vector3 lastLocalMove = Vector3.zero;
            for (int i = 0; i < state.Parts.Count; i++)
            {
                PartState part = state.Parts[i];
                Transform transform = part.Transform;
                if (transform == null)
                    continue;

                // Parts of one slot share a parent, so the world to local
                // conversion is done once per parent, not per part.
                Vector3 localMove = Vector3.zero;
                if (hasMove)
                {
                    Transform parent = transform.parent;
                    if (!ReferenceEquals(parent, lastParent))
                    {
                        lastParent = parent;
                        lastLocalMove = parent != null ? parent.InverseTransformVector(worldMove) : worldMove;
                    }
                    localMove = lastLocalMove;
                }

                float partScale = scale;
                float appliedRotation = 0f;
                switch (part.Kind)
                {
                    case PartKind.Icon: appliedRotation = iconRotation; break;
                    case PartKind.Amount: partScale *= amountScale; break;
                    case PartKind.EquipMarker: partScale *= equipMarkerScale; break;
                    case PartKind.QueueMarker: partScale *= queueMarkerScale; break;
                }

                transform.localPosition = part.BasePosition + localMove;
                transform.localScale = part.BaseScale * partScale;
                transform.localRotation = appliedRotation != 0f
                    ? part.BaseRotation * Quaternion.Euler(0f, 0f, appliedRotation)
                    : part.BaseRotation;

                part.AppliedPosition = localMove;
                part.AppliedScale = partScale;
                part.AppliedRotation = appliedRotation;
                part.Applied = true;
            }
        }

        private static Vector3 GetPrimaryWorld(ItemState state)
        {
            if (state.Element != null && state.Element.m_icon != null)
                return state.Element.m_icon.transform.position;
            return state.Element != null ? state.Element.transform.position : Vector3.zero;
        }

        private static void RestoreState(ItemState state)
        {
            if (state == null)
                return;

            for (int i = 0; i < state.Parts.Count; i++)
            {
                PartState part = state.Parts[i];
                Transform transform = part.Transform;
                if (transform == null || !part.Applied)
                    continue;

                Vector3 expectedPosition = part.BasePosition + part.AppliedPosition;
                Vector3 expectedScale = part.BaseScale * part.AppliedScale;
                Quaternion expectedRotation =
                    part.BaseRotation * Quaternion.Euler(0f, 0f, part.AppliedRotation);

                if ((transform.localPosition - expectedPosition).sqrMagnitude < 0.0009f)
                    transform.localPosition = part.BasePosition;
                else
                    part.BasePosition = transform.localPosition;

                if ((transform.localScale - expectedScale).sqrMagnitude < 0.0009f)
                    transform.localScale = part.BaseScale;
                else
                    part.BaseScale = transform.localScale;

                if (Quaternion.Angle(transform.localRotation, expectedRotation) < 0.05f)
                    transform.localRotation = part.BaseRotation;
                else
                    part.BaseRotation = transform.localRotation;

                part.Applied = false;
                part.AppliedPosition = Vector3.zero;
                part.AppliedScale = 1f;
                part.AppliedRotation = 0f;
            }
        }

        private static void Activate(ItemState state)
        {
            if (state.ActiveIndex >= 0)
                return;

            state.ActiveIndex = Active.Count;
            Active.Add(state);
            SmoothUIDriver.Wake();
        }

        private static void Deactivate(ItemState state)
        {
            if (state.ActiveIndex >= 0)
                DeactivateAt(state.ActiveIndex);
        }

        private static void DeactivateAt(int index)
        {
            int last = Active.Count - 1;
            ItemState removed = Active[index];
            if (index != last)
            {
                ItemState moved = Active[last];
                Active[index] = moved;
                moved.ActiveIndex = index;
            }

            Active.RemoveAt(last);
            removed.ActiveIndex = -1;
        }

        private static void RestoreActive()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                ItemState state = Active[i];
                RestoreState(state);
                DeactivateAt(i);
            }
        }

        internal static void NotifyEquipment(ItemDrop.ItemData item, bool equipped)
        {
            if (item == null || !ItemStates.TryGetValue(item, out ItemState state))
                return;

            state.Equipped = equipped;
            state.HasStatusSnapshot = true;
            state.EquipTimer = 0f;
            Activate(state);
        }

        internal static void NotifyQueue(ItemDrop.ItemData item, bool queued)
        {
            if (item == null || !ItemStates.TryGetValue(item, out ItemState state))
                return;

            if (state.Queued == queued && state.HasStatusSnapshot)
                return;

            state.Queued = queued;
            state.HasStatusSnapshot = true;
            state.QueueTimer = 0f;
            Activate(state);

            if (queued)
                QueuedStates.Add(state);
            else
                QueuedStates.Remove(state);
        }

        internal static void NotifyAllQueuesCleared()
        {
            foreach (ItemState state in QueuedStates)
            {
                if (state == null)
                    continue;

                state.Queued = false;
                state.HasStatusSnapshot = true;
                state.QueueTimer = 0f;
                Activate(state);
            }

            QueuedStates.Clear();
        }

        private static void PruneOldPositions(float now)
        {
            if (LastPositions.Count < 512)
                return;

            DeadItems.Clear();
            foreach (KeyValuePair<ItemDrop.ItemData, PositionRecord> pair in LastPositions)
            {
                // Items still shown in some grid keep their record.
                if (pair.Key != null && ItemStates.ContainsKey(pair.Key))
                    continue;
                if (pair.Key == null || now - pair.Value.LastSeen > 300f)
                    DeadItems.Add(pair.Key);
            }

            for (int i = 0; i < DeadItems.Count; i++)
                LastPositions.Remove(DeadItems[i]);
        }
        internal static void UnregisterGrid(InventoryGrid grid)
        {
            if (ReferenceEquals(grid, null) || !Grids.TryGetValue(grid, out GridContext context))
                return;

            if (context.Inventory != null && context.ChangedHandler != null)
                context.Inventory.m_onChanged -= context.ChangedHandler;

            for (int i = context.States.Count - 1; i >= 0; i--)
                RemoveState(context.States[i]);

            Grids.Remove(grid);
        }

        internal static void RestoreAll()
        {
            RestoreActive();
        }

        internal static void Shutdown()
        {
            RestoreActive();

            foreach (KeyValuePair<InventoryGrid, GridContext> pair in Grids)
            {
                GridContext context = pair.Value;
                if (context.Inventory != null && context.ChangedHandler != null)
                    context.Inventory.m_onChanged -= context.ChangedHandler;

                for (int i = context.States.Count - 1; i >= 0; i--)
                    RemoveState(context.States[i]);
            }

            States.Clear();
            Grids.Clear();
            ItemStates.Clear();
            QueuedStates.Clear();
            LastPositions.Clear();
            InFlight.Clear();
            DeadItems.Clear();
        }
    }

    internal sealed class SmoothUIElementLifecycle : MonoBehaviour
    {
        private InventoryElement _element;

        internal void Bind(InventoryElement element)
        {
            _element = element;
        }

        private void OnDestroy()
        {
            ItemAnimationSystem.UnregisterElement(_element);
        }
    }

    internal sealed class SmoothUIGridLifecycle : MonoBehaviour
    {
        private InventoryGrid _grid;

        internal void Bind(InventoryGrid grid)
        {
            _grid = grid;
        }

        private void OnDestroy()
        {
            ItemAnimationSystem.UnregisterGrid(_grid);
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateInventory")]
    internal static class SmoothUIInventoryGridBindPatch
    {
        [HarmonyPrefix]
        private static void Prefix(InventoryGrid __instance, Inventory inventory)
        {
            ItemAnimationSystem.BindGrid(__instance, inventory);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem", new Type[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class SmoothUIEquipItemPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer)
            {
                ItemAnimationSystem.NotifyEquipment(item, true);
                ArmorPop.EquipChanged(item);
                HudVisibilityReworkSystem
                    .NotifyHotbarEquipmentChanged();
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UnequipItem", new Type[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class SmoothUIUnequipItemPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemDrop.ItemData item, ref bool __state)
        {
            __state = item != null && item.m_equipped;
        }

        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __state)
        {
            if (__state && item != null && !item.m_equipped && __instance == Player.m_localPlayer)
            {
                ItemAnimationSystem.NotifyEquipment(item, false);
                ArmorPop.EquipChanged(item);
                HudVisibilityReworkSystem
                    .NotifyHotbarEquipmentChanged();
            }
        }
    }

    [HarmonyPatch(typeof(Player), "QueueEquipAction")]
    internal static class SmoothUIQueueEquipPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item)
        {
            if (__instance == Player.m_localPlayer)
            {
                bool queued = __instance.IsEquipActionQueued(item);
                ItemAnimationSystem.NotifyQueue(item, queued);
                EquipFillSystem.Begin();
            }
        }
    }

    [HarmonyPatch(typeof(Player), "QueueUnequipAction")]
    internal static class SmoothUIQueueUnequipPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item)
        {
            if (__instance == Player.m_localPlayer)
            {
                bool queued = __instance.IsEquipActionQueued(item);
                ItemAnimationSystem.NotifyQueue(item, queued);
                EquipFillSystem.Begin();
            }
        }
    }

    [HarmonyPatch(typeof(Player), "RemoveEquipAction")]
    internal static class SmoothUIRemoveEquipActionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item)
        {
            if (__instance == Player.m_localPlayer)
            {
                bool queued = __instance.IsEquipActionQueued(item);
                ItemAnimationSystem.NotifyQueue(item, queued);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "ClearActionQueue")]
    internal static class SmoothUIClearActionQueuePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
            {
                ItemAnimationSystem.NotifyAllQueuesCleared();
            }
        }
    }

    [HarmonyPatch(typeof(InventoryElement), "Initialize")]
    internal static class SmoothUIInventoryElementInitializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryElement __instance)
        {
            ItemAnimationSystem.RegisterElement(__instance);
        }
    }
}
