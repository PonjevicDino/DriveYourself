using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Scripts.QLearningModules
{
    public static class DecisionMatrix
    {
        // Action index represents (targetSpeedBin, steerDelta)
        /// <summary>
        /// Computes a unique state index from speed bin and lookahead layouts.
        /// </summary>
        /// <param name="speedIdx">Speed bin index [0..9]</param>
        /// <param name="L0">Layout at t (0=left,1=straight,2=right)</param>
        /// <param name="L1">Layout at t+1</param>
        /// <param name="L2">Layout at t+2</param>
        /// <returns>State index [0..269]</returns>
        public static int GetStateIndex(int speedIdx, int L0, int L1, int L2)
        {
            // ((s * 3 + L0) * 3 + L1) * 3 + L2
            return ((speedIdx * 3 + L0) * 3 + L1) * 3 + L2;
        }
        public static void UnpackSpeedAction(int action, out int deltaSpeed)
        {
            deltaSpeed = action - 1; // 0=decrease, 1=hold, 2=increase => -1, 0, +1
        }

        public static int PackSpeedAction(int deltaSpeed)
        {
            return deltaSpeed + 1; // -1,0,1 => 0,1,2
        }

        /// <summary>
        /// Wraps a segment index around total count N, ensuring looped track.
        /// </summary>
        /// <param name="index">Original segment index</param>
        /// <param name="N">Total number of segments</param>
        /// <returns>Wrapped index [0..N-1]</returns>
        public static int WrapIndex(int index, int N)
        {
            // handle negative or overflow
            int idx = index % N;
            return (idx < 0) ? idx + N : idx;
        }

        /// <summary>
        /// Gets the three wrapped segment indices for lookahead (t, t+1, t+2).
        /// </summary>
        /// <param name="current">Current segment index</param>
        /// <param name="N">Total segments</param>
        /// <param name="t">Output idx at t</param>
        /// <param name="t1">Output idx at t+1</param>
        /// <param name="t2">Output idx at t+2</param>
        public static void GetLookaheadIndices(int current, int N, out int t, out int t1, out int t2)
        {
            t = WrapIndex(current, N);
            t1 = WrapIndex(current + 1, N);
            t2 = WrapIndex(current + 2, N);
        }
    }
}