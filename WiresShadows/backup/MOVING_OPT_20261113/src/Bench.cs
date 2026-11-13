using UnityEngine;

namespace WiresShadows
{
    // Opt-in five-way frame-time test: the full mod, the full mod without the sun shadow caster
    // culling, the mod without its moving-shadow GPU work, the shader alone, and vanilla.
    // No extra work is added outside the already-existing benchmark.
    internal sealed class Bench : MonoBehaviour
    {
        private const float Window = 4f;
        private const float Settle = 2.5f; // switching back re-renders the cached maps first
        private const int Modes = 5, Rounds = 2;
        // Paired ABBA sequences reduce drift: full / no sun culling, then the moving-shadow skip,
        // then shader-only / vanilla in reverse order. Every mode runs twice.
        private static readonly int[] PhaseModes = { 0, 4, 4, 0, 1, 1, 2, 3, 3, 2 };
        private float _start;
        private float _settle;
        private int _phase;
        private double _sum;
        private int _frames;
        private readonly double[] _ms = new double[Modes];
        private bool _wasShader, _wasCached;
        private static readonly string[] ModeNames = {
            "full mod", "mod without moving GPU submission", "shader only", "vanilla", "mod without sun culling"
        };
        private string _result;
        private float _doneAt;
        private GUIStyle _style;
        private bool _restored;
        private bool _sampling;

        private void Start()
        {
            Plugin.BenchRunning = true;
            _wasShader = Plugin.UseShader.Value;
            _wasCached = Plugin.CachedShadows;
            Begin(0);
        }

        private void Begin(int phase)
        {
            if (_sampling)
            {
                ShadowCache.EndBenchmarkSampling();
                _sampling = false;
            }
            _phase = phase;
            int mode = PhaseModes[phase];
            // Changing skipped-GPU -> full invalidates stale faces once;
            // consecutive identical phases must NOT toggle the skip flag.
            ShadowCache.BenchmarkSkipMovingGpu(mode == 1);
            Plugin.UseShader.Value = mode != 3;
            Plugin.CachedShadows = mode == 0 || mode == 1 || mode == 4;
            Plugin.SunCulling = mode != 4;
            // Full and skipped-GPU blocks are now adjacent and paired;
            // the normal settling window covers the face refresh on return.
            _settle = Settle;
            _start = Time.realtimeSinceStartup;
            _sum = 0;
            _frames = 0;
        }

        private void Update()
        {
            float t = Time.realtimeSinceStartup - _start;
            if (_result != null)
            {
                if (Time.realtimeSinceStartup - _doneAt > 15f) Destroy(this);
                return;
            }
            if (t < _settle)
                return;
            if (!_sampling)
            {
                // Include only the measured window, never the warm-up.
                ShadowCache.BeginBenchmarkSampling();
                _sampling = true;
            }
            // A hitch (alt-tab, loading, a save) isn't the lighting: skip it.
            if (Time.unscaledDeltaTime < 0.25f)
            {
                _sum += Time.unscaledDeltaTime;
                _frames++;
            }
            if (t < _settle + Window)
                return;
            int mode = PhaseModes[_phase];
            _ms[mode] += (_frames > 0 ? _sum / _frames * 1000.0 : 0) / Rounds;
            if (_sampling)
            {
                Plugin.Note("[benchmark-work] " + ModeNames[mode] +
                    ", phase " + (_phase + 1) + "/" + PhaseModes.Length + ": " + ShadowCache.EndBenchmarkSampling());
                _sampling = false;
            }
            if (_phase < PhaseModes.Length - 1) { Begin(_phase + 1); return; }

            double full = _ms[0], skip = _ms[1], shader = _ms[2], off = _ms[3], nosun = _ms[4];
            // Paired mode delta is only a frame-time observation, NOT a
            // trustworthy GPU timer: other scene and pipeline work can vary.
            // It guides dedicated GPU profiling rather than replacing it.
            _result = string.Format(
                "mod {0:0.00} ms ({1:0} fps), without sun culling {10:0.00} ms ({11:0} fps), moving GPU skipped {2:0.00} ms ({3:0} fps), shader only {4:0.00} ms ({5:0} fps), vanilla {6:0.00} ms ({7:0} fps); sun culling {12:+0.00;-0.00} ms, full-minus-skip (not isolated GPU time) {8:+0.00;-0.00} ms, mod vs vanilla {9:+0.00;-0.00} ms.",
                full, 1000 / full, skip, 1000 / skip, shader, 1000 / shader,
                off, 1000 / off, full - skip, full - off, nosun, 1000 / nosun, full - nosun);
            Plugin.Log.LogInfo("Benchmark: " + _result + " " + SunCasters.Summary());
            _doneAt = Time.realtimeSinceStartup;
            Restore();
        }

        // Also on quitting or turning the benchmark off midway.
        private void OnDestroy() => Restore();

        private void Restore()
        {
            if (_restored)
                return;
            _restored = true;
            Plugin.BenchRunning = false;
            if (_sampling)
            {
                ShadowCache.EndBenchmarkSampling();
                _sampling = false;
            }
            ShadowCache.BenchmarkSkipMovingGpu(false);
            Plugin.UseShader.Value = _wasShader;
            Plugin.CachedShadows = _wasCached;
            Plugin.SunCulling = true;
            Plugin.Benchmark.Value = false;
        }

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(12, 12, 8, 8) };
            string text;
            if (_result != null)
                text = "Wire's Shadows benchmark done\n" + _result.Replace(", ", "\n");
            else
            {
                float t = Time.realtimeSinceStartup - _start;
                string stage = t < _settle ? "settling" : "measuring " + Mathf.CeilToInt(_settle + Window - t) + " s";
                int left = PhaseModes.Length - _phase - 1;
                float remaining = Mathf.Max(0f, _settle + Window - t) + left * (Settle + Window);
                text = "Wire's Shadows benchmark: " + ModeNames[PhaseModes[_phase]] + " (phase " + (_phase + 1) + "/" + PhaseModes.Length + "), " + stage +
                       "\nAbout " + Mathf.CeilToInt(remaining) + " s left. Keep the same view and position.";
            }
            GUI.Box(new Rect(20, 20, 700, _result != null ? 250 : 95), text, _style);
        }
    }
}
