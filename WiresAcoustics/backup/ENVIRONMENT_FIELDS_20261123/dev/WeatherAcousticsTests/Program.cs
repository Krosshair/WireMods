using System;
using WiresAcoustics;
namespace UnityEngine {
    internal class AudioClip {internal string name;internal AudioClip(string name){this.name=name;}}
    internal static class Mathf {
        internal static float Clamp01(float v)=>Math.Clamp(v,0f,1f);
        internal static float Max(float a,float b)=>Math.Max(a,b);
        internal static float Sqrt(float x)=>(float)Math.Sqrt(x);
        internal static float MoveTowards(float a,float b,float d)=>
            Math.Abs(b-a)<=d?b:a+Math.Sign(b-a)*d;
        internal static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    }
}
namespace WiresAcoustics {
    internal static class AcousticMath {
        internal static float LogLerp(float a,float b,float t)=>
            (float)Math.Exp(Math.Log(a)+(Math.Log(b)-Math.Log(a))*t);
    }
}
internal static class Program {
    static int n;
    static void Check(bool ok,string why) {
        if(!ok)throw new Exception("FAILED "+why);
        n++;
    }
    static void Near(float actual,float expected,float epsilon,string what) =>
       Check(Math.Abs(actual-expected)<=epsilon,$"{what}: actual {actual} expected {expected}");
    static void Main() {
        // Outdoor visibility near a wide doorway must not depend solely
        // on the enclosure of the entire room. Farther away, return to
        // the physically measured solid-angle share.
        Near(WeatherAcoustics.OpeningGain(0f,0f,1f),0f,.00001f,
            "no opening means no directional exterior feed");
        Near(WeatherAcoustics.OpeningGain(.25f,0f,1f),.5f,.00001f,
            "diffuse opening area only");
        float closeDoor=WeatherAcoustics.OpeningGain(.25f,2f,1f);
        float farDoor=WeatherAcoustics.OpeningGain(.25f,2f,8f);
        Near(closeDoor,1f,.00001f,
            "wide open door 1m away should carry exterior sound at outdoor level");
        Near(farDoor,.5f,.00001f,"far door returns to original diffuse share");
        Near(WeatherAcoustics.OpeningGain(1f,20f,.01f),1f,.00001f,
            "completely exposed opening cannot exceed vanilla outdoor amplitude");
        float previousDoor=0f;
        for(int d=100;d>=0;d--) {
            float current=WeatherAcoustics.OpeningGain(.25f,2f,d*.1f);
            Check(current>=previousDoor-.00001f,
                "approaching a visible opening cannot make exterior sound quieter");
            Check(current>=0f && current<=1f,
                "doorway transmission must remain normalized");
            previousDoor=current;
        }
        // Only the original game's recording is played; no independently
        // managed bridge or second rain audio source is permitted.
        var rain=new UnityEngine.AudioClip("Rain");
        var copy=new UnityEngine.AudioClip("Rain");
        var birds=new UnityEngine.AudioClip("Birds");
        Check(WeatherAcoustics.IsActiveRainLoop(true,rain,rain),"exact active weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(false,rain,rain),"dry weather");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,birds),"mismatched weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,copy),"same name is not clip identity");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,null,rain),"no weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,null),"no playing clip");
        float smooth=0f;
        for(int frame=0;frame<15;frame++)smooth=WeatherAcoustics.Approach(smooth,1f,1f/60f);
        Near(smooth,1f,.00001f,"rain reacts to coverage within 250 ms");
        smooth=WeatherAcoustics.Approach(smooth,0f,.05f);
        Near(smooth,.8f,.00001f,"rain exit attenuates continuously over 50 ms");
        smooth=WeatherAcoustics.Approach(smooth,0f,.2f);
        Near(smooth,0f,.00001f,"rain exit finishes after 250 ms");
        WeatherAcoustics.Blend(.08f,500f,1f,1f,1f,out float gain,out float cut);
        Near(gain,.72f,.00001f,"roof rain remains clearly audible");
        Near(cut,6500f,.01f,"roof rain retains its spectral texture");
        WeatherAcoustics.Blend(.08f,500f,1f,0f,1f,out gain,out cut);
        Near(gain,.24f,.00001f,"no line of sight retains transmitted indirect rain");
        Near(cut,500f,.001f,"no synthetic cave roof");
        WeatherAcoustics.Blend(.08f,500f,0f,1f,1f,out gain,out cut);
        Near(gain,.08f,.00001f,"dry weather unchanged");
        Near(cut,500f,.001f,"dry lowpass unchanged");
        WeatherAcoustics.Blend(.08f,500f,1f,.15f,.35f,out gain,out cut);
        Check(gain>.08f && gain<.72f,"partial shelter perceptually intermediate");
        Check(cut>500f && cut<6500f,"partial shelter spectrally intermediate");
        float previous=.24f;
        for(int i=0;i<=1000;i++){
            float roof=i/1000f;
            WeatherAcoustics.Blend(.08f,500f,1f,roof,1f,out gain,out cut);
            Check(gain>=previous-.00001f,"no decreasing rain presence across increasing roof");
            Check(Math.Abs(gain-previous)<.002f,"rain roof interpolation has no sudden jumps");
            previous=gain;
        }
        // Rain directly against roof tiles is an attic sound, not a
        // generic property of any ceiling. A floor between storeys must
        // NOT be reclassified as the building's exposed roofing.
        Check(WeatherAcoustics.IsRoofPieceName("wood_roof(Clone)"),
            "the actual wood roof prefab is a roof");
        Check(WeatherAcoustics.IsRoofPieceName("darkwood_roof_45"),
            "darkwood sloped roof is a roof");
        Check(WeatherAcoustics.IsRoofPieceName("piece_grausten_roof_45"),
            "stone roof is a roof");
        Check(!WeatherAcoustics.IsRoofPieceName("wood_floor"),
            "floor immediately above basement is not exposed roof");
        Check(!WeatherAcoustics.IsRoofPieceName("stone_floor_2x2"),
            "stone storey ceiling is not exposed roof");
        Check(!WeatherAcoustics.IsRoofPieceName("wood_wall_roof_45"),
            "gable wall panel is not exposed roof");
        Check(!WeatherAcoustics.IsRoofPieceName("ashwood_wall_roof_26"),
            "ashwood gable wall panel is not exposed roof");
        Check(!WeatherAcoustics.IsRoofPieceName(null), "null name not roof");
        Near(WeatherAcoustics.RoofImpactWeight(0f),1f,.00001f,
            "roof impact attenuation at listener");
        Near(WeatherAcoustics.RoofImpactWeight(3f),20.25f/29.25f,.00001f,
            "attic roof 3m above listener");
        Near(WeatherAcoustics.RoofImpactWeight(6f),.36f,.00001f,
            "higher roof attenuated");
        Check(WeatherAcoustics.RoofImpactWeight(12f)<.13f,
            "several-storey vertical separation reduces roof impacts");
        float lastRoofWeight=1f;
        for(int cm=1;cm<=1500;cm++) {
            float next=WeatherAcoustics.RoofImpactWeight(cm*.01f);
            Check(next<=lastRoofWeight+.00001f,
                "roof hit gain cannot increase with roof height");
            Check(lastRoofWeight-next<.003f,
                "roof hit gain must vary continuously across centimetres");
            lastRoofWeight=next;
        }
        WeatherAcoustics.Blend(.08f,500f,1f,WeatherAcoustics.RoofImpactWeight(3f),
            1f,out gain,out cut);
        float atticRain=gain;
        WeatherAcoustics.Blend(.08f,500f,1f,0f,1f,out gain,out cut);
        Check(atticRain>=.70f && gain<=.25f,
            "attic has rooftop trickle but basement under ordinary floor does not");
        Console.WriteLine($"WEATHER SOURCE + ROOF ACOUSTICS PASS: {n} source-linked checks");
    }
}