using System;
using System.Threading;
using System.Threading.Tasks;
using WiresAcoustics;

static class Test
{
    static void Check(bool x,string why){if(!x)throw new Exception("FAIL: "+why);}
    static void Main()
    {
        var simple = new AudioMeterHistory();
        for(int i=0;i<100;i++)simple.Publish(4,1,16);
        simple.Read(out var input,out var output,out var samples,out var dropped);
        Check(input==400 && output==100 && samples==1600 && dropped==0,"exact 100 packets");
        simple.Read(out input,out output,out samples,out dropped);
        Check(input==0 && output==0 && samples==0 && dropped==0,"consumed once");
        var overflow = new AudioMeterHistory();
        for(int i=0;i<700;i++)overflow.Publish(8,2,8);
        overflow.Read(out input,out output,out samples,out dropped);
        Check(dropped==188 && input==4096 && output==1024 && samples==4096,"overflow bounded and tracked");
        var concurrent = new AudioMeterHistory();
        long consumed=0, lost=0;
        double sumIn=0,sumOut=0;
        var publisher=Task.Run(()=>{
            for(int i=0;i<100_000;i++)concurrent.Publish(4,1,16);
        });
        var reader=Task.Run(()=>{
            while(!publisher.IsCompleted){
                concurrent.Read(out var a,out var b,out var s,out var d);
                Check(Math.Abs(a-4*b)<1e-9 && s==(long)a*4,"torn or mismatched packet");
                Interlocked.Add(ref consumed,s);
                Interlocked.Add(ref lost,d);
                sumIn+=a;sumOut+=b;
                Thread.Yield();
            }
            concurrent.Read(out var c,out var e,out var n,out var k);
            Check(Math.Abs(c-4*e)<1e-9 && n==(long)c*4,"final packet mismatch");
            Interlocked.Add(ref consumed,n);
            Interlocked.Add(ref lost,k);
            sumIn+=c;sumOut+=e;
        });
        Task.WaitAll(publisher,reader);
        Check(sumIn==4*sumOut,"ratio preserved");
        Check(consumed/16+lost==100_000,"all packets read or explicitly dropped");
        Console.WriteLine("METER HISTORY PASS: exact packets, bounded overflow, and 100000 concurrent publications with no torn reads");
    }
}
