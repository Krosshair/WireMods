using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Quick MouseTweaks-style stack merges need a visible ITEM in flight.
    // A quantity pulse alone cannot communicate which slot supplied it.
    // Reuse the EXISTING on-demand SmoothUIDriver, no new MonoBehaviour
    // tick, no continuous inventory scan or extra independent update.
    internal static class QuickTransferEffects
    {
        private sealed class Flight
        {
            internal RectTransform Rect;
            internal Image Image;
            internal Vector3 From, To;
            internal float Time;
        }
        private static readonly List<Flight> Active = new List<Flight>(12);
        private static readonly Stack<Flight> Pool = new Stack<Flight>(12);
        private const float Duration = 0.085f;
        internal static bool Busy => Active.Count > 0;

        internal static void Launch(ItemDrop.ItemData item, Vector3 from, Vector3 to)
        {
            if (item == null || !WiresSmoothUIPlugin.Enabled.Value ||
                !WiresSmoothUIPlugin.ItemAnimations.Value ||
                !InventoryGui.IsVisible() || (from - to).sqrMagnitude < 4f)
                return;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || item.GetIcon() == null) return;

            Flight f = null;
            while (Pool.Count > 0 && (f == null || f.Rect == null))
                f = Pool.Pop();
            if (f == null || f.Rect == null)
            {
                if (Active.Count >= 12)
                {
                    f = Active[0];
                    Active.RemoveAt(0);
                }
                else
                {
                    GameObject go = new GameObject("SmoothUI Quick Transfer",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    RectTransform rect = go.GetComponent<RectTransform>();
                    Image image = go.GetComponent<Image>();
                    image.raycastTarget = false;
                    image.preserveAspect = true;
                    rect.sizeDelta = new Vector2(48f, 48f);
                    f = new Flight { Rect = rect, Image = image };
                }
            }
            if (f.Rect == null || f.Image == null) return;
            f.Rect.SetParent(gui.transform, false);
            f.Rect.SetAsLastSibling();
            f.Rect.sizeDelta = new Vector2(48f, 48f);
            f.Rect.localScale = Vector3.one;
            f.Rect.position = from;
            f.Image.sprite = item.GetIcon();
            f.Image.color = Color.white;
            f.From = from;
            f.To = to;
            f.Time = 0f;
            f.Rect.gameObject.SetActive(true);
            Active.Add(f);
            SmoothUIDriver.Wake();
        }

        internal static void Tick()
        {
            if (Active.Count == 0) return;
            if (!InventoryGui.IsVisible() || !WiresSmoothUIPlugin.ItemAnimations.Value)
            {
                RestoreAll();
                return;
            }
            float dt = WiresSmoothUIPlugin.Dt;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Flight f = Active[i];
                if (f.Rect == null)
                {
                    Active.RemoveAt(i);
                    continue;
                }
                f.Time += dt;
                float raw = Mathf.Clamp01(f.Time / Duration);
                float t = WiresSmoothUIPlugin.EaseOutCubic(raw);
                f.Rect.position = Vector3.LerpUnclamped(f.From, f.To, t);
                float opacity = raw < 0.70f ? 1f : (1f - raw) / 0.30f;
                f.Image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
                f.Rect.localScale = Vector3.one * (1f - 0.15f * t);
                if (raw < 1f) continue;
                Active.RemoveAt(i);
                f.Rect.gameObject.SetActive(false);
                if (Pool.Count < 12) Pool.Push(f);
                else Object.Destroy(f.Rect.gameObject);
            }
        }

        internal static void RestoreAll()
        {
            for (int i = 0; i < Active.Count; i++)
            {
                Flight f = Active[i];
                if (f.Rect == null) continue;
                f.Rect.gameObject.SetActive(false);
                if (Pool.Count < 12) Pool.Push(f);
                else Object.Destroy(f.Rect.gameObject);
            }
            Active.Clear();
            // Do not retain UI objects parented to a destroyed inventory
            // across world transitions or when Smooth UI is disabled.
            while (Pool.Count > 0)
            {
                Flight pooled = Pool.Pop();
                if (pooled.Rect != null) Object.Destroy(pooled.Rect.gameObject);
            }
        }
    }
}
