using UnityEngine;

public class FeedbackProcessingVisualUpdater : MonoBehaviour
{
    void Update()
    {
        this.transform.Rotate(0f, 0f, 1f, Space.Self);
    }
}
