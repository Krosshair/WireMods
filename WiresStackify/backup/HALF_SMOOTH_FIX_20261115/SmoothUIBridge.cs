using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    // Optional compatibility with Wire's Smooth UI, with no hard assembly
    // reference and no new Update/coroutine/polling. Smooth UI already reacts
    // to Inventory.Changed for stack pulses and same-reference moves. Stackify
    // only needs to tell it where NEW item clones came from (mouse transfers).
    internal static class SmoothUIBridge
    {
        private static bool _resolved;
        private static MethodInfo _notifyDropped;
        private static readonly object[] Args = new object[2];

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!Chainloader.PluginInfos.ContainsKey("wire.valheim.smoothui")) return;
            Type type = AccessTools.TypeByName("WiresSmoothUI.ItemAnimationSystem");
            _notifyDropped = type == null ? null : AccessTools.Method(type, "NotifyDropped",
                new[] { typeof(ItemDrop.ItemData), typeof(Vector3) });
        }

        internal static void NewItem(ItemDrop.ItemData item, Vector3 fromWorld)
        {
            if (item == null) return;
            Resolve();
            if (_notifyDropped == null) return;
            try
            {
                Args[0] = item;
                Args[1] = fromWorld;
                _notifyDropped.Invoke(null, Args);
            }
            catch (Exception e)
            {
                // Treat a changed/missing optional API as unavailable; never
                // interrupt a successful inventory transfer or spam logs.
                _notifyDropped = null;
                StackifyPlugin.Log?.LogWarning("Smooth UI item animation bridge disabled: " + e.Message);
            }
            finally
            {
                Args[0] = null;
                Args[1] = null;
            }
        }
    }
}
