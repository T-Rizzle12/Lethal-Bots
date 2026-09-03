using LethalBots.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// A struct used by <see cref="LethalBotAI.IsPathDangerousAsync(UnityEngine.AI.NavMeshPath, bool, bool, bool, bool, System.Threading.CancellationToken)"/>
    /// to represent if it found a safe or dangerous path.
    /// </summary>
    public readonly struct SafePathResult
    {
        public bool isDangerous { get; }
        public bool isPathValid { get; }
        public float pathDistance { get; }

        public SafePathResult(bool isDangerous = false, bool isPathValid = false, float pathDistance = 0f)
        {
            this.isDangerous = isDangerous;
            this.isPathValid = isPathValid;
            this.pathDistance = pathDistance;
        }
    }
}
