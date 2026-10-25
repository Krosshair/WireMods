using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WiresStackify
{
    // What you see and hear after a quick stack or restock. All of it is
    // made only on your side: nobody else sees or hears it.
    //  quick stack: each chest that got items opens with its own sound and
    //               a small burst of sparks (the one crafting stations make
    //               when an extension connects), one after another
    //  restock:     each chest that gave items opens with its sound, one
    //               after another, then the burst on you
    internal static class StackifyEffects
    {
        internal enum Kind { Stack, Restock }

        internal struct Hit
        {
            public Container Chest;
            public int Count;
        }

        private static GameObject _connection;
        private static bool _searched;

        // Every chest a quick stack or restock used, for the do-everything
        // click to sort afterwards.
        internal static readonly HashSet<Container> Used = new HashSet<Container>();

        internal static void Play(Kind kind, List<Hit> hits, Container open, Player player, bool chestButton)
        {
            foreach (Hit h in hits) if (h.Chest != null) Used.Add(h.Chest);
            if (hits.Count == 0 || player == null) return;
            Vector3 from = player.transform.position;
            hits.Sort((a, b) =>
            {
                if (a.Chest == open) return -1;
                if (b.Chest == open) return 1;
                return (a.Chest.transform.position - from).sqrMagnitude.CompareTo((b.Chest.transform.position - from).sqrMagnitude);
            });
            Runner.Get().StartCoroutine(Sequence(kind, hits.ToArray(), player, chestButton));
        }

        // One sequence at a time: a quick stack and restock done together
        // play one after the other instead of over each other.
        private static int _playing;

        private static IEnumerator Sequence(Kind kind, Hit[] hits, Player player, bool chestButton)
        {
            while (_playing > 0) yield return null;
            _playing++;
            try
            {
                foreach (object step in Steps(kind, hits, player)) yield return step;
            }
            finally { _playing--; }
        }

        private static IEnumerable Steps(Kind kind, Hit[] hits, Player player)
        {
            // After the rustle and the result sound.
            yield return new WaitForSecondsRealtime(Actions.ResultDelay + 0.08f);
            for (int i = 0; i < hits.Length; i++)
            {
                if (i > 0) yield return new WaitForSecondsRealtime(Random.Range(0.105f, 0.24f));
                Container c = hits[i].Chest;
                if (c == null) continue;
                PlayContainerOpening(c);
                if (kind == Kind.Stack) Burst(ChestPoint(c));
            }
            if (kind == Kind.Restock && player != null)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                if (player == null) yield break;
                Burst(player.GetCenterPoint(), true);
            }
        }

        // The game's sound and effect objects are networked; made the way
        // the game makes its build ghosts (no network object at all), they
        // exist only here.
        internal static void SpawnLocal(EffectList list, Vector3 pos, Quaternion rot)
        {
            SpawnLocal(list, pos, rot, null);
        }

        // The game's sound at the game's own volume. clips: pick from these
        // instead of its own. pitch: times its own pitch (0: unchanged).
        // Every copy plays: the game's limit on how many copies of a sound
        // can play at once is lifted for these, so a run of chests isn't cut
        // short.
        internal static void SpawnLocal(EffectList list, Vector3 pos, Quaternion rot, AudioClip[] clips, float pitch = 0f)
        {
            if (list == null || list.m_effectPrefabs == null) return;
            foreach (EffectList.EffectData d in list.m_effectPrefabs)
            {
                if (d == null || !d.m_enabled || d.m_prefab == null) continue;
                PlayNow(d.m_prefab, pos, rot, clips, pitch);
            }
        }

        // Starts the sound this frame. The game's sound objects otherwise
        // wait for the next frame and many add a small random delay before
        // they start, which makes a click feel late.
        internal static void PlayNow(GameObject prefab, Vector3 pos, Quaternion rot, AudioClip[] clips = null, float pitch = 0f)
        {
            if (prefab == null) return;
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            try { go = Object.Instantiate(prefab, pos, rot); }
            finally { ZNetView.m_forceDisableInit = false; }
            if (go == null) return;
            foreach (ZSFX sfx in go.GetComponentsInChildren<ZSFX>(true))
            {
                // Locally created container action sounds are feedback,
                // not environmental SFX. Keep their spatial direction,
                // but route them through Valheim's dry GUI mixer.
                AudioSource source = sfx.GetComponent<AudioSource>();
                AudioMan audio = AudioMan.instance;
                if (source != null && audio != null)
                    source.outputAudioMixerGroup = audio.m_guiMixer;
                sfx.m_maxConcurrentSources = 0;
                if (clips != null && clips.Length > 0) sfx.m_audioClips = clips;
                if (pitch > 0f) { sfx.m_minPitch *= pitch; sfx.m_maxPitch *= pitch; }
                sfx.m_minDelay = sfx.m_maxDelay = 0f;
                if (sfx.m_playOnAwake && sfx.isActiveAndEnabled)
                {
                    sfx.m_playOnAwake = false; // so it doesn't play a second time next frame
                    sfx.Play();
                }
            }
            Object.Destroy(go, 10f);
        }

        // A UI sound played right now, directly: one of the sound's clips on
        // Stackify's own sound player, through the same mixer channel as the
        // game's sound (so your volume settings apply), at its own volume.
        private static AudioSource _ui;
        internal static void PlayUi(GameObject sfxPrefab, float pitch = 1f)
        {
            ZSFX z = sfxPrefab != null ? sfxPrefab.GetComponentInChildren<ZSFX>(true) : null;
            if (z == null || z.m_audioClips == null || z.m_audioClips.Length == 0) return;
            AudioClip clip = z.m_audioClips[Random.Range(0, z.m_audioClips.Length)];
            if (clip == null) return;
            if (_ui == null)
            {
                _ui = Runner.Get().gameObject.AddComponent<AudioSource>();
                _ui.playOnAwake = false;
                _ui.spatialBlend = 0f;
                // Valheim's ZSFX.UpdateReverb does this for 2D sounds.
                // A raw AudioSource does not, so every UI click picked up
                // an environmental reverb tail even through the GUI mixer.
                _ui.bypassReverbZones = true;
                _ui.reverbZoneMix = 0f;
            }
            // UI sounds use the native GUI mixer, not a world SFX
            // channel that adds environmental reverb.
            AudioMan audio = AudioMan.instance;
            _ui.outputAudioMixerGroup = audio != null ? audio.m_guiMixer : null;
            _ui.pitch = pitch * Random.Range(z.m_minPitch, z.m_maxPitch);
            _ui.PlayOneShot(clip, Random.Range(z.m_minVol, z.m_maxVol));
        }

        // The player's pickup/drop result sounds are UI feedback.
        // Keep their original clips, volumes and pitches on the UI channel.
        internal static void PlayUi(EffectList effects)
        {
            if (effects == null || effects.m_effectPrefabs == null) return;
            foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
                if (effect != null && effect.m_enabled && effect.m_prefab != null)
                    PlayUi(effect.m_prefab);
        }

        // Where you hear from (the camera). A sound placed here is as loud as
        // the game plays it when you're right next to its source.
        internal static Vector3 Ear()
        {
            Camera cam = Utils.GetMainCamera();
            if (cam != null) return cam.transform.position;
            return Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
        }

        // Right next to you, a step toward the source, so you can still tell
        // which way it is without it getting quieter with distance.
        internal static Vector3 Near(Vector3 source)
        {
            Vector3 ear = Ear();
            Vector3 d = source - ear;
            float m = d.magnitude;
            return m < 0.01f ? ear : ear + d / m * Mathf.Min(m, 1.5f);
        }

        // Continue playing a container's own effects as before. If it has
        // no opening sound, play one of the vanilla chest opening sounds
        // instead. Carts, ships and other containers need no special case.
        private static void PlayContainerOpening(Container container)
        {
            if (container == null) return;
            AudioClip[] clips = ChestClips();
            EffectList effects = container.m_openEffects;
            bool hasOpeningSound = false;
            if (effects != null && effects.m_effectPrefabs != null)
            {
                foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
                {
                    if (effect == null || !effect.m_enabled ||
                        effect.m_prefab == null) continue;
                    foreach (ZSFX sfx in effect.m_prefab.GetComponentsInChildren<ZSFX>(true))
                    {
                        if (sfx != null && sfx.enabled && sfx.m_playOnAwake &&
                            sfx.m_audioClips != null && sfx.m_audioClips.Length > 0)
                        {
                            hasOpeningSound = true;
                            break;
                        }
                    }
                    if (hasOpeningSound) break;
                }
            }

            Vector3 pos = Near(container.transform.position);
            Quaternion rot = container.transform.rotation;
            SpawnLocal(effects, pos, rot, clips);

            if (!hasOpeningSound && _chestSoundPrefab != null)
                PlayNow(_chestSoundPrefab, pos, rot, clips);
        }

        // Every chest opening sound in the game (all placeable chests), gathered once.
        private static AudioClip[] _chestClips;
        private static GameObject _chestSoundPrefab;
        private static AudioClip[] ChestClips()
        {
            if (_chestClips != null || ZNetScene.instance == null) return _chestClips;
            List<AudioClip> clips = new List<AudioClip>();
            HashSet<AudioClip> seen = new HashSet<AudioClip>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                // Placed chests only (not carts, ships, graves or the like).
                if (prefab == null || prefab.name.IndexOf("chest", System.StringComparison.OrdinalIgnoreCase) < 0 || prefab.GetComponent<Piece>() == null) continue;
                Container c = prefab.GetComponent<Container>();
                if (c == null || c.m_openEffects == null || c.m_openEffects.m_effectPrefabs == null) continue;
                foreach (EffectList.EffectData d in c.m_openEffects.m_effectPrefabs)
                {
                    if (d == null || !d.m_enabled || d.m_prefab == null) continue;
                    foreach (ZSFX sfx in d.m_prefab.GetComponentsInChildren<ZSFX>(true))
                    {
                        if (sfx.m_audioClips == null || sfx.m_audioClips.Length == 0) continue;
                        if (_chestSoundPrefab == null && sfx.m_playOnAwake)
                            _chestSoundPrefab = d.m_prefab;
                        foreach (AudioClip clip in sfx.m_audioClips)
                            if (clip != null && seen.Add(clip)) clips.Add(clip);
                    }
                }
            }
            _chestClips = clips.ToArray();
            return _chestClips;
        }

        // Just the burst from the crafting station connection effect: its
        // "poff" part, without the beam or the glow.
        private static GameObject _poff, _poffBlue;

        private static void Burst(Vector3 pos, bool blue = false)
        {
            GameObject template = blue ? BluePoff() : Poff();
            if (template == null) return;
            GameObject go = Object.Instantiate(template, pos, Quaternion.identity);
            go.SetActive(true);
            Object.Destroy(go, 3f);
        }

        // Made once: a copy of the connection effect with only its burst,
        // kept switched off as the template for each one.
        private static GameObject Poff()
        {
            if (_poff != null) return _poff;
            GameObject source = Connection();
            if (source == null) return null;
            _poff = Object.Instantiate(source);
            _poff.SetActive(false);
            _poff.name = "WireStackifyBurst";
            Object.DontDestroyOnLoad(_poff);
            for (int i = _poff.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _poff.transform.GetChild(i);
                if (child.name != "poff") Object.DestroyImmediate(child.gameObject);
            }
            _poff.transform.localScale = Vector3.one;
            return _poff;
        }

        private static GameObject BluePoff()
        {
            if (_poffBlue != null) return _poffBlue;
            GameObject source = Poff();
            if (source == null) return null;
            _poffBlue = Object.Instantiate(source);
            _poffBlue.SetActive(false);
            _poffBlue.name = "WireStackifyBurstBlue";
            Object.DontDestroyOnLoad(_poffBlue);
            _poffBlue.transform.localScale = source.transform.localScale;
            MakeBlue(_poffBlue);
            return _poffBlue;
        }

        private static GameObject Connection()
        {
            if (_connection != null || _searched || ZNetScene.instance == null) return _connection;
            _searched = true;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                StationExtension ext = prefab.GetComponent<StationExtension>();
                if (ext == null || ext.m_connectionPrefab == null) continue;
                if (ext.m_connectionPrefab.transform.Find("poff") == null) continue;
                _connection = ext.m_connectionPrefab;
                if (_connection.name == "vfx_ExtensionConnection") break; // the plain one, not the mage table's
            }
            return _connection;
        }

        // A copy turned the game's blue. The effect's yellow is the glow
        // colour of its material (an HDR orange-yellow); the copy gets its
        // own material with that glow turned blue, just as bright.
        internal static readonly Color Blue = new Color(0.376f, 0.66f, 0.897f, 1f);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static void MakeBlue(GameObject go)
        {
            Dictionary<Material, Material> made = new Dictionary<Material, Material>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || !m.HasProperty(EmissionId)) continue;
                    Material blue;
                    if (!made.TryGetValue(m, out blue))
                    {
                        blue = new Material(m) { name = m.name + " (Stackify blue)" };
                        Color e = m.GetColor(EmissionId);
                        float v = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                        if (v > 0f)
                        {
                            float k = v / Mathf.Max(Blue.r, Mathf.Max(Blue.g, Blue.b));
                            blue.SetColor(EmissionId, new Color(Blue.r * k, Blue.g * k, Blue.b * k, e.a));
                        }
                        made[m] = blue;
                    }
                    mats[i] = blue;
                }
                r.sharedMaterials = mats;
            }
            foreach (Light l in go.GetComponentsInChildren<Light>(true))
                l.color = new Color(Blue.r, Blue.g, Blue.b, l.color.a);
        }

        // A little above the middle of the chest.
        private static Vector3 ChestPoint(Container c)
        {
            Collider col = c.GetComponentInChildren<Collider>();
            if (col == null) return c.transform.position + Vector3.up * 0.5f;
            Bounds b = col.bounds;
            return b.center + Vector3.up * b.extents.y * 0.5f;
        }

        // Runs the sequences: a small object of its own for the session.
        internal sealed class Runner : MonoBehaviour
        {
            private static Runner _instance;
            internal static Runner Get()
            {
                if (_instance != null) return _instance;
                GameObject go = new GameObject("WireStackifyRunner");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<Runner>();
                return _instance;
            }
        }
    }
}
