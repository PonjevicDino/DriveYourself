using Unity.Collections;
using UnityEngine;

namespace Assets.Scripts.Components
{
    public enum RoadType { Straight, Left, Right }
    /// <summary>
    /// Defines entry/exit points and auto-detects its own road type based
    /// on the orientation from BeginPoint to EndPoint.
    /// Attach to each road prefab with two colored child Transforms: BeginPoint & EndPoint.
    /// </summary>
    [ExecuteInEditMode]
    public class RoadSegment : MonoBehaviour
    {
        [Tooltip("Entry point of this segment (e.g. green cube)")]
        public Transform BeginPoint;
        [Tooltip("Exit point of this segment (e.g. red cube)")]
        public Transform EndPoint;

        [ReadOnly]
        public RoadType Type;

        [SerializeField]
        public bool BeginSegment = false;
        public int RoadSegmentNumber = 0;

        [Header("Detection")]
        [Tooltip("Angle threshold (deg) to consider as straight")]
        [Range(0f, 45f)]
        public float straightThreshold = 10f;

        private void Awake() => DetermineType();
        private void OnValidate() => DetermineType();

        private void DetermineType()
        {
            if (BeginPoint == null || EndPoint == null)
                return;

            // Compute local direction from begin to end
            Vector3 worldDir = (EndPoint.position - BeginPoint.position).normalized;
            Vector3 segmentForward = transform.forward;

            // Signed angle in local XZ plane: positive = left turn, negative = right
            float angle = Vector3.SignedAngle(segmentForward, worldDir, Vector3.up);

            if (Mathf.Abs(angle) <= straightThreshold)
                Type = RoadType.Straight;
            else if (angle > 0)
                Type = RoadType.Right;
            else
                Type = RoadType.Left;

            // Optionally rename the GameObject for clarity in editor
            if (!Application.isPlaying)
                gameObject.name = $"{base.name.Split('_')[0]}_{Type}"
                    + $"_Segment_{RoadSegmentNumber}";
        }
    }
}
