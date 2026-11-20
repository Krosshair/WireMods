$ErrorActionPreference = 'Stop'
$root = 'C:\Users\kajet\WireMods\WiresNativeCursorSupport'
$src = Join-Path $root 'src'
$backup = Join-Path $root 'backup\PROTON_SCALING_FIX_20261120'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($file in @('WindowsNativeCursor.cs','LinuxSystemCursor.cs','Plugin.cs')) { Copy-Item (Join-Path $src $file) (Join-Path $backup $file) -Force }
foreach ($file in @('manifest.json','CHANGELOG.md')) { Copy-Item (Join-Path $root "package\$file") (Join-Path $backup $file) -Force }
function Edit($file,$old,$new) {
 $text = [IO.File]::ReadAllText($file)
 if (!$text.Contains($old)) { throw "Missing edit anchor: $file / $old" }
 [IO.File]::WriteAllText($file,$text.Replace($old,$new),[Text.UTF8Encoding]::new($false))
}
$w=Join-Path $src 'WindowsNativeCursor.cs'
Edit $w '        private const int GWLP_WNDPROC = -4;' @'
        private const int GWLP_WNDPROC = -4;
        private const uint IMAGE_CURSOR = 2;
        private const uint LR_COPYFROMRESOURCE = 0x4000;

        // Wine/Proton does not necessarily scale Windows stock cursors to the
        // host desktop's Xcursor size. Read the host hint only at startup.
        // Native Windows with no XCURSOR_SIZE retains unmodified OS handles.
        private static int ProtonCursorSize()
        {
            string raw = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
            if (int.TryParse(raw, out int size) && size >= 16 && size <= 256)
                return size;
            return 0;
        }
'@
Edit $w '        private static IntPtr[] s_handles;' @'
        private static IntPtr[] s_handles;
        private static bool[] s_owned;
'@
Edit $w '            var handles = new IntPtr[Ids.Length];' @'
            var handles = new IntPtr[Ids.Length];
            var owned = new bool[Ids.Length];
            int desktopSize = ProtonCursorSize();
'@
Edit $w '                if (handles[i] == IntPtr.Zero) handles[i] = LoadCursor(IntPtr.Zero, new IntPtr(Ids[0]));' @'
                if (handles[i] == IntPtr.Zero) handles[i] = LoadCursor(IntPtr.Zero, new IntPtr(Ids[0]));
                if (desktopSize > 0 && handles[i] != IntPtr.Zero)
                {
                    // CopyImage allocates a real HCURSOR at the requested size.
                    // Never resize Windows' normal accessibility-selected cursors.
                    IntPtr resized = CopyImage(handles[i], IMAGE_CURSOR,
                        desktopSize, desktopSize, LR_COPYFROMRESOURCE);
                    if (resized != IntPtr.Zero)
                    {
                        handles[i] = resized;
                        owned[i] = true;
                    }
                }
'@
Edit $w '            s_handles = handles;' @'
            s_handles = handles;
            s_owned = owned;
            if (desktopSize > 0)
                Plugin.Log?.LogInfo("Wine/Proton cursor requested size: " + desktopSize + "px (one-time native HCURSOR loading).");
'@
Edit $w '                    s_handles = null;' @'
                    if (s_handles != null && s_owned != null)
                        for (int i = 0; i < s_handles.Length; ++i)
                            if (s_owned[i] && s_handles[i] != IntPtr.Zero)
                                DestroyCursor(s_handles[i]);
                    s_handles = null;
                    s_owned = null;
'@
Edit $w '        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr resource);' @'
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr resource);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CopyImage(IntPtr handle, uint type, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
'@
$l=Join-Path $src 'LinuxSystemCursor.cs'
Edit $l '        private static IntPtr s_window;' @'
        private static IntPtr s_window;

        private static int ResolveSize(IntPtr display)
        {
            int size = 0;
            try { size = XcursorGetDefaultSize(display); }
            catch (EntryPointNotFoundException) { }
            string raw = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
            if (int.TryParse(raw, out int explicitSize) && explicitSize >= 16 && explicitSize <= 256)
                return explicitSize; // Wayland/XWayland commonly sets this even if X11 reports 24.
            return size >= 16 && size <= 256 ? size : 24;
        }

        private static IntPtr LoadSizedCursor(IntPtr display, string name, string theme, int size)
        {
            IntPtr images = XcursorLibraryLoadImages(name, theme, size);
            if (images == IntPtr.Zero) return IntPtr.Zero;
            try { return XcursorImagesLoadCursor(display, images); }
            finally { XcursorImagesDestroy(images); }
        }
'@
Edit $l '                var cursors = new IntPtr[Names.Length];' @'
                int size = ResolveSize(display);
                string theme = Environment.GetEnvironmentVariable("XCURSOR_THEME");
                if (string.IsNullOrEmpty(theme))
                {
                    IntPtr nativeTheme = XcursorGetTheme(display);
                    if (nativeTheme != IntPtr.Zero) theme = Marshal.PtrToStringAnsi(nativeTheme);
                }
                var cursors = new IntPtr[Names.Length];
'@
Edit $l '                        cursors[k] = XcursorLibraryLoadCursor(display, name);' @'
                        cursors[k] = LoadSizedCursor(display, name, theme, size);
'@
Edit $l '                Plugin.Log?.LogInfo("Native X11 system cursor handles enabled.");' @'
                Plugin.Log?.LogInfo("Native X11 system cursor handles enabled at " + size + "px (theme=" + (theme ?? "default") + ").");
'@
Edit $l '        [DllImport(Xcursor)] private static extern IntPtr XcursorLibraryLoadCursor(IntPtr display, string name);' @'
        [DllImport(Xcursor)] private static extern int XcursorGetDefaultSize(IntPtr display);
        [DllImport(Xcursor)] private static extern IntPtr XcursorGetTheme(IntPtr display);
        [DllImport(Xcursor, CharSet = CharSet.Ansi)] private static extern IntPtr XcursorLibraryLoadImages(string name, string theme, int size);
        [DllImport(Xcursor)] private static extern IntPtr XcursorImagesLoadCursor(IntPtr display, IntPtr images);
        [DllImport(Xcursor)] private static extern void XcursorImagesDestroy(IntPtr images);
'@
Edit (Join-Path $src 'Plugin.cs') 'public const string Version = "0.4.1";' 'public const string Version = "0.4.2";'
$manifest=Join-Path $root 'package\manifest.json'
Edit $manifest '"version_number": "0.4.1"' '"version_number": "0.4.2"'
$changelog=Join-Path $root 'package\CHANGELOG.md'
$text=[IO.File]::ReadAllText($changelog)
$text=$text.Replace('# Changelog', "# Changelog`r`n`r`n## 0.4.2`r`n- Fix: preserve the desktop XCURSOR_SIZE under Proton/Wine when native cursor handles are used; all sizing is performed once at initialization.`r`n- Fix: native Linux/X11 cursors load the requested theme size explicitly rather than relying on the X11 fallback.`r`n- Windows stock/accessibility cursor handles remain unchanged without an explicit XCURSOR_SIZE.`r`n- No per-frame cursor polling was added.")
[IO.File]::WriteAllText($changelog,$text,[Text.UTF8Encoding]::new($false))
dotnet build (Join-Path $src 'WiresNativeCursorSupport.csproj') -c Release --no-restore -v:q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
