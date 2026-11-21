using System.Diagnostics;

namespace WiresShadows
{
    // What the mod's own work costs on the main thread, measured (General -> Timing log): every 5
    // seconds one line in the log, each part's time per frame on average and its worst single frame,
    // and how many times the costly steps ran (a face of a cached map drawn again, a light's casters
    // searched). Off, nothing is measured.
    internal static class Timing
    {
        internal enum Part { Late, Frame, Tiled, Reassign, Pools, RankWake, Changed, Face, Find, SizeChange, Redraw, NewMap, SingleFace, ByPiece, ByDoor, ByStand, ByFood, FaceSetup, FaceCubemap, FaceCopy, FaceConvert, FaceRestore, PieceLoaded, PieceUnloaded, MachineRegistered, ContainerToggled, DoorMovedEvent, DoorSettledEvent, DoorFacesQueued, DoorFaceDrawn, FullMapFaceDrawn, ResizeDeferred, ResizeCanceled, ResizeUpgrade, ResizeDowngrade, SearchDeferred, SlotTaken, SlotReleased, KeptMapReused, NearPromoted, NearPromotionDeferred, NearPromotionRepeat, ReusedUploadBatched, ResizeSearchReuse, Count }
        private static readonly string[] Names = { "moving maps and scheduling (LateUpdate)", "before render (uploads, moving maps sent)", "tiled lights", "slot ranking", "of which map pools resized/moved", "of which first face drawn", "piece/item change", "cached faces drawn", "caster searches", "maps redrawn for a size change", "faces redrawn (map already there)", "faces of new maps", "single faces redrawn", "lights told: piece", "lights told: door", "lights told: item on a stand", "lights told: placed food", "cached face setup", "cached face cubemap render", "cached face texture copy", "cached face depth conversion", "cached face restore", "piece spawned/streamed", "piece destroyed/unloaded", "machine registered", "container visuals toggled", "door movement events", "door finished animation", "partial faces queued", "partial faces actually drawn", "full-map faces actually drawn", "downsizing windows started", "obsolete resize requests canceled", "resolution upgrades queued", "resolution downgrades queued", "worker search not yet ready (main-thread wait avoided)", "slot assignments", "slot releases", "cached map reused without rebuild", "near-resolution promotions started", "near promotions delayed for stable view", "same-light near promotions repeated within 15s", "redundant global uploads avoided on map reuse", "resizes reused existing caster lists" };
        private static readonly long[] Ticks = new long[(int)Part.Count], Worst = new long[(int)Part.Count], FrameTicks = new long[(int)Part.Count];
        private static readonly int[] Calls = new int[(int)Part.Count];
        private static int s_frames, s_lastFrame = -1;
        private static float s_next;

        internal static bool On => Plugin.TimingLog != null && Plugin.TimingLog.Value;
        internal static long Start() => On ? Stopwatch.GetTimestamp() : 0L;

        // how many times something happened (no time)
        internal static void Count(Part p)
        {
            if (!On) return;
            int f = UnityEngine.Time.frameCount;
            if (f != s_lastFrame) NewFrame(f);
            Calls[(int)p]++;
        }

        internal static void Stop(Part p, long t0)
        {
            if (t0 == 0L) return;
            long dt = Stopwatch.GetTimestamp() - t0;
            int f = UnityEngine.Time.frameCount;
            if (f != s_lastFrame) NewFrame(f);
            int i = (int)p;
            Ticks[i] += dt;
            FrameTicks[i] += dt;
            Calls[i]++;
        }

        // a frame's totals into the worsts, then the report when it's due
        private static void NewFrame(int f)
        {
            for (int i = 0; i < FrameTicks.Length; i++)
            {
                if (FrameTicks[i] > Worst[i]) Worst[i] = FrameTicks[i];
                FrameTicks[i] = 0L;
            }
            s_lastFrame = f;
            s_frames++;
            float now = UnityEngine.Time.unscaledTime;
            if (now < s_next) return;
            if (s_next > 0f && s_frames > 0) Report();
            s_next = now + 5f;
            System.Array.Clear(Ticks, 0, Ticks.Length);
            System.Array.Clear(Worst, 0, Worst.Length);
            System.Array.Clear(Calls, 0, Calls.Length);
            s_frames = 0;
        }

        private static void Report()
        {
            double ms = 1000.0 / Stopwatch.Frequency;
            var sb = new System.Text.StringBuilder("[timing] " + s_frames + " frames:");
            for (int i = 0; i < Names.Length; i++)
            {
                if (Calls[i] == 0) continue;
                if ((i >= (int)Part.SizeChange && i <= (int)Part.ByFood) || i >= (int)Part.PieceLoaded) { sb.Append(' ').Append(Names[i]).Append(' ').Append(Calls[i]).Append(';'); continue; }
                sb.Append(' ').Append(Names[i]).Append(' ').Append((Ticks[i] * ms / s_frames).ToString("F3")).Append(" ms/frame, worst ")
                  .Append((Worst[i] * ms).ToString("F2")).Append(" ms");
                if (i >= (int)Part.Changed) sb.Append(", ").Append(Calls[i]).Append(" times");
                sb.Append(';');
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
