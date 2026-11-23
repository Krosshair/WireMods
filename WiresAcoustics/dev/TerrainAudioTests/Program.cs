using System;
using System.Reflection;
using UnityEngine;
using WiresAcoustics;
namespace HarmonyLib {
    internal sealed class HarmonyPatch : Attribute {
        internal HarmonyPatch(Type type, string name) {}
    }
}
namespace UnityEngine {
    internal struct Vector3 {
        internal float x,y,z;
        internal Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
    internal static class Mathf {
        internal static float Max(float a,float b)=>Math.Max(a,b);
    }
}
internal sealed class Transform {
    internal Vector3 position;
}
internal sealed class EffectList {}
internal sealed class TerrainOp {
    internal sealed class Settings {internal bool m_raise;internal float m_raiseDelta;}
    internal Settings m_settings=new();
    internal EffectList m_onPlacedEffect=new();
    internal Transform transform=new();
}
internal static class Heightmap {
    internal static bool Available;
    internal static float Ground;
    internal static bool GetHeight(Vector3 at,out float h) {h=Ground;return Available;}
}
namespace WiresAcoustics {
    internal static class Plugin {
        internal static Entry Enabled=new(){Value=true};
        internal sealed class Entry {internal bool Value;}
    }
}
internal static class Program {
    private static int n;
    private static void Check(bool b,string label){if(!b)throw new Exception(label);n++;}
    private static void Near(float a,float b,string label) =>
        Check(Math.Abs(a-b)<.00001f,label+": "+a+" vs "+b);
    private static void Main(){
        var op=new TerrainOp{
            m_settings=new(){m_raise=true,m_raiseDelta=1.0f},
            transform=new(){position=new(0,5,0)}
        };
        var state=typeof(TerrainAudio).GetField("s_current",BindingFlags.NonPublic|BindingFlags.Static)!;
        state.SetValue(null,op);
        Heightmap.Available=false;
        var pos=new Vector3(0,5,0);
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,6.16f,"missing heightmap: raised point stays above surface");
        Heightmap.Available=true;Heightmap.Ground=5f;
        pos=new(0,5,0);
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,6.16f,"stale pre-raise height cannot bury sound");
        Heightmap.Ground=6.5f;
        pos=new(0,5,0);
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,6.66f,"actual raised surface overrides estimate");
        pos=new(0,5,0);
        TerrainAudio.Place(new EffectList(),ref pos);
        Near(pos.y,5f,"unrelated effect unaffected");
        op.m_settings.m_raise=false;
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,5f,"no raise: unchanged");
        op.m_settings.m_raise=true;op.m_settings.m_raiseDelta=-.5f;
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,5f,"lowering unchanged");
        op.m_settings.m_raiseDelta=1f;
        pos=new(0,7,0);
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,7f,"existing higher origin not pulled down");
        state.SetValue(null,null);
        pos=new(0,5,0);
        TerrainAudio.Place(op.m_onPlacedEffect,ref pos);
        Near(pos.y,5f,"no terrain context: unchanged");
        Console.WriteLine("TERRAIN AUDIO SOURCE PASS: "+n+" source-linked terrain placement checks");
    }
}