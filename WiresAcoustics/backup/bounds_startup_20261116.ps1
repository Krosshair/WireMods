$ErrorActionPreference='Stop'
$f='C:\Users\kajet\WireMods\WiresAcoustics\src\Occlusion.cs'
$nl=[string][char]10
$s=[IO.File]::ReadAllText($f).Replace(([string][char]13+[char]10),$nl)
$anchor='            float occ = starting && Plugin.Occlusion.Value && !Mine(__instance.transform, f) ? Blocking(Hearing.Eye, to, out block) : 0f; // what'+"'s in the way: from the camera (Hearing)"
if(!$s.Contains($anchor)){throw 'initial occlusion anchor missing'}
$extra=@'
            // One-shot sounds (notably chest closing) can finish before a
            // second worker result. Immediately check the six face centres
            // only when their direct source point is blocked, using the SAME
            // blocking/own-collider policy as the original direct ray.
            // The remaining eight corners are handled by the normal worker
            // batch. Frame.Over stops these checks at the existing budget.
            if (starting && occ > .05f && f != null && Area.HasFace(f))
                for (int sample = 0; sample < 6 && !Frame.Over; sample++)
                {
                    Block alternative;
                    if (Blocking(Hearing.Eye, Area.FacePoint(f, Hearing.Eye, sample), out alternative) <= .05f)
                    {
                        occ = 0f;
                        block = Block.Clear;
                        break;
                    }
                }
'@.Replace(([string][char]13+[char]10),$nl)
$s=$s.Replace($anchor,$anchor+$nl+$extra.TrimEnd())
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))
dotnet build 'C:\Users\kajet\WireMods\WiresAcoustics\src\WiresAcoustics.csproj' -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'build failed'}
