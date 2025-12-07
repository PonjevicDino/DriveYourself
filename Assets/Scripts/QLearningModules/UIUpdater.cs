using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

namespace Assets.Scripts.QLearningModules
{
    public class UIUpdater : MonoBehaviour
    {
        public TMP_Text speedText;
        public TMP_Text segmentText;
        public TMP_Text rewardText;
        public TMP_Text actionText;
        public TMP_Text speedBracketText;
        public TMP_Text qTableInfoText;

        private float currentSpeed = 0f;
        private int currentSegment = 0;
        private float currentReward = 0f;
        private string currentAction = "";
        private string qTableInfo = "";
        private int currentSpeedBracket = 0;

        public void UpdateSpeed(float speed)
        {
            currentSpeed = speed;
        }

        public void UpdateSegment(int segmentIndex)
        {
            currentSegment = segmentIndex;
        }

        public void UpdateReward(float reward)
        {
            currentReward = reward;
        }        
        public void UpdateSpeedBracket(int speedBracket)
        {
            currentSpeedBracket = speedBracket;
        }

        public void UpdateAction(int deltaSpeed)
        {
            currentAction = deltaSpeed switch
            {
                -1 => "Decrease Speed",
                0 => "Hold Speed",
                1 => "Increase Speed",
                _ => "Unknown"
            };
        }
        public void UpdateQTableInfo(float[,] Q, int stateIndex)
        {
            if (Q == null || Q.GetLength(0) <= stateIndex) return;

            float bestQ = float.MinValue;
            int bestAction = 0;
            string qValues = "";

            for (int i = 0; i < Q.GetLength(1); i++)
            {
                float val = Q[stateIndex, i];
                if (val > bestQ)
                {
                    bestQ = val;
                    bestAction = i;
                }

                int deltaSpeed = i - 1; // Since actions are -1, 0, +1
                string label = deltaSpeed switch
                {
                    -1 => "Dec",
                    0 => "Hold",
                    1 => "Inc",
                    _ => "?"
                };

                qValues += $"[{label}]: {val}\n";
            }

            qTableInfo = $"State {stateIndex}:\nBest: Action {bestAction} (Q={bestQ})\n\n{qValues}";

        }
        void Update()
        {
            if (speedText != null)
                speedText.text = $"Speed: {currentSpeed} km/h";

            if (segmentText != null)
                segmentText.text = $"Segment: {currentSegment}";

            if (rewardText != null)
                rewardText.text = $"Reward: {currentReward}";

            if (actionText != null)
                actionText.text = $"Action: {currentAction}";            
            if (speedBracketText != null)
                speedBracketText.text = $"Speed Bracket: {currentSpeedBracket}";
            if (qTableInfoText != null)
                qTableInfoText.text = qTableInfo;
        }
    }
}
