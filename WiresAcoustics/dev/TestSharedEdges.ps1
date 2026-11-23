$ErrorActionPreference='Stop'
foreach($m in @('UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.AudioModule.dll','assembly_valheim.dll')) { [void][Reflection.Assembly]::LoadFrom(('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\' + $m)) }; foreach($m in @('0Harmony.dll','BepInEx.dll')) { [void][Reflection.Assembly]::LoadFrom(('C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\core\' + $m)) }
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$vtype=[UnityEngine.Vector3]
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$member=[Reflection.BindingFlags]'NonPublic,Public,Instance'
$shared=$asm.GetType('WiresAcoustics.SharedEdges',$true)
$corners=$asm.GetType('WiresAcoustics.Corners+Record',$true)
$occ=$asm.GetType('WiresAcoustics.Occlusion',$true)
$clear=$shared.GetMethod('Clear',$flags)
$publish=$shared.GetMethod('Publish',$flags)
$copy=$shared.GetMethod('TryCopy',$flags)
function V([float]$x,[float]$y,[float]$z){return [UnityEngine.Vector3]::new($x,$y,$z)}
function SetField($o,[string]$name,$value){$o.GetType().GetField($name,$member).SetValue($o,$value)}
function GetField($o,[string]$name){return $o.GetType().GetField($name,$member).GetValue($o)}
function Rec {return [Activator]::CreateInstance($corners,$true)}
function Assert([bool]$ok,[string]$name){if(!$ok){throw ('FAIL '+$name)}}
[void]$clear.Invoke($null,@())
$source=Rec
SetField $source 'Valid' $true
SetField $source 'Pending' 0
SetField $source 'At' (V 3 0 0)
SetField $source 'N' (V 1 0 0)
SetField $source 'Span' ([single]4)
try { SetField $source 'Gen' ($occ.GetField('WorldGen',$flags).GetValue($null)) } catch { Write-Output ($_.Exception.ToString()); throw }
$has=GetField $source 'Has'
$has[0]=$true
$edges=GetField $source 'Edges'
$edges[0]=V 3 0 2
[void]$publish.Invoke($null,@($source))
$target=Rec
$good=[bool]$copy.Invoke($null,[object[]]@($target,(V 3.04 0 0),(V 1 0 0),[single]3))
Assert $good 'nearby wall cache hit'
Assert ((GetField $target 'Has')[0] -eq $true) 'edge flag copied'
Assert ((GetField $target 'Edges')[0].z -eq 2) 'edge geometry copied'
(GetField $target 'Has')[0]=$false
$target2=Rec
Assert ([bool]$copy.Invoke($null,[object[]]@($target2,(V 3.01 0 0),(V 1 0 0),[single]3))) 'immutable published snapshot'
Assert ((GetField $target2 'Has')[0]) 'copy must not share destination array'
Assert (-not [bool]$copy.Invoke($null,[object[]]@((Rec),(V 5 0 0),(V 1 0 0),[single]3))) 'reject spatially different wall'
Assert (-not [bool]$copy.Invoke($null,[object[]]@((Rec),(V 3 0 0),(V -1 0 0),[single]3))) 'reject opposite normal'
Assert (-not [bool]$copy.Invoke($null,[object[]]@((Rec),(V 3 0 0),(V 1 0 0),[single]5))) 'reject insufficient probe span'
[void]$clear.Invoke($null,@())
Assert (-not [bool]$copy.Invoke($null,[object[]]@((Rec),(V 3 0 0),(V 1 0 0),[single]3))) 'clear must invalidate cache'
'SHARED EDGE CACHE: PASS'


