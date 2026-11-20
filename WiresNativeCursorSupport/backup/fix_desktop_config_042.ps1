$r='C:\Users\kajet\WireMods\WiresNativeCursorSupport'
Copy-Item "$r\backup\NATIVE_OS_CURSOR_20261120\DesktopCursorSize.cs" "$r\src\DesktopCursorSize.cs" -Force
$f="$r\src\WindowsNativeCursor.cs"
$s=[IO.File]::ReadAllText($f)
$old='            string raw = Environment.GetEnvironmentVariable("XCURSOR_SIZE");'+[Environment]::NewLine+'            if (int.TryParse(raw, out int size) && size >= 16 && size <= 256)'+[Environment]::NewLine+'                return size;'+[Environment]::NewLine+'            return 0;'
if (!$s.Contains($old)) {throw 'Windows size anchor missing'}
$s=$s.Replace($old,'            return DesktopCursorSize.Preferred();')
[IO.File]::WriteAllText($f,$s)
$f="$r\src\LinuxSystemCursor.cs"
$s=[IO.File]::ReadAllText($f)
$old='            string raw = Environment.GetEnvironmentVariable("XCURSOR_SIZE");'+[Environment]::NewLine+'            if (int.TryParse(raw, out int explicitSize) && explicitSize >= 16 && explicitSize <= 256)'+[Environment]::NewLine+'                return explicitSize; // Wayland/XWayland commonly sets this even if X11 reports 24.'
if (!$s.Contains($old)) {throw 'Linux size anchor missing'}
$s=$s.Replace($old,'            int explicitSize = DesktopCursorSize.Preferred();'+[Environment]::NewLine+'            if (explicitSize > 0) return explicitSize; // Wayland/XWayland may report an X11 fallback size.')
[IO.File]::WriteAllText($f,$s)
$f="$r\package\CHANGELOG.md"
$s=[IO.File]::ReadAllText($f).Replace('desktop XCURSOR_SIZE under Proton/Wine','desktop XCURSOR_SIZE and KDE/GTK cursor size under Proton/Wine')
[IO.File]::WriteAllText($f,$s)
dotnet build "$r\src\WiresNativeCursorSupport.csproj" -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'Build failed'}
