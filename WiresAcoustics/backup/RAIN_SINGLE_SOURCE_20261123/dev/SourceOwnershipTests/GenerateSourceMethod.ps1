$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$path=Join-Path $r 'src\Spots.cs'
$s=[IO.File]::ReadAllText($path)
$start=$s.IndexOf('private static Transform PieceAt(Vector3 p)',[StringComparison]::Ordinal)
if($start -lt 0){throw 'Real Spots.PieceAt method not found'}
$open=$s.IndexOf('{',$start)
$level=0;$end=-1
for($i=$open;$i -lt $s.Length;$i++){
 if($s[$i] -eq '{'){$level++}
 elseif($s[$i] -eq '}'){$level--;if($level -eq 0){$end=$i;break}}
}
if($end -le $open){throw 'Malformed Spots.PieceAt method body'}
$method=$s.Substring($start,$end-$start+1)
if(!$method.Contains('if (found != null && found != root) return null;') -or
   !$method.Contains('if (n == RootNear.Length) return null;')){
 throw 'Ownership logic no longer matches review'
}
$generated=@'
using UnityEngine;
namespace WiresAcoustics {
internal static class Spots {
 private static readonly Collider[] RootNear=new Collider[64];
 private static int Mask=>1;
'@+[Environment]::NewLine+$method+[Environment]::NewLine+@'
 internal static Transform Choose(Vector3 at)=>PieceAt(at);
}}
'@
$dst=Join-Path $r 'dev\SourceOwnershipTests\ProductionMethods.cs'
[IO.File]::WriteAllText($dst,$generated,[Text.UTF8Encoding]::new($false))
"ACTUAL_SOURCE_METHOD_EXTRACTED=$dst"
