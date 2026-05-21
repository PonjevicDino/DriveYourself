using Assets.Scripts.Components;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer))]
public class RoadLayout : MonoBehaviour
{
    public List<RoadSegment> roadSegments = new List<RoadSegment>();
    
    [HideInInspector]
    public SplineContainer trackSpline;

    void Awake()
    {
        trackSpline = GetComponent<SplineContainer>();
        for (int child = 0; child < this.transform.childCount; child++)
        {
            roadSegments.Add(this.transform.GetChild(child).GetComponent<RoadSegment>());
        }

        if (roadSegments.Count > 0)
        {
            GenerateSpline();
        }
        else
        {
            Debug.LogWarning("No RoadLayout found during Awake - Spline generation skipped.");
        }
    }

    [ContextMenu("Generate Track Spline")]
    public void GenerateSpline()
    {
        trackSpline = GetComponent<SplineContainer>();
        Spline spline = trackSpline.Spline;
        spline.Clear();

        Matrix4x4 worldToLocal = trackSpline.transform.worldToLocalMatrix;

        for (int i = 0; i < roadSegments.Count; i++)
        {
            var seg_prev = roadSegments[(i - 1 + roadSegments.Count) % roadSegments.Count];
            var seg_curr = roadSegments[i];
            var seg_next = roadSegments[(i + 1) % roadSegments.Count];

            if (seg_prev.EndPoint == null || seg_curr.BeginPoint == null || seg_curr.EndPoint == null ||
                seg_next.BeginPoint == null || seg_next.EndPoint == null) continue;

            Vector3 P_entry = seg_prev.EndPoint.position;
            Vector3 P_mid   = seg_curr.BeginPoint.position;
            Vector3 P_exit  = seg_curr.EndPoint.position;

            Vector3 P_next_mid  = seg_next.BeginPoint.position;
            Vector3 P_next_exit = seg_next.EndPoint.position;

            Vector3 V1_curr = (P_mid - P_entry).normalized;
            Vector3 V2_curr = (P_exit - P_mid).normalized;
            Vector3 T1_curr = (V1_curr + V2_curr).normalized;
            Vector3 T2_curr = (2f * Vector3.Dot(T1_curr, V2_curr) * V2_curr - T1_curr).normalized; 
            
            Vector3 V1_next = (P_next_mid - P_exit).normalized;
            Vector3 V2_next = (P_next_exit - P_next_mid).normalized;
            Vector3 T1_next = (V1_next + V2_next).normalized;
            Vector3 T0_next = (2f * Vector3.Dot(T1_next, V1_next) * V1_next - T1_next).normalized;
            
            Vector3 forward_mid = T1_curr;
            Vector3 forward_exit = (T2_curr + T0_next).normalized; 
            
            Vector3 localPos_mid = worldToLocal.MultiplyPoint3x4(P_mid);
            Vector3 localFwd_mid = worldToLocal.MultiplyVector(forward_mid).normalized;
            spline.Add(new BezierKnot(new float3(localPos_mid.x, localPos_mid.y, localPos_mid.z),
                new float3(localFwd_mid.x, localFwd_mid.y, localFwd_mid.z),
                new float3(localFwd_mid.x, localFwd_mid.y, localFwd_mid.z),
                quaternion.identity));
            
            Vector3 localPos_exit = worldToLocal.MultiplyPoint3x4(P_exit);
            Vector3 localFwd_exit = worldToLocal.MultiplyVector(forward_exit).normalized;
            spline.Add(new BezierKnot(new float3(localPos_exit.x, localPos_exit.y, localPos_exit.z),
                new float3(localFwd_exit.x, localFwd_exit.y, localFwd_exit.z),
                new float3(localFwd_exit.x, localFwd_exit.y, localFwd_exit.z),
                quaternion.identity));
        }
        
        if (spline.Count > 1)
        {
            for (int i = 0; i < spline.Count; i++)
            {
                int prevIdx = (i - 1 + spline.Count) % spline.Count;
                int nextIdx = (i + 1) % spline.Count;

                float distToPrev = math.distance(spline[i].Position, spline[prevIdx].Position);
                float distToNext = math.distance(spline[i].Position, spline[nextIdx].Position);

                var knot = spline[i];
                float3 localForward = knot.TangentOut;
                
                knot.TangentIn = -localForward * (distToPrev / 3f);
                knot.TangentOut = localForward * (distToNext / 3f);
                
                spline[i] = knot;
            }
        }

        spline.Closed = true;
        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.Mirrored);
        }
        
        Debug.Log($"Shared Spline generated with {spline.Count} knots.");
    }
}