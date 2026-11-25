using UnityEngine;

namespace WiresAcoustics
{
    // Compact Valheim-specific propagation for a sound behind ONE known box
    // surface. The authored material remains a fixed prefab property.
    // Dynamic geometry is an EXACT disk/rectangle projected overlap and a
    // published SINGLE knife-edge Fresnel diffraction approximation.
    // No additional physics ray jobs or scene mesh traversal.
    internal static class AnalyticOcclusion
    {
        private static float Arc(float x)
        {
            x = Mathf.Clamp01(x);
            return .5f * (x * Mathf.Sqrt(Mathf.Max(0f, 1f - x*x)) +
                          Mathf.Asin(x));
        }

        private static float Primitive(float x, float y)
        {
            if (x == 0f || y == 0f) return 0f;
            float sign = (x < 0f) == (y < 0f) ? 1f : -1f;
            float a = Mathf.Min(Mathf.Abs(x), 1f);
            float b = Mathf.Min(Mathf.Abs(y), 1f);
            float split = Mathf.Sqrt(Mathf.Max(0f, 1f-b*b));
            float area = b * Mathf.Min(a, split);
            if (a > split) area += Arc(a) - Arc(split);
            return sign * area;
        }

        // Exact overlap area of the unit source disk and projected rectangle.
        internal static float DiskRectangle(float x0, float y0,
                                            float x1, float y1)
        {
            if (x0 >= x1 || y0 >= y1) return 0f;
            float area = Primitive(x1,y1) - Primitive(x0,y1) -
                         Primitive(x1,y0) + Primitive(x0,y0);
            return Mathf.Clamp01(area / Mathf.PI);
        }

        private static float Component(Vector3 v, int i) =>
            i == 0 ? v.x : i == 1 ? v.y : v.z;

        internal static bool BoxCoverage(BoxCollider box, Vector3 listener,
                                         Vector3 source, float radius,
                                         out float covered) =>
            BoxCoverage(box, listener, source, radius, out covered,
                        out _, out _, out _, out _);

        // Signed edge clearance is in METERS, positive outside, negative
        // behind the nearest edge. The chosen face maximizes projected area.
        // Its selection can still change for a nearly cubic collider; this
        // is a documented approximation, not a source of arbitrary gain.
        internal static bool BoxCoverage(BoxCollider box, Vector3 listener,
                                         Vector3 source, float radius,
                                         out float covered, out float clearance,
                                         out float d1, out float d2,
                                         out Vector3 edge)
        {
            covered = 0f; clearance = 0f; d1 = d2 = 0f;
            edge = source;
            if (box == null || !box.enabled ||
                !box.gameObject.activeInHierarchy || radius <= 0f)
                return false;
            Vector3 scale = box.transform.lossyScale;
            Vector3 size = box.size;
            float sx = Mathf.Abs(size.x * scale.x);
            float sy = Mathf.Abs(size.y * scale.y);
            float sz = Mathf.Abs(size.z * scale.z);
            Vector3 delta = source - listener;
            float length = delta.magnitude;
            if (length <= 1e-5f) return false;
            Vector3 facing = delta / length;
            Vector3 nx = box.transform.TransformDirection(Vector3.right);
            Vector3 ny = box.transform.TransformDirection(Vector3.up);
            Vector3 nz = box.transform.TransformDirection(Vector3.forward);
            float px = Mathf.Abs(Vector3.Dot(facing, nx)) * sy * sz;
            float py = Mathf.Abs(Vector3.Dot(facing, ny)) * sx * sz;
            float pz = Mathf.Abs(Vector3.Dot(facing, nz)) * sx * sy;
            int normal = px >= py && px >= pz ? 0 : py >= pz ? 1 : 2;
            int u = (normal + 1) % 3, v = (normal + 2) % 3;
            Vector3 worldNormal = normal == 0 ? nx : normal == 1 ? ny : nz;
            float denom = Vector3.Dot(delta, worldNormal);
            if (Mathf.Abs(denom) < 1e-5f) return false;
            Vector3 center = box.transform.TransformPoint(box.center);
            float t = Vector3.Dot(center-listener, worldNormal) / denom;
            if (t <= 0f || t >= 1f) return true; // no intervening plane

            Vector3 hit = box.transform.InverseTransformPoint(listener + delta*t)
                        - box.center;
            float su = Mathf.Abs(Component(scale,u));
            float sv = Mathf.Abs(Component(scale,v));
            float projectedRadius = radius * t;
            float ru = projectedRadius / Mathf.Max(1e-5f,su);
            float rv = projectedRadius / Mathf.Max(1e-5f,sv);
            if (ru <= 1e-5f || rv <= 1e-5f) return false;
            float hu = Component(size,u)*.5f, hv = Component(size,v)*.5f;
            float cu = Component(hit,u), cv = Component(hit,v);
            covered = DiskRectangle((-hu-cu)/ru,(-hv-cv)/rv,
                                    ( hu-cu)/ru,( hv-cv)/rv);

            float eu = (hu - Mathf.Abs(cu)) * su;
            float ev = (hv - Mathf.Abs(cv)) * sv;
            if (eu >= 0f && ev >= 0f)
                clearance = -Mathf.Min(eu,ev);
            else
            {
                float ou = Mathf.Max(0f,-eu), ov = Mathf.Max(0f,-ev);
                clearance = Mathf.Sqrt(ou*ou + ov*ov);
            }
            // Virtual arrival direction comes from the nearest *real*
            // physical box edge rather than inventing a source point.
            Vector3 edgeLocal = hit;
            if (eu <= ev) edgeLocal[u] = Mathf.Sign(cu == 0f ? 1f : cu)*hu;
            else edgeLocal[v] = Mathf.Sign(cv == 0f ? 1f : cv)*hv;
            edgeLocal[u] = Mathf.Clamp(edgeLocal[u],-hu,hu);
            edgeLocal[v] = Mathf.Clamp(edgeLocal[v],-hv,hv);
            edge = box.transform.TransformPoint(box.center+edgeLocal);
            d1 = t * length;
            d2 = (1f - t) * length;
            return true;
        }

        internal static void Bind(AcousticFilter f, BoxCollider wall,
                                  Solids.Info solid, Vector3 source)
        {
            if (f == null || wall == null || solid == null) return;
            f.AnalyticWall = wall;
            f.AnalyticSource = source;
            f.AnalyticWorldGen = Occlusion.WorldGen;
            f.AnalyticPoseValid = false; // a new wall/material must be evaluated
            f.AnalyticLow = solid.First * solid.First;
            f.AnalyticMid = SpectralPaths.HighEnergy(solid.First,solid.Cut);
        }

        internal static bool Evaluate(AcousticFilter f, Vector3 listener,
                                      Vector3 source, out float gain,
                                      out float cutoff, out float covered)
        {
            gain = 1f; cutoff = 22000f; covered = 0f;
            if (f == null || f.AnalyticWall == null) return false;
            if (Occlusion.ChangedNear(f.AnalyticWorldGen, listener, source,
                                      Corners.Reach))
            {
                f.AnalyticWall = null;
                return false;
            }
            SourceVolume.Projection p = SourceVolume.Project(f, listener, source);
            if (!BoxCoverage(f.AnalyticWall,listener,p.Center,p.Radius,
                             out covered, out float clearance,
                             out float d1, out float d2,
                             out Vector3 edge)) return false;

            // An emitter barely crossing the geometric wall shadow still
            // reaches the listener by knife-edge diffraction. Use the
            // documented Fresnel pressure-field solution with the authored
            // material transmission, not a binary attenuate-or-bypass.
            float lowThrough = Mathf.Sqrt(Mathf.Clamp01(f.AnalyticLow));
            float midThrough = Mathf.Sqrt(Mathf.Clamp01(f.AnalyticMid));
            float lowAtEdge = covered > 0f
                ? FresnelEdge.Intensity(clearance,d1,d2,125f,lowThrough) : 1f;
            float midAtEdge = covered > 0f
                ? FresnelEdge.Intensity(clearance,d1,d2,2000f,midThrough) : 1f;
            f.AnalyticEdgePoint = edge;
            float directEnergy = 1f - covered*(1f-lowAtEdge);
            f.AnalyticBendShare = covered *
                Mathf.Max(0f, lowAtEdge - f.AnalyticLow) /
                Mathf.Max(1e-6f, directEnergy);

            // Each emitting region contributes ENERGY. The geometrically
            // uncovered area arrives directly; the covered area is bent
            // around the nearest screen edge or transmitted through it.
            float low = 1f - covered*(1f-lowAtEdge);
            float mid = 1f - covered*(1f-midAtEdge);
            float strength = AcousticMath.OcclusionStrength;
            f.AnalyticBendShare *= Mathf.Clamp01(strength);
            if (strength <= 0f) return true;
            if (Mathf.Abs(strength-1f) > 1e-5f)
            {
                low = Mathf.Pow(Mathf.Max(1e-8f,low),strength);
                mid = Mathf.Pow(Mathf.Max(1e-8f,mid),strength);
            }
            SpectralPaths.Decode(low,Mathf.Min(low,mid),out gain,out cutoff);
            return true;
        }

        // Run from WA's existing tracked-source update. The cached surface
        // and material are constant; only relative geometry is recalculated.
        internal static void Refresh(AcousticFilter f,Vector3 listener,
                                     Vector3 source)
        {
            if(!f.AnalyticActive || f.AnalyticWall == null ||
               !f.Active || !f.Placed || f.Source == null ||
               !f.Source.isPlaying || !Plugin.Enabled.Value ||
               !Plugin.Occlusion.Value) return;
            float strength = AcousticMath.OcclusionStrength;
            if (f.AnalyticPoseValid &&
                f.AnalyticWorldGen == Occlusion.WorldGen &&
                (listener-f.AnalyticLastEar).sqrMagnitude < 1e-10f &&
                (source-f.AnalyticLastSource).sqrMagnitude < 1e-10f &&
                Mathf.Abs(strength-f.AnalyticLastStrength) < 1e-5f &&
                Mathf.Abs(f.UnderAmount-f.AnalyticLastWater) < 1e-5f &&
                Mathf.Abs(f.AnalyticIndirectGain-f.AnalyticLastIndirect) < 1e-5f)
                return; // stationary source/listener: ZERO expensive geometry math
            if(!Evaluate(f,listener,source,out float gain,
                         out float cutoff,out float covered))
            {
                f.AnalyticActive = false;
                f.AnalyticPoseValid = false;
                return;
            }
            f.AnalyticPoseValid = true;
            f.AnalyticLastEar = listener;
            f.AnalyticLastSource = source;
            f.AnalyticLastStrength = strength;
            f.AnalyticLastWater = f.UnderAmount;
            f.AnalyticLastIndirect = f.AnalyticIndirectGain;
            f.AnalyticWorldGen = Occlusion.WorldGen;
            f.LastCoverage = covered;
            f.LastOcc = covered;
            if (!f.AnalyticHasPortal)
            {
                float bend = Mathf.Clamp01(f.AnalyticBendShare);
                f.Routed = bend > .001f;
                f.RoutePoint = Vector3.Lerp(source,f.AnalyticEdgePoint,bend);
            }
            if (f.AnalyticIndirectGain > 0f && covered > 0f)
            {
                float path = f.AnalyticIndirectGain * Mathf.Sqrt(covered);
                SpectralPaths.Combine(gain,cutoff,
                    path,f.AnalyticIndirectCutoff,out gain,out cutoff);
            }
            Underwater.Apply(ref gain,ref cutoff,f.UnderAmount);
            if(Mathf.Abs(f.TargetGain-gain) > .0001f ||
               Mathf.Abs(f.TargetCutoff-cutoff) > 1f)
            {
                // Glides, as every change does (doors are rechecked as they swing).
                f.Set(gain,cutoff,false);
            }
        }
    }
}
