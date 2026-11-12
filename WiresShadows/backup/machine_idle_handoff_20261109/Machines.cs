using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresLightOcclusion
{
    // Animated parts of stationary pieces (smelter/kiln/refinery animators,
    // windmill blades, spinning wheels): kept out of the cached maps and drawn
    // into the moving maps instead. While still, their moving faces stay cached
    // (unchanged hash); only while they move do those faces redraw.
    internal sealed partial class ShadowCache
    {
        private sealed class Machine
        {
            internal Component Owner;
            internal Smelter Smelter;
            internal Windmill Windmill;
            internal Transform Turret; // ballista body: moving whenever it turns
            internal Quaternion LastTurn;
            internal Renderer[] Renderers;
            internal Vector3 Center;
            internal float Radius;
            internal bool Active;
            internal float Until; // keeps redrawing while animators ease back to idle
            internal int StillHash;
        }

        private static readonly List<Machine> Machines = new List<Machine>();
        private static readonly HashSet<Renderer> MachineHidden = new HashSet<Renderer>();
        private const float MachineSettle = 2.5f;

        private static readonly AccessTools.FieldRef<Windmill, Transform> Propeller =
            AccessTools.FieldRefAccess<Windmill, Transform>("m_propeller");
        private static readonly AccessTools.FieldRef<Windmill, Transform> Bom =
            AccessTools.FieldRefAccess<Windmill, Transform>("m_bom");
        private static readonly System.Reflection.FieldInfo GrindstoneField =
            AccessTools.Field(typeof(Windmill), "m_grindstone");

        internal static void RegisterMachine(Smelter smelter)
        {
            if (smelter == null || ZNetView.m_forceDisableInit || Machines.Exists(m => m.Owner == smelter))
                return;
            // The Eitr refinery's Smelter animation state is an operating
            // indicator, not evidence that its shadow-casting shell moves.
            // Keep its meshes in the stationary map. The refinery's Light
            // already uses Register(), NEVER MovingLight(); this also avoids
            // rebuilding otherwise unchanged machinery moving maps whenever
            // it is processing Eitr. Windmill/spinning-wheel rotating parts
            // still use the normal dynamic caster path below.
            if (smelter.name.IndexOf("eitrrefinery", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            var roots = new List<Transform>();
            if (smelter.m_animators != null)
                foreach (Animator a in smelter.m_animators)
                    if (a != null) roots.Add(a.transform);
            Windmill wm = smelter.GetComponent<Windmill>();
            if (wm != null)
            {
                if (Propeller(wm) != null) roots.Add(Propeller(wm));
                if (Bom(wm) != null) roots.Add(Bom(wm));
                if (GrindstoneField?.GetValue(wm) is Transform g && g != null) roots.Add(g);
            }
            AddMachine(smelter, roots, smelter, wm, null);
        }

        internal static void RegisterTurret(Turret turret)
        {
            if (turret == null || ZNetView.m_forceDisableInit || Machines.Exists(m => m.Owner == turret))
                return;
            var roots = new List<Transform>();
            foreach (GameObject go in new[] { turret.m_turretBody, turret.m_turretNeck, turret.m_turretBodyArmed, turret.m_turretBodyUnarmed })
                if (go != null) roots.Add(go.transform);
            AddMachine(turret, roots, null, null, turret.m_turretBody != null ? turret.m_turretBody.transform : null);
        }

        private static void AddMachine(Component owner, List<Transform> roots, Smelter smelter, Windmill wm, Transform turret)
        {
            var rs = new List<Renderer>();
            foreach (Transform root in roots)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && r.shadowCastingMode != ShadowCastingMode.Off &&
                        !SeeThrough(r) && !rs.Contains(r))
                        rs.Add(r);
            if (rs.Count == 0)
                return;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            var m = new Machine
            {
                Owner = owner, Smelter = smelter, Windmill = wm, Turret = turret,
                LastTurn = turret != null ? turret.rotation : Quaternion.identity,
                Renderers = rs.ToArray(),
                Center = b.center, Radius = b.extents.magnitude + 0.5f,
                StillHash = Quantize(b.center, 100f) * 13 + 7
            };
            Machines.Add(m);
            foreach (Renderer r in m.Renderers) MachineHidden.Add(r);
            // Its cached maps were made with the parts in; take them out.
            PieceChanged(m.Center, owner);
        }

        internal static void MachineAnimation(Smelter smelter, bool active)
        {
            Machine m = Machines.Find(x => x.Owner == smelter);
            if (m == null) return;
            if (m.Active && !active) m.Until = Time.time + MachineSettle;
            m.Active = active;
        }

        private void AddMachineGroups(Vector3 cam, float far)
        {
            for (int i = Machines.Count - 1; i >= 0; i--)
            {
                Machine m = Machines[i];
                if (m.Owner == null)
                {
                    foreach (Renderer r in m.Renderers) MachineHidden.Remove(r);
                    Machines.RemoveAt(i);
                    continue;
                }
                if ((m.Center - cam).sqrMagnitude > (far + m.Radius) * (far + m.Radius))
                    continue;
                if (m.Turret != null && Quaternion.Angle(m.Turret.rotation, m.LastTurn) > 0.05f)
                {
                    m.LastTurn = m.Turret.rotation;
                    m.Until = Time.time + 0.3f;
                }
                bool moving = m.Active || Time.time < m.Until ||
                    (m.Windmill != null && m.Windmill.GetPowerOutput() > 0.001f);
                AddGroup(m.Center, m.Radius, moving ? Time.frameCount : m.StillHash, false, m.Renderers, false);
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class MachineAwake
    {
        private static void Postfix(Smelter __instance) => ShadowCache.RegisterMachine(__instance);
    }

    [HarmonyPatch(typeof(Turret), "Awake")]
    internal static class TurretAwake
    {
        private static void Postfix(Turret __instance) => ShadowCache.RegisterTurret(__instance);
    }

    // Chests and the like snap between an open and a closed model: re-cache
    // the nearby lights right away when that flips.
    [HarmonyPatch(typeof(Container), "UpdateUseVisual")]
    internal static class ContainerVisual
    {
        private static void Prefix(Container __instance, out bool __state) =>
            __state = __instance.m_open != null && __instance.m_open.activeSelf;
        private static void Postfix(Container __instance, bool __state)
        {
            if (__instance.m_open != null && __instance.m_open.activeSelf != __state)
                ShadowCache.PieceChanged(__instance.transform.position, 0f, __instance);
        }
    }

    [HarmonyPatch(typeof(Smelter), "SetAnimation")]
    internal static class MachineAnim
    {
        private static void Postfix(Smelter __instance, bool active) => ShadowCache.MachineAnimation(__instance, active);
    }
}
