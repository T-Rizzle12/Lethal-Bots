using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.AI
{
    public class LethalBotSearchRoutine
    {
        public List<GameObject> unsearchedNodes { get; set; } = [];
        public HashSet<GameObject> passedByNodes { get; set; } = [];
        public GameObject? currentTargetNode;
        public GameObject? nextTargetNode;

        public Vector3 searchCenter;
        public bool searchCenterFollowsAI = false;
        public float searchRadius = float.MaxValue;
        public float proximityThreshold = 5f;
        public float minimumPathDistance = 0f;
        public float nodeChance = 0.65f;
        
        public bool isWaitingForTarget;
        public bool hasChosenTarget;
        public bool isCalculating;
        public Vector3 lastCheckPosition;
        public bool inProgress;


    }
}
