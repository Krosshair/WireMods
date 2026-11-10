using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Collections.Generic;using System.Reflection;
class S{static void Main(string[] a){
 var root=a[0];var files=File.ReadAllLines("list.txt");int from=int.Parse(a[1]),cnt=int.Parse(a[2]);
 var find=typeof(WiresAcoustics.DeclickMath).GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static);
 using var o=File.AppendText("result.tsv");
 for(int k=from;k<Math.Min(files.Length,from+cnt);k++){
  var p=new Process{StartInfo=new ProcessStartInfo("ffmpeg",$"-v quiet -i \"{Path.Combine(root,files[k])}\" -f f32le -ac 1 -"){RedirectStandardOutput=true,UseShellExecute=false}};
  p.Start();var ms=new MemoryStream();p.StandardOutput.BaseStream.CopyTo(ms);p.WaitForExit();
  var b=ms.ToArray();var x=new float[b.Length/4];Buffer.BlockCopy(b,0,x,0,b.Length);
  if(x.Length<2000||x.Length>48000*8){o.WriteLine($"{files[k]}\tskip\t{x.Length}");continue;}
  int loud=0;for(int i=1;i<x.Length;i++)if(Math.Abs(x[i])>Math.Abs(x[loud]))loud=i;
  // tail start: 10 ms RMS falls to a quarter of its max
  var sq=new double[x.Length+1];for(int i=0;i<x.Length;i++)sq[i+1]=sq[i]+x[i]*x[i];
  Func<int,double> env=i=>{int lo=Math.Max(0,i-240),hi=Math.Min(x.Length,i+240);return (sq[hi]-sq[lo])/480;};
  int pk=0;double pe=0;for(int i=0;i<x.Length;i+=24){var e=env(i);if(e>pe){pe=e;pk=i;}}
  int tail=x.Length;for(int i=pk;i<x.Length;i+=24)if(env(i)<pe*.0625){tail=i;break;}
  var hits=(List<int>)find.Invoke(null,new object[]{x,8f,loud});
  int inTail=hits.Count(h=>h>=tail);
  o.WriteLine($"{files[k]}\t{hits.Count}\t{inTail}\t{x.Length}\t{string.Join(",",hits.Where(h=>h>=tail).Take(6).Select(h=>(h/48000.0).ToString("F3")))}");
 }}}
