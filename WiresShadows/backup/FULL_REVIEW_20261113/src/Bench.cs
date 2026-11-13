using UnityEngine;

namespace WiresShadows
{
    // Opt-in frame-time test in the same time as before (about 52 s), five modes, each twice:
    //  0 the full mod;
    //  1 the full mod with faint lights' moving shadows redrawn every frame (FaintCut off; on,
    //    they're redrawn less often the further out in a light's range the moving things are);
    //  2 the mod without its moving shadows (the cached ones only);
    //  3 the cached shadows plus the game's own shadows for those lights, as vanilla draws its
    //    moving shadows: 3 - 2 is what vanilla's way costs, 0 - 2 what ours does;
    //  4 vanilla.
    // A switch that has to redraw or fade shadows gets the full settle; the rest a short one.
    // No extra work is added outside the already-existing benchmark.
    internal sealed class Bench : MonoBehaviour
    {
        private const float Window = 3.9f;
        private const float Settle = 2.5f, Quick = .5f;
        private const int Modes = 5, Rounds = 2;
        // Paired ABBA sequences reduce drift: full / moving-shadow skip, then shader-only / vanilla
        // in reverse order. Every mode runs twice.
        private static readonly int[] PhaseModes = { 0, 1, 1, 0, 2, 3, 3, 2, 4, 4 };
        private float _start;
        private float _settle;
        private int _phase;
        private double _sum;
        private int _frames;
        private readonly double[] _ms = new double[Modes];
        private bool _wasShader, _wasCached;
        private static readonly string[] ModeNames = {
            "full mod", "faint lights every frame", "cached shadows only", "cached + vanilla moving shadows", "vanilla"
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
            int before = phase > 0 ? PhaseModes[phase - 1] : 0;
            _phase = phase;
            int mode = PhaseModes[phase];
            // Changing skipped-GPU -> full invalidates stale faces once;
            // consecutive identical phases must NOT toggle the skip flag.
            ShadowCache.BenchmarkSkipMovingGpu(mode == 2 || mode == 3);
            ShadowCache.BenchmarkVanillaShadows(mode == 3);
            Plugin.FaintCut = mode != 1;
            Plugin.UseShader.Value = mode != 4;
            Plugin.CachedShadows = mode != 4;
            bool quick = mode == before || (mode <= 1 && before <= 1) || (mode == 2 && before <= 1);
            _settle = quick ? Quick : Settle;
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

            double full = _ms[0], wide = _ms[1], cached = _ms[2], van = _ms[3], off = _ms[4];
            // Paired mode delta is only a frame-time observation, NOT a
            // trustworthy GPU timer: other scene and pipeline work can vary.
            // It guides dedicated GPU profiling rather than replacing it.
            _result = string.Format(
                "mod {0:0.00} ms ({1:0} fps), faint lights every frame {2:0.00} ms, cached shadows only {3:0.00} ms, cached + vanilla moving {4:0.00} ms, vanilla {5:0.00} ms ({6:0} fps); faint lights every other frame {7:+0.00;-0.00} ms, our moving shadows {8:+0.00;-0.00} ms, vanilla's moving shadows {9:+0.00;-0.00} ms, mod vs vanilla {10:+0.00;-0.00} ms.",
                full, 1000 / full, wide, cached, van, off, 1000 / off,
                full - wide, full - cached, van - cached, full - off);
            Plugin.Log.LogInfo("Benchmark: " + _result);
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
            ShadowCache.BenchmarkVanillaShadows(false);
            Plugin.FaintCut = true;
            Plugin.UseShader.Value = _wasShader;
            Plugin.CachedShadows = _wasCached;
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
                float remaining = Mathf.Max(0f, _settle + Window - t) + left * (1.3f + Window);
                text = "Wire's Shadows benchmark: " + ModeNames[PhaseModes[_phase]] + " (phase " + (_phase + 1) + "/" + PhaseModes.Length + "), " + stage +
                       "\nAbout " + Mathf.CeilToInt(remaining) + " s left. Keep the same view and position.";
            }
            GUI.Box(new Rect(20, 20, 700, _result != null ? 320 : 95), text, _style);
        }
    }
}
