$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$src=Join-Path $root 'src'
$bk=Join-Path $root 'backup\SMELTER_BOUNDS_20261116'
if(Test-Path $bk){throw 'Backup exists; do not repeat patch'}
New-Item -ItemType Directory -Force $bk |Out-Null
foreach($name in @('Spots.cs','Area.cs','Filter.cs','Occlusion.cs','Batch.cs')){
 Copy-Item (Join-Path $src $name) (Join-Path $bk $name) -Force
}
$nl=[string][char]10
function Update([string]$name,[scriptblock]$changes) {
 $file=Join-Path $src $name
 $s=[IO.File]::ReadAllText($file).Replace(([string][char]13+[char]10),$nl)
 & $changes ([ref]$s)
 [IO.File]::WriteAllText($file,$s,[Text.UTF8Encoding]::new($false))
}
function Rep([ref]$s,[string]$old,[string]$new,[string]$label) {
 $n=[regex]::Matches($s.Value,[regex]::Escape($old)).Count
 if($n -ne 1){throw "$label matching count=$n"}
 $s.Value=$s.Value.Replace($old,$new)
}
Update 'Filter.cs' {
 param($s)
 Rep $s '        internal bool AreaKnown;' ('        internal bool AreaKnown;'+$nl+
 '        internal bool PartInteraction; // interaction sounds use the operated part, not the whole machine') 'filter marker'
}
Update 'Spots.cs' {
 param($s)
 $anchor='            // made at a character'+"'s pivot with no owner given"
 $insert=@'
            // When filling a smelter, Valheim creates the sound at the machine
            // pivot, even though there are separate ore and fuel switches.
            // The effect-list identity is authoritative; production and idle
            // effects remain where vanilla places them.
            if (root != null && PlaceSmelter(list, made, root, ref given)) return;
'@.TrimEnd().Replace(([string][char]13+[char]10),$nl)
 Rep $s $anchor ($insert+$nl+$anchor) 'early owner smelter'
 $anchor='            given = piece;'+$nl+'            Vector3 at;'
 Rep $s $anchor ('            given = piece;'+$nl+
  '            if (PlaceSmelter(list, made, piece, ref given)) return;'+$nl+
  '            Vector3 at;') 'local smelter'
 $anchor='        private static int s_noted;'
 $method=@'
        // A single event-time change; no added Update or sound scanning.
        private static bool PlaceSmelter(EffectList list, GameObject[] made,
            Transform owner, ref Transform given)
        {
            Smelter machine = owner.GetComponent<Smelter>();
            if (machine == null) return false;
            Switch sw = null;
            if (ReferenceEquals(list, machine.m_oreAddedEffects))
                sw = machine.m_addOreSwitch;
            else if (ReferenceEquals(list, machine.m_fuelAddedEffects))
                sw = machine.m_addWoodSwitch;
            else return false;
            if (sw == null) return false;
            given = machine.transform;
            Move(made, machine.transform, sw.transform.position, null);
            foreach (GameObject go in made)
            {
                if (go == null || go.GetComponent<ZSFX>() == null) continue;
                AcousticFilter filter = go.GetComponent<AcousticFilter>();
                if (filter != null) { filter.PartInteraction = true; filter.AreaKnown = false; }
            }
            Note(machine.name + " " + sw.name + " input", made);
            return true;
        }

'@.Replace(([string][char]13+[char]10),$nl)
 Rep $s $anchor ($method+$anchor) 'method'
}
Update 'Area.cs' {
 param($s)
 $anchor='            int id = owner.GetInstanceID();'
 $insert=@'
            // An ore/fuel interaction is the small input component of a
            // smelter, not a sound radiated by the entire smelter's box.
            if (f.PartInteraction)
            {
                f.ShapeRoot = owner;
                f.ShapeCenter = owner.InverseTransformPoint(sound.position);
                f.ShapeHalf = Vector3.one * .12f;
                f.AreaRadius = .12f;
                return;
            }
'@.Replace(([string][char]13+[char]10),$nl)
 # only add this to Measure, not Cached (which has a second identical id line)
 $begin=$s.Value.IndexOf('        internal static void Measure(')
 $end=$s.Value.IndexOf('        private static void Voice(',$begin)
 if($begin -lt 0 -or $end -lt 0){throw 'Measure scope missing'}
 $section=$s.Value.Substring($begin,$end-$begin)
 if(([regex]::Matches($section,[regex]::Escape($anchor))).Count -ne 1){throw 'Measure root anchor mismatch'}
 $section=$section.Replace($anchor,$insert+$anchor)
 $s.Value=$s.Value.Substring(0,$begin)+$section+$s.Value.Substring($end)
 $anchor='        // A piece'+"'s own sounds sit wherever the prefab put them."
 $method=@'
        // Conservative world-space facing surface of the cached source AABB.
        // Five points: the face centre and its four corners. This is an
        // audibility test ONLY; neither the source's HRTF nor its playback
        // position is moved. We do not cast for point-like sound effects.
        internal static bool HasFace(AcousticFilter f)
        {
            return f != null && f.AreaKnown && f.ShapeRoot != null &&
                f.ShapeHalf.sqrMagnitude > .05f;
        }

        internal static Vector3 FacePoint(AcousticFilter f, Vector3 listener, int index)
        {
            Vector3 center = f.ShapeRoot.TransformPoint(f.ShapeCenter);
            Vector3 half = f.ShapeHalf;
            Vector3 toward = listener - center;
            float x = Mathf.Abs(toward.x) / Mathf.Max(.01f, half.x);
            float y = Mathf.Abs(toward.y) / Mathf.Max(.01f, half.y);
            float z = Mathf.Abs(toward.z) / Mathf.Max(.01f, half.z);
            // The closest-facing face includes all four edges. A 2cm
            // inward offset avoids sampling a collider exactly on its edge.
            float u = index == 0 ? 0f : (index == 1 || index == 2 ? -1f : 1f);
            float v = index == 0 ? 0f : (index == 1 || index == 3 ? -1f : 1f);
            if (x >= y && x >= z)
                return center + new Vector3(Mathf.Sign(toward.x) * Mathf.Max(0f, half.x - .02f),
                    u * Mathf.Max(0f, half.y - .02f), v * Mathf.Max(0f, half.z - .02f));
            if (y >= z)
                return center + new Vector3(u * Mathf.Max(0f, half.x - .02f),
                    Mathf.Sign(toward.y) * Mathf.Max(0f, half.y - .02f),
                    v * Mathf.Max(0f, half.z - .02f));
            return center + new Vector3(u * Mathf.Max(0f, half.x - .02f),
                v * Mathf.Max(0f, half.y - .02f),
                Mathf.Sign(toward.z) * Mathf.Max(0f, half.z - .02f));
        }

'@.Replace(([string][char]13+[char]10),$nl)
 Rep $s $anchor ($method+$anchor) 'face functions'
}
Update 'Occlusion.cs' {
 param($s)
 $anchor='            Own = null;'+$nl+'            s_checked++;'
 $extra=@'
            // A sound radiating across a box is unobstructed if the listener
            // has a genuinely clear path to ANY part of its facing surface.
            // Keep original authored source position and panning unchanged.
            // At playback onset we must check synchronously so the first
            // audible milliseconds do not start incorrectly muffled.
            if (starting && occ > .05f && f != null && Area.HasFace(f))
            {
                for (int p = 0; p < 5; p++)
                {
                    Block sampleBlock;
                    float sample = Blocking(Hearing.Eye, Area.FacePoint(f, Hearing.Eye, p), out sampleBlock);
                    if (sample <= .05f)
                    {
                        occ = 0f;
                        block = Block.Clear;
                        break;
                    }
                }
            }
'@.Replace(([string][char]13+[char]10),$nl)
 Rep $s $anchor ($extra+$anchor) 'initial clearance'
}
Update 'Batch.cs' {
 param($s)
 Rep $s '            internal int First, Routes, EdgeN;' (
 '            internal int First, Routes, EdgeN;'+$nl+
 '            internal int BoundFirst, BoundCount; // 3-ray lines to the source facing surface') 'request fields'
 $anchor='                r.EdgeN = 0;'
 Rep $s $anchor ($anchor+$nl+
 '                r.BoundCount = Area.HasFace(r.F) ? 5 : 0;'+$nl+
 '                r.BoundFirst = -1;') 'request bounds count'
 $anchor='                cmds += 3 * (1 + r.Routes) + 6 * r.EdgeN;'
 Rep $s $anchor ('                cmds += 3 * (1 + r.Routes) + 6 * r.EdgeN + 3 * r.BoundCount;') 'batch capacity'
 $anchor='                s_cornerRays += 2 * r.EdgeN;'+$nl+'                s_flight[i] = r;'
 Rep $s $anchor (
 '                s_cornerRays += 2 * r.EdgeN;'+$nl+
 '                // Same asynchronous worker-ray batch, no new per-frame loop.'+$nl+
 '                r.BoundFirst = c;'+$nl+
 '                for (int k = 0; k < r.BoundCount; k++)'+$nl+
 '                    Cast3(listener, Area.FacePoint(r.F, listener, k), q, ref c);'+$nl+
 '                s_flight[i] = r;') 'batch cast'
 $anchor='                if (on) occ = Walls(r.First, r.To, ref direct);'
 $extra=@'
                // Already-cast source-volume rays: one clear 3-ray group
                // is enough to make the WHOLE source unobstructed. The
                // original sound position stays r.To; no corner redirection.
                if (on && occ > .05f && r.BoundCount > 0)
                    for (int k = 0; k < r.BoundCount; k++)
                    {
                        Occlusion.Block candidate = Occlusion.Block.Clear;
                        float accessible = Walls(r.BoundFirst + k * 3, r.To, ref candidate);
                        if (accessible <= .05f)
                        {
                            direct = Occlusion.Block.Clear;
                            occ = 0f;
                            break;
                        }
                    }
'@.Replace(([string][char]13+[char]10),$nl)
 Rep $s $anchor ($anchor+$nl+$extra.TrimEnd()) 'batch clearance'
 # Walls() must interpret the sound target as the actual sample point; it
 # already uses RaycastCommand.from and hit distances; the wall/counting
 # process still excludes the source owner based on f.ShapeRoot.
}
'PATCH APPLIED; ORIGINAL SOURCE SAVED IN '+$bk
dotnet build (Join-Path $src 'WiresAcoustics.csproj') -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'BUILD FAILED; do not install'}
