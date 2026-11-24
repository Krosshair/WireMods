using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Weather particles (rain, snow, mist, wind dust) lit by every point light in one draw. The game's
    // Custom/LitParticles is a forward shader: Unity draws it once and then again for each point light
    // reaching it, so rain over a lit base was drawn over and over. Its materials get Wire/LitParticles
    // (the same shader, plus the mod's point lights in its main pass) when the weather turns them on.
    // Per frame, only while one is in use: the lights' positions and colours (they flicker), or the
    // tiled lights' own buffers when those are on.
    internal sealed partial class ShadowCache
    {
        internal static Shader LitParticlesShader;
        private static Shader s_gameLitParticles;
        private static readonly Dictionary<Material, Shader> s_litSwapped = new Dictionary<Material, Shader>();

        private ComputeBuffer _lPosBuf, _lColorBuf;
        private readonly Plane[] _lPlanes = new Plane[6];
        private bool _lBound;

        private static readonly int LitPosId = Shader.PropertyToID("_WireLitPos"), LitColorId = Shader.PropertyToID("_WireLitColor"),
            LitCountId = Shader.PropertyToID("_WireLitCount"),
            LitTileModeId = Shader.PropertyToID("_WireLitTileMode"),
            LitTilesXId = Shader.PropertyToID("_WireLitTilesX"),
            LitTilesYId = Shader.PropertyToID("_WireLitTilesY");

        private static bool LitWanted =>
            Plugin.LitParticles != null && Plugin.LitParticles.Value && Plugin.UseShader.Value &&
            LitParticlesShader != null && LitParticlesShader.isSupported;

        private static readonly AccessTools.FieldRef<EnvMan, GameObject[]> CurrentPSystems =
            AccessTools.FieldRefAccess<EnvMan, GameObject[]>("m_currentPSystems");

        // EnvMan turns weather particles on: their materials get the one-pass shader (once each).
        [HarmonyPatch(typeof(EnvMan), "SetParticleArrayEnabled")]
        private static class WeatherParticlesOn
        {
            private static void Postfix(GameObject[] psystems, bool enabled)
            {
                if (!enabled) return;
                if (LitWanted) LitSwap(psystems);
                if (FacingWanted) FacingApply(psystems);
            }
        }

        // ---- Weather catching the light ----
        // The game's particle shader can light each particle by which way it faces the light
        // (_LightNormalFactor, 0 to 1), but rain and snow ship at 0: a flake is lit the same whichever
        // way it's turned, so falling snow reads as flat white specks. Weather materials left at 0 get
        // this value; ones the game set itself are left alone. Once per material, when the weather
        // turns it on. Idea from ValheimVisuals (Ivvty), which sets the same 0.5.
        private const float FacingValue = .5f; // picked (ValheimVisuals' default)
        private static readonly int LightNormalFactorId = Shader.PropertyToID("_LightNormalFactor");
        private static readonly List<Material> s_facingSet = new List<Material>();

        private static bool FacingWanted => Plugin.WeatherFacing != null && Plugin.WeatherFacing.Value;

        private static void FacingApply(GameObject[] psystems)
        {
            if (psystems == null) return;
            foreach (GameObject go in psystems)
            {
                if (go == null) continue;
                foreach (ParticleSystemRenderer r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || !m.HasProperty(LightNormalFactorId) || m.GetFloat(LightNormalFactorId) != 0f) continue;
                        m.SetFloat(LightNormalFactorId, FacingValue);
                        s_facingSet.Add(m);
                    }
            }
        }

        // Setting changed (Plugin): set it now on the weather showing, or put the game's 0 back.
        internal static void FacingSettingChanged()
        {
            if (FacingWanted)
            {
                if (EnvMan.instance != null) FacingApply(CurrentPSystems(EnvMan.instance));
                return;
            }
            foreach (Material m in s_facingSet) if (m != null) m.SetFloat(LightNormalFactorId, 0f);
            s_facingSet.Clear();
        }

        private static void LitSwap(GameObject[] psystems)
        {
            if (psystems == null) return;
            foreach (GameObject go in psystems)
            {
                if (go == null) continue;
                foreach (ParticleSystemRenderer r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || s_litSwapped.ContainsKey(m)) continue;
                        Shader s = m.shader;
                        if (s == null) continue;
                        if (s_gameLitParticles == null && s.name == "Custom/LitParticles") s_gameLitParticles = s;
                        if (s != s_gameLitParticles) continue;
                        int queue = m.renderQueue;
                        m.shader = LitParticlesShader;
                        m.renderQueue = queue;
                        s_litSwapped[m] = s;
                    }
            }
        }

        // The setting turned off: the game's shader back on every material.
        private static void LitRestore()
        {
            foreach (KeyValuePair<Material, Shader> kv in s_litSwapped)
                if (kv.Key != null)
                {
                    int queue = kv.Key.renderQueue;
                    kv.Key.shader = kv.Value;
                    kv.Key.renderQueue = queue;
                }
            s_litSwapped.Clear();
        }

        // Setting changed (Plugin): swap now, for the weather already showing, or put the game's back.
        internal static void LitSettingChanged()
        {
            if (LitWanted)
            {
                if (EnvMan.instance != null) LitSwap(CurrentPSystems(EnvMan.instance));
            }
            else LitRestore();
        }

        // From RunFrame, after TiledFrame, for the main camera.
        private void LitFrame(Camera cam)
        {
            if (!LitWanted || s_litSwapped.Count == 0) { LitUnbind(); return; }
            if (!_tHaveLut) { if (!TiledWanted) TiledCapture(cam); LitUnbind(); return; } // Unity's falloff isn't copied yet: Unity lights them meanwhile
            if (TiledWanted && _tPosBuf != null)
            {
                // the tiled pass has them gathered already
                Shader.SetGlobalBuffer(LitPosId, _tPosBuf);
                Shader.SetGlobalBuffer(LitColorId, _tColorBuf);
                Shader.SetGlobalFloat(LitCountId, _tCount);
            }
            else
            {
                if (_lPosBuf == null)
                {
                    _lPosBuf = new ComputeBuffer(MaxTiled, 16);
                    _lColorBuf = new ComputeBuffer(MaxTiled, 16);
                }
                int n = LitGather(cam);
                _lPosBuf.SetData(_tPos, 0, 0, Mathf.Max(1, n));
                _lColorBuf.SetData(_tColor, 0, 0, Mathf.Max(1, n));
                Shader.SetGlobalBuffer(LitPosId, _lPosBuf);
                Shader.SetGlobalBuffer(LitColorId, _lColorBuf);
                Shader.SetGlobalFloat(LitCountId, n);
            }
            // Same ordered set as _tPosBuf/_tColorBuf, already culled by
            // the regular tiled compute pass. Bind its conservative 2D tile
            // list to weather: no extra dispatch, no per-particle CPU work.
            // TiledFrame ran earlier this frame; its command buffer executes
            // before transparent weather particles are rendered.
            bool useTiles = TiledWanted && _tTileBuf != null && _tPosBuf != null &&
                            _tCount > 0 && _tTilesX > 0 && _tTilesY > 0;
            if (useTiles)
            {
                Shader.SetGlobalBuffer(TileLightsId, _tTileBuf);
                Shader.SetGlobalFloat(LitTilesXId, _tTilesX);
                Shader.SetGlobalFloat(LitTilesYId, _tTilesY);
            }
            Shader.SetGlobalFloat(LitTileModeId, useTiles ? 1f : 0f);
            Shader.SetGlobalTexture(AttenId, _tLut);
            _lBound = true;
        }

        // The tiled pass is off: the lights reaching anything in view, the same ones it would take.
        private int LitGather(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, _lPlanes);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            int n = 0;
            foreach (Tracked t in Lights)
            {
                if (n >= MaxTiled) break;
                Light l = t != null ? t.Light : null;
                if (l == null || !l.isActiveAndEnabled) continue;
                if (l.type != LightType.Point || l.cookie != null || l.shadows != LightShadows.None) continue;
                if (l.range <= 0f || l.intensity <= 0f) continue;
                Vector3 p = l.transform.position;
                float r = l.range;
                bool seen = true;
                for (int k = 0; k < 6; k++)
                    if (_lPlanes[k].GetDistanceToPoint(p) < -r) { seen = false; break; }
                if (!seen) continue;
                _tPos[n] = new Vector4(p.x, p.y, p.z, 1f / (r * r));
                _tColor[n] = UnityLightColor(l, linear);
                n++;
            }
            return n;
        }

        private void LitUnbind()
        {
            if (!_lBound && _tDummy != null) return;
            if (_tDummy == null) EnsureTiledDummy();
            Shader.SetGlobalBuffer(LitPosId, _tDummy);
            Shader.SetGlobalBuffer(LitColorId, _tDummy);
            Shader.SetGlobalFloat(LitCountId, 0f);
            _lBound = false;
        }

        private void LitRelease()
        {
            LitRestore();
            _lPosBuf?.Release(); _lPosBuf = null;
            _lColorBuf?.Release(); _lColorBuf = null;
            Shader.SetGlobalFloat(LitCountId, 0f);
            _lBound = false;
        }
    }
}
