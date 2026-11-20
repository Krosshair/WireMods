$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before restoring DLL'}
$b=Split-Path -Parent $MyInvocation.MyCommand.Path
$r=Split-Path -Parent (Split-Path -Parent $b)
$game=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresNativeCursorSupport'
foreach($pair in @(@((Join-Path $b 'WiresNativeCursorSupport.dll'),$game),@((Join-Path $b 'WiresNativeCursorSupport.package.dll'),(Join-Path $r 'package')))){
 $tmp=Join-Path $pair[1] 'WiresNativeCursorSupport.dll.new'
 Copy-Item $pair[0] $tmp -Force
 Move-Item $tmp (Join-Path $pair[1] 'WiresNativeCursorSupport.dll') -Force
}
Write-Output 'Restored previous Enngard and package DLLs.'
