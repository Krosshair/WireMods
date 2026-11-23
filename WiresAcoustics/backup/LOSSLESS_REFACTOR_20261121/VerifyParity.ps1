param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$before="$root\backup\LOSSLESS_REFACTOR_20261121\src"
$after="$root\src"
$checks=0
function Check([bool]$ok,[string]$what){if(!$ok){throw "FAIL: $what"};$script:checks++;Write-Output "PASS: $what"}
function Source([string]$dir,[string]$name){[IO.File]::ReadAllText((Join-Path $dir $name))}
$oldFiles=Get-ChildItem $before -Filter '*.cs'
$newFiles=Get-ChildItem $after -Filter '*.cs'
Check ($oldFiles.Count -eq 35) 'source baseline contains original 35 C# files'
Check ($newFiles.Count -eq $oldFiles.Count+2) 'only RayJobs and AcousticMath added'
$modified=@('Ambience.cs','Batch.cs','Diffraction.cs','Filter.cs','Menu.cs','Occlusion.cs','Ocean.cs','Room.cs','Starts.cs','Underwater.cs','VoiceChat.cs')
$untouched=0
foreach($f in $oldFiles){
 if($modified -contains $f.Name){continue}
 Check ((Source $before $f.Name) -ceq (Source $after $f.Name)) ("untouched behavior: "+$f.Name)
 $untouched++
}
Check ((Source $before 'Plugin.cs') -ceq (Source $after 'Plugin.cs')) 'configuration and plugin startup exactly unchanged'
function Lines([string]$s,[string]$pattern){
 $ms=[regex]::Matches($s,$pattern)
 @($ms|ForEach-Object {$_.Value.Trim()})
}
$oldPatches=@();$newPatches=@()
foreach($f in $oldFiles){
 $oldPatches+=Lines (Source $before $f.Name) '(?m)^\s*\[HarmonyPatch[^\r\n]*'
 $newPatches+=Lines (Source $after $f.Name) '(?m)^\s*\[HarmonyPatch[^\r\n]*'
}
Check ($oldPatches.Count -eq $newPatches.Count -and (($oldPatches -join '||') -ceq ($newPatches -join '||'))) 'all Harmony patches and targets identical'
# Capture gameplay source playback and lifecycle calls, ignoring unrelated
# comment changes; these are the exact expressions in unchanged lifecycle paths.
$sourceCallPattern='(?m)^\s*(?!//).*?\.(?:Play|PlayOneShot|Stop|Pause|UnPause)\s*\([^\r\n]*'
$oldCalls=@();$newCalls=@()
foreach($f in $oldFiles){
 $oldCalls+=Lines (Source $before $f.Name) $sourceCallPattern
 $newCalls+=Lines (Source $after $f.Name) $sourceCallPattern
}
Check (($oldCalls -join '||') -ceq ($newCalls -join '||')) 'all audio playback and stop sites unchanged'
$oldFilter=Source $before 'Filter.cs';$newFilter=Source $after 'Filter.cs'
$mark='            // direction'
$origTail=$oldFilter.Substring($oldFilter.IndexOf($mark))
$newTail=$newFilter.Substring($newFilter.IndexOf($mark))
Check ($origTail -ceq $newTail) 'HRTF directional DSP and output loop byte-for-byte unchanged'
$start='            // Through pointers:'
$endOld='        private void Spatialize('
$endNew='        // One identical state transition'
$a=$oldFilter.Substring($oldFilter.IndexOf($start),$oldFilter.IndexOf($endOld)-$oldFilter.IndexOf($start)).Trim()
$b=$newFilter.Substring($newFilter.IndexOf($start),$newFilter.IndexOf($endNew)-$newFilter.IndexOf($start)).Trim()
Check ($a -ceq $b) 'plain audio sample-processing loop byte-for-byte unchanged'
$shared=Source $after 'RayJobs.cs'
Check ($shared.Contains('NextPowerOfTwo(Mathf.Max(64, required))') -and $shared.Contains('cap * hitsPerRay')) 'batch allocation sizes unchanged'
Check ($shared.Contains('Batch.Cast(from, to, query)') -and $shared.Contains('Batch.Cast(from + side, to + side, query)') -and $shared.Contains('Batch.Cast(from + up, to + up, query)')) 'same three raycast origins in original order'
Check ($shared.Contains('if (hit.colliderInstanceID == 0) break;')) 'same first-empty-hit guard'
Check ($shared.Contains('JobHandle.ScheduleBatchedJobs();')) 'worker-job flush retained'
$batch=Source $after 'Batch.cs';$starts=Source $after 'Starts.cs';$room=Source $after 'Room.cs';$ocean=Source $after 'Ocean.cs'
Check ($batch.Contains('s_hits.GetSubArray(0, c * MaxHits), 8, MaxHits)') -and $starts.Contains('s_hits.GetSubArray(0, c * MaxHits), 4, MaxHits)') -and $room.Contains('RayJobs.Submit(s_cmds, s_hits, 16, 1)') -and $ocean.Contains('RayJobs.Submit(s_cmds, s_hits, 16, 1)')) 'original batched-ray worker sizes unchanged'
$math=Source $after 'AcousticMath.cs'
Check ($math.Contains('Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), amount))')) 'original logarithmic-frequency math retained'
Check ($math.Contains('Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f)')) 'original occlusion configuration clamp retained'
$under=Source $after 'Underwater.cs'
Check ($under.Contains('gain *= Gain;') -and $under.Contains('cutoff = Mathf.Min(cutoff, Cutoff);')) 'original air-water transmission math retained'
Check ($batch.Contains('Underwater.Apply(ref gain, ref cutoff)') -and (Source $after 'Occlusion.cs').Contains('Underwater.Apply(ref gain, ref cutoff)')) 'both original water transmission consumers unified'
Check ($newFilter.Contains('SmoothOcclusion(frames, tg, tc') -and $newFilter.Contains('m_gain = g1;')) 'same DSP smoothing shared between plain/HRTF'
Check ((Source $after 'Plugin.cs').Contains('Version = "0.6.0"')) 'plugin release version unchanged'
if(!$NoBuild){
 dotnet build "$root\src\WiresAcoustics.csproj" -c Release --no-restore -v:q
 if($LASTEXITCODE -ne 0){throw 'FAIL: Release build'}
}
Write-Output "AUDIT COMPLETE: $checks checks passed; compile succeeds. Runtime/audio signal equivalence remains unmeasured."
