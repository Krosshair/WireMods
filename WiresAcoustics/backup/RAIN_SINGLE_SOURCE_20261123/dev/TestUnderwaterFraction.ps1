$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$t=$asm.GetType('WiresAcoustics.Underwater',$true)
$apply=@($t.GetMethods([Reflection.BindingFlags]'NonPublic,Static') | Where-Object { $_.Name -eq 'Apply' -and $_.GetParameters().Length -eq 3 })[0]
$previous=2.0
foreach($fraction in @(0.0,0.1,0.25,0.5,0.75,0.9,1.0)) {
   [object[]]$args=@([single]1,[single]22000,[single]$fraction)
   [void]$apply.Invoke($null,$args)
   $gain=[double]$args[0]
   ('fraction={0:N2}, amplitude={1:N6}' -f $fraction,$gain)
   if($gain -gt $previous -or $gain -lt 0 -or $gain -gt 1) {throw 'Underwater gain discontinuity / out of range'}
   $previous=$gain
}
'UNDERWATER FRACTION TEST: PASS'
