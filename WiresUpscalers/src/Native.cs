using System;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace WiresUpscalers
{
    // The native plugin (native/WiresUpscalers.cpp). Everything it does runs on Unity's render thread,
    // from events issued here.
    internal static class Native
    {
        internal const string Dll = "WiresUpscalersNative";
        private const int EventDetect = 1, EventEvaluate = 2, EventRelease = 3;
        internal const int AvailDLSS = 1, AvailFSR3 = 2, AvailFSR4 = 4, AvailXeSS = 8, Ready = 1 << 16;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Params
        {
            public int backend, model;
            public int inW, inH, outW, outH;
            public IntPtr color, depth, motion, output;
            public float jitterX, jitterY, mvScaleX, mvScaleY;
            public float sharpness, frameTimeMs, cameraNear, cameraFar, fovY;
            public int reset, hdr, depthInverted;
        }

        [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern void WU_SetModFolder(string dir);
        [DllImport(Dll)] private static extern int WU_Renderer();
        [DllImport(Dll)] private static extern int WU_Available();
        [DllImport(Dll)] private static extern IntPtr WU_Message();
        [DllImport(Dll)] private static extern IntPtr WU_EventFunc();

        private static bool s_loaded, s_asked;
        private static int s_ready;
        private static IntPtr s_event;
        private static string s_lastMessage = "";
        // three frames' worth, as the render thread can run a frame behind
        private static readonly IntPtr[] s_params = new IntPtr[3];
        private static int s_paramAt;
        private static readonly CommandBuffer s_cb = new CommandBuffer { name = "Wire's Upscalers" };

        internal static void Start(string modDir)
        {
            try
            {
                WU_SetModFolder(modDir);
                int renderer = WU_Renderer();
                if (renderer < 0) { Plugin.Warn("Unity didn't hand the native part its graphics device; restart Valheim once."); return; }
                s_event = WU_EventFunc();
                for (int i = 0; i < s_params.Length; i++) s_params[i] = Marshal.AllocHGlobal(Marshal.SizeOf<Params>());
                s_loaded = true;
                // asked right away, so the settings menu knows by the time it opens
                s_asked = true;
                Issue(EventDetect, IntPtr.Zero);
            }
            catch (Exception e) { Plugin.Warn("Native part unavailable: " + e.Message); }
        }

        // What this PC can run; asks the render thread once, ready a frame later.
        internal static int Available
        {
            get
            {
                if (!s_loaded) return 0;
                if (s_ready != 0) return s_ready;
                int a = WU_Available();
                if ((a & Ready) == 0 && !s_asked)
                {
                    s_asked = true;
                    Issue(EventDetect, IntPtr.Zero);
                }
                if ((a & Ready) != 0) { s_ready = a; LogMessage(); }
                return a;
            }
        }

        internal static bool Has(Upscaler u)
        {
            int a = Available;
            switch (u)
            {
                case Upscaler.DLSS: return (a & AvailDLSS) != 0;
                case Upscaler.FSR3: return (a & AvailFSR3) != 0;
                case Upscaler.FSR4: return (a & AvailFSR4) != 0;
                case Upscaler.XeSS: return (a & AvailXeSS) != 0;
                default: return true;
            }
        }

        internal static void Evaluate(ref Params p)
        {
            if (!s_loaded) return;
            s_paramAt = (s_paramAt + 1) % s_params.Length;
            Marshal.StructureToPtr(p, s_params[s_paramAt], false);
            Issue(EventEvaluate, s_params[s_paramAt]);
        }

        internal static void Release() { if (s_loaded) Issue(EventRelease, IntPtr.Zero); }

        private static void Issue(int id, IntPtr data)
        {
            s_cb.Clear();
            s_cb.IssuePluginEventAndData(s_event, id, data);
            UnityEngine.Graphics.ExecuteCommandBuffer(s_cb);
        }

        // What the native side last reported, once each time it changes.
        internal static void LogMessage()
        {
            if (!s_loaded) return;
            string m = Marshal.PtrToStringAnsi(WU_Message());
            if (string.IsNullOrEmpty(m) || m == s_lastMessage) return;
            s_lastMessage = m;
            Plugin.Log(m);
        }
    }
}
