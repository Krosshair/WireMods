$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$p=Join-Path $r 'src'
$hearing=[IO.File]::ReadAllText("$p\Hearing.cs")
if(!$hearing.Contains('At = listener.position;')){throw 'Acoustic listener origin is not the configured listener'}
if($hearing.Contains('internal static Vector3 Eye;')){throw 'Unused second acoustic origin retained'}
foreach($file in Get-ChildItem $p -Filter '*.cs') {
 if($file.Name -eq 'Hearing.cs') {continue}
 $s=[IO.File]::ReadAllText($file.FullName)
 if($s.Contains('Hearing.Eye')) {throw "$($file.Name) still uses the old independent camera ray origin"}
}
foreach($case in @(
 @('Batch.cs','Vector3 listener = Hearing.At;'),
 @('Batch.cs','f.CheckedFrom = Hearing.At;'),
 @('Room.cs','Batch.Step(Hearing.At)'),
 @('Starts.cs','Eye = Hearing.At'),
 @('Directions.cs','ContinuousEdges.Evaluate(f, Hearing.At'),
 @('Directions.cs','Occlusion.WatchLoop(f, physicalSource, Hearing.At'),
 @('Occlusion.cs','FacingPoint(f, Hearing.At)'),
 @('AcousticTrace.cs','.Append(" listenerPos=")')
)) {
 $source=[IO.File]::ReadAllText((Join-Path $p $case[0]))
 if(!$source.Contains($case[1])){throw "$($case[0]) is missing $($case[1])"}
}
$directions=[IO.File]::ReadAllText("$p\Directions.cs")
if(!$directions.Contains('Directions.Step(Hearing.Camera)') -and
   !([IO.File]::ReadAllText("$p\Room.cs")).Contains('Directions.Step(Hearing.Camera)')){
 throw 'Third-person camera orientation no longer used for spatialization'
}
$volume=[IO.File]::ReadAllText("$p\SourceVolume.cs")
foreach($s in @('p.Radius','Plugin.SourceSphereRadius','Mathf.Pow(Halton(i, 5), 1f / 3f)','p.Center + p.Right')) {
 if(!$volume.Contains($s)){throw "Spherical volumetric sampling missing: $s"}
}
$plugin=[IO.File]::ReadAllText("$p\Plugin.cs")
if(!$plugin.Contains('Unshaped source sphere radius')){throw 'Volume radius authoring control missing'}
'SOURCE STANDARD INTEGRATION PASS: physical listener for geometry/distance, camera orientation for spatialization, sphere-volume sampling and authored radius'
