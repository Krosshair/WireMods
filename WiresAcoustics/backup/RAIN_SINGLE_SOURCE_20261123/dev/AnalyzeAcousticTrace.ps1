param(
  [string]$LogPath = "$env:APPDATA\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\LogOutput.log",
  [string]$ReportPath = "$env:USERPROFILE\WireMods\WiresAcoustics\ACOUSTIC_TRACE_REPORT.txt"
)
$ErrorActionPreference = 'Stop'
$c = [System.Globalization.CultureInfo]::InvariantCulture
if (!(Test-Path $LogPath)) { throw "Log not found: $LogPath" }
function Num([string]$s) {
  if ([string]::IsNullOrWhiteSpace($s)) { return 0.0 }
  return [double]::Parse($s, $c)
}
function Field([string]$line,[string]$name) {
  $m = [regex]::Match($line, "(?:^|\s)" + [regex]::Escape($name) + '=([^\s]+)')
  return $(if ($m.Success) { $m.Groups[1].Value } else { '' })
}
$snaps = 0; $sources = 0; $geometry = 0; $changes = 0
$paths = @{}
$warnings = New-Object 'System.Collections.Generic.List[string]'
$highChanges = New-Object 'System.Collections.Generic.List[object]'
$maxOutput = 0.0
$maxSource = ''
$latestTimestamp = 0.0
$overwritten = 0
foreach ($line in [System.IO.File]::ReadLines($LogPath)) {
  if (!$line.Contains('[acoustics.trace')) { continue }
  if ($line.Contains('[acoustics.trace.geometry]')) { $geometry++; continue }
  if ($line.Contains('[acoustics.trace.transition]')) {
    $changes++
    $m = [regex]::Match($line, 'source=(.*?) pathAtUpdate=(.*?) world=(\d+) gain=([0-9.]+)->([0-9.]+) cut=([0-9.]+)->([0-9.]+)Hz')
    if ($m.Success) {
      $before = Num $m.Groups[4].Value
      $after = Num $m.Groups[5].Value
      $delta = 20.0 * [Math]::Log10([Math]::Max(0.000001,$after) / [Math]::Max(0.000001,$before))
      if ([Math]::Abs($delta) -ge 6.0) {
        $highChanges.Add([PSCustomObject]@{ Magnitude=[Math]::Abs($delta); Description=("{0}: {1:N1} dB, path {2}" -f $m.Groups[1].Value,$delta,$m.Groups[2].Value) })
      }
    }
    continue
  }
  if ($line -match '\[acoustics.trace\] snapshot=') {
    $snaps++
    $time = Field $line 'time'
    if ($time) { $latestTimestamp = Num $time }
    continue
  }
  if ($line -match '\[acoustics.trace\] #\d+ name=') {
    $sources++
    $nm = [regex]::Match($line, 'name=(.*?) kind=')
    $name = $(if ($nm.Success) { $nm.Groups[1].Value } else { '?' })
    $path = Field $line 'path'
    if ($path -eq '') { $path = '(missing)' }
    if (!$paths.ContainsKey($path)) { $paths[$path] = 0 }
    $paths[$path]++
    $actual = Field $line 'actualDSP'
    $target = Field $line 'targetDSP'
    $rms = Field $line 'outputRMS'
    $age = Field $line 'age'
    $virtual = Field $line 'virtual'
    $meterDropped = Field $line 'meterDropped'
    $world = Field $line 'world'
    if ($meterDropped -and [long]$meterDropped -gt 0) {
      $warnings.Add("meter diagnostic packets dropped: $meterDropped, source $name; audio unaffected")
    }
    if ($rms -ne '' -and (Num $rms) -gt $maxOutput) {
      $maxOutput = Num $rms
      $maxSource = $name
    }
    if ($target -ne '' -and $actual -ne '') {
      $t = Num $target
      $a = Num $actual
      if ($t -gt 0.05 -and [Math]::Abs($t-$a) / $t -gt 0.35) {
        $warnings.Add(("{0}: actual DSP gain {1:F3} differs >35% from target {2:F3}; may be smoothing" -f $name,$a,$t))
      }
    }
    if ($virtual -eq 'True' -and $rms -ne '' -and (Num $rms) -gt .015) {
      $warnings.Add("${name}: high output RMS while Unity source is virtual (pre-mixer RMS is not audible)")
    }
    if ($age -ne '' -and (Num ($age.TrimEnd('s'))) -gt 10.0) {
      $warnings.Add(("{0}: acoustic path measurement age {1}s (may be intentionally cached)" -f $name,$age))
    }
    if ($world -and $world.Contains('/')) {
      $g = $world.Split('/')
      if ($g.Length -eq 2 -and $g[0] -ne $g[1]) {
        $warnings.Add("${name}: path result from older world generation $world (check if geometry changed near source)")
      }
    }
    continue
  }
  if ($line.Contains('overwritten=')) {
    $s = Field $line 'overwritten'
    if ($s) { $overwritten += [int]$s }
  }
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("WIRE'S ACOUSTICS - OFFLINE TRACE SUMMARY")
[void]$sb.AppendLine("Source log: $LogPath")
[void]$sb.AppendLine("Counts: $snaps snapshots; $sources source entries; $changes significant gain/filter transitions; $geometry geometry events; $overwritten overwritten history entries")
[void]$sb.AppendLine("Last game time in trace: $latestTimestamp")
[void]$sb.AppendLine(("Strongest per-source post-DSP RMS among logged snapshots: {0:F5} ({1})" -f $maxOutput,$maxSource))
[void]$sb.AppendLine()
[void]$sb.AppendLine("PATH DISTRIBUTION (logged samples, not unique sounds):")
foreach($item in $paths.GetEnumerator() | Sort-Object -Property Value -Descending) {
  [void]$sb.AppendLine(("  {0,-24} {1,6}" -f $item.Key,$item.Value))
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("LARGEST RECORDED GAIN TRANSITIONS (>=6 dB):")
foreach($item in $highChanges | Sort-Object -Property Magnitude -Descending | Select-Object -First 20) {
  [void]$sb.AppendLine("  "+$item.Description)
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("POSSIBLE ANOMALIES (heuristics, not proof of an audible defect):")
foreach($w in $warnings | Select-Object -First 50) { [void]$sb.AppendLine("  "+$w) }
if($warnings.Count -gt 50){[void]$sb.AppendLine("  ... +"+($warnings.Count-50)+" more")}
if($warnings.Count -eq 0){[void]$sb.AppendLine("  None flagged")}
[void]$sb.AppendLine()
[void]$sb.AppendLine("LIMITATION: RMS measures per-source audio before final Unity mixing/output. Paths, attenuation, reverb and source fields are telemetry, NOT a waveform recording. This report cannot reproduce sound, spatial hearing, audio-device processing or audible crackles. Compare with a captured output audio signal if exact playback diagnosis is required.")
[System.IO.File]::WriteAllText($ReportPath,$sb.ToString(),[System.Text.UTF8Encoding]::new($false))
Write-Output $sb.ToString()
Write-Output "Saved: $ReportPath"
