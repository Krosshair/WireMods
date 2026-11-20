$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresNativeCursorSupport'
$bk=Join-Path $r 'backup\CURSOR_DESKTOP_SIZE_20261120'
if(Test-Path $bk){throw 'Backup already exists'}
New-Item -ItemType Directory -Force $bk|Out-Null
foreach($name in @('SystemCursor.cs','LinuxNativeCursorBackend.cs','Plugin.cs')){
 Copy-Item (Join-Path $r "src\$name") (Join-Path $bk $name)
}
foreach($name in @('manifest.json','CHANGELOG.md','README.md')){
 Copy-Item (Join-Path $r "package\$name") (Join-Path $bk $name)
}
$install='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresNativeCursorSupport'
Copy-Item "$install\WiresNativeCursorSupport.dll" "$bk\WiresNativeCursorSupport.before.dll"
$nl=[string][char]10
$f=Join-Path $r 'src\DesktopCursorSize.cs'
$s=[IO.File]::ReadAllText($f)
$s=$s.Replace('int target, ref Vector2 hotspot)','int target, ref Vector2 hotspot, int nominal = 0)')
$s=$s.Replace('int natural = Math.Max(input.width, input.height);','int natural = nominal > 0 ? nominal : Math.Max(input.width, input.height);')
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))
$f=Join-Path $r 'src\SystemCursor.cs'
$s=[IO.File]::ReadAllText($f).Replace(([string][char]13+[char]10),$nl)
$old='            if (!s_read)'+$nl+'            {'+$nl+'                s_read = true;'
$new='            if (!s_read)'+$nl+'            {'+$nl+'                s_read = true;'+$nl+'                int desktopSize = DesktopCursorSize.Preferred();'
if(!$s.Contains($old)){throw 'SystemCursor initializer missing'}
$s=$s.Replace($old,$new)
$old='                        s_pictures[(int)k].Texture = WindowsCursor(WindowsIds[(int)k], out s_pictures[(int)k].Hotspot);'
$new='                    {'+$nl+
'                        int index = (int)k;'+$nl+
'                        s_pictures[index].Texture = WindowsCursor(WindowsIds[index], out s_pictures[index].Hotspot);'+$nl+
'                        if (desktopSize > 0)'+$nl+
'                            s_pictures[index].Texture = DesktopCursorSize.Resize('+ $nl+
'                                s_pictures[index].Texture, desktopSize, ref s_pictures[index].Hotspot);'+$nl+
'                    }'
if(!$s.Contains($old)){throw 'Windows cursor loading missing'}
$s=$s.Replace($old,$new)
$old='                else if (Application.platform == RuntimePlatform.LinuxPlayer)'+$nl+
'                    s_linux = LinuxNativeCursorBackend.Initialize();'
$new=$old+$nl+
'                if (desktopSize > 0)'+$nl+
'                    Plugin.Log?.LogInfo("Using desktop cursor size " + desktopSize +'+$nl+
'                        " px (including Proton/Wine when applicable).");'
if(!$s.Contains($old)){throw 'SystemCursor backend select missing'}
$s=$s.Replace($old,$new)
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))
$f=Join-Path $r 'src\LinuxNativeCursorBackend.cs'
$s=[IO.File]::ReadAllText($f).Replace(([string][char]13+[char]10),$nl)
$old='            if (_size <= 0)'+$nl+'                _size = 24;'
$new='            // Desktop configuration takes precedence over X11 defaults:'+ $nl+
'            // Proton/XWayland may report a fallback 24px size despite a larger'+$nl+
'            // user cursor. Resolve once, never poll each frame.'+$nl+
'            int preferred = DesktopCursorSize.Preferred();'+$nl+
'            if (preferred > 0) _size = preferred;'+$nl+
'            if (_size <= 0) _size = 24;'
if(!$s.Contains($old)){throw 'Linux final size missing'}
$s=$s.Replace($old,$new)
$old='                    CursorFrame frame ='+$nl+
'                        new CursorFrame'+$nl+
'                        {'+$nl+
'                            Texture = texture,'+$nl+
'                            Hotspot ='+$nl+
'                                new Vector2('+$nl+
'                                    image.xhot,'+$nl+
'                                    image.yhot),'
$new='                    Vector2 hot = new Vector2(image.xhot, image.yhot);'+$nl+
'                    texture = DesktopCursorSize.Resize(texture, _size, ref hot,'+$nl+
'                        (int)image.size);'+$nl+
'                    CursorFrame frame ='+$nl+
'                        new CursorFrame'+$nl+
'                        {'+$nl+
'                            Texture = texture,'+$nl+
'                            Hotspot = hot,'
if(!$s.Contains($old)){throw 'Linux CursorFrame missing'}
$s=$s.Replace($old,$new)
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))
$f=Join-Path $r 'src\Plugin.cs'
$s=[IO.File]::ReadAllText($f)
if(!$s.Contains('public const string Version = "0.4.0";')){throw 'Expected 0.4.0'}
[IO.File]::WriteAllText($f,$s.Replace('public const string Version = "0.4.0";','public const string Version = "0.4.1";'),[Text.UTF8Encoding]::new($false))
dotnet build (Join-Path $r 'src\WiresNativeCursorSupport.csproj') -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'Build failed'}
