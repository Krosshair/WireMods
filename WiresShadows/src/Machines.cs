using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
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
            internal Transform Spin;     // a windmill's propeller: how far it has turned
            internal Animator Anim;      // else the animation's loops
            internal Quaternion LastSpin;
            internal float LastLoop = -1f;
            internal float Turns;        // whole turns (or animation loops) since it was registered
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
            Machine added = AddMachine(smelter, roots, smelter, wm, null);
            if (added != null)
            {
                added.Spin = wm != null ? Propeller(wm) : null;
                if (added.Spin != null) added.LastSpin = added.Spin.rotation;
                else if (smelter.m_animators != null) foreach (Animator a in smelter.m_animators) if (a != null) { added.Anim = a; break; }
            }
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

        private static Machine AddMachine(Component owner, List<Transform> roots, Smelter smelter, Windmill wm, Transform turret)
        {
            var rs = new List<Renderer>();
            foreach (Transform root in roots)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && r.shadowCastingMode != ShadowCastingMode.Off &&
                        !SeeThrough(r) && !rs.Contains(r))
                        rs.Add(r);
            if (rs.Count == 0)
                return null;
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
            Timing.Count(Timing.Part.MachineRegistered);
            Machines.Add(m);
            MachineByOwner[owner] = m;
            if (smelter != null) MachineBySmelter[smelter] = m;
            foreach (Renderer r in m.Renderers) MachineHidden.Add(r);
            // Its cached maps were made with the parts in; take them out (redrawn, not searched again).
            s_machine = true;
            PieceChanged(m.Center, owner);
            s_machine = false;
            return m;
        }
        private static bool s_machine;

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
                    MachineByOwner.Remove(m.Owner);
                    if (Pairs.TryGetValue(m, out var gone)) { foreach (Pair p in gone.Values) { p.State = 2; s_pairFree.Push(p.Index); } Pairs.Remove(m); }
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
                    if (m.Spin != null)
                    {
                        m.Turns += Quaternion.Angle(m.Spin.rotation, m.LastSpin) / 360f;
                        m.LastSpin = m.Spin.rotation;
                    }
                    else if (m.Anim != null)
                    {
                        float loop = m.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        if (m.LastLoop >= 0f) m.Turns += Mathf.Max(0f, loop - m.LastLoop);
                        m.LastLoop = loop;
                    }
                    int before = _groupCount;
                    AddGroup(m.Center, m.Radius, Time.frameCount, false, m.Renderers, false, KindMachine);
                    TagMachine(m, before);
                    continue;
                }
                int visibility = DropGeometryVisibility(m.Renderers);
                if (m.RestGeometry == null || m.RestVisibility != visibility)
                    CacheRestMachineGeometry(m);
                int was = _groupCount;
                if (m.RestGeometry != null)
                    AddCachedCasterGroup(m.Center, m.Radius,
                        m.StillHash ^ m.RestVisibility, m.RestGeometry, KindMachine);
                else
                    AddGroup(m.Center, m.Radius, m.StillHash ^ visibility,
                        false, m.Renderers, false, KindMachine);
                TagMachine(m, was);
            }
        }

        // ---- Machines behind walls ----
        // While a light draws a machine's turning parts into its moving map, the GPU marks it if any
        // of their pixels is lit by that light past its own walls (WireShadowDistance, WireCount).
        // Unmarked over one whole turn: whatever the parts would shade is in a wall's shadow already,
        // so that light stops drawing them. Asked again when that light's cached map is redrawn.
        private sealed class Pair { internal Machine M; internal int Index; internal int Stamp; internal float Start, Asked; internal byte State; } // 0 counting, 1 hidden, 2 seen
        private const int MaxPairs = 1024;
        private static readonly Dictionary<Component, Machine> MachineByOwner = new Dictionary<Component, Machine>();
        private static readonly Dictionary<Machine, Dictionary<Tracked, Pair>> Pairs = new Dictionary<Machine, Dictionary<Tracked, Pair>>();
        private static readonly List<Pair> s_counting = new List<Pair>();
        private static readonly Stack<int> s_pairFree = new Stack<int>();
        private static int s_pairNext;
        private static Machine[] s_casterMachine = new Machine[256];
        private static ComputeBuffer s_seenBuf;
        private static bool s_seenPending;
        private static readonly uint[] s_zero = new uint[1];
        private static readonly int CountIndexId = Shader.PropertyToID("_WireCountIndex"),
            CountSlotId = Shader.PropertyToID("_WireCountSlot");

        // the casters of the group just added belong to this machine
        private static void TagMachine(Machine m, int groupBefore)
        {
            if (_groupCount <= groupBefore) return;
            Group g = Groups[_groupCount - 1];
            g.Owner = m.Owner;
            if (s_casterMachine.Length < Casters.Count) System.Array.Resize(ref s_casterMachine, Mathf.NextPowerOfTwo(Casters.Count));
            for (int k = g.Start; k < g.Start + g.Count; k++) s_casterMachine[k] = m;
        }

        private static bool MachineHiddenFrom(Object owner, Tracked t)
        {
            if (!(owner is Component c) || !MachineByOwner.TryGetValue(c, out Machine m) || (m.Spin == null && m.Anim == null))
                return false;
            if (!Pairs.TryGetValue(m, out var byLight)) Pairs[m] = byLight = new Dictionary<Tracked, Pair>();
            if (!byLight.TryGetValue(t, out Pair p))
            {
                if (s_pairFree.Count == 0 && s_pairNext >= MaxPairs) return false;
                byLight[t] = p = new Pair { M = m, Index = s_pairFree.Count > 0 ? s_pairFree.Pop() : s_pairNext++, Stamp = int.MinValue };
            }
            if (p.Stamp != t.MapStamp)
            {
                // a new cached map (built, broken, a door): counted afresh
                p.Stamp = t.MapStamp; p.Start = p.Asked = m.Turns; p.State = 0;
                EnsureSeenBuffer();
                s_seenBuf.SetData(s_zero, 0, p.Index, 1);
                if (!s_counting.Contains(p)) s_counting.Add(p);
            }
            return p.State == 1;
        }

        // The shader's count index for caster k drawn for light t: 0 when not counting.
        private static int CountIndex(int k, Tracked t)
        {
            Machine m = k < s_casterMachine.Length ? s_casterMachine[k] : null;
            if (m == null || !Pairs.TryGetValue(m, out var byLight) || !byLight.TryGetValue(t, out Pair p) || p.State != 0) return 0;
            return p.Index + 1;
        }

        // Around a light's moving-map draws: -1 when nothing is being counted, else the index set last.
        private int CountBegin(Tracked t)
        {
            if (s_counting.Count == 0 || s_seenBuf == null) return -1;
            _frameCb.SetRandomWriteTarget(1, s_seenBuf);
            _frameCb.SetGlobalFloat(CountSlotId, t.Slot);
            return 0;
        }

        private int CountFor(int k, Tracked t, int counted)
        {
            int ci = CountIndex(k, t);
            if (ci != counted) _frameCb.SetGlobalFloat(CountIndexId, ci);
            return ci;
        }

        private void CountEnd(int counted)
        {
            if (counted < 0) return;
            if (counted != 0) _frameCb.SetGlobalFloat(CountIndexId, 0f);
            _frameCb.ClearRandomWriteTargets();
        }

        private static void EnsureSeenBuffer()
        {
            if (s_seenBuf == null) s_seenBuf = new ComputeBuffer(MaxPairs, 4);
        }

        // Once a frame (LateUpdate): what last frame's draws marked, read back without waiting.
        private static void SeenRead()
        {
            if (s_counting.Count == 0 || s_seenPending || s_seenBuf == null) return;
            foreach (Pair p in s_counting) p.Asked = p.M.Turns;
            s_seenPending = true;
            UnityEngine.Rendering.AsyncGPUReadback.Request(s_seenBuf, SeenBack);
        }

        private static void SeenBack(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
        {
            s_seenPending = false;
            if (req.hasError) return;
            var seen = req.GetData<uint>();
            for (int i = s_counting.Count - 1; i >= 0; i--)
            {
                Pair p = s_counting[i];
                if (p.State != 0) { s_counting.RemoveAt(i); continue; }
                if (seen[p.Index] != 0) p.State = 2;
                else if (p.Asked - p.Start >= 1f) p.State = 1;
                if (p.State != 0) s_counting.RemoveAt(i);
            }
        }

        private static void SeenRelease()
        {
            s_seenBuf?.Release(); s_seenBuf = null;
            s_seenPending = false;
            s_counting.Clear(); Pairs.Clear(); s_pairFree.Clear(); s_pairNext = 0;
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
            {
                Timing.Count(Timing.Part.ContainerToggled);
                ShadowCache.PieceChanged(__instance.transform.position, 0f, __instance);
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), "SetAnimation")]
    internal static class MachineAnim
    {
        private static void Postfix(Smelter __instance, bool active) => ShadowCache.MachineAnimation(__instance, active);
    }
}



