using UnityEngine;

namespace WiresAcoustics
{
    // The game plays many of a player's own sounds unplaced (2D): equipping, moving, drawing a bow,
    // reloading a crossbow, staff casts. Unplaced sounds are heard "in the camera". Any unplaced sound
    // on a player, or spawned right at one, is made placed instead (heard from the player, through the
    // HRTF and walls like any sound) and follows that player (Follow). Its near distance is raised to
    // a third-person camera's distance, so it isn't quieter than the game made it. Interface sounds,
    // ambience and the game's cues that aren't sounds in the world (skill level-up, the adrenaline
    // and perfect dodge cues) stay as they are.
    internal static class PlayerSounds
    {
        private const float Near = 2.5f;

        // For each effect sound spawned (Follow, before it starts) and each unplaced sound starting
        // (Occlusion: sounds on the player's own objects): true if it's now placed.
        internal static bool Place(ZSFX z, AudioSource src)
        {
            if (!Plugin.Enabled.Value || src == null || src.spatialBlend >= .5f) return false;
            AcousticFilter cue = z.GetComponent<AcousticFilter>();
            if (cue != null && cue.Cue) return false;
            AudioMan man = AudioMan.instance;
            if (man != null && src.outputAudioMixerGroup != null &&
                (src.outputAudioMixerGroup == man.m_guiMixer || src.outputAudioMixerGroup == man.m_ambientMixer)) return false;
            global::Player p = Of(z.transform);
            if (p == null) return false;
            src.spatialBlend = 1f;
            src.bypassReverbZones = false; // the game skips the reverb for unplaced sounds
            src.minDistance = Mathf.Max(src.minDistance, 5f);
            src.maxDistance = Mathf.Max(src.maxDistance, 40f);
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            if (f != null) f.Player = p.transform;
            return true;
        }

        internal static bool IsCue(EffectList e)
        {
            foreach (global::Player p in global::Player.GetAllPlayers())
                if (p != null && (e == p.m_skillLevelupEffects || e == p.m_adrenalinePopEffects || e == p.m_perfectDodgeEffects)) return true;
            return false;
        }

        internal static void MarkCue(GameObject[] made)
        {
            foreach (GameObject go in made)
            {
                if (go == null) continue;
                foreach (ZSFX z in go.GetComponentsInChildren<ZSFX>(true))
                {
                    if (z.GetComponent<AudioSource>() == null) continue;
                    AcousticFilter f = z.GetComponent<AcousticFilter>();
                    if (f == null) f = z.gameObject.AddComponent<AcousticFilter>();
                    f.Cue = true;
                }
            }
        }

        private static global::Player Of(Transform t)
        {
            global::Player p = t.GetComponentInParent<global::Player>();
            if (p != null) return p;
            Vector3 at = t.position;
            float best = Near * Near;
            foreach (global::Player q in global::Player.GetAllPlayers())
            {
                if (q == null) continue;
                float d = (q.transform.position + Vector3.up - at).sqrMagnitude;
                if (d < best) { best = d; p = q; }
            }
            return p;
        }
    }
}
