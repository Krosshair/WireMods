$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics\src'
$a=Join-Path $r 'Area.cs'
$b=Join-Path $r 'Batch.cs'
$s=[IO.File]::ReadAllText($a)
$s=$s.Replace('            }            int id = owner.GetInstanceID();','            }'+[Environment]::NewLine+'            int id = owner.GetInstanceID();')
$start=$s.IndexOf('        // Conservative world-space facing surface of the cached source AABB.')
$end=$s.IndexOf('        // A piece', $start)
if($start -lt 0 -or $end -lt 0){throw 'Area sample boundaries absent'}
$section=@'
        // Surface sampling for extended sources: each of the six face centres
        // and eight corners. No playback/panning changes; only occlusion.
        // Existing worker ray batching handles the additional tests only
        // when a sound is (or has just become) obstructed.
        internal const int BoundarySamples = 14;
        internal static bool HasFace(AcousticFilter f) =>
            f != null && f.AreaKnown && f.ShapeRoot != null &&
            f.ShapeHalf.sqrMagnitude > .05f;

        internal static Vector3 FacePoint(AcousticFilter f, Vector3 listener, int index)
        {
            Vector3 center = f.ShapeRoot.TransformPoint(f.ShapeCenter);
            Vector3 half = f.ShapeHalf;
            // Stay just inside the source boundary so nearby collider
            // numerical tolerances do not manufacture a clear air path.
            float x = Mathf.Max(0f, half.x - .02f);
            float y = Mathf.Max(0f, half.y - .02f);
            float z = Mathf.Max(0f, half.z - .02f);
            switch (index)
            {
                case 0: return center + Vector3.right * x;
                case 1: return center - Vector3.right * x;
                case 2: return center + Vector3.up * y;
                case 3: return center - Vector3.up * y;
                case 4: return center + Vector3.forward * z;
                case 5: return center - Vector3.forward * z;
                default:
                    int corner = index - 6;
                    return center + new Vector3(
                        (corner & 1) == 0 ? -x : x,
                        (corner & 2) == 0 ? -y : y,
                        (corner & 4) == 0 ? -z : z);
            }
        }

'@.Replace(([string][char]13+[char]10),[string][char]10)
$s=$s.Substring(0,$start)+$section+$s.Substring($end)
[IO.File]::WriteAllText($a,$s,[Text.UTF8Encoding]::new($false))
$s=[IO.File]::ReadAllText($b)
$s=$s.Replace('r.BoundCount = Area.HasFace(r.F) && (r.F.LastOcc > .05f || r.Corners) ? 5 : 0;','r.BoundCount = Area.HasFace(r.F) && (r.F.LastOcc > .05f || r.Corners) ? Area.BoundarySamples : 0;')
$s=$s.Replace('Walls(r.BoundFirst + k * 3, r.To, ref candidate)','Walls(r.BoundFirst + k * 3, Area.FacePoint(f, r.Listener, k), ref candidate)')
[IO.File]::WriteAllText($b,$s,[Text.UTF8Encoding]::new($false))
$s=[IO.File]::ReadAllText((Join-Path $r 'Occlusion.cs'))
$s=$s.Replace('for (int p = 0; p < 5; p++)','for (int p = 0; p < Area.BoundarySamples; p++)')
[IO.File]::WriteAllText((Join-Path $r 'Occlusion.cs'),$s,[Text.UTF8Encoding]::new($false))
dotnet build (Join-Path $r 'WiresAcoustics.csproj') -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'BUILD FAILED'}
