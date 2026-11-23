using UnityEngine;

namespace WiresAcoustics
{
    // Analytical projected coverage for a circular source silhouette and
    // a Valheim box-collider wall. Unlike counting blocked sample rays,
    // disk/rectangle intersection area is continuous as the listener moves.
    // This is a planar-source / planar-obstacle approximation, not an
    // arbitrary volume-fade or a new transmission coefficient.
    internal static class AnalyticOcclusion
    {
        // Integral from 0 to x of sqrt(1-u*u) du for a unit disk.
        private static float Arc(float x)
        {
            x = Mathf.Clamp01(x);
            return .5f * (x * Mathf.Sqrt(Mathf.Max(0f, 1f - x*x)) +
                          Mathf.Asin(x));
        }

        // Signed area of a disk in the rectangle from (0,0) to (x,y).
        private static float Primitive(float x, float y)
        {
            if (x == 0f || y == 0f) return 0f;
            float sign = (x < 0f) == (y < 0f) ? 1f : -1f;
            float a = Mathf.Min(Mathf.Abs(x),1f), b = Mathf.Min(Mathf.Abs(y),1f);
            float split = Mathf.Sqrt(Mathf.Max(0f,1f-b*b));
            float area = b * Mathf.Min(a,split);
            if (a > split) area += Arc(a)-Arc(split);
            return sign * area;
        }

        // Fraction of the circular emitter's projected area covered by a
        // rectangle. Exact 2D intersection; no rays, steps or smoothing.
        internal static float DiskRectangle(float x0,float y0,
                                            float x1,float y1)
        {
            if (x0 >= x1 || y0 >= y1) return 0f;
            float area = Primitive(x1,y1)-Primitive(x0,y1)-
                         Primitive(x1,y0)+Primitive(x0,y0);
            return Mathf.Clamp01(area/Mathf.PI);
        }

        private static float Component(Vector3 v,int i) =>
            i == 0 ? v.x : i == 1 ? v.y : v.z;

        // Projects an approximate circular source cross-section onto the
        // physical wall plane; handles arbitrary box rotation and scale.
        // Assumes an axis-aligned disk in the collider's two in-plane axes.
        internal static bool BoxCoverage(BoxCollider box,Vector3 listener,
                                         Vector3 source,float radius,
                                         out float covered)
        {
            covered=0f;
            if (box == null || !box.enabled || !box.gameObject.activeInHierarchy ||
                radius <= 0f) return false;
            Vector3 scale = box.transform.lossyScale;
            Vector3 size = box.size;
            float sx = Mathf.Abs(size.x*scale.x);
            float sy = Mathf.Abs(size.y*scale.y);
            float sz = Mathf.Abs(size.z*scale.z);
            // A thin post/bar has TWO narrow dimensions. Taking the
            // smallest dimension blindly selects the wrong side face at
            // many Valheim iron grate and wooden fence angles. Choose the
            // box face with the greatest projected area toward the listener.
            // For broad wall panels this naturally selects the large face.
            Vector3 delta = source-listener;
            Vector3 facing = delta.sqrMagnitude > 1e-8f
                ? delta.normalized : Vector3.forward;
            Vector3 nx = box.transform.TransformDirection(Vector3.right);
            Vector3 ny = box.transform.TransformDirection(Vector3.up);
            Vector3 nz = box.transform.TransformDirection(Vector3.forward);
            float px = Mathf.Abs(Vector3.Dot(facing,nx)) * sy * sz;
            float py = Mathf.Abs(Vector3.Dot(facing,ny)) * sx * sz;
            float pz = Mathf.Abs(Vector3.Dot(facing,nz)) * sx * sy;
            int normal = px >= py && px >= pz ? 0 : py >= pz ? 1 : 2;
            int u=(normal+1)%3,v=(normal+2)%3;
            Vector3 worldNormal = normal == 0 ? nx : normal == 1 ? ny : nz;
            float denom=Vector3.Dot(delta,worldNormal);
            if (Mathf.Abs(denom)<1e-5f) return false;
            Vector3 center=box.transform.TransformPoint(box.center);
            float t=Vector3.Dot(center-listener,worldNormal)/denom;
            if(t<=0f || t>=1f) return true; // no wall between listener and source
            Vector3 hit=box.transform.InverseTransformPoint(listener+delta*t)-box.center;
            float ru=radius*t/Mathf.Max(1e-5f,Mathf.Abs(Component(scale,u)));
            float rv=radius*t/Mathf.Max(1e-5f,Mathf.Abs(Component(scale,v)));
            if(ru<=1e-5f || rv<=1e-5f) return false;
            float hu=Component(size,u)*.5f, hv=Component(size,v)*.5f;
            covered=DiskRectangle((-hu-Component(hit,u))/ru,
                                  (-hv-Component(hit,v))/rv,
                                  ( hu-Component(hit,u))/ru,
                                  ( hv-Component(hit,v))/rv);
            return true;
        }

        internal static void Bind(AcousticFilter f, BoxCollider wall,
                                  Solids.Info solid, Vector3 source)
        {
            if (f == null || wall == null || solid == null) return;
            f.AnalyticWall=wall;
            f.AnalyticSource=source;
            f.AnalyticWorldGen=Occlusion.WorldGen;
            // Cached solid properties came directly from the exact Valheim
            // object prefab/material. Only geometry is dynamic.
            f.AnalyticLow=solid.First*solid.First;
            f.AnalyticMid=SpectralPaths.HighEnergy(solid.First,solid.Cut);
        }

        internal static bool Evaluate(AcousticFilter f,Vector3 listener,
                                      Vector3 source,
                                      out float gain,out float cutoff,
                                      out float covered)
        {
            gain=1f; cutoff=22000f; covered=0f;
            if(f==null || f.AnalyticWall==null) return false;
            // A moving emitter must not drop back to the discrete model
            // simply because it crossed a distance threshold. Geometry is
            // recomputed from the current source/listener positions.
            if(Occlusion.ChangedNear(f.AnalyticWorldGen,listener,source,
                                     Corners.Reach))
            {
                f.AnalyticWall=null;
                return false;
            }
            SourceVolume.Projection p=SourceVolume.Project(f,listener,source);
            if(!BoxCoverage(f.AnalyticWall,listener,p.Center,p.Radius,
                            out covered)) return false;
            float low=1f-covered*(1f-f.AnalyticLow);
            float mid=1f-covered*(1f-f.AnalyticMid);
            SpectralPaths.Decode(low,mid,out gain,out cutoff);
            return true;
        }

        // Existing Directions.Step already gets the listener and loop
        // positions each audio update. No new scanning/coroutine/raycasts.
        internal static void Refresh(AcousticFilter f,Vector3 listener,
                                     Vector3 source)
        {
            if(!f.AnalyticActive || f.AnalyticWall==null ||
               !f.Active || !f.Placed || f.Source==null ||
               !f.Source.isPlaying || !Plugin.Enabled.Value ||
               !Plugin.Occlusion.Value) return;
            if(!Evaluate(f,listener,source,out float gain,
                         out float cutoff,out float covered))
            {
                f.AnalyticActive=false;
                return;
            }
            f.LastCoverage=covered;
            f.LastOcc=covered;
            if (f.AnalyticIndirectGain > 0f && covered > 0f)
            {
                float path = f.AnalyticIndirectGain * Mathf.Sqrt(covered);
                SpectralPaths.Combine(gain, cutoff,
                    path, f.AnalyticIndirectCutoff,
                    out gain, out cutoff);
            }
            Underwater.Apply(ref gain,ref cutoff,f.UnderAmount);
            if(Mathf.Abs(f.TargetGain-gain)>.0001f ||
               Mathf.Abs(f.TargetCutoff-cutoff)>1f)
            {
                f.FastGeometryResponse=true;
                f.Set(gain,cutoff,false);
            }
        }
    }
}
