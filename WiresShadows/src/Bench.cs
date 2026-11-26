using System.Collections.Generic;
using UnityEngine;

namespace WiresShadows
{
    // Opt-in frame-time test, about 60 s, aimed at what is being worked on:
    //  0 the full mod;
    //  2 the mod without its moving shadows (drawn, not sent to the GPU): 0 - 2 is what they cost;
    //  4 vanilla;
    //  5 the mod with every point light off, 6 vanilla with every point light off.
    // The moving-shadow pair runs four times each (it's the one being worked on), the rest twice.
    // Each phase counts its median frame, so a stutter or a spike doesn't move the result.
    // A switch that has to redraw or fade shadows gets the full settle; the rest a short one.
    internal sealed class Bench : MonoBehaviour
    {
        private float Window = 3.2f;
        private float Settle = 2f;
        private const float Quick = .5f;
        private const float MaxWait = 30f; // s a phase waits for shadow maps at most (picked)
        private float _drawingAt; // when a shadow map was last waiting to be drawn
        private const int Modes = 8;
        //  7 the mod with every point light off and the game's own lighting shader: 5 - 7 is what
        //    our shader costs, 7 - 6 everything else the mod runs.
        private int[] PhaseModes = { 0, 2, 2, 0, 4, 6, 6, 4, 5, 7, 7, 5 };
        // The quick run (about 10 s): the mod and vanilla only, ABBA.
        private static readonly int[] QuickPhases = { 0, 4, 4, 0 };
        private bool _quick;
        private static bool LightsOff(int mode) => mode >= 5;
        private static bool Vanilla(int mode) => mode == 4 || mode == 6;
        // Point lights switched off for the last phases, switched back on after.
        private readonly List<Light> _off = new List<Light>();
        private readonly List<LightLod> _lods = new List<LightLod>();
        private void SetLights(bool off)
        {
            if (off && _off.Count == 0)
            {
                foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    // point and spot: every light but the sun goes through the lighting shader
                    if ((l.type == LightType.Point || l.type == LightType.Spot) && l.enabled)
                    {
                        // its LightLod switches it back on every frame while off: paused with it
                        LightLod lod = l.GetComponent<LightLod>();
                        if (lod != null && lod.enabled) { lod.enabled = false; _lods.Add(lod); }
                        l.enabled = false; _off.Add(l);
                    }
            }
            else if (!off)
            {
                foreach (Light l in _off) if (l != null) l.enabled = true;
                foreach (LightLod lod in _lods) if (lod != null) lod.enabled = true;
                _lods.Clear();
                _off.Clear();
            }
        }
        private float _start;
        private float _settle;
        private int _phase;
        private readonly List<float> _frameMs = new List<float>(512);
        private readonly double[] _ms = new double[Modes];
        private readonly double[] _cpu = new double[Modes]; // the mod's own CPU time per frame, per mode
        private readonly int[] _runs = new int[Modes];
        private bool _wasShader, _wasCached;
        private static readonly string[] ModeNames = {
            "full mod", "", "moving shadows not sent to the GPU", "", "vanilla",
            "mod, point and spot lights off", "vanilla, point and spot lights off", "mod, point and spot lights off, game's lighting shader"
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
            if (Plugin.QuickBenchmark != null && Plugin.QuickBenchmark.Value)
            {
                _quick = true;
                PhaseModes = QuickPhases;
                Window = 1.6f;
                Settle = .8f;
            }
            Begin(0);
        }

        private void Begin(int phase)
        {
            if (_sampling)
            {
                ShadowCache.EndBenchmarkSampling();
                _sampling = false;
            }
            int before = phase > 0 ? PhaseModes[phase - 1] : -1;
            _phase = phase;
            int mode = PhaseModes[phase];
            // Changing skipped-GPU -> full invalidates stale faces once;
            // consecutive identical phases must NOT toggle the skip flag.
            ShadowCache.BenchmarkSkipMovingGpu(mode == 2);
            Plugin.UseShader.Value = !Vanilla(mode);
            Plugin.CachedShadows = !Vanilla(mode);
            SetLights(LightsOff(mode));
            Plugin.BenchGameLighting = mode == 7;
            Plugin.ReApply();
            // same mode, or full -> moving shadows not sent (nothing to redraw): a short settle
            bool quick = mode == before || (mode == 2 && before == 0);
            _settle = quick ? Quick : Settle;
            _start = Time.realtimeSinceStartup;
            _frameMs.Clear();
        }

        private void Update()
        {
            float t = Time.realtimeSinceStartup - _start;
            if (_result != null)
            {
                if (Time.realtimeSinceStartup - _doneAt > 15f) Destroy(this);
                return;
            }
            // the game's light LOD can switch one back on: kept off (benchmark only)
            if (_off.Count > 0)
                foreach (Light l in _off) if (l != null && l.enabled) l.enabled = false;
            if (ShadowCache.Drawing) _drawingAt = Time.realtimeSinceStartup;
            if (t < _settle)
                return;
            // A mod phase after a vanilla one starts with every shadow map thrown away: measured only once
            // they're all drawn again, or it measures the refill (a face a frame). Lights get their slots
            // at each 1 s ranking, so nothing drawing for a moment isn't done yet: quiet for longer than
            // one ranking. At most MaxWait.
            if (!_sampling && !Vanilla(PhaseModes[_phase]) && Time.realtimeSinceStartup - _drawingAt < 1.25f && t < MaxWait)
            {
                _settle = t;
                return;
            }
            if (!_sampling)
            {
                // Include only the measured window, never the warm-up.
                ShadowCache.BeginBenchmarkSampling();
                _sampling = true;
            }
            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (t < _settle + Window)
                return;
            int mode = PhaseModes[_phase];
            _frameMs.Sort();
            _ms[mode] += _frameMs.Count > 0 ? _frameMs[_frameMs.Count / 2] : 0f;
            _runs[mode]++;
            if (_sampling)
            {
                Plugin.Note("[benchmark-work] " + ModeNames[mode] +
                    ", phase " + (_phase + 1) + "/" + PhaseModes.Length + ": " + ShadowCache.EndBenchmarkSampling());
                _cpu[mode] += ShadowCache.LastCpuMs;
                _sampling = false;
            }
            if (_phase < PhaseModes.Length - 1) { Begin(_phase + 1); return; }

            for (int m = 0; m < Modes; m++)
                if (_runs[m] > 0) { _ms[m] /= _runs[m]; _cpu[m] /= _runs[m]; }
            if (_quick)
            {
                double m = _ms[0], v = _ms[4];
                _result = string.Format("quick run: mod {0:0.00} ms ({1:0} fps), vanilla {2:0.00} ms ({3:0} fps), mod vs vanilla {4:+0.00;-0.00} ms.",
                    m, 1000 / m, v, 1000 / v, m - v);
                Plugin.Log.LogInfo("Benchmark: " + _result);
                _doneAt = Time.realtimeSinceStartup;
                Restore();
                return;
            }
            double full = _ms[0], noMoving = _ms[2], van = _ms[4], modDark = _ms[5], vanDark = _ms[6], gameShader = _ms[7];
            double movingCpu = _cpu[0] - _cpu[2];
            _result = string.Format(
                "mod {0:0.00} ms ({1:0} fps), vanilla {2:0.00} ms ({3:0} fps), mod vs vanilla {4:+0.00;-0.00} ms; " +
                "our moving shadows {5:+0.00;-0.00} ms: CPU {6:+0.00;-0.00} ms, GPU and the rest {7:+0.00;-0.00} ms; " +
                "point and spot lights off: mod {8:0.00} ms, vanilla {9:0.00} ms, mod vs vanilla {10:+0.00;-0.00} ms; " +
                "point lights cost: mod {11:+0.00;-0.00} ms, vanilla {12:+0.00;-0.00} ms; " +
                "with no point or spot lights: our lighting shader {13:+0.00;-0.00} ms, the rest of the mod {14:+0.00;-0.00} ms (median frames).",
                full, 1000 / full, van, 1000 / van, full - van,
                full - noMoving, movingCpu, (full - noMoving) - movingCpu,
                modDark, vanDark, modDark - vanDark,
                full - modDark, van - vanDark,
                modDark - gameShader, gameShader - vanDark);
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
            SetLights(false);
            Plugin.BenchGameLighting = false;
            Plugin.ReApply();
            Plugin.UseShader.Value = _wasShader;
            Plugin.CachedShadows = _wasCached;
            Plugin.Benchmark.Value = false;
            if (Plugin.QuickBenchmark != null) Plugin.QuickBenchmark.Value = false;
        }

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(12, 12, 8, 8) };
            string text;
            if (_result != null)
                text = "Wire's Shadows benchmark done\n" + _result.Replace(", ", "\n").Replace("; ", "\n");
            else
            {
                float t = Time.realtimeSinceStartup - _start;
                string stage = t < _settle ? "settling" : "measuring " + Mathf.CeilToInt(_settle + Window - t) + " s";
                int left = PhaseModes.Length - _phase - 1;
                float remaining = Mathf.Max(0f, _settle + Window - t) + left * (1.3f + Window);
                text = "Wire's Shadows benchmark: " + ModeNames[PhaseModes[_phase]] + " (phase " + (_phase + 1) + "/" + PhaseModes.Length + "), " + stage +
                       "\nAbout " + Mathf.CeilToInt(remaining) + " s left. Keep the same view and position.";
            }
            GUI.Box(new Rect(20, 20, 700, _result != null ? 460 : 95), text, _style);
        }
    }
}



