using System;using System.IO;using System.Linq;using System.Collections.Generic;
class T{
 static float[] Hp(float[] x){float[] B(float[] v){int n=v.Length;var s=new double[n+1];for(int i=0;i<n;i++)s[i+1]=s[i]+v[i];var y=new float[n];for(int i=0;i<n;i++){int a=Math.Max(0,i-4),b=Math.Min(n-1,i+4);y[i]=(float)((s[b+1]-s[a])/9);}return y;}
  var l=B(B(x));var h=new float[x.Length];for(int i=0;i<x.Length;i++)h[i]=x[i]-l[i];return h;}
 static double Rms(float[] h,int a,int b){double s=0;for(int i=a;i<b;i++)s+=h[i]*h[i];return Math.Sqrt(s/(b-a));}
 static void Main(){
  var known=new Dictionary<string,double[]>{{"Wood_Walk_M_03",new[]{.1736,.1933}},{"Wood_Run_M_03",new[]{.2025,.2726}},{"Wood_Run_M_05",new[]{.1801}},{"Wood_Run_M_06",new[]{.3202}},{"Wood_Run_M_07",new[]{.183}},{"Wood_Walk_M_05",new[]{.2824}},{"Stone_Run_M_11",new[]{.2008,.2257}}};
  foreach(var f in Directory.GetFiles(Environment.GetEnvironmentVariable("HOME")+"/dtest/raw","*.in").OrderBy(s=>s)){
   var b=File.ReadAllBytes(f);var x=new float[b.Length/4];Buffer.BlockCopy(b,0,x,0,b.Length);
   var y=(float[])x.Clone();var d=new WiresAcoustics.StreamDeclick();
   for(int o=0;o<y.Length;o+=1024){int n=Math.Min(1024,y.Length-o);var buf=new float[n];Array.Copy(y,o,buf,0,n);d.Process(buf,1,48000);Array.Copy(buf,0,y,o,n);}
   var z=new float[x.Length];Array.Copy(y,24,z,0,x.Length-24); // undo the 20-sample delay
   var hx=Hp(x);var hz=Hp(z);var name=Path.GetFileNameWithoutExtension(f);
   double diff=0,tot=0;for(int i=0;i<x.Length-40;i++){diff+=(z[i]-x[i])*(z[i]-x[i]);tot+=x[i]*x[i];}
   string k="";if(known.TryGetValue(name,out var ts))foreach(var t in ts){int j=(int)(t*48000);k+=$" {t}:{20*Math.Log10(Rms(hz,j-24,j+24)/Rms(hx,j-24,j+24)):F1}dB";}
   Console.WriteLine($"{name,-16} triggers {d.Triggers,4} closed {100.0*d.Closed/d.Seen,5:F1}% change {10*Math.Log10(diff/tot+1e-30),6:F1} dB{k}");}}}
