$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$t=$asm.GetType('WiresAcoustics.ContinuousEdges',$true)
$f=$t.GetMethod('Transfer',[Reflection.BindingFlags]'NonPublic,Static')
function TestTransfer([float]$clearance,[float]$detour,[float]$wall,[float]$cut) {
    [object[]]$args=@($clearance,$detour,$wall,$cut,[single]0,[single]0,[single]0)
    [void]$f.Invoke($null,$args)
    return [PSCustomObject]@{Clearance=$clearance;Detour=$detour;Gain=[float]$args[4];Cutoff=[float]$args[5];Direction=[float]$args[6]}
}
$bad=0
foreach($wall in @(0.05,0.2,0.5,0.9)){
    $left=TestTransfer -clearance -0.00001 -detour 0.00000001 -wall $wall -cut 1400
    $right=TestTransfer -clearance 0.00001 -detour 0.00000001 -wall $wall -cut 1400
    $diff=[Math]::Abs($left.Gain-$right.Gain)
    'edge wall='+$wall+' left='+$left.Gain+' right='+$right.Gain+' difference='+$diff
    if($diff -gt 0.002){$bad++}
}
foreach($side in @(-1,1)){
    foreach($wall in @(0.05,0.2,0.5,0.9)){
        foreach($m in @(0.0,0.0001,0.001,0.01,0.05,0.1,0.2,0.4,0.8,1.5,5.0)){
            $r=TestTransfer -clearance ([float]($side * $m)) -detour ([float]($m * $m * 0.02)) -wall $wall -cut 1400
            if([double]::IsNaN($r.Gain) -or [double]::IsInfinity($r.Gain) -or $r.Gain -lt 0 -or $r.Gain -gt 1 -or $r.Cutoff -lt 200 -or $r.Cutoff -gt 22000 -or $r.Direction -lt 0 -or $r.Direction -gt 1){$bad++; 'FAIL: '+($r|Out-String)}
        }
    }
}
'NUMERIC CHECKS: '+$(if($bad -eq 0){'PASS'}else{'FAIL '+$bad})
if($bad -ne 0){exit 1}
