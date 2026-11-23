using System;
using System.Collections.Generic;
using UnityEngine;

public class ZSFX : MonoBehaviour, IMonoUpdater
{
	public bool m_playOnAwake = true;

	[Header("Captions")]
	[Tooltip("If set, will create an entry in the Closed Captions display.")]
	public string m_closedCaptionToken = "";

	[Tooltip("Appended after the first token, usually used for actions - $enemyname $attack for example.")]
	public string m_secondaryCaptionToken = "";

	[Tooltip("Sorted in order of importance. If too many captions are pushed, low importance ones will be removed first.")]
	public ClosedCaptions.CaptionType m_captionType;

	[Tooltip("Don't draw a caption if the sound is quieter than this (takes distance fading into account)")]
	public float m_minimumCaptionVolume = 0.3f;

	[Header("Clips")]
	public AudioClip[] m_audioClips = new AudioClip[0];

	[Header("Audio System")]
	[Tooltip("How many of the same sound can play in a small area? Uses the min distance of 3D sounds, or 1 meter, whichever is higher")]
	public int m_maxConcurrentSources;

	[Tooltip("Ignore the distance check, don't play sound if any other of the same sound were played recently")]
	public bool m_ignoreConcurrencyDistance;

	[Header("Random")]
	public float m_maxPitch = 1f;

	public float m_minPitch = 1f;

	public float m_maxVol = 1f;

	public float m_minVol = 1f;

	[Header("Fade")]
	public float m_fadeInDuration;

	public float m_fadeOutDuration;

	public float m_fadeOutDelay;

	public bool m_fadeOutOnAwake;

	[Header("Pan")]
	public bool m_randomPan;

	public float m_minPan = -1f;

	public float m_maxPan = 1f;

	[Header("Delay")]
	public float m_maxDelay;

	public float m_minDelay;

	[Header("Reverb")]
	public bool m_distanceReverb = true;

	public bool m_useCustomReverbDistance;

	public float m_customReverbDistance = 10f;

	[Header("Vibration")]
	[Tooltip("Settings for platforms / controllers that support audio based vibration, also known as haptic feedback.")]
	public bool m_useVibration;

	[Tooltip("By default, vibration will only affect creators of the SFX - some SFX should vibrate for everyone however. Loud sounds like trees falling or boats creaking.")]
	public bool m_vibrateAllAudible;

	[Tooltip("By default, the vibration uses the volume of the audio clip being played as vibration strength. This allows for fine-tuning the strength. 1 is default. Allows for up to 3 in order to emphasize certain vibration effects further.")]
	[Range(0f, 3f)]
	public float m_vibrationModifier = 1f;

	[Header("Controller Audio")]
	public bool m_playThroughController;

	[Tooltip("By default, audio playing through controllers only affects creator of the SFX, same as vibrations. Some audio effects should however affect everyone, e.g. petting.")]
	public bool m_playThroughAllControllers;

	[Range(0f, 2f)]
	public float m_controllerAudioVolume = 0.5f;

	[HideInInspector]
	public int m_hash;

	private const float m_globalReverbDistance = 64f;

	private const float m_minReverbSpread = 45f;

	private const float m_maxReverbSpread = 120f;

	private float m_delay;

	private float m_time;

	private float m_fadeOutTimer = -1f;

	private float m_fadeInTimer = -1f;

	private float m_vol = 1f;

	private float m_concurrencyVolumeModifier = 1f;

	private float m_volumeModifier = 1f;

	private float m_pitchModifier = 1f;

	private float m_reverbPitchModifier;

	private bool m_disabledFromConcurrency;

	private float m_baseSpread;

	private float m_basePitch;

	private float m_updateReverbTimer;

	private ZDOID m_sfxCreator;

	private AudioSource m_audioSource;

	public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();

	public event Action<ZSFX> OnDestroyingSfx = delegate
	{
	};

	public void Awake()
	{
		m_delay = UnityEngine.Random.Range(m_minDelay, m_maxDelay);
		m_audioSource = GetComponent<AudioSource>();
		m_baseSpread = m_audioSource.spread;
	}

	private void Start()
	{
		CustomUpdate(0f, float.NaN);
	}

	private void OnEnable()
	{
		if (m_audioSource != null)
		{
			Instances.Add(this);
		}
	}

	private void OnDisable()
	{
		if (m_audioSource != null && m_playOnAwake && m_audioSource.loop)
		{
			m_time = 0f;
			m_delay = UnityEngine.Random.Range(m_minDelay, m_maxDelay);
			m_audioSource.Stop();
			if (m_useVibration && (bool)GamepadRumble.instance)
			{
				GamepadRumble.instance.StopVibration(m_audioSource);
			}
		}
		Instances.Remove(this);
	}

	private void OnDestroy()
	{
		this.OnDestroyingSfx(this);
	}

	public void CustomUpdate(float dt, float time)
	{
		m_time += dt;
		if (m_delay >= 0f && m_time >= m_delay)
		{
			m_delay = -1f;
			if (m_playOnAwake)
			{
				Play();
			}
		}
		if (IsLooping())
		{
			m_concurrencyVolumeModifier = Mathf.MoveTowards(m_concurrencyVolumeModifier, (!m_disabledFromConcurrency) ? 1 : 0, dt / 0.5f);
		}
		if (!m_audioSource.isPlaying)
		{
			return;
		}
		if (m_distanceReverb && m_audioSource.loop)
		{
			m_updateReverbTimer += dt;
			if (m_updateReverbTimer > 1f)
			{
				m_updateReverbTimer = 0f;
				UpdateReverb();
			}
		}
		if (m_fadeOutOnAwake && m_time > m_fadeOutDelay)
		{
			m_fadeOutOnAwake = false;
			FadeOut();
		}
		float vol = m_vol;
		float num = 1f;
		if (m_fadeOutTimer >= 0f)
		{
			m_fadeOutTimer += dt;
			if (m_fadeOutTimer >= m_fadeOutDuration)
			{
				m_audioSource.volume = 0f;
				Stop();
				return;
			}
			num = 1f - Mathf.Clamp01(m_fadeOutTimer / m_fadeOutDuration);
		}
		else if (m_fadeInTimer >= 0f)
		{
			m_fadeInTimer += dt;
			num = Mathf.Clamp01(m_fadeInTimer / m_fadeInDuration);
			if (m_fadeInTimer > m_fadeInDuration)
			{
				m_fadeInTimer = -1f;
			}
		}
		m_audioSource.volume = vol * num * m_concurrencyVolumeModifier * m_volumeModifier;
		float num2 = m_basePitch * m_pitchModifier;
		num2 -= num2 * m_reverbPitchModifier;
		m_audioSource.pitch = num2;
	}

	public void FadeOut()
	{
		if (m_fadeOutTimer < 0f)
		{
			m_fadeOutTimer = 0f;
		}
	}

	public void Stop()
	{
		if (m_audioSource != null)
		{
			m_audioSource.Stop();
		}
		if (m_useVibration && (bool)GamepadRumble.instance)
		{
			GamepadRumble.instance.StopVibration(m_audioSource);
		}
	}

	public bool IsPlaying()
	{
		if (m_audioSource == null)
		{
			return false;
		}
		return m_audioSource.isPlaying;
	}

	private void UpdateReverb()
	{
		Camera mainCamera = Utils.GetMainCamera();
		if (m_distanceReverb && m_audioSource.spatialBlend != 0f && mainCamera != null)
		{
			float num = Vector3.Distance(mainCamera.transform.position, base.transform.position);
			bool num2 = Mister.InsideMister(base.transform.position);
			float num3 = (m_useCustomReverbDistance ? m_customReverbDistance : 64f);
			float num4 = Mathf.Clamp01(num / num3);
			float b = Mathf.Clamp01(m_audioSource.maxDistance / num3) * Mathf.Clamp01(num / m_audioSource.maxDistance);
			float num5 = Mathf.Max(num4, b);
			if (num2)
			{
				num5 = Mathf.Lerp(num5, 0f, num4);
				m_reverbPitchModifier = 0.5f * num4;
			}
			m_audioSource.bypassReverbZones = false;
			m_audioSource.reverbZoneMix = num5;
			if (m_baseSpread < 120f)
			{
				float a = Mathf.Max(m_baseSpread, 45f);
				m_audioSource.spread = Mathf.Lerp(a, 120f, num5);
			}
		}
		else
		{
			m_audioSource.bypassReverbZones = true;
		}
	}

	public void Play()
	{
		if (!(m_audioSource == null) && m_audioClips.Length != 0 && m_audioSource.gameObject.activeInHierarchy && AudioMan.instance.RequestPlaySound(this))
		{
			if (m_audioSource.loop && m_disabledFromConcurrency)
			{
				m_concurrencyVolumeModifier = 0f;
			}
			if (ClosedCaptions.Valid && !m_audioSource.loop && m_closedCaptionToken.Length > 0)
			{
				ClosedCaptions.Instance.RegisterCaption(this);
			}
			int num = UnityEngine.Random.Range(0, m_audioClips.Length);
			m_audioSource.clip = m_audioClips[num];
			m_audioSource.pitch = UnityEngine.Random.Range(m_minPitch, m_maxPitch);
			m_basePitch = m_audioSource.pitch;
			if (m_randomPan)
			{
				m_audioSource.panStereo = UnityEngine.Random.Range(m_minPan, m_maxPan);
			}
			m_vol = UnityEngine.Random.Range(m_minVol, m_maxVol);
			if (m_fadeInDuration > 0f)
			{
				m_audioSource.volume = 0f;
				m_fadeInTimer = 0f;
			}
			else
			{
				m_audioSource.volume = m_vol;
			}
			UpdateReverb();
			m_audioSource.Play();
			if (m_useVibration && m_audioSource.clip != null)
			{
				SetUpVibration(m_audioSource.clip.name);
			}
			if (m_playThroughController && m_audioSource.clip != null)
			{
				PlayAudioThroughController();
			}
		}
	}

	public void SetSoundEffectCreator(ZDOID creator)
	{
		m_sfxCreator = creator;
	}

	private bool IsPlayerCreator()
	{
		ZNetView zNetView = GetComponent<ZNetView>();
		if ((object)zNetView == null)
		{
			zNetView = GetComponentInParent<ZNetView>();
		}
		ZDOID zDOID = (Player.m_localPlayer ? Player.m_localPlayer.GetZDOID() : ZDOID.None);
		bool result = true;
		if ((object)zNetView != null && zNetView.IsValid() && m_sfxCreator == ZDOID.None)
		{
			result = zDOID.UserID == zNetView.GetZDO().GetLong(ZDOVars.s_ownerZDOUserId, 0L);
			result &= zDOID.ID == zNetView.GetZDO().GetLong(ZDOVars.s_ownerZDOId, 0L);
		}
		else if (m_sfxCreator != ZDOID.None)
		{
			result = zDOID.ID == m_sfxCreator.ID && zDOID.UserID == m_sfxCreator.UserID;
		}
		return result;
	}

	private void SetUpVibration(string clipName)
	{
		if (m_vibrateAllAudible || IsPlayerCreator())
		{
			Vector3 vector = (Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero);
			if (Application.platform == RuntimePlatform.PS5 && GamepadRumble.instance != null)
			{
				GamepadRumble.instance.AddVibration(m_audioSource, m_vol, m_vibrationModifier, m_vibrateAllAudible, vector);
			}
			else if (Application.platform == RuntimePlatform.Switch2 && GamepadRumble.instance != null)
			{
				GamepadRumble.instance.AddBinaryVibration(clipName, m_vol, m_vibrationModifier, m_vibrateAllAudible, vector, m_audioSource);
			}
		}
	}

	private void PlayAudioThroughController()
	{
	}

	public void GenerateHash()
	{
		m_hash = Guid.NewGuid().GetHashCode();
	}

	public float GetConcurrencyDistance()
	{
		if (!m_ignoreConcurrencyDistance)
		{
			return Mathf.Max(1f, m_audioSource.minDistance);
		}
		return float.PositiveInfinity;
	}

	public void ConcurrencyDisable()
	{
		m_disabledFromConcurrency = true;
	}

	public void ConcurrencyEnable()
	{
		m_disabledFromConcurrency = false;
	}

	public float GetVolumeModifierByDistance(float distance)
	{
		float num = 0f;
		switch (m_audioSource.rolloffMode)
		{
		default:
			return Mathf.InverseLerp(m_audioSource.maxDistance, m_audioSource.minDistance, distance);
		case AudioRolloffMode.Logarithmic:
		{
			float f = distance / m_audioSource.minDistance;
			return 1f * (1f / (1f + 1f * Mathf.Log(f)));
		}
		case AudioRolloffMode.Custom:
		{
			float time = Mathf.InverseLerp(m_audioSource.minDistance, m_audioSource.maxDistance, distance);
			return m_audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(time);
		}
		}
	}

	public bool IsLooping()
	{
		return m_audioSource.loop;
	}

	public void SetVolumeModifier(float v)
	{
		m_volumeModifier = v;
	}

	public float GetVolumeModifier()
	{
		return m_volumeModifier;
	}

	public void SetPitchModifier(float p)
	{
		m_pitchModifier = p;
	}

	public float GetPitchModifier()
	{
		return m_pitchModifier;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
