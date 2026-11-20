$ErrorActionPreference='Stop'
$b=Split-Path -Parent $MyInvocation.MyCommand.Path
$r=Split-Path -Parent (Split-Path -Parent $b)
$src=Join-Path $r 'src'
foreach($file in @('SystemCursor.cs','Plugin.cs','MacCursors.cs','DesktopCursorSize.cs','LinuxNativeCursorBackend.cs','CursorEvents.cs')){
 $tmp=Join-Path $src ($file+'.new')
 Copy-Item (Join-Path $b $file) $tmp -Force
 Move-Item $tmp (Join-Path $src $file) -Force
}
$removed=Join-Path $r '_removed\NATIVE_CURSOR_20261120_ROLLBACK'
New-Item -ItemType Directory -Path $removed -Force|Out-Null
foreach($file in @('WindowsNativeCursor.cs','LinuxSystemCursor.cs')){
 $p=Join-Path $src $file
 if(Test-Path $p){Move-Item $p (Join-Path $removed $file) -Force}
}
Copy-Item (Join-Path $b 'README.md') (Join-Path $r 'package\README.md') -Force
Write-Output 'Restored pre-native source and README; experimental files moved to _removed.'
