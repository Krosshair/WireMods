using System.Collections.Generic;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // New sounds and the travel of sound. A sound that starts doesn't play at once: its wall check
    // (three rays to it and three to the side of its source you'd see) goes to the worker threads,
    // and the next frame it starts already muffled as it should be. A random bird or insect call
    // first has its open-air spot found the same way (rays down from above), then its wall check.
    // Then it plays as late as its distance makes it at 350 m/s (Doppler.Speed), the frame or two it
    // waited counted in: up close the wait is the travel, further off it's hidden inside it.
    // Your own sounds (nothing between your head and your body) start at once.
    internal static class Starts
    {
        private const int MaxHits = Occlusion.MaxHits;
        private const float Most = .5f; // longest travel delay (a one-off's object is removed after its clip; this keeps its end)

        private struct Req
        {
            internal ZSFX Z;
            internal AudioSource Src;
            internal Vector3 Eye, To, Face;
            internal bool HasFace, Air;
            internal Vector3 Away; // Air: the way out from you
            internal float Asked;
            internal int First, Generation;
        }
        private static readonly List<Req> Queue = new List<Req>(32);
        private static Req[] s_flight = new Req[32];
        private static int s_flightCount;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static readonly RaycastHit[] Down = new RaycastHit[MaxHits];

        private static ZSFX s_hold; // the sound whose start is being held (UpdateReverb inside its Play, then Play's end)

        // From Occlusion.Run, inside the sound's Play: check its walls first, start it after.
        internal static void Hold(ZSFX z, AudioSource src, Vector3 to, Vector3? face)
        {
            AcousticFilter f = z.GetComponent<AcousticFilter>() ?? z.gameObject.AddComponent<AcousticFilter>();
            Queue.Add(new Req { Z = z, Src = src, Eye = Hearing.Eye, To = to, Face = face ?? to,
                HasFace = face.HasValue, Asked = Time.time, Generation = f.StartGeneration });
            s_hold = z;
        }

        // A random call: its open-air spot first (six rays down, Occlusion.InOpenAir's), then its walls.
        internal static void HoldAir(ZSFX z, AudioSource src)
        {
            AcousticFilter f = z.GetComponent<AcousticFilter>() ?? z.gameObject.AddComponent<AcousticFilter>();
            Vector3 away = z.transform.position - Hearing.At; away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Queue.Add(new Req { Z = z, Src = src, To = z.transform.position, Air = true, Away = away,
                Asked = Time.time, Generation = f.StartGeneration });
            s_hold = z;
        }

        // The end of the game's Play: a held sound is stopped before it's heard; any other one-off
        // placed sound starts as late as its distance makes it.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class PlayBegin
        {
            private static void Prefix(ZSFX __instance)
            {
                // Invalidate an earlier job if a pooled ZSFX plays again.
                AcousticFilter f = __instance.GetComponent<AcousticFilter>();
                if (f != null) f.StartGeneration++;
            }
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class PlayEnd
        {
            private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
            {
                AudioSource src = ___m_audioSource;
                if (src == null) return;
                if (ReferenceEquals(s_hold, __instance)) { s_hold = null; src.Stop(); return; }
                if (!Plugin.Enabled.Value || !src.isPlaying) return;
                RandomizeLoop(__instance, src);
                Travel(__instance, src, 0f);
            }
        }

        // Offset independent positional world loops once when they begin.
        // No extra Update/coroutine and no changes to the actual audio clip.
        // Keeping the ambient and UI sources synchronized avoids altering
        // soundtracks, weather, and the game's authored transition logic.
        private static void RandomizeLoop(ZSFX z, AudioSource src)
        {
            if (!Plugin.RandomLoopPhase.Value || !src.loop ||
                src.spatialBlend < .5f || src.clip == null ||
                src.clip.length < .75f || src.clip.samples < 64) return;
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            if (f != null && (f.Cue || f.Voice || f.WorldRandomAmbient ||
                              f.Kind == AcousticFilter.Kinds.Ambient ||
                              f.Kind == AcousticFilter.Kinds.Outside ||
                              f.Kind == AcousticFilter.Kinds.Menu)) return;
            // Called at the accepted ZSFX.Play event, not each loop repeat.
            src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);
        }

        // Held back by its distance from your character's head, less what it already waited.
        private static void Travel(ZSFX z, AudioSource src, float waited)
        {
            if (src.loop || src.spatialBlend < .5f) { if (!src.isPlaying) src.Play(); return; }
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            float delay = (f != null && f.Cue) || !Plugin.TravelTime.Value ? 0f : Mathf.Min((z.transform.position - Hearing.At).magnitude / Doppler.Speed, Most) - waited;
            if (delay < .001f) { if (!src.isPlaying) src.Play(); return; } // under a millisecond: now
            if (src.isPlaying) src.Stop();
            src.PlayDelayed(delay);
        }

        // Every frame from the game's audio update, first: last frame's checks read and their sounds
        // started, then (Schedule, last) this frame's sent off.
        internal static void Step()
        {
            if (!s_pending) return;
            // Worker jobs usually finish by the next audio update, but may
            // need another frame in a heavy base. Don't block the main thread.
            if (!s_job.IsCompleted) return;
            s_job.Complete();
            s_pending = false;
            float now = Time.time;
            for (int i = 0; i < s_flightCount; i++)
            {
                Req r = s_flight[i];
                s_flight[i].Z = null; s_flight[i].Src = null;
                if (r.Z == null || r.Src == null) continue;
                AcousticFilter live = r.Z.GetComponent<AcousticFilter>();
                if (live == null || live.StartGeneration != r.Generation) continue;
                if (r.Air)
                {
                    PlaceInOpenAir(r);
                    if (!Plugin.Occlusion.Value) { Occlusion.Resume(r.Z, r.Src, 0f, Occlusion.Block.Clear); Travel(r.Z, r.Src, now - r.Asked); continue; }
                    // its walls from the new spot, next
                    r.Air = false;
                    r.To = r.Z.transform.position;
                    r.Eye = Hearing.Eye;
                    r.HasFace = false;
                    Queue.Add(r);
                    continue;
                }
                AcousticFilter f = r.Z.GetComponent<AcousticFilter>();
                Occlusion.Own = f != null && f.ShapeRoot != null ? f.ShapeRoot.root : null;
                Occlusion.Block block = Occlusion.Block.Clear;
                float occ = Batch.Walls(s_cmds, s_hits, r.First, r.To, ref block);
                // you can see the thing it comes from: no muffle
                if (occ > .05f && r.HasFace)
                {
                    Occlusion.Block seen = Occlusion.Block.Clear;
                    if (Batch.Walls(s_cmds, s_hits, r.First + 3, r.Face, ref seen) <= .05f) { occ = 0f; block = seen; }
                }
                Occlusion.Own = null;
                Occlusion.Resume(r.Z, r.Src, occ, block);
                Travel(r.Z, r.Src, now - r.Asked);
            }
            s_flightCount = 0;
        }

        // Every frame from the game's audio update, last: this frame's new sounds' rays to the workers.
        internal static void Schedule()
        {
            if (s_pending || Queue.Count == 0) return;
            int n = Queue.Count, cmds = 6 * n; // six rays each: two lines of three, or six down
            if (!s_cmds.IsCreated || s_cmds.Length < cmds)
            {
                if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, cmds));
                s_cmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(cap * MaxHits, Allocator.Persistent);
            }
            if (s_flight.Length < n) System.Array.Resize(ref s_flight, Mathf.NextPowerOfTwo(n));
            var q = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                Req r = Queue[i];
                r.First = c;
                if (r.Air)
                {
                    Vector3 p = r.To;
                    for (int s = 0; s < 6; s++, p += r.Away * Occlusion.CallStep)
                        s_cmds[c++] = new RaycastCommand(new Vector3(p.x, p.y + 150f, p.z), Vector3.down, q, 300f);
                }
                else
                {
                    Cast3(r.Eye, r.To, q, ref c);
                    Cast3(r.Eye, r.HasFace ? r.Face : r.To, q, ref c);
                }
                s_flight[i] = r;
            }
            s_flightCount = n;
            Queue.Clear();
            s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, c), s_hits.GetSubArray(0, c * MaxHits), 4, MaxHits);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Cast3(Vector3 from, Vector3 to, QueryParameters q, ref int c)
        {
            Occlusion.Lines(from, to, out Vector3 side, out Vector3 up);
            s_cmds[c++] = Batch.Cast(from, to, q);
            s_cmds[c++] = Batch.Cast(from + side, to + side, q);
            s_cmds[c++] = Batch.Cast(from + up, to + up, q);
        }

        // Occlusion.InOpenAir's choice, from the rays' hits: on top of whatever is there, 1 m up, not
        // on a building right by you.
        private static void PlaceInOpenAir(Req r)
        {
            Transform t = r.Z.transform;
            Vector3 p = r.To, last = p;
            bool any = false;
            for (int s = 0; s < 6; s++, p += r.Away * Occlusion.CallStep)
            {
                int n = 0, cmd = r.First + s;
                for (int h = 0; h < MaxHits; h++)
                {
                    RaycastHit hit = s_hits[cmd * MaxHits + h];
                    if (hit.colliderInstanceID == 0) break; // the rest is from earlier batches
                    Down[n++] = hit;
                }
                float highest = float.MinValue;
                int top = -1;
                for (int i = 0; i < n; i++)
                    if (Down[i].point.y > highest) { highest = Down[i].point.y; top = i; }
                if (top < 0) return;
                float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : highest;
                last = new Vector3(p.x, Mathf.Max(highest, sea) + Occlusion.CallHeight, p.z);
                any = true;
                Vector3 flat = p - Hearing.At; flat.y = 0f;
                if (flat.sqrMagnitude < Occlusion.NotOver * Occlusion.NotOver && (Solids.Of(Down[top])?.Built ?? false)) continue;
                t.position = last;
                return;
            }
            if (any) t.position = last;
        }

        // Switched off: what's waiting starts now as the game would.
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_flightCount; i++) { Req r = s_flight[i]; if (r.Src != null && !r.Src.isPlaying) r.Src.Play(); s_flight[i].Z = null; s_flight[i].Src = null; }
            foreach (Req r in Queue) if (r.Src != null && !r.Src.isPlaying) r.Src.Play();
            s_flightCount = 0;
            Queue.Clear();
            s_hold = null;
        }

        internal static void Shutdown()
        {
            Flush();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }
    }
}
