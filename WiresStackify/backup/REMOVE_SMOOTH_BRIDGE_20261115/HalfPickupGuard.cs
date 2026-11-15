using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WiresStackify
{
    // Valheim's UpdateItemDrag cancels EVERY drag while RMB is down.
    // Mouse Tweaks assigns RMB to placing from the held stack, so its native
    // cancellation conflicts with BOTH half pickup and continued drag-place.
    // Suppress ONLY that one right-click cancellation while holding an item
    // in the inventory. Other keyboard/closing cleanup stays native.
    internal static class HalfPickupGuard
    {
        private static bool RightClickCancelsDrag(int button)
        {
            bool pressed = ZInput.GetMouseButton(button);
            // MouseTweaks RMB while carrying means PLACE ONE, not CANCEL.
            // This also covers the same initiating RMB that picked up half.
            // Native keyboard/ESC or inventory-close cleanup remains intact.
            return pressed && !(button == 1 && MouseControls.HasHeldStack &&
                                InventoryGui.IsVisible());
        }
        // In native UpdateItemDrag the method only asks about RMB once.
        // If a game update changes that contract, leave vanilla intact.
        [HarmonyPatch(typeof(InventoryGui), "UpdateItemDrag")]
        private static class GuardNativeDragCancel
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> original)
            {
                var list = new List<CodeInstruction>(original);
                MethodInfo mouse = AccessTools.Method(typeof(ZInput), "GetMouseButton", new[] { typeof(int) });
                MethodInfo replacement = AccessTools.Method(typeof(HalfPickupGuard), nameof(RightClickCancelsDrag));
                int count = 0;
                foreach (CodeInstruction instruction in list)
                    if (instruction.operand is MethodInfo call &&
                        call.Name == mouse.Name && call.DeclaringType == mouse.DeclaringType)
                        count++;
                if (count != 1)
                {
                    StackifyPlugin.Log?.LogWarning("Mouse half-stack guard expects one RMB check in InventoryGui.UpdateItemDrag, found " + count);
                    return list;
                }
                foreach (CodeInstruction instruction in list)
                {
                    if (!(instruction.operand is MethodInfo call) ||
                        call.Name != mouse.Name || call.DeclaringType != mouse.DeclaringType) continue;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    break;
                }
                return list;
            }
        }
    }
}
