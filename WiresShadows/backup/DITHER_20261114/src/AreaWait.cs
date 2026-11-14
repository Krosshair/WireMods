using System.Collections.Generic;
using UnityEngine;

namespace WiresShadows
{
    // After a portal the game only waits for your own zone; the rest of a base appears afterwards, 10
    // objects a frame (ZNetScene.CreateObjects). Every piece that appeared made the lights around it
    // render their maps again, so the same lights rendered over and over while the base came in. Now
    // a map isn't rendered until the game's own check says everything around the light exists
    // (ZNetScene.IsAreaReady, the one the portal waits on), then once. Checked at most every 0.25 s
    // per zone (the reassignment's own cadence). Never longer than 15 s, the game's own give-up time
    // for a portal (Player.UpdateTeleport), in case something there never appears.
    internal sealed partial class ShadowCache
    {
        private const float AreaRecheck = 0.25f, AreaGiveUp = 15f;
        private struct AreaState { internal bool Ready; internal float At; }
        private static readonly Dictionary<Vector2s, AreaState> Areas = new Dictionary<Vector2s, AreaState>();

        private static bool AreaLoaded(Vector3 p)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null) return true;
            Vector2s zone = ZoneSystem.GetZone(p);
            float now = Time.time;
            if (Areas.TryGetValue(zone, out AreaState s) && now - s.At < AreaRecheck) return s.Ready;
            if (Areas.Count > 256) Areas.Clear();
            bool ready = ZNetScene.instance.IsAreaReady(p);
            Areas[zone] = new AreaState { Ready = ready, At = now };
            return ready;
        }

        // A map about to be rendered: false (and tried again later) while its area is still coming in.
        private static bool ReadyToRender(Tracked t)
        {
            if (t.Light == null) return true;
            if (AreaLoaded(t.Light.transform.position)) { t.WaitSince = -1f; return true; }
            if (t.WaitSince < 0f) t.WaitSince = Time.time;
            if (Time.time - t.WaitSince >= AreaGiveUp) { t.WaitSince = -1f; return true; }
            t.WaitUntil = Time.time + AreaRecheck;
            return false;
        }
    }
}

