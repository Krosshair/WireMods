$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$directions=[IO.File]::ReadAllText((Join-Path $r 'src\Directions.cs'))
$ambient=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$model=[IO.File]::ReadAllText((Join-Path $r 'src\WindPropagation.cs'))
$stub=@'
namespace UnityEngine {
 public static class Mathf {
  public static float Abs(float x) {return System.Math.Abs(x);}
  public static float Clamp01(float x) {return System.Math.Max(0f,System.Math.Min(1f,x));}
  public static float Lerp(float a,float b,float t) {return a+(b-a)*Clamp01(t);}
 }
}
'@
$tests=@'
namespace WiresAcoustics {
 public static class WindValidation {
  static void Near(float value,float expected,string name) {
   if(System.Math.Abs(value-expected)>.001f)
    throw new System.Exception(name+": "+value+" expected "+expected);
  }
  public static string Run() {
   // All four measured FHWA ground/height endpoints at 122 m.
   Near(WindPropagation.Correction(-5f,122f,1f,0f,1.5f),-11f,"hard-low upwind strong");
   Near(WindPropagation.Correction(5f,122f,1f,0f,1.5f),1f,"hard-low downwind strong");
   Near(WindPropagation.Correction(-5f,122f,1f,1f,1.5f),-9f,"soft-low upwind strong");
   Near(WindPropagation.Correction(5f,122f,1f,1f,1.5f),14f,"soft-low downwind strong");
   Near(WindPropagation.Correction(-5f,122f,1f,0f,4.6f),-4f,"hard-high upwind strong");
   Near(WindPropagation.Correction(5f,122f,1f,0f,4.6f),1f,"hard-high downwind strong");
   Near(WindPropagation.Correction(-5f,122f,1f,1f,4.6f),-8f,"soft-high upwind strong");
   Near(WindPropagation.Correction(5f,122f,1f,1f,4.6f),10f,"soft-high downwind strong");
   // Moderate wind 2.5m/s and intermediate interpolation.
   Near(WindPropagation.Correction(-2.5f,122f,1f,0f,1.5f),-7f,"hard moderate upwind");
   Near(WindPropagation.Correction(2.5f,122f,1f,1f,1.5f),13f,"soft moderate downwind");
   Near(WindPropagation.Correction(-3.75f,122f,1f,0f,1.5f),-9f,"wind-speed interpolation");
   Near(WindPropagation.Correction(-5f,122f,1f,.5f,3.05f),-8f,"ground+height interpolation");
   // Midpoint distance between 61m and 122m; low wind moderate hard up.
   Near(WindPropagation.Correction(-2.5f,91.5f,1f,0f,1.5f),-4.5f,"distance interpolation");
   Near(WindPropagation.Correction(-5f,122f,.5f,0f,1.5f),-5.5f,"partially sheltered");
   Near(WindPropagation.Correction(5f,122f,0f,1f,1.5f),0f,"fully sheltered");
   Near(WindPropagation.Correction(-5f,122f,0f,0f,1.5f),0f,"fully sheltered upwind");
   Near(WindPropagation.Correction(-5f,0f,1f,1f,1.5f),0f,"same-position emitter");
   Near(WindPropagation.Correction(0f,122f,1f,1f,1.5f),0f,"no wind");
   Near(WindPropagation.Correction(-5f,488f,1f,0f,1.5f),-25f,"far strongest measured hard");
   Near(WindPropagation.Correction(-5f,5000f,1f,0f,1.5f),-25f,"no extrapolation beyond measured range");
   Near(WindPropagation.Correction(50f,122f,1f,1f,1.5f),14f,"no extrapolation beyond measured wind");
   Near(WindPropagation.Correction(-1.25f,122f,1f,0f,1.5f),-3.5f,"wind below moderate interpolated from calm");
   Near(WindPropagation.Correction(5f,122f,2f,0f,1.5f),1f,"shelter clamped high");
   Near(WindPropagation.Correction(5f,122f,-1f,0f,1.5f),0f,"shelter clamped low");
   Near(WindPropagation.Correction(5f,122f,1f,1f,100f),10f,"height clamped high");
   return "EMPIRICAL WIND FHWA TABLE TEST PASS: 25 numeric cases, hard/soft ground, receiver height, speed/distance and shelter";
  }
 }
}
'@
$source='using UnityEngine;'+[Environment]::NewLine+$stub+[Environment]::NewLine+$model.Replace('using UnityEngine;', '')+[Environment]::NewLine+$tests
Add-Type -TypeDefinition $source -ErrorAction Stop
[WiresAcoustics.WindValidation]::Run()
function Require([bool]$ok,[string]$reason){if(!$ok){throw "FHWA WIND INTEGRATION: $reason"}}
Require ($directions.Contains('WindPropagation.Correction(speed, distance,') -and
         $directions.Contains('Room.GroundSoftness, Room.GroundReceiverHeight,') -and
         $directions.Contains('f.PastRange(er.w) * Mathf.Pow(10f, carryDb / 20f)')) 'model not wired to true world source rolloff'
Require ($directions.Contains('if (slow && f.Placed)')) 'wind not limited to existing scheduled 4 Hz positional sounds'
Require ($ambient.Contains('WindExposure => Room.WindSurveyReady ?')) 'existing shelter data no longer shared'
Require ($room.Contains('internal static float GroundSoftness = .5f;') -and
         $room.Contains('internal static float GroundReceiverHeight = 1.5f;')) 'default receiver properties missing'
Require ($room.Contains('(hitSurface.Ground || hitSurface.Hm != null)') -and
         $room.Contains('GroundSoftness = Mathf.Lerp(GroundSoftness,') -and
         $room.Contains('GroundReceiverHeight = Mathf.Lerp(GroundReceiverHeight,')) 'old room probes not used to sample ground'
Require (!$model.Contains('Raycast') -or $model.Contains('No new raycasts')) 'new model should not raycast'
Write-Output 'EMPIRICAL WIND INTEGRATION PASS: reused room probes and source distance with no extra physics'
