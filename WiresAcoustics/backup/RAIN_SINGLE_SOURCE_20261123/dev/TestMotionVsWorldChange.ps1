$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics\src'
function File([string]$name){[IO.File]::ReadAllText((Join-Path $r $name))}
function Body([string]$text,[string]$start,[string]$end) {
  $a=$text.IndexOf($start,[StringComparison]::Ordinal)
  if($a -lt 0){throw "Function start missing: $start"}
  $b=$text.IndexOf($end,$a+[Math]::Max(1,$start.Length),[StringComparison]::Ordinal)
  if($b -lt 0){throw "Function end missing: $end"}
  $text.Substring($a,$b-$a)
}
$occ=File 'Occlusion.cs'
$anal=File 'AnalyticOcclusion.cs'
$edge=File 'ContinuousEdges.cs'
$batch=File 'Batch.cs'
$filter=File 'Filter.cs'
$watch=Body $occ 'internal static void WatchLoop(' 'internal static string Summary()'
$refresh=$anal.Substring($anal.IndexOf('internal static void Refresh('))
$cont=$edge.Substring($edge.IndexOf('internal static bool Evaluate('))
$changed=Body $batch 'internal static void GeometryChanged(' 'internal static void Enqueue('
$set=Body $filter 'internal void Set(' 'internal float TargetGain'
$smoothing=Body $filter 'private void SmoothOcclusion(' 'private void Spatialize('
function Require($ok,$msg){if(!$ok){throw "MOTION VS WORLD TEST FAILED: $msg"}}
Require (!$watch.Contains('f.FastGeometryResponse = true;')) 'Walking still forces a one-buffer transition'
Require (!$refresh.Contains('f.FastGeometryResponse=true;')) 'Analytic movement still forces a one-buffer transition'
Require (!$cont.Contains('f.FastGeometryResponse = true;')) 'Fresnel movement still forces a one-buffer transition'
Require ($changed.Contains('f.FastGeometryResponse = true;')) 'Building/door edits no longer respond promptly'
Require ($set.Contains('m_quickOcclusion = true;')) 'Fast response path accidentally removed'
Require ($smoothing.Contains('m_quickOcclusion ? 1f : Glide(frames)')) 'Normal glide is not preserved'
Require ($watch.Contains('Batch.Enqueue(f, p,')) 'Movement update is no longer propagated'
Require ($refresh.Contains('f.Set(gain,cutoff,false);')) 'Continuous movement no longer updates target'
'MOTION VS WORLD TRANSITION CONTRACT PASS: motion uses standard glide, world changes are prompt'
