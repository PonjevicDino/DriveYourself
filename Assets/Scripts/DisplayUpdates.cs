using Assets.Scripts.QLearningModules;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts
{
    public class DisplayUpdates : MonoBehaviour
    {
        public Text SpeedText;
        public Text EligibilityText;

        public VehicleController VehicleController;

        private void Awake()
        {
            if (VehicleController == null)
            {
                VehicleController = gameObject.GetComponent<VehicleController>();
            }
        }
        private void Start()
        {
            if (SpeedText != null)
            {
                InitializeTable(SpeedText, new Vector2(0, 1), new UnityEngine.Vector2(10, -10));
            }
            if (EligibilityText != null)
            {
                InitializeTable(EligibilityText, new UnityEngine.Vector2(1, 1), new UnityEngine.Vector2(-10, -10));
            }
        }
        public void InitializeTable(Text t, UnityEngine.Vector2 vec, UnityEngine.Vector2 pos)
        {
            RectTransform rt = t.GetComponent<RectTransform>();

            rt.anchorMin = vec;
            rt.anchorMax = vec;
            rt.pivot = vec;
            rt.anchoredPosition = pos;

        }

        public void UpdateQTableDisplay()
        {
            if (SpeedText == null || VehicleController.carCont == null) return;

            float vehicleSpeed = VehicleController.carCont.speed; // Get actual vehicle speed
            float targetSpeedLabel = VehicleController.targetSpeed; // Show current target speed

            float currentStateLabel = VehicleController.state; //get current tate
            float currentActionLabel = VehicleController.action; //get current action

            string qTableString = $"🚗 Vehicle Speed: {vehicleSpeed:F1} km/h\n";
            qTableString += $"🎯 Target Speed: {targetSpeedLabel:F1} km/h\n";
            qTableString += $" Current state: {currentStateLabel}\n";
            qTableString += $" Current action: {currentActionLabel}\n\n";
            qTableString += $" User reward mode: Q increase, A decrease, W reward, S punishment\n";
            qTableString += $" Special reward mode: R duration of keypress directly into Q-table\n";


            if (VehicleController.rewardMode == VehicleController.RewardMode.SimpleController 
                | VehicleController.rewardMode == VehicleController.RewardMode.SpeedBasedPotential)
                qTableString += $"🎯 Desired Speed: {VehicleController.desiredSpeed:F1} km/h\n\n";

            qTableString += generateTable("Q-Learning Table", VehicleController.qTable);



            SpeedText.text = qTableString;
        }

        public string generateTable(string TableName, float[,] theTable)
        {
            string result = "";
            result += TableName + "\n";
            result += "State (Speed) |  Decrease (0)  |  Maintain (1)  |  Increase (2)\n";
            result += "-------------------------------------------------------------\n";

            for (int s = 0; s < VehicleController.numstates; s++)
            {
                float stateSpeed = s * 10; // Speed associated with this state



                if (stateSpeed == 0f)
                    result += $"   {stateSpeed} km/h                     | ";
                else
                    result += $" {stateSpeed} km/h                     | ";

                for (int a = 0; a < VehicleController.numactions; a++)
                {
                    string addon = $"     {theTable[s, a]:F2}                                     ";

                    result += addon.Substring(0, 15) + "    |";  // Format Q-values // for an overview of the maximum valeus ({maxValues[(s,a)][0]:F2},{maxValues[(s,a)][1]:F2})
                }

                result += "\n";
            }


            return result;

        }

        public void UpdateEligibilityTableDisplay()
        {
            if (EligibilityText == null || VehicleController.carCont == null) return;

            float currentStateLabel = VehicleController.state; //get current tate
            float currentActionLabel = VehicleController.action; //get current action

            string eTableString = $" Current state: {currentStateLabel}\n";
            eTableString += $" Current action: {currentActionLabel}\n\n";

            eTableString += generateTable("EligibilityTable", VehicleController.eTable);

            EligibilityText.text = eTableString;

        }

    }
}
