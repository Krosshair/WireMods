using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresLightOcclusion
{
    // Actually moving parts of stationary pieces (refinery animator,
    // windmill blades, spinning wheels, turrets): kept out of static maps and drawn
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
            // The vanilla active/idle animation event marks when geometry
            // becomes stationary. Cache its bounds only AFTER settling.
            internal Caster[] RestGeometry;
            internal int RestVisibility;
        }

        private static readonly List<Machine> Machines = new List<Machine>();
        // Vanilla Smelter.SetAnimation is called for every machine state
        // refresh, including furnaces with NO moving geometry. O(1) lookup
        // avoids an allocated List.Find closure and an O(N) search each time.
        private static readonly Dictionary<Smelter, Machine> MachineBySmelter = new Dictionary<Smelter, Machine>();
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
            if (smelter == null || ZNetView.m_forceDisableInit || MachineBySmelter.ContainsKey(smelter))
                return;
            // Smelter is a processing component, NOT a motion classification.
            // Furnaces/kilns have fixed geometry. Refinery grinders, spinning
            // wheels and windmill propellers have genuine moving geometry.
            // All point-light positions remain independent and stationary.
            bool animatedMachine =
                smelter.name.IndexOf("eitrrefinery", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                smelter.name.IndexOf("spinningwheel", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!animatedMachine && smelter.m_windmill == null && smelter.GetComponent<Windmill>() == null)
                return;
            // Register the animator subtree as a whole. The existing active
            // state decides when its moving shadow faces must redraw; idle
            // machines keep the previously cached result. No motion detection.
            var roots = new List<Transform>();
            if (smelter.m_animators != null)
                foreach (Animator a in smelter.m_animators)
                    if (a != null) roots.Add(a.transform);
            // Valheim's Smelter can hold a linked Windmill field even when the
            // rotor's component isn't found on this exact GameObject.
            Windmill wm = smelter.m_windmill != null ? smelter.m_windmill : smelter.GetComponent<Windmill>();
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
            if (smelter != null) MachineBySmelter[smelter] = m;
            foreach (Renderer r in m.Renderers) MachineHidden.Add(r);
            // Its cached maps were made with the parts in; take them out.
            PieceChanged(m.Center, owner);
        }

        private static void CacheRestMachineGeometry(Machine m)
        {
            // SetAnimation(active=false) has already completed its settle
            // interval. Stationary bounds and mesh counts are now reusable
            // until the machine activates, wind spins, or a visual toggles.
            Caster[] geometry = new Caster[m.Renderers.Length];
            for (int i = 0; i < geometry.Length; i++)
            {
                Renderer renderer = m.Renderers[i];
                if (renderer == null) { m.RestGeometry = null; return; }
                if (!SubCounts.TryGetValue(renderer, out int subs))
                {
                    if (SubCounts.Count > 8192) SubCounts.Clear();
                    subs = renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null
                        ? smr.sharedMesh.subMeshCount
                        : renderer.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null
                            ? mf.sharedMesh.subMeshCount : 1;
                    SubCounts[renderer] = subs;
                }
                Bounds b = renderer.bounds;
                geometry[i] = new Caster { R = renderer, B = b,
                    Radius = b.extents.magnitude, Subs = subs };
            }
            m.RestVisibility = DropGeometryVisibility(m.Renderers);
            m.RestGeometry = geometry;
        }

        internal static void MachineAnimation(Smelter smelter, bool active)
        {
            if (smelter == null || !MachineBySmelter.TryGetValue(smelter, out Machine m))
                return;
            if (m.Active && !active) m.Until = Time.time + MachineSettle;
            if (active && !m.Active) m.RestGeometry = null;
            m.Active = active;
        }

        private void AddMachineGroups(Vector3 cam, float far)
        {
            for (int i = Machines.Count - 1; i >= 0; i--)
            {
                Machine m = Machines[i];
                if (m.Owner == null)
                {
                    if (!object.ReferenceEquals(m.Smelter, null)) MachineBySmelter.Remove(m.Smelter);
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
                    // Windmill.Update rotates even at very low nonzero wind.
                    (m.Windmill != null && m.Windmill.GetPowerOutput() > 0f);
                if (moving)
                {
                    // The native Windmill.Update also rotates its propeller
                    // when the processor itself is idle. Never cache a
                    // wind-powered or active moving silhouette.
                    m.RestGeometry = null;
                    AddGroup(m.Center, m.Radius, Time.frameCount, false, m.Renderers, false, KindMachine);
                    continue;
                }
                int visibility = DropGeometryVisibility(m.Renderers);
                if (m.RestGeometry == null || m.RestVisibility != visibility)
                    CacheRestMachineGeometry(m);
                if (m.RestGeometry != null)
                    AddCachedCasterGroup(m.Center, m.Radius,
                        m.StillHash ^ m.RestVisibility, m.RestGeometry, KindMachine);
                else
                    AddGroup(m.Center, m.Radius, m.StillHash ^ visibility,
                        false, m.Renderers, false, KindMachine);
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



