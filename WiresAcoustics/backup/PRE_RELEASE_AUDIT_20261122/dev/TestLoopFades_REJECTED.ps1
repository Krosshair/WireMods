$ErrorActionPreference='Stop'
$f=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\Filter.cs')
$s=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\Starts.cs')
$g=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\GeometryEvents.cs')
function Extract([string]$signature,[int]$after=0){
 $start=$f.IndexOf($signature,$after)
 if($start -lt 0){throw "Missing method $signature"}
 $open=$f.IndexOf('{',$start);$depth=0;$end=-1
 for($i=$open;$i -lt $f.Length;$i++){
  if($f[$i] -eq '{'){$depth++}
  if($f[$i] -eq '}'){$depth--;if($depth -eq 0){$end=$i;break}}
 }
 if($end -lt 0){throw "Unbalanced method $signature"}
 $f.Substring($start,$end-$start+1)
}
$attack=Extract 'private void FadeInLoop(float[] data, int channels)'
$init=Extract 'internal void Initialize(float seconds)'
$release=Extract 'private void OnAudioFilterRead(float[] data, int channels)' ($f.IndexOf('internal sealed class LoopReleaseEnvelope'))
$stub=@'
using System;
public static class Mathf {public static float Min(float a, float b) {return Math.Min(a,b);}}
public static class AudioSettings {public static int outputSampleRate=48000;}
public class Attack {
 private static volatile int s_rate=48000;
 internal const float LoopAttackSeconds=.30f;
 private volatile bool m_loopFadeEligible=true, m_loopAudioSeen;
 private int m_loopAttackSamples,m_loopAttackAt;
'@
$stub+='\n'+$attack+@'
 public float[] Run(int frames, bool stereo) {
  int channels=stereo?2:1;var data=new float[frames*channels];
  for(int i=0;i<data.Length;i++) data[i]=1f;
  FadeInLoop(data,channels);return data;
 }
 public void Disable(){m_loopFadeEligible=false;}
 public int Progress {get{return m_loopAttackAt;}}
 public bool Saw {get{return m_loopAudioSeen;}}
}
public class Release {
 private int m_totalSamples,m_remaining;
'@
$stub+='\n'+$init+'\n'+$release+@'
 public float[] Run(int frames, bool stereo) {
  int channels=stereo?2:1;var data=new float[frames*channels];
  for(int i=0;i<data.Length;i++) data[i]=1f;
  OnAudioFilterRead(data,channels);return data;
 }
}
public static class Check {
 static void Assert(bool yes,string msg){if(!yes)throw new Exception(msg);}
 static void Near(float a,float b,float tolerance,string msg){Assert(Math.Abs(a-b)<tolerance,msg+" actual="+a);}
 public static void Run(){
  var a=new Attack();
  var b0=a.Run(4800,true);Near(b0[0],0f,.0001f,"start must be zero");
  Near(b0[4800*2-2],.3333f,.001f,"100ms gain");
  var b1=a.Run(4800,true);Near(b1[4800*2-2],.6667f,.001f,"200ms gain");
  var b2=a.Run(4800,true);Near(b2[4800*2-2],1f,.001f,"300ms gain");
  var b3=a.Run(4800,true);Near(b3[0],1f,.0001f,"steady gain");
  Near(b1[0],b1[1],.000001f,"same fade in both stereo ears");
  var unchanged=new Attack(); unchanged.Disable();
  Near(unchanged.Run(100,true)[0],1f,.0001f,"nonloop effects unchanged");
  var tail=new Release();tail.Initialize(.55f);
  var t0=tail.Run(13200,true);Near(t0[0],1f,.0001f,"release starts at full power");
  Near(t0[t0.Length-2],.5f,.001f,"release half at 275ms");
  var t1=tail.Run(13200,true);Near(t1[t1.Length-2],0f,.001f,"release zero at 550ms");
  Near(tail.Run(64,true)[0],0f,.0001f,"expired tail silence");
  Console.WriteLine("LOOP FADES DSP SOURCE EXTRACTION PASS: .3s attack, .55s release, stereo, no one-shot fade");
 }
}
'@
$stub=$stub.Replace('\n',[Environment]::NewLine)
Add-Type -TypeDefinition $stub -ErrorAction Stop
[Check]::Run()
function Need([bool]$yes,[string]$why){if(!$yes){throw 'LOOP FADES SOURCE INTEGRATION: '+$why}}
Need ($f.Contains('EndLoopFade();') -and $f.Contains('Object.Destroy(go, AcousticFilter.LoopReleaseSeconds + .12f)')) 'release tail not connected to lifecycle'
Need ($f.Contains('FadeInLoop(data, channels);')) 'audio callbacks do not apply gain envelope'
Need ($s.Contains('[HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Stop))]') -and $s.Contains('[HarmonyPatch(typeof(ZSFX), "OnDisable")]') -and $s.Contains('loopFilter.BeginLoopFade();')) 'ZSFX stop/disable and start event hooks missing'
Need ($g.Contains('[HarmonyPatch(typeof(WearNTear), "Destroy")]') -and $g.Contains('f.SuppressLoopTail = true;')) 'broken pieces may leave tails'
Need ($f.Contains('tail.timeSamples = phase') -and $f.Contains('source.clip') -and $f.Contains('tail.outputAudioMixerGroup = source.outputAudioMixerGroup;')) 'tail failed to retain actual source, phase or mixer'
Need ($f.Contains('f.SuppressLoopTail = true;') -and $f.Contains('SuppressLoopTail = false;')) 'ReleaseAll or restart could emit phantom tails'
Need ($f.Contains('tail.Play();') -and $f.Contains('tail.timeSamples = phase')) 'tail position must be assigned after Play'
Write-Output 'LOOP FADES SOURCE INTEGRATION PASS: event-driven starts/stops; fire/smelter disabling; demolition excluded'
