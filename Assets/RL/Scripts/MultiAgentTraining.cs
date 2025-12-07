using UnityEngine;

public class MultiAgentTraining : MonoBehaviour
{
    public int agents = 1;

    void Start()
    {
        Vector3 origPosition = transform.GetChild(0).position;
        Quaternion origRotation = transform.GetChild(0).rotation;

        for (int agent = 1; agent < agents; agent++)
        {
            GameObject.Instantiate(transform.GetChild(0), origPosition, origRotation, transform);
            transform.GetChild(agent).Find("MainCamera").gameObject.SetActive(false);
        }
    }
}
