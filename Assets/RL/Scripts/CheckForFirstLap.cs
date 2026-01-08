using UnityEngine;

public class CheckForFirstLap : MonoBehaviour
{
    private void OnTriggerEnter(Collider collider)
    {
        if (collider.gameObject.name.Contains("M3_E46"))
        {
            DriveYourselfAgent agent = collider.transform.parent.parent.parent.Find("Controller").GetComponent<DriveYourselfAgent>();
            agent.startedFirstLap = true;
        }
    }
}
