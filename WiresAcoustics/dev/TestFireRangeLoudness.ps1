$ErrorActionPreference = 'Stop'
$src = [IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\Area.cs')
$match = [regex]::Match($src, 'private const float ReferenceFireRange = (?<reference>[^;]+);\s*private static float FireLevelFromRange\(float authoredRange\) =>\s*(?<expression>[^;]+);')
if (!$match.Success) { throw 'Could not extract production fire-range gain source' }
$reference = $match.Groups['reference'].Value
$expression = $match.Groups['expression'].Value
$testCode = @"
using System;
using UnityEngine;
namespace UnityEngine {
  public static class Mathf {public static float Min(float a,float b) {return Math.Min(a,b);} }
}
public static class ProductionFireRangeTest {
 private const float ReferenceFireRange = $reference;
 public static float Gain(float authoredRange) { return $expression; }
}
"@
Add-Type -TypeDefinition $testCode
foreach ($case in @(@(1.0,0.1),@(2.0,0.2),@(3.0,0.3),@(5.0,0.5),@(10.0,1.0),@(20.0,1.0),@(0.0,1.0),@(-1.0,1.0))) {
  $actual = [ProductionFireRangeTest]::Gain([single]$case[0])
  if ([Math]::Abs($actual - $case[1]) -gt 0.000001) {
    throw "Authored range $($case[0])m gave $actual, expected $($case[1])"
  }
}
$nan = [ProductionFireRangeTest]::Gain([single]::NaN)
if ($nan -ne 1.0) {throw "Invalid range did not preserve default loudness: $nan"}
$three = [ProductionFireRangeTest]::Gain([single]3)
$five = [ProductionFireRangeTest]::Gain([single]5)
Write-Output ("FIRE-RANGE MATH PASS: 3m {0:F2}x ({1:F2} dB), 5m {2:F2}x ({3:F2} dB), 10m 1x; nonpositive/NaN ranges neutral" -f $three,(20*[Math]::Log10($three)),$five,(20*[Math]::Log10($five)))
