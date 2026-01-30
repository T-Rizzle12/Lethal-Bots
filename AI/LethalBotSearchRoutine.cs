using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.AI
{
    public class LethalBotSearchRoutine
    {
        public List<GameObject> UnsearchedNodes { get; set; } = [];
        public HashSet<GameObject> PassedByNodes { get; set; } = [];

        public GameObject? currentTargetNode;
        public GameObject? nextTargetNode;

        [Header("State Flags")]
        public bool isWaitingForTarget;
        public bool hasChosenTarget;
        public bool isCalculating;
        
        [Header("Settings")]
        public float searchWidth = 200f;
        public float searchPrecision = 5f;
        public float searchMinDistance = 0f;
        public bool searchCenterFollowsBot = false;
        public float nodeChance = 1f;

        // Runtime state
        public Vector3 searchCenter;
        public Vector3 lastCheckPosition;
        public bool inProgress;
    }
}
