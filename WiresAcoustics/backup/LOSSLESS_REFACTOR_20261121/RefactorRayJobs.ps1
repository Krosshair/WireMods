$ErrorActionPreference='Stop'
$dir='C:\Users\kajet\WireMods\WiresAcoustics\src'
function Take([string]$name,[string]$pattern,[string]$replacement){
 $p=Join-Path $dir $name
 $s=[IO.File]::ReadAllText($p)
 $rx=[regex]::new($pattern)
 $n=$rx.Matches($s).Count
 if($n -ne 1){throw "Expected exactly one pattern in $name : got $n"}
 $s=$rx.Replace($s,[System.Text.RegularExpressions.MatchEvaluator]{param($m) $replacement},1)
 [IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
 Write-Output "OK $name"
}
foreach($n in @('Batch.cs','Starts.cs')){
 Take $n '(?s)            if \(!s_cmds\.IsCreated \|\| s_cmds\.Length < cmds\)\s*\{\s*if \(s_cmds\.IsCreated\) \{ s_cmds\.Dispose\(\); s_hits\.Dispose\(\); \}\s*int cap = Mathf\.NextPowerOfTwo\(Mathf\.Max\(64, cmds\)\);\s*s_cmds = new NativeArray<RaycastCommand>\(cap, Allocator\.Persistent\);\s*s_hits = new NativeArray<RaycastHit>\(cap \* MaxHits, Allocator\.Persistent\);\s*\}' '            RayJobs.Ensure(ref s_cmds, ref s_hits, cmds, MaxHits);'
 Take $n '        private static void Cast3\(Vector3 from, Vector3 to, QueryParameters q, ref int c\)\s*\{\s*Occlusion\.Lines\(from, to, out Vector3 side, out Vector3 up\);\s*s_cmds\[c\+\+\] = (?:Batch\.)?Cast\(from, to, q\);\s*s_cmds\[c\+\+\] = (?:Batch\.)?Cast\(from \+ side, to \+ side, q\);\s*s_cmds\[c\+\+\] = (?:Batch\.)?Cast\(from \+ up, to \+ up, q\);\s*\}' '        private static void Cast3(Vector3 from, Vector3 to, QueryParameters q, ref int c) =>'+[Environment]::NewLine+'            RayJobs.Cast3(s_cmds, from, to, q, ref c);'
 Take $n '            s_job = RaycastCommand\.ScheduleBatch\(s_cmds\.GetSubArray\(0, c\), s_hits\.GetSubArray\(0, c \* MaxHits\), (8|4), MaxHits\);\s*JobHandle\.ScheduleBatchedJobs\(\);' $(if($n -eq 'Batch.cs'){'            s_job = RayJobs.Submit(s_cmds.GetSubArray(0, c),'+[Environment]::NewLine+'                s_hits.GetSubArray(0, c * MaxHits), 8, MaxHits);'}else{'            s_job = RayJobs.Submit(s_cmds.GetSubArray(0, c),'+[Environment]::NewLine+'                s_hits.GetSubArray(0, c * MaxHits), 4, MaxHits);'})
 Take $n '            if \(s_cmds\.IsCreated\) \{ s_cmds\.Dispose\(\); s_hits\.Dispose\(\); \}' '            RayJobs.Dispose(ref s_cmds, ref s_hits);'
}
Take 'Room.cs' '                s_job = RaycastCommand\.ScheduleBatch\(s_cmds, s_hits, 16, 1\);\s*JobHandle\.ScheduleBatchedJobs\(\);' '                s_job = RayJobs.Submit(s_cmds, s_hits, 16, 1);'
Take 'Ocean.cs' '            s_job = RaycastCommand\.ScheduleBatch\(s_cmds, s_hits, 16, 1\);\s*JobHandle\.ScheduleBatchedJobs\(\);' '            s_job = RayJobs.Submit(s_cmds, s_hits, 16, 1);'
Take 'Ocean.cs' '            if \(s_cmds\.IsCreated\) \{ s_cmds\.Dispose\(\); s_hits\.Dispose\(\); \}' '            RayJobs.Dispose(ref s_cmds, ref s_hits);'
Write-Output 'Shared ray batch dispatch and buffers refactored'
