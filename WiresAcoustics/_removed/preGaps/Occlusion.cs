using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds behind walls, buildings or hills. Checked when a sound starts and once a second for
    // looping ones (the game's own reverb update, ZSFX.UpdateReverb): one ray from the listener to the
    // sound, every solid thing in between is a layer. Each layer muffles (low-pass) and quietens it,
    // and it gets more of the room's reverb (what you hear of it comes round the walls). Also sets
    // each sound's reverb send from the measured space instead of distance alone.
    [HarmonyPatch(typeof(ZSFX), "UpdateReverb")]
    internal static class Occlusion
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static readonly float[] Gain = { 1f, .6f, .4f, .28f };
        private static readonly float[] Cutoff = { 22000f, 1500f, 800f, 500f };
        internal const float SourceClearance = .5f; // the thing making the sound (a hearth, a door) doesn't hide it
        private static int s_checked, s_muffled, s_added;
        private static float s_worstOcc;
        private static string s_worst;

        private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            Run(__instance, ___m_audioSource);
            if (t0 != 0L) Cost.Add(ref Cost.Sound, t0);
        }

        private static void Run(ZSFX __instance, AudioSource ___m_audioSource)
        {
            AudioSource src = ___m_audioSource;
            if (!Plugin.Enabled.Value || src == null) return;
            if (src.spatialBlend < .5f)
            {
                // Positionless ambience (birds, biome loops): muffled by how closed in you are.
                if (Ambience.IsAmbient(src)) Ambience.Apply(__instance.gameObject, !src.isPlaying);
                return;
            }
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (listener == null) return;

            Vector3 from = listener.transform.position, to = __instance.transform.position;
            Vector3 d = to - from;
            float dist = d.magnitude;

            // Reverb send. Vanilla raises it with distance only (/64 m). In a room what tells you how far
            // a sound is, is how much of it is direct and how much is the room: near sounds are mostly
            // direct, past the room's critical distance mostly reverb. That distance grows with how
            // much the room swallows (furniture).
            bool reverb = Plugin.RoomReverb.Value && !src.bypassReverbZones;
            if (reverb)
            {
                float critical = 2f + 10f * Room.Absorption;
                src.reverbZoneMix = Mathf.Lerp(src.reverbZoneMix, dist / (dist + critical), Room.Enclosure);
            }

            bool hrtf = Plugin.Hrtf.Value;
            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            bool under = Underwater.Muffles(to, __instance.transform);
            bool starting = !src.isPlaying;
            // A sound starting now is checked at once and jumps to its value; a playing loop is checked
            // on worker threads (Batch) and glides to it.
            float occ = starting && Plugin.Occlusion.Value ? Blocking(from, to) : 0f;
            // walls in the way: also through the openings (Portals)
            float lengthGain = 1f;
            Vector3 heard = to;
            bool routed = starting && Plugin.Occlusion.Value && Portals.Route(from, to, ref occ, out lengthGain, out heard);
            s_checked++;
            if (occ > .05f) s_muffled++;
            if (occ <= .05f && !under && f == null && !hrtf && !AcousticFilter.GlobalActive && starting) return;
            if (f == null) { f = __instance.gameObject.AddComponent<AcousticFilter>(); s_added++; }
            float gain = 1f, cutoff = 22000f;
            if (starting)
            {
                Muffle(occ, out gain, out cutoff);
                gain *= lengthGain;
                f.Routed = routed;
                f.RoutePoint = heard;
                if (under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); } // the sound is under water
                f.Set(gain, cutoff, true);
            }
            else Batch.Enqueue(f, to, under, reverb);
            // Direction is ours (AcousticFilter): Unity's panning is flattened to "both ears equally".
            if (hrtf)
            {
                if (f.BaseSpread < 0f) f.BaseSpread = src.spread;
                src.spread = 180f;
                Vector3 hd = (starting && routed ? heard : to) - from;
                f.SetDirection(hd.sqrMagnitude > 1e-4f ? listener.transform.InverseTransformDirection(hd.normalized) : Vector3.forward);
            }
            else if (f.BaseSpread >= 0f) { src.spread = f.BaseSpread; f.BaseSpread = -1f; }
            f.Hrtf = hrtf;
            if (starting && occ > .05f && reverb)
                src.reverbZoneMix = Mathf.Max(src.reverbZoneMix, Mathf.Min(1f, .3f + .25f * occ));
            // Debug: the most muffled sound of each report (one line per report, not one per sound).
            if (Plugin.DebugLog.Value && occ > s_worstOcc)
            {
                s_worstOcc = occ;
                s_worst = __instance.name + " " + dist.ToString("F1") + " m, " + occ.ToString("F2") + " walls: gain " + gain.ToString("F2") + ", low-pass " + cutoff.ToString("F0") + " Hz";
            }
        }

        // How much is between the listener and a sound, in walls. A wall, floor, roof, beam, hill or
        // rock counts 1, furniture and props 0.25 (sound bends around them), trees 0.3. The thing the
        // sound comes from doesn't count (a chest, a workbench, a hearth: anything whose box holds
        // the sound's position), nor anything in the last 0.5 m.
        internal static float Blocking(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist <= SourceClearance + .1f) return 0f;
            int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore);
            float occ = 0f;
            for (int i = 0; i < n; i++)
                if (Hits[i].collider != null) occ += Counts(Hits[i].collider, to);
            return occ;
        }

        // How much one hit collider counts between you and a sound at "to". Not at all if it is what
        // makes the sound: furniture, stations, props whose box (+30 cm) holds it, or anything the sound
        // is actually inside. A wall, floor or roof right next to a sound (a wall torch, a sconce)
        // still counts: from the other side of that wall it's behind it.
        internal static float Counts(Collider c, Vector3 to)
        {
            Bounds b = c.bounds;
            b.Expand(.3f);
            if (!b.Contains(to)) return Weight(c);
            if (!Room.Structural(c)) return 0f;
            return Inside(c, to) ? 0f : Weight(c);
        }

        private static bool Inside(Collider c, Vector3 p)
        {
            // Closest point only exists for these (Unity): anything else just counts.
            bool ok = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
            if (!ok || !c.enabled || !c.gameObject.activeInHierarchy) return false;
            return (c.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        private static readonly System.Collections.Generic.Dictionary<int, float> Weights = new System.Collections.Generic.Dictionary<int, float>();
        private static float Weight(Collider c)
        {
            int id = c.GetInstanceID();
            if (Weights.TryGetValue(id, out float w)) return w;
            if (Weights.Count > 8192) Weights.Clear();
            if (c.GetComponentInParent<WearNTear>() != null) w = Room.Structural(c) ? 1f : .25f;
            else if (c.GetComponentInParent<Heightmap>() != null || c.gameObject.layer == LayerMask.NameToLayer("static_solid")) w = 1f;
            else w = .3f;
            Weights[id] = w;
            return w;
        }

        // Walls in between as gain and low-pass cutoff (strength applied), between the table's steps.
        internal static void Muffle(float occ, out float gain, out float cutoff)
        {
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            float x = Mathf.Clamp(occ, 0f, Gain.Length - 1);
            int i = Mathf.Min((int)x, Gain.Length - 2);
            float t = x - i;
            float g = Mathf.Lerp(Gain[i], Gain[i + 1], t);
            float lc = Mathf.Lerp(Mathf.Log(Cutoff[i]), Mathf.Log(Cutoff[i + 1]), t);
            gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, g, s), .05f, 1f);
            cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), lc, s)), 200f, 22000f);
        }

        // Voice chat streams, 5 times a second: placed or global (its own switch), then a batched wall
        // check.
        internal static void Voices(Vector3 listener)
        {
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || !f.Voice || f.Source == null) continue;
                bool placed = f.Source.spatialBlend >= .5f;
                bool on = Plugin.Hrtf.Value && placed;
                if (on != f.Hrtf)
                {
                    f.Hrtf = on;
                    if (on) { if (f.BaseSpread < 0f) f.BaseSpread = f.Source.spread; f.Source.spread = 180f; }
                    else if (f.BaseSpread >= 0f) f.Source.spread = f.BaseSpread;
                }
                if (!placed) { f.Set(1f, 22000f, false); continue; }
                Batch.Enqueue(f, f.transform.position, Underwater.Muffles(f.transform.position, f.transform), false);
            }
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + (s_worst != null ? " (most: " + s_worst + ")" : "") +
                ", filters added late " + s_added + "; " + AcousticFilter.Format();
            s_checked = s_muffled = s_added = 0;
            s_worstOcc = 0f; s_worst = null;
            return s;
        }
    }
}
