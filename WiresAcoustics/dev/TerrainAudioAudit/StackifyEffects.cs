using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WiresStackify;

internal static class StackifyEffects
{
	internal enum Kind
	{
		Stack,
		Restock
	}

	internal struct Hit
	{
		public Container Chest;

		public int Count;
	}

	internal sealed class Runner : MonoBehaviour
	{
		private static Runner _instance;

		internal static Runner Get()
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Expected O, but got Unknown
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			if ((Object)(object)_instance != (Object)null)
			{
				return _instance;
			}
			GameObject val = new GameObject("WireStackifyRunner");
			Object.DontDestroyOnLoad((Object)val);
			((Object)val).hideFlags = (HideFlags)61;
			_instance = val.AddComponent<Runner>();
			return _instance;
		}
	}

	private static GameObject _connection;

	private static bool _searched;

	internal static readonly HashSet<Container> Used = new HashSet<Container>();

	private static int _playing;

	private static AudioSource _ui;

	private static AudioClip[] _chestClips;

	private static GameObject _chestSoundPrefab;

	private static GameObject _poff;

	private static GameObject _poffBlue;

	internal static readonly Color Blue = new Color(0.376f, 0.66f, 0.897f, 1f);

	private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

	internal static void Play(Kind kind, List<Hit> hits, Container open, Player player, bool chestButton)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		foreach (Hit hit in hits)
		{
			if ((Object)(object)hit.Chest != (Object)null)
			{
				Used.Add(hit.Chest);
			}
		}
		if (hits.Count == 0 || (Object)(object)player == (Object)null)
		{
			return;
		}
		Vector3 from = ((Component)player).transform.position;
		hits.Sort(delegate(Hit a, Hit b)
		{
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0061: Unknown result type (might be due to invalid IL or missing references)
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			if ((Object)(object)a.Chest == (Object)(object)open)
			{
				return -1;
			}
			if ((Object)(object)b.Chest == (Object)(object)open)
			{
				return 1;
			}
			Vector3 val = ((Component)a.Chest).transform.position - from;
			float sqrMagnitude = ((Vector3)(ref val)).sqrMagnitude;
			val = ((Component)b.Chest).transform.position - from;
			return sqrMagnitude.CompareTo(((Vector3)(ref val)).sqrMagnitude);
		});
		((MonoBehaviour)Runner.Get()).StartCoroutine(Sequence(kind, hits.ToArray(), player, chestButton));
	}

	private static IEnumerator Sequence(Kind kind, Hit[] hits, Player player, bool chestButton)
	{
		while (_playing > 0)
		{
			yield return null;
		}
		_playing++;
		try
		{
			foreach (object item in Steps(kind, hits, player))
			{
				yield return item;
			}
		}
		finally
		{
			_playing--;
		}
	}

	private static IEnumerable Steps(Kind kind, Hit[] hits, Player player)
	{
		yield return (object)new WaitForSecondsRealtime(0.18f);
		for (int i = 0; i < hits.Length; i++)
		{
			if (i > 0)
			{
				yield return (object)new WaitForSecondsRealtime(Random.Range(0.105f, 0.24f));
			}
			Container chest = hits[i].Chest;
			if (!((Object)(object)chest == (Object)null))
			{
				PlayContainerOpening(chest);
				if (kind == Kind.Stack)
				{
					Burst(ChestPoint(chest));
				}
			}
		}
		if (kind == Kind.Restock && (Object)(object)player != (Object)null)
		{
			yield return (object)new WaitForSecondsRealtime(0.15f);
			if (!((Object)(object)player == (Object)null))
			{
				Burst(((Character)player).GetCenterPoint(), blue: true);
			}
		}
	}

	internal static void SpawnLocal(EffectList list, Vector3 pos, Quaternion rot)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		SpawnLocal(list, pos, rot, null);
	}

	internal static void SpawnLocal(EffectList list, Vector3 pos, Quaternion rot, AudioClip[] clips, float pitch = 0f)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		if (list == null || list.m_effectPrefabs == null)
		{
			return;
		}
		EffectData[] effectPrefabs = list.m_effectPrefabs;
		foreach (EffectData val in effectPrefabs)
		{
			if (val != null && val.m_enabled && !((Object)(object)val.m_prefab == (Object)null))
			{
				PlayNow(val.m_prefab, pos, rot, clips, pitch);
			}
		}
	}

	internal static void PlayNow(GameObject prefab, Vector3 pos, Quaternion rot, AudioClip[] clips = null, float pitch = 0f)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)prefab == (Object)null)
		{
			return;
		}
		ZNetView.m_forceDisableInit = true;
		GameObject val;
		try
		{
			val = Object.Instantiate<GameObject>(prefab, pos, rot);
		}
		finally
		{
			ZNetView.m_forceDisableInit = false;
		}
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		ZSFX[] componentsInChildren = val.GetComponentsInChildren<ZSFX>(true);
		foreach (ZSFX val2 in componentsInChildren)
		{
			AudioSource component = ((Component)val2).GetComponent<AudioSource>();
			AudioMan instance = AudioMan.instance;
			if ((Object)(object)component != (Object)null && (Object)(object)instance != (Object)null)
			{
				component.outputAudioMixerGroup = instance.m_guiMixer;
			}
			val2.m_maxConcurrentSources = 0;
			if (clips != null && clips.Length != 0)
			{
				val2.m_audioClips = clips;
			}
			if (pitch > 0f)
			{
				val2.m_minPitch *= pitch;
				val2.m_maxPitch *= pitch;
			}
			val2.m_minDelay = (val2.m_maxDelay = 0f);
			if (val2.m_playOnAwake && ((Behaviour)val2).isActiveAndEnabled)
			{
				val2.m_playOnAwake = false;
				val2.Play();
			}
		}
		Object.Destroy((Object)(object)val, 10f);
	}

	internal static void PlayUi(GameObject sfxPrefab, float pitch = 1f)
	{
		ZSFX val = (((Object)(object)sfxPrefab != (Object)null) ? sfxPrefab.GetComponentInChildren<ZSFX>(true) : null);
		if ((Object)(object)val == (Object)null || val.m_audioClips == null || val.m_audioClips.Length == 0)
		{
			return;
		}
		AudioClip val2 = val.m_audioClips[Random.Range(0, val.m_audioClips.Length)];
		if (!((Object)(object)val2 == (Object)null))
		{
			if ((Object)(object)_ui == (Object)null)
			{
				_ui = ((Component)Runner.Get()).gameObject.AddComponent<AudioSource>();
				_ui.playOnAwake = false;
				_ui.spatialBlend = 0f;
				_ui.bypassReverbZones = true;
				_ui.reverbZoneMix = 0f;
			}
			AudioMan instance = AudioMan.instance;
			_ui.outputAudioMixerGroup = (((Object)(object)instance != (Object)null) ? instance.m_guiMixer : null);
			_ui.pitch = pitch * Random.Range(val.m_minPitch, val.m_maxPitch);
			_ui.PlayOneShot(val2, Random.Range(val.m_minVol, val.m_maxVol));
		}
	}

	internal static void PlayUi(EffectList effects)
	{
		if (effects == null || effects.m_effectPrefabs == null)
		{
			return;
		}
		EffectData[] effectPrefabs = effects.m_effectPrefabs;
		foreach (EffectData val in effectPrefabs)
		{
			if (val != null && val.m_enabled && (Object)(object)val.m_prefab != (Object)null)
			{
				PlayUi(val.m_prefab);
			}
		}
	}

	internal static Vector3 Ear()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		Camera mainCamera = Utils.GetMainCamera();
		if ((Object)(object)mainCamera != (Object)null)
		{
			return ((Component)mainCamera).transform.position;
		}
		if (!((Object)(object)Player.m_localPlayer != (Object)null))
		{
			return Vector3.zero;
		}
		return ((Component)Player.m_localPlayer).transform.position;
	}

	internal static Vector3 Near(Vector3 source)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = Ear();
		Vector3 val2 = source - val;
		float magnitude = ((Vector3)(ref val2)).magnitude;
		if (!(magnitude < 0.01f))
		{
			return val + val2 / magnitude * Mathf.Min(magnitude, 1.5f);
		}
		return val;
	}

	private static void PlayContainerOpening(Container container)
	{
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)container == (Object)null)
		{
			return;
		}
		AudioClip[] clips = ChestClips();
		EffectList openEffects = container.m_openEffects;
		bool flag = false;
		if (openEffects != null && openEffects.m_effectPrefabs != null)
		{
			EffectData[] effectPrefabs = openEffects.m_effectPrefabs;
			foreach (EffectData val in effectPrefabs)
			{
				if (val == null || !val.m_enabled || (Object)(object)val.m_prefab == (Object)null)
				{
					continue;
				}
				ZSFX[] componentsInChildren = val.m_prefab.GetComponentsInChildren<ZSFX>(true);
				foreach (ZSFX val2 in componentsInChildren)
				{
					if ((Object)(object)val2 != (Object)null && ((Behaviour)val2).enabled && val2.m_playOnAwake && val2.m_audioClips != null && val2.m_audioClips.Length != 0)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					break;
				}
			}
		}
		Vector3 pos = Near(((Component)container).transform.position);
		Quaternion rotation = ((Component)container).transform.rotation;
		SpawnLocal(openEffects, pos, rotation, clips);
		if (!flag && (Object)(object)_chestSoundPrefab != (Object)null)
		{
			PlayNow(_chestSoundPrefab, pos, rotation, clips);
		}
	}

	private static AudioClip[] ChestClips()
	{
		if (_chestClips != null || (Object)(object)ZNetScene.instance == (Object)null)
		{
			return _chestClips;
		}
		List<AudioClip> list = new List<AudioClip>();
		HashSet<AudioClip> hashSet = new HashSet<AudioClip>();
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			if ((Object)(object)prefab == (Object)null || ((Object)prefab).name.IndexOf("chest", StringComparison.OrdinalIgnoreCase) < 0 || (Object)(object)prefab.GetComponent<Piece>() == (Object)null)
			{
				continue;
			}
			Container component = prefab.GetComponent<Container>();
			if ((Object)(object)component == (Object)null || component.m_openEffects == null || component.m_openEffects.m_effectPrefabs == null)
			{
				continue;
			}
			EffectData[] effectPrefabs = component.m_openEffects.m_effectPrefabs;
			foreach (EffectData val in effectPrefabs)
			{
				if (val == null || !val.m_enabled || (Object)(object)val.m_prefab == (Object)null)
				{
					continue;
				}
				ZSFX[] componentsInChildren = val.m_prefab.GetComponentsInChildren<ZSFX>(true);
				foreach (ZSFX val2 in componentsInChildren)
				{
					if (val2.m_audioClips == null || val2.m_audioClips.Length == 0)
					{
						continue;
					}
					if ((Object)(object)_chestSoundPrefab == (Object)null && val2.m_playOnAwake)
					{
						_chestSoundPrefab = val.m_prefab;
					}
					AudioClip[] audioClips = val2.m_audioClips;
					foreach (AudioClip val3 in audioClips)
					{
						if ((Object)(object)val3 != (Object)null && hashSet.Add(val3))
						{
							list.Add(val3);
						}
					}
				}
			}
		}
		_chestClips = list.ToArray();
		return _chestClips;
	}

	private static void Burst(Vector3 pos, bool blue = false)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = (blue ? BluePoff() : Poff());
		if (!((Object)(object)val == (Object)null))
		{
			GameObject obj = Object.Instantiate<GameObject>(val, pos, Quaternion.identity);
			obj.SetActive(true);
			Object.Destroy((Object)(object)obj, 3f);
		}
	}

	private static GameObject Poff()
	{
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_poff != (Object)null)
		{
			return _poff;
		}
		GameObject val = Connection();
		if ((Object)(object)val == (Object)null)
		{
			return null;
		}
		_poff = Object.Instantiate<GameObject>(val);
		_poff.SetActive(false);
		((Object)_poff).name = "WireStackifyBurst";
		Object.DontDestroyOnLoad((Object)(object)_poff);
		for (int num = _poff.transform.childCount - 1; num >= 0; num--)
		{
			Transform child = _poff.transform.GetChild(num);
			if (((Object)child).name != "poff")
			{
				Object.DestroyImmediate((Object)(object)((Component)child).gameObject);
			}
		}
		_poff.transform.localScale = Vector3.one;
		return _poff;
	}

	private static GameObject BluePoff()
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_poffBlue != (Object)null)
		{
			return _poffBlue;
		}
		GameObject val = Poff();
		if ((Object)(object)val == (Object)null)
		{
			return null;
		}
		_poffBlue = Object.Instantiate<GameObject>(val);
		_poffBlue.SetActive(false);
		((Object)_poffBlue).name = "WireStackifyBurstBlue";
		Object.DontDestroyOnLoad((Object)(object)_poffBlue);
		_poffBlue.transform.localScale = val.transform.localScale;
		MakeBlue(_poffBlue);
		return _poffBlue;
	}

	private static GameObject Connection()
	{
		if ((Object)(object)_connection != (Object)null || _searched || (Object)(object)ZNetScene.instance == (Object)null)
		{
			return _connection;
		}
		_searched = true;
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			if ((Object)(object)prefab == (Object)null)
			{
				continue;
			}
			StationExtension component = prefab.GetComponent<StationExtension>();
			if (!((Object)(object)component == (Object)null) && !((Object)(object)component.m_connectionPrefab == (Object)null) && !((Object)(object)component.m_connectionPrefab.transform.Find("poff") == (Object)null))
			{
				_connection = component.m_connectionPrefab;
				if (((Object)_connection).name == "vfx_ExtensionConnection")
				{
					break;
				}
			}
		}
		return _connection;
	}

	private static void MakeBlue(GameObject go)
	{
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<Material, Material> dictionary = new Dictionary<Material, Material>();
		Renderer[] componentsInChildren = go.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			Material[] sharedMaterials = val.sharedMaterials;
			for (int j = 0; j < sharedMaterials.Length; j++)
			{
				Material val2 = sharedMaterials[j];
				if ((Object)(object)val2 == (Object)null || !val2.HasProperty(EmissionId))
				{
					continue;
				}
				if (!dictionary.TryGetValue(val2, out var value))
				{
					value = new Material(val2)
					{
						name = ((Object)val2).name + " (Stackify blue)"
					};
					Color color = val2.GetColor(EmissionId);
					float num = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
					if (num > 0f)
					{
						float num2 = num / Mathf.Max(Blue.r, Mathf.Max(Blue.g, Blue.b));
						value.SetColor(EmissionId, new Color(Blue.r * num2, Blue.g * num2, Blue.b * num2, color.a));
					}
					dictionary[val2] = value;
				}
				sharedMaterials[j] = value;
			}
			val.sharedMaterials = sharedMaterials;
		}
		Light[] componentsInChildren2 = go.GetComponentsInChildren<Light>(true);
		foreach (Light val3 in componentsInChildren2)
		{
			val3.color = new Color(Blue.r, Blue.g, Blue.b, val3.color.a);
		}
	}

	private static Vector3 ChestPoint(Container c)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		Collider componentInChildren = ((Component)c).GetComponentInChildren<Collider>();
		if ((Object)(object)componentInChildren == (Object)null)
		{
			return ((Component)c).transform.position + Vector3.up * 0.5f;
		}
		Bounds bounds = componentInChildren.bounds;
		return ((Bounds)(ref bounds)).center + Vector3.up * ((Bounds)(ref bounds)).extents.y * 0.5f;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
