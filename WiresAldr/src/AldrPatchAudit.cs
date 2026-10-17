using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    internal static class AldrPatchAudit
    {
        internal static void Check()
        {
            if (!AldrDiagnostics.Active) return;
            CheckOne("Skill XP", AccessTools.Method(typeof(Skills), "RaiseSkill"));
            CheckOne("Fishing XP event", AccessTools.Method(typeof(FishingFloat), "FixedUpdate"));
            CheckOne("Creature star rolls", AccessTools.Method(typeof(SpawnSystem),
                "GetLevelUpChance", new [] { typeof(Vector3), typeof(SpawnSystem.SpawnData) }));
            CheckOne("Wild harvest", AccessTools.Method(typeof(Pickable), "Interact"));
            CheckOne("Natural drop rolls", AccessTools.Method(typeof(DropTable),
                "GetDropList", Type.EmptyTypes));
            CheckOne("Fishing rod catches", AccessTools.Method(typeof(FishingFloat), "Catch"));
            CheckOne("Native fish spawning", AccessTools.Method(typeof(SpawnSystem), "UpdateSpawnList"));
            CheckOne("Fishing comfort", AccessTools.Method(typeof(FishingFloat), "Awake"));
            CheckOne("Verified parry stagger", AccessTools.Method(typeof(Character), "AddStaggerDamage"));
            CheckOne("Cooking XP", AccessTools.Method(typeof(CookingStation), "OnInteract"));
            CheckOne("Crafting XP", AccessTools.Method(typeof(InventoryGui), "DoCrafting"));
            CheckOne("Melee XP context", AccessTools.Method(typeof(Attack), "DoMeleeAttack"));
            CheckOne("Perfect dodge", AccessTools.Method(typeof(Player), "RPC_HitWhileDodging"));
            CheckOne("Parry XP", AccessTools.Method(typeof(Humanoid), "BlockAttack"));
            CheckOne("World spawn", AccessTools.Method(typeof(Player), "OnSpawned"));
            CheckOne("Smelter batch", AccessTools.Method(typeof(Smelter), "QueueProcessed"));
            CheckOne("Smelter output", AccessTools.Method(typeof(Smelter), "Spawn"));
            int installed = 0;
            foreach (MethodBase method in Harmony.GetAllPatchedMethods())
            {
                try
                {
                    var patch = Harmony.GetPatchInfo(method);
                    if (patch != null && patch.Prefixes.Concat(patch.Postfixes)
                        .Concat(patch.Transpilers).Concat(patch.Finalizers)
                        .Any(info => info.owner == Plugin.Guid)) installed++;
                }
                catch (Exception e)
                {
                    AldrDiagnostics.Log("PATCH_AUDIT_WARNING", e.Message);
                }
            }
            AldrDiagnostics.Log("PATCH_TOTAL", "Aldr owns " + installed +
                " patched Valheim methods; detailed key hook checks above.");
        }
        private static void CheckOne(string label, MethodBase method)
        {
            if (method == null)
            {
                AldrDiagnostics.Log("PATCH_MISSING", label + ": method not found");
                return;
            }
            try
            {
                var info = Harmony.GetPatchInfo(method);
                bool has = info != null &&
                    info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                        .Concat(info.Finalizers).Any(p => p.owner == Plugin.Guid);
                AldrDiagnostics.Log(has ? "PATCH_OK" : "PATCH_MISSING",
                    label + ": " + method.DeclaringType.Name + "." + method.Name +
                    (has ? " registered" : " NOT REGISTERED"));
            }
            catch (Exception e)
            {
                AldrDiagnostics.Log("PATCH_ERROR", label + ": " + e.Message);
            }
        }
    }

    // Observation-only: smelting does not itself grant XP or change metal yield.
    [HarmonyPatch(typeof(Smelter), "QueueProcessed")]
    internal static class AldrSmelterBatch
    {
        private static int count;
        [HarmonyPostfix]
        private static void Postfix(Smelter __instance, string ore)
        {
            if (!AldrDiagnostics.Active) return;
            count++;
            AldrDiagnostics.Sample("SMELTER_CONVERTED",
                "station=" + __instance.m_name + " input=" + ore + " batch=1");
            if (count == 1 || count == 5 || count % 10 == 0)
            {
                if (Player.m_localPlayer)
                    AldrDiagnostics.World(Player.m_localPlayer, "smelter batch " + count);
                AldrDiagnostics.Summary("smelter batch " + count);
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class AldrSmelterOutput
    {
        [HarmonyPostfix]
        private static void Postfix(Smelter __instance, string ore, int stack)
        {
            AldrDiagnostics.Sample("SMELTER_OUTPUT",
                "station=" + __instance.m_name + " input=" + ore +
                " spawned_stack=" + stack);
        }
    }

    [HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
    internal static class AldrSmelterInput
    {
        [HarmonyPostfix]
        private static void Postfix(Smelter __instance, string name)
        {
            AldrDiagnostics.Sample("SMELTER_QUEUED",
                "station=" + __instance.m_name + " material=" + name);
        }
    }

    [HarmonyPatch(typeof(Smelter), "RPC_AddFuel")]
    internal static class AldrSmelterFuel
    {
        [HarmonyPostfix]
        private static void Postfix(Smelter __instance)
        {
            AldrDiagnostics.Sample("SMELTER_FUEL",
                "station=" + __instance.m_name + " fuel added");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class AldrWorldSpawnDiagnostics
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                AldrDiagnostics.World(__instance, "player spawned");
        }
    }
}
