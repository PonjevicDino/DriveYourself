using TMPro;
using UnityEngine;

public class GetVehicleSpeed : MonoBehaviour
{
    private TextMeshProUGUI speedTextTMP;
    private RCC_CarControllerV4 carController;

    void Start()
    {
        speedTextTMP = this.GetComponent<TextMeshProUGUI>();
        carController = GameObject.FindFirstObjectByType<RCC_CarControllerV4>();
    }

    void Update()
    {
        speedTextTMP.text = "Speed: " + carController.speed.ToString("000.0") + "km/h";
    }
}
