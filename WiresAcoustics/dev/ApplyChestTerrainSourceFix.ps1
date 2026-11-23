$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$backup="$r\backup\CHEST_TERRAIN_SOURCE_FIX_20261123"
if(Test-Path $backup){throw 'Chest terrain backup path already exists'}
New-Item -ItemType Directory -Path "$backup\src" -Force|Out-Null
New-Item -ItemType Directory -Path "$backup\dev" -Force|Out-Null
foreach($f in @('Filter.cs','Follow.cs','SourceVolume.cs','Stackify.cs')){Copy-Item "$r\src\$f" "$backup\src\$f"}
foreach($f in @('TestEventSourceExtent.ps1','VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1')){
 Copy-Item "$r\dev\$f" "$backup\dev\$f"
}
Copy-Item "$r\dev\VolumeSamplingTests\Program.cs" "$backup\dev\VolumeSamplingTests-Program.cs"
Copy-Item "$r\package\WiresAcoustics.dll" "$backup\WiresAcoustics.dll"
$profile='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item $profile "$backup\Installed-WiresAcoustics.dll"
function ReplaceOne([string]$file,[string]$old,[string]$new){
 $text=[IO.File]::ReadAllText($file)
 $eol=if($text.Contains("`r`n")){"`r`n"}else{"`n"}
 $old=$old.Replace("`r`n","`n").Replace("`n",$eol)
 $new=$new.Replace("`r`n","`n").Replace("`n",$eol)
 $at=$text.IndexOf($old,[StringComparison]::Ordinal)
 if($at -lt 0){throw "Could not find patch context in $file : $old"}
 if($text.IndexOf($old,$at+$old.Length,[StringComparison]::Ordinal) -ge 0){throw "Ambiguous patch in $file : $old"}
 $text=$text.Substring(0,$at)+$new+$text.Substring($at+$old.Length)
 [IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($false))
}
ReplaceOne "$r\src\Filter.cs" @'
        internal bool LocalizedEvent;
'@ @'
        internal bool LocalizedEvent;
        // Opening a container is heard from its exposed sides, not
        // exclusively from the lid point inside a tightly stacked chest.
        // This marks a physical SURFACE source rather than all its volume.
        internal bool SurfaceEmitter;
'@
ReplaceOne "$r\src\Follow.cs" @'
        private static class Created
        {
            private static void Postfix(EffectList __instance, GameObject[] __result, Vector3 basePos, Transform baseParent)
'@ @'
        private static class Created
        {
            // Before Valheim instantiates and plays terrain sound effects,
            // place raised-ground impacts on the new exposed surface.
            private static void Prefix(EffectList __instance, ref Vector3 basePos)
            {
                if (Plugin.Enabled.Value) TerrainAudio.Place(__instance, ref basePos);
            }

            private static void Postfix(EffectList __instance, GameObject[] __result, Vector3 basePos, Transform baseParent)
'@
ReplaceOne "$r\src\Follow.cs" @'
                                if (!a.WeaponSource) a.ShapeRoot = given;
                                a.AreaKnown = false;
'@ @'
                                if (!a.WeaponSource) a.ShapeRoot = given;
                                a.SurfaceEmitter = false;
                                a.AreaKnown = false;
'@
ReplaceOne "$r\src\Follow.cs" @'
                            if (audio != null) AcousticFilter.For(audio).LocalizedEvent = true;
'@ @'
                            if (audio != null)
                            {
                                AcousticFilter a = AcousticFilter.For(audio);
                                a.ShapeRoot = given;
                                a.AreaKnown = false;
                                a.LocalizedEvent = false;
                                // A chest can have another chest directly
                                // above its lid. Its front and side panels
                                // are still real, audible emitting surfaces.
                                a.SurfaceEmitter = true;
                            }
'@
ReplaceOne "$r\src\SourceVolume.cs" @'
            internal bool Box;
            internal Vector3 AxisX, AxisY, AxisZ; // world-space box half-axes
'@ @'
            internal bool Box;
            internal Vector3 AxisX, AxisY, AxisZ; // world-space box half-axes
            internal bool Surface; // container opening: front-facing shell
            internal float SurfaceX, SurfaceY; // cumulative projected face shares
            internal float SignX, SignY, SignZ;
'@
ReplaceOne "$r\src\SourceVolume.cs" @'
            p.AxisX = boxX; p.AxisY = boxY; p.AxisZ = boxZ;
            return p;
'@ @'
            p.AxisX = boxX; p.AxisY = boxY; p.AxisZ = boxZ;
            p.Surface = knownBox && f.SurfaceEmitter;
            p.SurfaceX = p.SurfaceY = 0f;
            p.SignX = p.SignY = p.SignZ = 1f;
            if (p.Surface)
            {
                // Project the three listener-facing faces by their physical
                // areas and orientation. Sampling only the lid makes a chest
                // buried in a compact stack sound completely occluded, even
                // when its broad side is right in front of the listener.
                // All samples remain on the real oriented shell, within the
                // existing source-volume ray budget.
                Vector3 toward = listener - center;
                float lx = boxX.magnitude, ly = boxY.magnitude,
                      lz = boxZ.magnitude;
                float wx = lx > 1e-5f ? Mathf.Abs(Vector3.Dot(toward, boxX / lx)) * ly * lz : 0f;
                float wy = ly > 1e-5f ? Mathf.Abs(Vector3.Dot(toward, boxY / ly)) * lx * lz : 0f;
                float wz = lz > 1e-5f ? Mathf.Abs(Vector3.Dot(toward, boxZ / lz)) * lx * ly : 0f;
                float total = wx + wy + wz;
                p.SurfaceX = total > 1e-5f ? wx / total : 0f;
                p.SurfaceY = total > 1e-5f ? (wx + wy) / total : 0f;
                p.SignX = Vector3.Dot(toward, boxX) < 0f ? -1f : 1f;
                p.SignY = Vector3.Dot(toward, boxY) < 0f ? -1f : 1f;
                p.SignZ = Vector3.Dot(toward, boxZ) < 0f ? -1f : 1f;
            }
            return p;
'@
ReplaceOne "$r\src\SourceVolume.cs" @'
            if (p.Box)
            {
                // Halton lattice inside the cached oriented volume.
'@ @'
            if (p.Box)
            {
                if (p.Surface)
                {
                    // Reuse exactly the existing samples. Each lies 2 cm
                    // inside a real listener-facing panel, never behind the
                    // chest or at an artificial point outside its collider.
                    float u = 2f * Halton(i, 2) - 1f;
                    float v = 2f * Halton(i, 3) - 1f;
                    float face = Halton(i, 7);
                    if (face < p.SurfaceX)
                    {
                        float inset = Mathf.Max(0f, 1f - .02f /
                            Mathf.Max(.02f, p.AxisX.magnitude));
                        return p.Center + p.AxisX * (p.SignX * inset)
                            + p.AxisY * u + p.AxisZ * v;
                    }
                    if (face < p.SurfaceY)
                    {
                        float inset = Mathf.Max(0f, 1f - .02f /
                            Mathf.Max(.02f, p.AxisY.magnitude));
                        return p.Center + p.AxisX * u
                            + p.AxisY * (p.SignY * inset) + p.AxisZ * v;
                    }
                    float zInset = Mathf.Max(0f, 1f - .02f /
                        Mathf.Max(.02f, p.AxisZ.magnitude));
                    return p.Center + p.AxisX * u + p.AxisY * v
                        + p.AxisZ * (p.SignZ * zInset);
                }
                // Halton lattice inside the cached oriented volume.
'@
ReplaceOne "$r\src\Stackify.cs" @'
        private static bool s_active;
        private static AudioMixerGroup s_group;
'@ @'
        private static bool s_active;
        private static Container s_chest;
        private static AudioMixerGroup s_group;
'@
ReplaceOne "$r\src\Stackify.cs" @'
            private static void Prefix() => s_active = Plugin.Enabled.Value;
            private static void Finalizer() { s_active = false; s_group = null; }
'@ @'
            private static void Prefix(Container container)
            {
                s_active = Plugin.Enabled.Value;
                s_chest = s_active ? container : null;
            }
            private static void Finalizer()
            {
                s_active = false; s_group = null; s_chest = null;
            }
'@
ReplaceOne "$r\src\Stackify.cs" @'
                if (s_active) __result = source;
'@ @'
                if (s_active)
                {
                    // Restore a real container's audible opening to its lid;
                    // source occlusion is then evaluated across exposed faces.
                    Vector3 lid;
                    __result = s_chest != null &&
                        Area.TopOf(s_chest.transform, out lid) ? lid : source;
                }
'@
ReplaceOne "$r\src\Stackify.cs" @'
        [HarmonyPatch]
        private static class PlayNow
'@ @'
        // Stackify instantiates the ZSFX itself, outside EffectList.Create.
        // Give its new chest sounds the same owner/surface semantics before
        // ZSFX.UpdateReverb and before any opening-ray jobs are scheduled.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class ChestPlay
        {
            private static void Prefix(ZSFX __instance)
            {
                if (!s_active || s_chest == null || __instance == null) return;
                AudioSource audio = __instance.GetComponent<AudioSource>();
                if (audio == null || audio.spatialBlend < .5f) return;
                AcousticFilter f = AcousticFilter.For(audio);
                f.ShapeRoot = s_chest.transform;
                f.AreaKnown = false;
                f.LocalizedEvent = false;
                f.SurfaceEmitter = true;
            }
        }

        [HarmonyPatch]
        private static class PlayNow
'@
$newTerrain=@'
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Valheim applies a TerrainOp before calling its OnPlaced effect at the
    // ORIGINAL build position. A raised surface can then cover that point.
    // Move the one-shot origin to the newly exposed upper surface before
    // EffectList.Create instantiates/plays the sound; no audio polling.
    [HarmonyPatch(typeof(TerrainOp), "OnPlaced")]
    internal static class TerrainAudio
    {
        private static TerrainOp s_current;

        private static void Prefix(TerrainOp __instance, out TerrainOp __state)
        {
            __state = s_current;
            if (Plugin.Enabled.Value) s_current = __instance;
        }

        private static void Finalizer(TerrainOp __state)
        {
            s_current = __state;
        }

        internal static float RaisedSurfaceHeight(float originalY,
            float raiseDelta, bool hasCurrentHeight, float currentHeight)
        {
            // Heightmap can still contain the pre-operation vertex data if
            // its mesh is queued for rebuild. Use the authored raise delta
            // then, but prefer the real changed surface when it is already
            // available. A small 16 cm air clearance avoids a sound exactly
            // on the ground being interpreted as inside the ground collider.
            float y = originalY + Mathf.Max(0f, raiseDelta);
            if (hasCurrentHeight && currentHeight > originalY + .06f)
                y = currentHeight;
            return y + .16f;
        }

        internal static void Place(EffectList list, ref Vector3 at)
        {
            TerrainOp op = s_current;
            if (op == null || op.m_settings == null ||
                !ReferenceEquals(list, op.m_onPlacedEffect) ||
                !op.m_settings.m_raise ||
                op.m_settings.m_raiseDelta <= 0f) return;
            Vector3 atOperation = op.transform.position;
            bool measured = Heightmap.GetHeight(atOperation, out float ground);
            at.y = Mathf.Max(at.y, RaisedSurfaceHeight(atOperation.y,
                op.m_settings.m_raiseDelta, measured, ground));
        }
    }
}
'@
$path="$r\src\TerrainAudio.cs"
if(Test-Path $path){throw 'TerrainAudio.cs already exists'}
[IO.File]::WriteAllText($path,$newTerrain.TrimStart(),[Text.UTF8Encoding]::new($false))
ReplaceOne "$r\dev\VolumeSamplingTests\Program.cs" @'
  internal bool AreaKnown,LocalizedEvent; internal UnityEngine.Transform ShapeRoot;
'@ @'
  internal bool AreaKnown,LocalizedEvent,SurfaceEmitter; internal UnityEngine.Transform ShapeRoot;
'@
ReplaceOne "$r\dev\TestEventSourceExtent.ps1" @'
Must ($follow.Contains('go.transform.position = lid;') -and
       $follow.Contains('AcousticFilter.For(audio).LocalizedEvent = true;')) 'chest-lid event no longer point localized'
'@ @'
Must ($follow.Contains('go.transform.position = lid;') -and
       $follow.Contains('a.SurfaceEmitter = true;') -and
       $follow.Contains('a.LocalizedEvent = false;')) 'chest opening must sample its audible exposed shell, not just the buried lid point'
'@
ReplaceOne "$r\dev\VerifyReleaseCandidate.ps1" @'
'EnvironmentalField.cs')){throw "Unexpected new source $($f.Name)"}
'@ @'
'EnvironmentalField.cs','TerrainAudio.cs')){throw "Unexpected new source $($f.Name)"}
'@
ReplaceOne "$r\dev\VerifyReleaseCandidate.ps1" @'
'Follow.cs','EnvironmentalField.cs')
'@ @'
'Follow.cs','EnvironmentalField.cs','Stackify.cs','TerrainAudio.cs')
'@
Write-Output 'CODE PATCHED: chest shell, Stackify chest source and terrain raise origin'
& dotnet build "$r\src\WiresAcoustics.csproj" -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'Build failed'}
Write-Output 'BUILD SUCCEEDED'
