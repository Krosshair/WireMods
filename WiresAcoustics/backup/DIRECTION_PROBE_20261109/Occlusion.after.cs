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
        private static int s_checked, s_muffled, s_added, s_missingNativeLoopRechecks;
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
            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            if (f != null && f.Cue) return; // a game cue: as the game made it
            if (Stackify.Active) Stackify.Restore(src); // a chest's sound from Wire's Stackify: a world sound at the chest
            if (Plugin.DebugLog.Value) Describe(__instance, src, f);
            // The game's random ambience (a bird, a gust, an insect) is put at a made-up spot 1 to 40 m
            // round you at your own height: indoors that's often inside your own room, a bird singing
            // next to you with nothing in the way. It's the world outside, so indoors it's muffled the
            // way the rain and wind are (Ambience). It keeps its spot in the world, so it stays where it
            // is as you turn (made positionless, its left/right was fixed when it started).
            if (f != null && f.WorldRandomAmbient &&
                (src.spatialBlend < .5f || Room.Enclosure >= .35f))
            {
                Ambience.Apply(__instance.gameObject, !src.isPlaying);
                return;
            }
            if (src.spatialBlend < .5f)
            {
                // Positionless ambience (birds, biome loops): muffled by how closed in you are.
                if (Ambience.IsAmbient(src)) { Ambience.Apply(__instance.gameObject, !src.isPlaying); return; }
                // a player's own unplaced sounds are placed at the player; anything else stays as it is
                if (!PlayerSounds.Place(__instance, src)) return;
            }
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (listener == null) return;

            Vector3 from = listener.transform.position, to = __instance.transform.position;
            Vector3 d = to - from;
            float dist = d.magnitude;
            Mist.Undo(__instance, src, dist); // no pitch drop in the mist

            // Reverb send, ours everywhere (vanilla raises it with distance only, /64 m). What tells you how far
            // a sound is, is how much of it is direct and how much is the room: near sounds are mostly
            // direct, past the room's critical distance mostly reverb. That distance grows with how
            // much the room swallows (furniture).
            bool reverb = Plugin.RoomReverb.Value && !src.bypassReverbZones;
            if (reverb)
            {
                float critical = 2f + 10f * Room.Absorption;
                src.reverbZoneMix = dist / (dist + critical);
            }

            bool hrtf = Plugin.Hrtf.Value;
            bool under = Underwater.Muffles(to, __instance.transform);
            bool starting = !src.isPlaying;
            // A sound starting now gets its direct ray at once and jumps to it (a frame late would let
            // its start through the wall). If walls are in the way, the paths round them (openings,
            // corners) go to the worker threads (Batch) and it glides open if one is better. A playing
            // loop is checked there entirely and glides to its value.
            float first = float.MaxValue;
            Collider firstCollider = null;
            if (f != null)
            {
                Area.Measure(f, __instance.transform);
                f.Stereo = src.clip != null && src.clip.channels > 1;
            }
            Own = f != null && f.ShapeRoot != null ? f.ShapeRoot.root : null;
            float occ = starting && Plugin.Occlusion.Value ? Blocking(from, to, out first, out firstCollider) : 0f;
            if (occ >= .3f) Bounces.Wake();
            Own = null;
            s_checked++;
            if (occ > .05f) s_muffled++;
            if (occ <= .05f && !under && f == null && !hrtf && !AcousticFilter.GlobalActive && starting) return;
            if (f == null) { f = __instance.gameObject.AddComponent<AcousticFilter>(); s_added++; }
            f.Kind = AcousticFilter.Kinds.Sfx;
            f.Placed = true;
            // The game's random ambience (a bird, a gust, an insect somewhere round you) is placed at a
            // made-up spot 1 to 40 m off with its own gentle fall-off: it stands for the world around
            // you, not one creature, so it keeps that (on a real distance curve it was 15 to 20 dB quieter at 10 to 20 m).
            // Authored provenance, not AudioMan parenting: positional lava
            // uses exactly the same prefab and hierarchy as random birds.
            bool ambience = f.WorldRandomAmbient;
            f.NativeReverbLoop = __instance.m_distanceReverb;
            f.Physical(Plugin.RealDistance.Value && !f.Voice && !ambience);
            Budget.Note(f, dist); // processed now if it's among the nearest playing sounds
            if (Bounces.EchoGain > .003f) f.EnsureEcho();
            float gain = 1f, cutoff = 22000f;
            if (starting)
            {
                f.StartedAt = Time.time;
                f.LastOcc = occ;
                f.LastBlockDist = first < float.MaxValue ? first : dist * .5f;
                f.LastBlocker = firstCollider;
                Muffle(occ, out gain, out cutoff);
                f.Routed = false;
                f.RoutePoint = to;
                if (under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); } // the sound is under water
                f.DistanceGain = f.FadeAt(dist);
                f.AirCutoff = AcousticFilter.AirAt(dist);
                f.Set(gain, cutoff, true);
                if (occ >= .3f && f.Active) Batch.Enqueue(f, to, under, reverb);
            }
            else if (f.Active) Batch.Enqueue(f, to, under, reverb);
            // Direction is ours (AcousticFilter): Unity's panning is flattened to "both ears equally".
            if (hrtf)
            {
                f.TakePanning(BaseSpreadOf(__instance, src));
                Vector3 hd = to - from;
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
        internal static float Blocking(Vector3 from, Vector3 to, out float first, out Collider firstCollider)
        {
            first = float.MaxValue; firstCollider = null;
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist <= SourceClearance + .1f) return 0f;
            int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore);
            float occ = 0f;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider == null) continue;
                float w = Counts(Hits[i].collider, to);
                if (w <= 0f) continue;
                occ += w;
                if (Hits[i].distance < first)
                {
                    first = Hits[i].distance;
                    firstCollider = Hits[i].collider;
                }
            }
            return occ;
        }

        // How much one hit collider counts between you and a sound at "to". Not at all if it's part
        // of what makes the sound (Own: its shape, Area), or furniture, stations, props whose box
        // (+30 cm) holds it, or anything the sound is actually inside. A wall, floor or roof right
        // next to a sound (a wall torch, a sconce) still counts: from the other side it's behind it.
        internal static Transform Own;
        internal static float Counts(Collider c, Vector3 to)
        {
            if (Own != null && c.transform.root == Own) return 0f;
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
            WearNTear part = c.GetComponentInParent<WearNTear>();
            if (part != null && Room.Structural(c))
            {
                // Approximate MATERIAL TRANSMISSION, not reflection
                // absorption: stone, marble and iron block more than wood.
                // Wood is anchored at one full wall, as before materials
                // were told apart (0.68 let outside sounds, birds and
                // seagulls, through plank walls far too clearly; a real
                // plank wall takes off well over the 4 dB of one wall here).
                switch (part.m_materialType)
                {
                    case WearNTear.MaterialType.Marble: w = 1.40f; break;
                    case WearNTear.MaterialType.Stone:
                    case WearNTear.MaterialType.Ashstone: w = 1.25f; break;
                    case WearNTear.MaterialType.Iron: w = 1.35f; break;
                    case WearNTear.MaterialType.Ancient: w = 1.10f; break;
                    case WearNTear.MaterialType.Ice: w = 1.00f; break;
                    case WearNTear.MaterialType.HardWood:
                    case WearNTear.MaterialType.Timberwood: w = 1.10f; break;
                    default: w = 1.00f; break; // wood
                }
            }
            else if (part != null) w = .25f;
            else if (Room.IsTree(c)) w = .1f; // a trunk shadows a sound a little, it doesn't hide it
            else if (c.GetComponentInParent<Heightmap>() != null || c.gameObject.layer == LayerMask.NameToLayer("static_solid")) w = 1f;
            else w = .3f;
            Weights[id] = w;
            return w;
        }

        // Walls in between as gain and low-pass cutoff (strength applied), between the table's steps.
        // Debug: each sound once by name, as the game made it and how it's handled here.
        private static readonly System.Collections.Generic.HashSet<string> Described = new System.Collections.Generic.HashSet<string>();
        private static void Describe(ZSFX z, AudioSource src, AcousticFilter f)
        {
            string n = z.name.Replace("(Clone)", "");
            if (Described.Count > 2000 || !Described.Add(n)) return;
            if (f != null) Area.Measure(f, z.transform);
            string how = src.spatialBlend < .5f ? (Ambience.IsAmbient(src) ? "ambience (muffled by how enclosed you are)" : "unplaced, left as the game plays it") : "placed";
            string shape = f != null && f.AreaRadius > 0f ? ", shape " + (2f * f.AreaRadius).ToString("F1") + " m wide" : "";
            Plugin.Log.LogInfo("[acoustics] sound " + n + ": " + how + "; blend " + src.spatialBlend.ToString("F2") + ", " +
                (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "no mixer group") + ", " + src.rolloffMode + " " +
                src.minDistance.ToString("F0") + "-" + src.maxDistance.ToString("F0") + " m, spread " +
                src.spread.ToString("F0") + ", volume " + src.volume.ToString("F2") + shape);
        }

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
                f.Kind = AcousticFilter.Kinds.Voice;
                if (on != f.Hrtf)
                {
                    f.Hrtf = on;
                    if (on) f.TakePanning(f.BaseSpread >= 0f ? f.BaseSpread : f.Source.spread);
                    else if (f.BaseSpread >= 0f) f.Source.spread = f.BaseSpread;
                }
                f.Placed = placed;
                Budget.Note(f, (f.transform.position - listener).magnitude);
                if (!placed) { f.Set(1f, 22000f, false); f.AirCutoff = 22000f; continue; }
                if (Bounces.EchoGain > .003f) f.EnsureEcho();
                Batch.Enqueue(f, f.transform.position, Underwater.Muffles(f.transform.position, f.transform), false);
            }
        }

        // The sound's own spread before the game widens it with distance (ZSFX.m_baseSpread).
        private static readonly HarmonyLib.AccessTools.FieldRef<ZSFX, float> ZsfxSpread = HarmonyLib.AccessTools.FieldRefAccess<ZSFX, float>("m_baseSpread");
        private static float BaseSpreadOf(ZSFX z, AudioSource src)
        {
            try { return ZsfxSpread(z); } catch { return src.spread; }
        }

        // Twice a second (Room's tick): sounds that don't get the game's once-a-second update are
        // rechecked on worker threads: one-off sounds still playing after 0.4 s (a long roar moving
        // behind a wall), other placed sources (Others) and the menu's.
        // Other placed sources also get the HRTF here when they're playing (their filter comes from
        // the prefab, see Prefabs).
        internal static void Recheck(Vector3 listener)
        {
            bool occlusion = Plugin.Occlusion.Value, hrtf = Plugin.Hrtf.Value;
            float now = Time.time;
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || f.Source == null || f.Voice || f.Cue || f.Kind == AcousticFilter.Kinds.Ocean || f.Kind == AcousticFilter.Kinds.Ambient) continue;
                AudioSource src = f.Source;
                if (!src.isPlaying || src.spatialBlend < .5f) continue;
                if (f.Kind == AcousticFilter.Kinds.Other && hrtf && !f.Hrtf) { f.TakePanning(src.spread); f.Hrtf = true; }
                Area.Measure(f, f.transform);
                f.Placed = true;
                // Biome-random positional ambience keeps its authored
                // rolloff; true lava/world SFX uses physical distance.
                f.Physical(Plugin.RealDistance.Value && !f.WorldRandomAmbient);
                Budget.Note(f, (f.transform.position - listener).magnitude);
                if (!f.Active) continue; // past the count: left to Unity until it's among the nearest
                if (Bounces.EchoGain > .003f) f.EnsureEcho();
                if (!occlusion) continue;
                // Vanilla ZSFX.CustomUpdate refreshes loop reverb ONLY when
                // m_distanceReverb is true. Loops that opt out must still
                // receive our wall checks when their geometry/door changes.
                if (f.Kind == AcousticFilter.Kinds.Sfx &&
                    (now - f.StartedAt < .4f || (src.loop && f.NativeReverbLoop))) continue;
                if (f.Kind == AcousticFilter.Kinds.Sfx && src.loop && !f.NativeReverbLoop)
                    s_missingNativeLoopRechecks++;
                Vector3 at = f.transform.position;
                Batch.Enqueue(f, at, Underwater.Muffles(at, f.transform), Plugin.RoomReverb.Value && !src.bypassReverbZones);
            }
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + (s_worst != null ? " (most: " + s_worst + ")" : "") +
                ", filters added late " + s_added +
                ", non-native-reverb loop rechecks " + s_missingNativeLoopRechecks +
                "; " + AudioProvenance.Summary() + "; " + AcousticFilter.Format();
            s_checked = s_muffled = s_added = s_missingNativeLoopRechecks = 0;
            s_worstOcc = 0f; s_worst = null;
            return s;
        }
    }
}
