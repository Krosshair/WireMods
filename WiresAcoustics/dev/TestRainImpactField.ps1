$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$f=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('FIELD ENERGY REGRESSION: '+$why)}}
Must ($f.Contains('internal static float AngularWeight(float directionY)') -and
      $f.Contains('directionY >= -.5f ? 1f : 0f;')) 'outdoor angular integration missing'
Must ($f.Contains('reference += AngularWeight(directions[i].y);') -and
      !$f.Contains('if (directions[i].y >= 0f)')) 'open reference must use same angular domain as received field'
Must ($f.Contains('float energy = w * gain * gain;') -and
      $f.Contains('high += w * SpectralPaths.HighEnergy(gain, cutoff);') -and
      $f.Contains('SpectralPaths.Decode(Normalize(low, reference),')) 'sound path energy and low/high spectral aggregation missing'
Must ($f.Contains('if (open)') -and $f.Contains('gain = 1f;') -and
      $f.Contains('cutoff = 22000f;') -and
      $f.Contains('Room.AcousticBoundary(i)')) 'open rays or blocked-wall fallback missing'
Must ($f.Contains('Occlusion.Wall(block, out gain, out cutoff);') -and
      $f.Contains('block.ThroughHigh = SpectralPaths.Response(material.Cut);')) 'shared SFX material transfer not used'
Must ($room.Contains('EnvironmentalField.Measure(Dirs, Picked, Hit);') -and
      !$room.Contains('EnvironmentalField.Step(') -and
      !$room.Contains('RainField.ScheduleGroundSight(')) 'field still schedules separate physics work'
if(Test-Path "$r\src\RainField.cs"){throw 'obsolete field still exists'}
& dotnet run --project "$r\dev\EnvironmentalFieldTests\EnvironmentalFieldTests.csproj" -c Release --no-restore
if($LASTEXITCODE -ne 0){throw 'source-linked incident field acoustic test failed'}
Write-Output 'FIELD ENERGY REGRESSION PASS: weighted open apertures and actual wall materials with no extra physics'
