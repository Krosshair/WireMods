using System;using System.IO;using System.Diagnostics;
class T{static void Main(){foreach(var f in Directory.GetFiles("raw","*.in")){
 var b=File.ReadAllBytes(f);var x=new float[b.Length/4];Buffer.BlockCopy(b,0,x,0,b.Length);
 var pb=File.ReadAllBytes(Path.ChangeExtension(f,".py"));var p=new float[pb.Length/4];Buffer.BlockCopy(pb,0,p,0,pb.Length);
 var sw=Stopwatch.StartNew();var y=WiresAcoustics.DeclickMath.Clean(x,out int n);sw.Stop();
 double md=0,pk=0;for(int i=0;i<y.Length;i++){md=Math.Max(md,Math.Abs(y[i]-p[i]));pk=Math.Max(pk,Math.Abs(p[i]));}
 Console.WriteLine($"{Path.GetFileNameWithoutExtension(f),-18} spots {n,3} maxdiff {20*Math.Log10(md/pk+1e-12),7:F1} dB  {sw.Elapsed.TotalMilliseconds,6:F1} ms");}}}
