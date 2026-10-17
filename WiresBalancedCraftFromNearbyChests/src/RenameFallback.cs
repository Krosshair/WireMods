using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class RenameFallback
    {
        private static Container _textInputForContainer;
        private static bool? _externalRenamer;

        private static readonly MethodInfo ShowTextInputMethod =
            AccessTools.Method(
                typeof(TextInput),
                "Show",
                new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(int)
                });

        internal static bool Active
        {
            get
            {
                return ModConfig.EnableCustomNames != null &&
                       ModConfig.EnableCustomNames.Value &&
                       !HasExternalRenamer();
            }
        }

        internal static void ResetDetection()
        {
            _externalRenamer = null;
        }

        private static bool HasExternalRenamer()
        {
            if (_externalRenamer.HasValue)
                return _externalRenamer.Value;

            if (Chainloader.PluginInfos.ContainsKey(
                    "shudnal.MyLittleUI"))
            {
                _externalRenamer = true;
                return true;
            }

            MethodBase interact =
                AccessTools.Method(
                    typeof(Container),
                    nameof(Container.Interact));

            MethodBase setText =
                AccessTools.Method(
                    typeof(TextInput),
                    "setText");

            HashSet<string> interactionOwners =
                GetOtherOwners(interact);

            HashSet<string> textOwners =
                GetOtherOwners(setText);

            interactionOwners.IntersectWith(
                textOwners);

            _externalRenamer =
                interactionOwners.Count > 0;

            if (_externalRenamer.Value &&
                Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "Another chest renamer is active; Wire's rename fallback is disabled.");
            }

            return _externalRenamer.Value;
        }

        private static HashSet<string> GetOtherOwners(
            MethodBase method)
        {
            var result =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (method == null)
                return result;

            Patches patches =
                Harmony.GetPatchInfo(method);

            if (patches == null)
                return result;

            foreach (string owner in patches.Owners)
            {
                if (!string.Equals(
                        owner,
                        Plugin.Guid,
                        StringComparison.Ordinal))
                    result.Add(owner);
            }

            return result;
        }

        internal static bool BeginRename(
            Container container,
            Humanoid character,
            bool hold,
            bool alt)
        {
            if (!Active ||
                container == null ||
                !alt ||
                hold)
                return false;

            if (container.m_checkGuardStone &&
                !PrivateArea.CheckAccess(
                    container.transform.position,
                    0f,
                    true,
                    false))
            {
                character.Message(
                    MessageHud.MessageType.Center,
                    "$piece_noaccess");
                return true;
            }

            if (!ContainerRegistry.IsUsable(container))
            {
                character.Message(
                    MessageHud.MessageType.Center,
                    "$msg_cantopen");
                return true;
            }

            ZNetView nview =
                ContainerRegistry.GetNetView(container);

            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null ||
                TextInput.instance == null)
                return true;

            _textInputForContainer = container;

            if (ShowTextInputMethod == null)
                return true;

            ShowTextInputMethod.Invoke(
                TextInput.instance,
                new object[]
                {
                    "$hud_rename " + container.m_name,
                    nview.GetZDO().GetString(
                        ZDOVars.s_text,
                        ""),
                    32
                });

            return true;
        }

        internal static void CommitRename(
            string text)
        {
            if (!Active ||
                _textInputForContainer == null)
                return;

            Container container =
                _textInputForContainer;

            _textInputForContainer = null;

            ZNetView nview =
                ContainerRegistry.GetNetView(
                    container);

            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null)
                return;

            nview.GetZDO().Set(
                ZDOVars.s_text,
                text ?? "");

            ContainerRegistry.NotifyTitleChanged(container);
        }

        internal static void CancelRename()
        {
            _textInputForContainer = null;
        }

        internal static string BuildHoverText(
            Container container,
            string vanilla)
        {
            if (!Active ||
                container == null)
                return vanilla;

            ZNetView nview =
                ContainerRegistry.GetNetView(
                    container);

            if (nview == null ||
                !nview.IsValid() ||
                nview.GetZDO() == null)
                return vanilla;

            string custom =
                nview.GetZDO().GetString(
                    ZDOVars.s_text,
                    "");

            string display = container.m_name;

            if (!string.IsNullOrWhiteSpace(custom))
            {
                switch (ModConfig.HoverNameFormat.Value)
                {
                    case ChestNameHover.CustomName:
                        display = custom;
                        break;

                    case ChestNameHover.TypeThenCustomName:
                        display =
                            container.m_name +
                            " (" + custom + ")";
                        break;

                    case ChestNameHover.CustomNameThenType:
                        display =
                            custom +
                            " (" + container.m_name + ")";
                        break;
                }
            }

            Inventory inventory =
                container.GetInventory();

            if (inventory != null &&
                inventory.NrOfItems() == 0)
            {
                display +=
                    " ( $piece_container_empty )";
            }

            string result =
                display +
                "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open $msg_stackall_hover";

            if (ModConfig.ShowRenameHint.Value &&
                ContainerRegistry.IsUsable(container))
            {
                result +=
                    "\n[<color=#ffff00ff><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename";
            }

            return Localization.instance.Localize(
                result);
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ContainerRenameInteractPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(
            Container __instance,
            Humanoid character,
            bool hold,
            bool alt)
        {
            return !RenameFallback.BeginRename(
                __instance,
                character,
                hold,
                alt);
        }
    }

    [HarmonyPatch(typeof(TextInput), "setText")]
    internal static class TextInputSetTextRenamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(string text)
        {
            RenameFallback.CommitRename(text);
        }
    }

    [HarmonyPatch(typeof(TextInput), "Hide")]
    internal static class TextInputHideRenamePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RenameFallback.CancelRename();
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ContainerHoverRenamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Container __instance,
            ref string __result)
        {
            __result =
                RenameFallback.BuildHoverText(
                    __instance,
                    __result);
        }
    }
}
