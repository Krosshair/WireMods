using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresWeaponMaster
{
    internal static class VanillaAttachment
    {
        private static readonly MethodInfo AttachBackItemMethod =
            AccessTools.Method(typeof(VisEquipment), "AttachBackItem");

        internal static GameObject AttachBackItem(
            VisEquipment vis,
            int hash,
            int variant,
            bool rightHand,
            int quality)
        {
            if (vis == null || hash == 0 || AttachBackItemMethod == null)
                return null;

            try
            {
                return AttachBackItemMethod.Invoke(
                    vis,
                    new object[] { hash, variant, rightHand, quality }) as GameObject;
            }
            catch (Exception ex)
            {
                Plugin.LogWarning(
                    "Vanilla holster attach failed for item " + hash +
                    ": " + ex.GetType().Name);
                return null;
            }
        }
    }
}
