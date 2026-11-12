using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // A portal's hum carries through with you. The game's: the hum of the portal you step into keeps
    // going while you load (you're still there), stops when you've left it, and the exit's own hum
    // starts once you're near that one: a stop and a fresh start. Here the hum you were hearing is
    // never stopped. As you go through, it glides to where the exit portal will be around you (you
    // always come out a step in front of it, facing away), riding with the camera while you load, so
    // nothing moves when the camera lands. Then it's handed to the exit portal and keeps playing as
    // that portal's own hum; the exit's hum takes the old portal's place, silent. A different kind of
    // portal at the other end (another sound) is crossfaded instead.
    internal static class PortalLoop
    {
        private static readonly AccessTools.FieldRef<EffectFade, AudioSource> Source = AccessTools.FieldRefAccess<EffectFade, AudioSource>("m_audioSource");
        private static readonly AccessTools.FieldRef<EffectFade, float> Intensity = AccessTools.FieldRefAccess<EffectFade, float>("m_intensity");
        private static readonly AccessTools.FieldRef<EffectFade, float> BaseVolume = AccessTools.FieldRefAccess<EffectFade, float>("m_baseVolume");
        private static readonly AccessTools.FieldRef<Player, Vector3> TargetPos = AccessTools.FieldRefAccess<Player, Vector3>("m_teleportTargetPos");
        private static readonly AccessTools.FieldRef<Player, Quaternion> TargetRot = AccessTools.FieldRefAccess<Player, Quaternion>("m_teleportTargetRot");

        private const float Glide = 1.5f, Settle = .5f, Fade = 1f, GiveUp = 40f;
        private enum Stage { None, Riding, Settling, Fading }
        private static Stage s_stage;
        private static AudioSource s_src;
        private static EffectFade s_from;
        private static Transform s_fromParent;
        private static Vector3 s_fromLocal, s_startLocal, s_endLocal, s_exit, s_settleFrom;
        private static Quaternion s_fromLocalRot;
        private static ZDOID s_exitId;
        private static float s_t, s_started;

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        private static class Through
        {
            private static void Postfix(TeleportWorld __instance, Player player)
            {
                if (!Plugin.Enabled.Value || player == null || player != Player.m_localPlayer || !player.IsTeleporting()) return;
                EffectFade fade = __instance.m_target_found;
                AudioSource src = fade != null ? Source(fade) : null;
                if (src == null || !src.isPlaying || src.volume < .01f) return;
                AudioListener l = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
                ZNetView nv = __instance.GetComponent<ZNetView>();
                if (l == null || nv == null || nv.GetZDO() == null) return;
                if (Follow.Networked(src.gameObject)) return; // a networked hum stays the game's to manage
                ZDOID exitId = nv.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                ZDO exit = ZDOMan.instance.GetZDO(exitId);
                if (exit == null) return;
                Clear();
                Take(__instance, fade, src, exit, exitId, player, l.transform.parent != null ? l.transform.parent : l.transform);
            }
        }

        private static void Take(TeleportWorld portal, EffectFade fade, AudioSource src, ZDO exit, ZDOID exitId, Player player, Transform cam)
        {
            s_src = src;
            s_from = fade;
            s_fromParent = src.transform.parent;
            s_fromLocal = src.transform.localPosition;
            s_fromLocalRot = src.transform.localRotation;
            s_exitId = exitId;
            Source(fade) = null; // the portal you left no longer sets its volume

            // where the exit portal's hum will be: the same spot on the exit portal as on this one
            Quaternion exitRot = exit.GetRotation();
            s_exit = exit.GetPosition() + exitRot * portal.transform.InverseTransformPoint(src.transform.position);

            // and where the camera will be: you land at the game's target, turned like the exit portal,
            // the camera as far behind and above you as it is now
            Quaternion yaw = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
            Quaternion exitYaw = Quaternion.Euler(0f, TargetRot(player).eulerAngles.y, 0f);
            Vector3 camOff = Quaternion.Inverse(yaw) * (cam.position - player.transform.position);
            Vector3 camAt = TargetPos(player) + exitYaw * camOff;
            Quaternion camRot = exitYaw * (Quaternion.Inverse(yaw) * cam.rotation);
            s_endLocal = Quaternion.Inverse(camRot) * (s_exit - camAt);

            // rides with the camera from here, so the camera landing moves nothing
            src.transform.SetParent(cam, true);
            s_startLocal = src.transform.localPosition;
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f != null) { f.ShapeRoot = null; f.AreaKnown = true; f.AreaRadius = 0f; f.Width = f.BaseWidth; }
            s_stage = Stage.Riding;
            s_t = 0f;
            s_started = Time.time;
            if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[acoustics] portal hum carried through, exit at " + s_exit.ToString("F0"));
        }

        // Every frame, from the game's audio update.
        internal static void Step(Transform cam)
        {
            if (s_stage == Stage.None) return;
            if (s_src == null) { Reset(); return; }
            float dt = Time.deltaTime;
            Player p = Player.m_localPlayer;
            if (s_stage != Stage.Fading && (p == null || p.IsDead() || Time.time - s_started > GiveUp)) Let();

            switch (s_stage)
            {
                case Stage.Riding:
                {
                    s_t += dt;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s_t / Glide));
                    if (s_src.transform.parent != cam) s_src.transform.SetParent(cam, true); // a new camera
                    s_src.transform.localPosition = Vector3.Lerp(s_startLocal, s_endLocal, k);
                    // landed: you're at the exit and so is the camera (it moves after you, a frame later)
                    bool there = (p.transform.position - TargetPos(p)).sqrMagnitude < 4f || !p.IsTeleporting();
                    if (k >= 1f && there && (cam.position - p.transform.position).magnitude < 15f)
                    {
                        s_src.transform.SetParent(null, true);
                        s_settleFrom = s_src.transform.position;
                        s_stage = Stage.Settling;
                        s_t = 0f;
                    }
                    break;
                }
                case Stage.Settling:
                {
                    // from where it landed onto the exit portal itself (the camera's guess is close)
                    s_t += dt;
                    s_src.transform.position = Vector3.Lerp(s_settleFrom, s_exit, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s_t / Settle)));
                    if (s_t >= Settle) Hand();
                    break;
                }
                case Stage.Fading:
                {
                    s_src.volume = Mathf.MoveTowards(s_src.volume, 0f, dt / Fade * BaseOf());
                    if (s_src.volume <= 0f) Drop();
                    break;
                }
            }
        }

        // Onto the exit portal, once it's loaded: our hum becomes its hum.
        private static void Hand()
        {
            ZNetScene scene = ZNetScene.instance;
            GameObject go = scene != null ? scene.FindInstance(s_exitId) : null;
            TeleportWorld exit = go != null ? go.GetComponent<TeleportWorld>() : null;
            if (exit == null) return; // not loaded yet: the hum waits at its spot
            EffectFade fade = exit.m_target_found;
            AudioSource own = fade != null ? Source(fade) : null;
            if (own == null || own.clip != s_src.clip) { Let(); return; } // another kind of portal: crossfade
            float level = BaseVolume(fade) > 0f ? s_src.volume / BaseVolume(fade) : 1f;
            Transform at = own.transform;
            s_src.transform.SetParent(at.parent, false);
            s_src.transform.localPosition = at.localPosition;
            s_src.transform.localRotation = at.localRotation;
            Source(fade) = s_src;
            Intensity(fade) = Mathf.Clamp01(level);
            AcousticFilter f = s_src.GetComponent<AcousticFilter>();
            if (f != null) { f.ShapeRoot = exit.transform; f.AreaKnown = false; }

            // the exit's own hum takes the left portal's place (silent), or goes if that's unloaded
            if (s_from != null && s_fromParent != null)
            {
                own.transform.SetParent(s_fromParent, false);
                own.transform.localPosition = s_fromLocal;
                own.transform.localRotation = s_fromLocalRot;
                own.volume = 0f;
                Source(s_from) = own;
                Intensity(s_from) = 0f;
            }
            else Object.Destroy(own.gameObject);
            if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[acoustics] portal hum handed to the exit portal");
            Reset();
        }

        // Can't hand it over (another portal sound, gave up waiting): it fades where it is.
        private static void Let()
        {
            if (s_src.transform.parent != null && s_src.transform.parent != s_fromParent) s_src.transform.SetParent(null, true);
            s_stage = Stage.Fading;
        }

        private static float BaseOf() => s_from != null && BaseVolume(s_from) > 0f ? BaseVolume(s_from) : Mathf.Max(.01f, s_src.volume);

        // Faded out: back to its own portal if that's still here, else gone.
        private static void Drop()
        {
            if (s_from != null && s_fromParent != null)
            {
                s_src.transform.SetParent(s_fromParent, false);
                s_src.transform.localPosition = s_fromLocal;
                s_src.transform.localRotation = s_fromLocalRot;
                Source(s_from) = s_src;
                Intensity(s_from) = 0f;
                AcousticFilter f = s_src.GetComponent<AcousticFilter>();
                if (f != null) { f.ShapeRoot = s_from.transform.root; f.AreaKnown = false; }
            }
            else Object.Destroy(s_src.gameObject);
            Reset();
        }

        // The mod switched off or a new teleport: whatever is being carried goes back or goes.
        internal static void Clear()
        {
            if (s_stage == Stage.None) return;
            if (s_src != null) { s_src.volume = 0f; Drop(); }
            else Reset();
        }

        private static void Reset()
        {
            s_stage = Stage.None;
            s_src = null;
            s_from = null;
            s_fromParent = null;
        }
    }
}
