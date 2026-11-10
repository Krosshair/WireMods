using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Collections.Generic;using System.Globalization;
using WiresAcoustics;
// Builds the click table: for every exported clip, what has to be redone in the game's copy.
// Footsteps: the "declicked3" cleaning (tail start + smoothing steps). Others: the ring-matched
// fixes from the earlier build (v3.tsv). Plus each clip's first sample and peak (start click).
class B{
 static void Run(string exe,string args,out byte[] bytes){var p=new Process{StartInfo=new ProcessStartInfo(exe,args){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false}};p.Start();var ms=new MemoryStream();var err=p.StandardError.ReadToEndAsync();p.StandardOutput.BaseStream.CopyTo(ms);p.WaitForExit();bytes=ms.ToArray();}
 static string F(float v)=>v.ToString("0.#####",CultureInfo.InvariantCulture);
 static void Main(string[] a){
  var root=a[0];var files=File.ReadAllLines("list.txt");int from=int.Parse(a[1]),cnt=int.Parse(a[2]);
  var v3=new Dictionary<string,string[]>();foreach(var l in File.ReadAllLines("v3.tsv")){var f=l.Split('\t');if(f.Length==7)v3[f[4]]=f;}
  for(int k=from;k<Math.Min(files.Length,from+cnt);k++){
   if(!v3.TryGetValue(files[k],out var old)||old[5]=="skip"||old[5]=="unreadable")continue;
   int ch=int.Parse(old[2]),rate=int.Parse(old[3]);
   Run("ffmpeg",$"-v quiet -i \"{Path.Combine(root,files[k])}\" -f f32le -",out var b);
   var x=new float[b.Length/4];Buffer.BlockCopy(b,0,x,0,b.Length);int frames=x.Length/ch;
   string name=Path.GetFileNameWithoutExtension(files[k]);
   float peak=0;foreach(var v in x)peak=Math.Max(peak,Math.Abs(v));
   var first=new float[ch];for(int c=0;c<ch;c++)first[c]=x[c];
   bool step=name.StartsWith("Player_Footstep_")||name.StartsWith("Foot_Mud_");
   string kind="-",tails="-",fixes="-";int pc=0,pp=8;double worst=-999;
   if(step){
    var tl=new List<string>();var fx=new List<string>();bool any=false;int firstPos=int.MaxValue;
    for(int c=0;c<ch;c++){
     var one=new float[frames];for(int i=0;i<frames;i++)one[i]=x[i*ch+c];
     var steps=new List<DeclickMathC.Step>();var y=DeclickMathC.Clean(one,true,out int found,steps,out int tail);
     var z=(float[])one.Clone();DeclickMathC.Replay(z,tail,steps);
     double pk=Math.Max(1e-9,peak);for(int i=0;i<frames;i++){double d=Math.Abs(z[i]-y[i])/pk;if(d>0)worst=Math.Max(worst,20*Math.Log10(d));}
     tl.Add(tail.ToString());foreach(var s in steps)fx.Add($"{c}:{s.Pos}:{s.Pass}");
     if(steps.Count>0||tail<frames)any=true;
     int cand=steps.Count>0?steps.Min(s=>s.Pos):(tail+600<frames?tail+600:int.MaxValue);
     if(cand<firstPos){firstPos=cand;pc=c;}
    }
    if(any){kind="C";tails=string.Join(",",tl);fixes=fx.Count>0?string.Join(";",fx):"-";if(firstPos!=int.MaxValue)pp=Math.Max(8,Math.Min(frames-8,firstPos));}
   }else if(old[5]!="-"){
    kind="R";fixes=old[5];var q=old[5].Split(';')[0].Split(':');pc=int.Parse(q[0]);pp=Math.Max(8,Math.Min(frames-8,int.Parse(q[1])));
   }
   bool lead=first.Max(v=>Math.Abs(v))>.02f*peak&&first.Max(v=>Math.Abs(v))>1e-3f;
   string print="-";
   if(kind!="-"||lead){var pr=new List<string>();for(int i=pp-8;i<pp+8;i++)pr.Add(((int)Math.Round(x[i*ch+pc]*32767)).ToString());print=$"{pc}:{pp}:{string.Join(",",pr)}";}
   string line=$"{name}\t{ch}\t{frames}\t{kind}\t{tails}\t{fixes}\t{print}\t{string.Join(",",first.Select(F))}:{F(peak)}\t{(lead?1:0)}\t{worst:F1}\t{files[k]}\n";
   File.AppendAllText("table4.tsv",line);
  }}}
