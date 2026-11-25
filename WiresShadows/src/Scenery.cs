using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresShadows
{
    // Trees, rocks and bushes are drawn into the cached maps like buildings. One that's chopped down,
    // broken or picked left its shadow there; now the lights reaching it are redrawn, the way edited
    // ground does it (TerrainChanged). Felled logs roll and get pushed, so they're in the moving maps.
    internal sealed partial class ShadowCache
    {
        // A networked object is really gone (not unloaded with its zone, which doesn't come through
        // here). Its renderers' own models are read: a felled tree is switched off by now, and a
        // switched-off renderer's bounds sit at the world's origin.
        internal static void SceneryGone(ZNetView nv)
        {
            ShadowCache c = Instance;
            if (c == null || !c._active || nv == null || !nv.m_persistent || nv.GetZDO() == null) return;
            GameObject go = nv.gameObject;
            if (go.GetComponent<Piece>() != null || go.GetComponent<TreeLog>() != null ||
                go.GetComponent<ItemDrop>() != null || go.GetComponent<Character>() != null) return;
            Bounds all = default;
            bool any = false;
            go.GetComponentsInChildren(true, ChangedPieceRenderers);
            foreach (Renderer r in ChangedPieceRenderers)
            {
                if (!r.enabled || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off ||
                    (StaticLayerMask & (1 << r.gameObject.layer)) == 0) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Vector3 s = r.transform.lossyScale;
                float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                Bounds m = mf.sharedMesh.bounds;
                var b = new Bounds(r.transform.TransformPoint(m.center), Vector3.one * (2f * scale * m.extents.magnitude));
                if (any) all.Encapsulate(b);
                else { all = b; any = true; }
            }
            ChangedPieceRenderers.Clear();
            if (any) TerrainChanged(all.center, all.extents.x);
        }

        // Part of a deposit mined away.
        internal static void ScenerySplit(Collider part)
        {
            if (part == null || !part.gameObject.activeInHierarchy) return;
            Bounds b = part.bounds;
            TerrainChanged(b.center, b.extents.magnitude);
        }

        // ---- Logs ----

        private sealed class Log { internal TreeLog Of; internal Renderer[] Renderers; }
        private static readonly List<Log> Logs = new List<Log>(16);
        private static readonly HashSet<Renderer> LogHidden = new HashSet<Renderer>();

        internal static void LogMade(TreeLog log)
        {
            if (ZNetView.m_forceDisableInit) return;
            var made = new Log { Of = log, Renderers = log.GetComponentsInChildren<Renderer>(true) };
            foreach (Renderer r in made.Renderers) LogHidden.Add(r);
            Logs.Add(made);
            // a light near it finds its casters anew before its next face, so the log stays out of it
            ShadowCache c = Instance;
            if (c == null || !c._active) return;
            foreach (Tracked t in c._slots)
            {
                if (t == null || t.Light == null) continue;
                float reach = Range(t) + 4f;
                foreach (Renderer r in made.Renderers)
                    if (r.bounds.SqrDistance(t.Center) < reach * reach) { t.DoorPrepared = false; break; }
            }
        }

        // Lying still they're drawn once: the pose is the hash, as for a dropped item.
        private void AddLogGroups(Vector3 cam, float far2)
        {
            for (int i = Logs.Count - 1; i >= 0; i--)
            {
                Log l = Logs[i];
                if (l.Of == null)
                {
                    foreach (Renderer r in l.Renderers) LogHidden.Remove(r);
                    Logs[i] = Logs[Logs.Count - 1];
                    Logs.RemoveAt(Logs.Count - 1);
                    continue;
                }
                Transform tr = l.Of.transform;
                Vector3 pos = tr.position;
                if ((pos - cam).sqrMagnitude > far2) continue;
                Bounds b = default;
                bool any = false;
                foreach (Renderer r in l.Renderers)
                {
                    if (r == null || !CastsHere(r)) continue;
                    if (any) b.Encapsulate(r.bounds);
                    else { b = r.bounds; any = true; }
                }
                if (any)
                    AddGroup(b.center, b.extents.magnitude, Quantize(pos, 200f) * 7 + Quantize(tr.eulerAngles, 2f),
                        false, l.Renderers, true, KindDrop);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    internal static class SceneryDestroyed
    {
        private static void Prefix(GameObject go)
        {
            if (go != null) ShadowCache.SceneryGone(go.GetComponent<ZNetView>());
        }
    }

    // the same, destroyed by another player's game
    [HarmonyPatch(typeof(ZNetScene), "OnZDODestroyed")]
    internal static class SceneryDestroyedElsewhere
    {
        private static void Prefix(ZNetScene __instance, ZDO zdo) => ShadowCache.SceneryGone(__instance.FindInstance(zdo));
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_SetAreaHealth")]
    internal static class DepositMined
    {
        private static readonly System.Reflection.FieldInfo Areas = AccessTools.Field(typeof(MineRock5), "m_hitAreas");
        private static System.Reflection.FieldInfo s_collider;

        private static void Prefix(MineRock5 __instance, int index, float health)
        {
            if (health > 0f || !(Areas?.GetValue(__instance) is IList areas) || index < 0 || index >= areas.Count) return;
            object area = areas[index];
            if (area == null) return;
            if (s_collider == null) s_collider = AccessTools.Field(area.GetType(), "m_collider");
            ShadowCache.ScenerySplit(s_collider?.GetValue(area) as Collider);
        }
    }

    [HarmonyPatch(typeof(MineRock), "RPC_Hide")]
    internal static class OreMined
    {
        private static readonly AccessTools.FieldRef<MineRock, Collider[]> Areas = AccessTools.FieldRefAccess<MineRock, Collider[]>("m_hitAreas");

        private static void Prefix(MineRock __instance, int index)
        {
            Collider[] areas = Areas(__instance);
            if (areas != null && index >= 0 && index < areas.Length) ShadowCache.ScenerySplit(areas[index]);
        }
    }

    [HarmonyPatch(typeof(TreeLog), "Awake")]
    internal static class LogMade
    {
        private static void Postfix(TreeLog __instance) => ShadowCache.LogMade(__instance);
    }
}
