using UnityEngine;

namespace WiresAcoustics
{
    // The game plays some of a player's own sounds unplaced (2D), "in the camera" (picking things up,
    // some tool and item sounds). Any unplaced sound on a player, or spawned right at one, is made
    // placed instead (heard from the player, through the HRTF and walls like any sound) and follows
    // that player (Follow). You hear from your character's head, so your own stay as loud as the
    // game made them; the near distance is kept at 1 m at least. Interface sounds, ambience and the game's cues that aren't
    // sounds in the world (skill level-up, the adrenaline and perfect dodge cues) stay as they are.
    internal static class PlayerSounds
    {
        private const float Near = 2.5f;
        // The backup lives on the filter, and is destroyed with its sound.
        // A global dictionary retained thousands of dead short-lived
        // AudioSource wrappers until the mod was switched off.
        internal static void RestoreAll()
        {
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || !f.ConvertedPlayer) continue;
                AudioSource src = f.Source;
                if (src != null)
                {
                    src.spatialBlend = f.OldPlayerBlend;
                    src.bypassReverbZones = f.OldPlayerBypass;
                    src.minDistance = f.OldPlayerMin;
                    src.maxDistance = f.OldPlayerMax;
                }
                f.ConvertedPlayer = false;
                f.Player = null;
            }
        }

        // For each effect sound spawned (Follow, before it starts) and each unplaced sound starting
        // (Occlusion: sounds on the player's own objects): true if it's now placed.
        internal static bool Place(ZSFX z, AudioSource src)
        {
            if (!Plugin.Enabled.Value || z == null || src == null || src.spatialBlend >= .5f) return false;
            AcousticFilter cue = z.GetComponent<AcousticFilter>();
            if (cue != null && cue.Cue) return false;
            AudioMan man = AudioMan.instance;
            if (man != null && src.outputAudioMixerGroup != null &&
                (src.outputAudioMixerGroup == man.m_guiMixer || src.outputAudioMixerGroup == man.m_ambientMixer)) return false;
            global::Player p = Of(z.transform);
            if (p == null) return false;
            AcousticFilter f = cue ?? z.gameObject.AddComponent<AcousticFilter>();
            if (!f.ConvertedPlayer)
            {
                f.ConvertedPlayer = true;
                f.OldPlayerBlend = src.spatialBlend;
                f.OldPlayerMin = src.minDistance;
                f.OldPlayerMax = src.maxDistance;
                f.OldPlayerBypass = src.bypassReverbZones;
            }
            src.spatialBlend = 1f;
            src.bypassReverbZones = false; // the game skips the reverb for unplaced sounds
            src.minDistance = Mathf.Max(src.minDistance, 1f); // no source: a judgement value
            src.maxDistance = Mathf.Max(src.maxDistance, 40f);
            f.Player = p.transform;
            return true;
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
                    f.Active = false;
                }
            }
        }

    }
}
