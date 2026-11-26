using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

namespace WiresShakeItUp
{
    // Lens effect on the main camera: depth of field measured like a real
    // lens (diopters). Replaces Valheim's DoF while
    // the in-game Depth of Field setting is on. If the shader bundle can't
    // load, Valheim's DoF stays with the smoothed focus from DepthOfFieldPatch.
    internal sealed class LensEffect : MonoBehaviour
    {
        private const string BundleResource = "wirecameratweaks.bundle";

        // 20 px at 1080p, the single-pass bokeh reference.
        private const float MaxBlurOfHeight = 20f / 1080f;
        // Player capsule radius (Player.prefab).
        private const float PlayerRadius = 0.49f;

        private static Material _material;
        private static bool _loadTried;

        internal static LensEffect Instance;

        private Camera _camera;
        private CameraEffects _effects;
        private DepthOfField _vanillaDof;
        private bool _vanillaWanted;
        private bool _dofOn;
        private RenderTexture _focusA;
        private RenderTexture _focusB;
        private float _lastTime = -1f;

        private static readonly int FocusId = Shader.PropertyToID("_WireFocus");
        private static readonly int BlurId = Shader.PropertyToID("_WireBlur");
        private static readonly int ExcludeId = Shader.PropertyToID("_WireExclude");
        private static readonly int KInId = Shader.PropertyToID("_WireKIn");
        private static readonly int KOutId = Shader.PropertyToID("_WireKOut");
        private static readonly int NearLimitId = Shader.PropertyToID("_WireNearLimit");
        private static readonly int NearOnId = Shader.PropertyToID("_WireNearOn");
        private static readonly int FarCapId = Shader.PropertyToID("_WireFarCap");
        private static readonly int CocScaleId = Shader.PropertyToID("_WireCocScale");
        private static readonly int MaxRadiusId = Shader.PropertyToID("_WireMaxRadius");
        private static readonly int RadStepId = Shader.PropertyToID("_WireRadStep");

        internal static bool Ready => LoadMaterial() != null;

        private static Material LoadMaterial()
        {
            if (_loadTried) return _material;
            _loadTried = true;
            using (Stream stream = typeof(LensEffect).Assembly.GetManifestResourceStream(BundleResource))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning("Lens shader bundle missing, using Valheim's depth of field.");
                    return null;
                }
                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                AssetBundle bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                Shader shader = bundle != null ? bundle.LoadAsset<Shader>("Assets/Shaders/WireLens.shader") : null;
                if (shader == null || !shader.isSupported)
                {
                    Plugin.Log.LogWarning("Lens shader failed to load, using Valheim's depth of field.");
                    return null;
                }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _material;
        }

        internal void Init(CameraEffects effects, DepthOfField vanillaDof)
        {
            _effects = effects;
            _vanillaDof = vanillaDof;
            _vanillaWanted = vanillaDof != null && vanillaDof.enabled;
            Refresh();
        }

        internal void SetVanillaWanted(bool wanted)
        {
            _vanillaWanted = wanted;
            Refresh();
        }

        internal void Refresh()
        {
            bool ready = Ready;
            _dofOn = ready && _vanillaWanted && ModConfig.DofEnabled.Value;
            if (_vanillaDof != null)
                _vanillaDof.enabled = _vanillaWanted && !_dofOn;
            _lastTime = -1f;
            enabled = _dofOn;
        }

        private void Awake()
        {
            Instance = this;
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (_camera != null)
                _camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Release(ref _focusA);
            Release(ref _focusB);
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        private static RenderTexture Focus(ref RenderTexture rt)
        {
            if (rt == null || !rt.IsCreated())
            {
                Release(ref rt);
                rt = new RenderTexture(1, 1, 0, RenderTextureFormat.RGFloat) { hideFlags = HideFlags.HideAndDontSave };
                rt.Create();
            }
            return rt;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            Material m = _material;
            if (m == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            RenderTexture blur = null;
            if (_dofOn)
            {
                float now = Time.unscaledTime;
                bool reset = _lastTime < 0f || now - _lastTime > 0.5f;
                float dt = now - _lastTime;
                _lastTime = now;
                m.SetFloat(KInId, reset ? 1f : Blend(dt, ModConfig.DofBlurInTime.Value));
                m.SetFloat(KOutId, reset ? 1f : Blend(dt, ModConfig.DofClearTime.Value));

                // Keep the player out of the focus and out of the near blur.
                float exclude = 0f;
                float nearLimit = 0f;
                float farCap = 1e6f;
                Player player = Player.m_localPlayer;
                if (player != null)
                {
                    float dist = Vector3.Distance(transform.position, player.GetCenterPoint());
                    exclude = dist + PlayerRadius;
                    if (dist - PlayerRadius > 0.05f)
                        nearLimit = 1f / (dist - PlayerRadius);
                    // Steering a ship keeps focus out at Valheim's ship minimum; building does too.
                    if (player.GetControlledShip() != null ||
                        (ModConfig.DofClearWhileBuilding.Value && player.InPlaceMode()))
                        farCap = 1f / Mathf.Max(_effects.m_dofMinDistanceShip, 1f);
                }

                float halfHeight = src.height * 0.5f;
                float tanHalfFov = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                m.SetFloat(ExcludeId, exclude);
                m.SetFloat(NearLimitId, nearLimit);
                m.SetFloat(NearOnId, ModConfig.DofNearBlur.Value ? 1f : 0f);
                m.SetFloat(FarCapId, farCap);
                m.SetFloat(CocScaleId, ModConfig.DofAperture.Value * 0.001f / (2f * tanHalfFov) * halfHeight);
                float maxRadius = MaxBlurOfHeight * halfHeight;
                m.SetFloat(MaxRadiusId, maxRadius);
                // Reference sample spacing: wider leaves visible dots.
                m.SetFloat(RadStepId, 0.5f);

                RenderTexture oldFocus = Focus(ref _focusA);
                RenderTexture newFocus = Focus(ref _focusB);
                Graphics.Blit(oldFocus, newFocus, m, 0);
                _focusA = newFocus;
                _focusB = oldFocus;
                m.SetTexture(FocusId, newFocus);

                int w = Mathf.Max(1, src.width / 2);
                int h = Mathf.Max(1, src.height / 2);
                RenderTexture prepared = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
                blur = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
                Graphics.Blit(src, prepared, m, 1);
                Graphics.Blit(prepared, blur, m, 2);
                RenderTexture.ReleaseTemporary(prepared);
                m.SetTexture(BlurId, blur);
            }

            Graphics.Blit(src, dst, m, 3);
            if (blur != null) RenderTexture.ReleaseTemporary(blur);
        }

        // Fraction to move this frame so a change is 95% done after `settle` seconds.
        private static float Blend(float dt, float settle)
        {
            return 1f - Mathf.Exp(-3f * dt / Mathf.Max(settle, 0.01f));
        }
    }

    [HarmonyPatch(typeof(CameraEffects), "Awake")]
    internal static class CameraEffectsAwakePatch
    {
        private static readonly AccessTools.FieldRef<CameraEffects, DepthOfField> DofRef =
            AccessTools.FieldRefAccess<CameraEffects, DepthOfField>("m_dof");

        [HarmonyPostfix]
        private static void Postfix(CameraEffects __instance)
        {
            if (__instance.GetComponent<Camera>() == null) return;
            LensEffect lens = __instance.gameObject.GetComponent<LensEffect>();
            if (lens == null)
                lens = __instance.gameObject.AddComponent<LensEffect>();
            lens.Init(__instance, DofRef(__instance));
        }
    }

    [HarmonyPatch(typeof(CameraEffects), "SetDof")]
    internal static class CameraEffectsSetDofPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CameraEffects __instance, bool enabled)
        {
            LensEffect lens = __instance.GetComponent<LensEffect>();
            if (lens != null)
                lens.SetVanillaWanted(enabled || __instance.m_forceDof);
        }
    }
}
