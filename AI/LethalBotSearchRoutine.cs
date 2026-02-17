using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LethalBots.Constants;
using UnityEngine;
using UnityEngine.AI;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.AI
{
    public class LethalBotSearchRoutine(LethalBotAI ai)
    {
        private LethalBotAI ai = ai;
        private List<GameObject?> unsearchedNodes { get; set; } = new List<GameObject?>();
        private Coroutine? searchCoroutine;
        private Coroutine? selectTargetCoroutine;
        private Coroutine? visitNodesCoroutine;
        private bool isWaitingTarget;
        private bool isSelectingTarget;
        private GameObject? currentTarget;
        private GameObject? nextTarget;
        private int targetCheckIndex;
        private int visitCheckIndex;
        private bool unsearchedNodesHasNullRef;

        public bool searchInProgress;
        public bool searchCenterFollowsAI = true;
        public Vector3 searchCenter;
        public float searchRadius = float.MaxValue;
        public float proximityThreshold = 5f;
        public float minimumPathDistance = 0f;
        public float nodeChance = 0.65f;
        public bool visitInProgress;


        public void StartSearch()
        {
            if (!searchInProgress)
            {
                searchCoroutine = ai.StartCoroutine(SearchCoroutine());
            }
            StartVisit();
        }

        public void StartVisit()
        {
            if (proximityThreshold > 0f && !visitInProgress)
            {
                visitNodesCoroutine = ai.StartCoroutine(VisitCoroutine());
            }
        }

        public void StopSearch(bool clearVisited = false, bool clearTarget = true)
        {
            if (searchCoroutine != null) ai.StopCoroutine(searchCoroutine);
            if (selectTargetCoroutine != null) ai.StopCoroutine(selectTargetCoroutine);
            if (visitNodesCoroutine != null) ai.StopCoroutine(visitNodesCoroutine);
            searchInProgress = false;
            isWaitingTarget = false;
            isSelectingTarget = false;
            targetCheckIndex = 0;
            // TODO: if clearTarget is false, nextTarget will not be used when search is resumed, for now clearTarget isn't used anywhere
            if (clearTarget)
            {
                currentTarget = null;
                nextTarget = null;
            }
            visitInProgress = false;
            if (clearVisited)
            {
                unsearchedNodes.Clear();
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3? GetTargetPosition(bool current = true)
        {
            return current ? currentTarget?.transform.position : nextTarget?.transform.position;
        }

        private IEnumerator SearchCoroutine()
        {
            searchInProgress = true;
            yield return null;
            while (searchCoroutine != null && ai.IsOwner && ai.NpcController != null)
            {
                if (nextTarget == null || !ai.IsValidPathToTarget(nextTarget.transform.position))
                {
                    nextTarget = null;
                    isWaitingTarget = true;
                    yield return UpdateSearchCenter();
                    if (!isSelectingTarget)
                    {
                        selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
                    }
                    while (isSelectingTarget)
                    {
                        yield return null;
                    }
                }
                else
                {
                    currentTarget = nextTarget;
                    nextTarget = null;
                }
                if (currentTarget == null)
                {
                    // TODO: Remove only reachable nodes so we can still remember the already visited nodes still reachable from other entrances
                    // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: populated in if (currentTarget == null)");
                    PopulateNodes();
                    continue;
                }
                isWaitingTarget = false;
                yield return UpdateSearchCenter();
                selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
                float proximitySqr = proximityThreshold * proximityThreshold;
                float nextValidCheck = Time.timeSinceLevelLoad + 0.2f;
                float lowestPathDistance = float.MaxValue;
                int stuckChecks = 0;
                while (searchCoroutine != null && ai.IsOwner && ai.NpcController != null && currentTarget != null)
                {
                    if (ai.agent.isOnNavMesh)
                    {
                        if (ai.agent.remainingDistance == 0 && (ai.transform.position - currentTarget.transform.position).sqrMagnitude < proximitySqr)
                        {
                            int currentTargetIndex = unsearchedNodes.FindIndex(x => x == currentTarget);
                            if (currentTargetIndex != -1)
                            {
                                unsearchedNodes[currentTargetIndex] = null;
                                unsearchedNodesHasNullRef = true;
                            }
                            break;
                        }
                        if (Time.timeSinceLevelLoad > nextValidCheck)
                        {
                            if (ai.IsValidPathToTarget(currentTarget.transform.position, true) && ai.pathDistance < lowestPathDistance)
                            {
                                stuckChecks = 0;
                                lowestPathDistance = ai.pathDistance - 0.5f;
                            }
                            else
                            {
                                stuckChecks++;
                            }
                            if (stuckChecks >= 20)
                            {
                                currentTarget = null;
                                if (searchCenterFollowsAI)
                                {
                                    nextTarget = null;
                                }
                                break;
                            }
                            nextValidCheck += 0.2f;
                        }
                        if (nextTarget == null && !isSelectingTarget)
                        {
                            // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: populated in if (nextTarget == null && !isSelectingTarget)");
                            PopulateNodes();
                            selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
                        }
                    }
                    yield return null;
                }
            }
            StopSearch();
        }

        private void PopulateNodes()
        {
            // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: populating nodes, nodes count: {unsearchedNodes.Count}");
            unsearchedNodes = ai.allAINodes.ToList();
            if (nodeChance < 1f || minimumPathDistance > 0f)
            {
                // The currentSearch.NodeChance random checks favours the deepest nodes in the list, the shuffle below improves the random node selection
                // TODO: Make unsearchedNodes a List with (GameObject node, float weight) tuple, the weight in the tuples are based on how far nodes are from each other, cache node weights in LethalBotManager so weights are processed only once, this should balance random node selection to select a node based on the density of nodes in an area
                int n = unsearchedNodes.Count;
                while (n > 1)
                {
                    n--;
                    int k = UnityEngine.Random.Range(0,n+1);
                    (unsearchedNodes[k], unsearchedNodes[n]) = (unsearchedNodes[n], unsearchedNodes[k]);
                }
            }
            // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: populated nodes, nodes count: {unsearchedNodes.Count}");
            targetCheckIndex = 0;
            if (isSelectingTarget)
            {
                ai.StopCoroutine(selectTargetCoroutine);
                selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
            }
            visitCheckIndex = unsearchedNodes.Count - 1;
            unsearchedNodesHasNullRef = false;
        }

        private IEnumerator UpdateSearchCenter()
        {
            // If our next searchCenter use is guaranteed to be on navmesh or reachable, we can still use it to check for reachability even if we are outside navmesh
            if (currentTarget == null || !searchCenterFollowsAI)
            {
                while (!ai.agent.isOnNavMesh)
                {
                    yield return null;
                }
            }
            Vector3 newSearchCenter = currentTarget?.transform.position ?? ai.transform.position;
            // The IsValidPathToTarget check here is because if we can't reach searchCenter, we can't reach the nodes reachable from it, so searchCenter becomes our current position
            if (searchCenter != newSearchCenter && (searchCenterFollowsAI || !ai.IsValidPathToTarget(searchCenter)))
            {
                searchCenter = newSearchCenter;
                // We stop selectTargetCoroutine so we can use the new searchCenter in the next selectTargetCoroutine
                if (isSelectingTarget)
                {
                    ai.StopCoroutine(selectTargetCoroutine);
                    isSelectingTarget = false;
                }
            }
        }

        private IEnumerator SelectTargetCoroutine()
        {
            isSelectingTarget = true;
            yield return null;
            while (selectTargetCoroutine != null)
            {
                float closestDist = searchRadius;
                float closestDistSqr = searchRadius * searchRadius;
                GameObject? selectedNode = null;
                int iterAmount = 0;
                for (targetCheckIndex = unsearchedNodes.Count - 1;targetCheckIndex >= 0;targetCheckIndex--)
                {
                    if (iterAmount >= 10)
                    {
                        iterAmount = 0;
                        yield return null;
                    }
                    GameObject? node = unsearchedNodes[targetCheckIndex];
                    if (node == null)
                    {
                        unsearchedNodesHasNullRef = true;
                        continue;
                    }
                    if (node == currentTarget)
                    {
                        continue;
                    }
                    if (selectedNode == null || nodeChance >= 1f || UnityEngine.Random.value < nodeChance)
                    {
                        // Checks above are cheap so we increase iterAmount here
                        iterAmount++;
                        float sqrDistToNode = (searchCenter - node.transform.position).sqrMagnitude;
                        if (sqrDistToNode < closestDistSqr && LethalBotAI.IsValidPathToTarget(searchCenter, node.transform.position, ai.agent.areaMask, ref ai.path1, true, out float pathDistance) && pathDistance < closestDist) // || !ai.State.IsNodeValidForTarget(node)), uncomment and add null checks when it gets used
                        {
                            // TODO: Dynamic minimumPathDistance increase when client is at low framerate
                            if (pathDistance >= minimumPathDistance)
                            {
                                // Node is in proximityThreshold range and is visible to searchCenter
                                if (pathDistance <= proximityThreshold && NavMesh.Raycast(searchCenter, node.transform.position, out _, ai.agent.areaMask))
                                {
                                    continue;
                                }
                                selectedNode = node;
                                closestDist = pathDistance;
                                closestDistSqr = pathDistance * pathDistance;
                            }
                        }
                    }
                }
                // We try again because selectedNode has been visited while nodes were being processed
                if (selectedNode != null && !unsearchedNodes.Contains(selectedNode))
                {
                    continue;
                }
                // We don't have a target yet
                if (isWaitingTarget)
                {
                    currentTarget = selectedNode;
                }
                // This will be our target after we reach currentTarget
                else
                {
                    nextTarget = selectedNode;
                }
                break;
            }
            isSelectingTarget = false;
        }

        // This Coroutine is used to remove nodes we are passing by from unsearchedNodes
        private IEnumerator VisitCoroutine()
        {
            visitInProgress = true;
            yield return null;
            Vector3 lastVisitCheckPos = ai.transform.position;
            float proximitySqr = proximityThreshold * proximityThreshold;
            float checkDist = proximityThreshold / 2f;
            while (visitNodesCoroutine != null)
            {
                // We check nodes with the amount based on how much we moved and the amount of unsearchedNodes we have
                int checkAmount = ai.agent.isOnNavMesh ? (int)Mathf.Ceil(Mathf.Lerp(0f, unsearchedNodes.Count, Mathf.Min((ai.transform.position - lastVisitCheckPos).magnitude / checkDist, 1.0f))) : 0;
                // if (checkAmount > 0)
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: iterated through {checkAmount} nodes in a single frame");
                for (int i = 0;i < checkAmount;i++)
                {
                    GameObject? node = unsearchedNodes[visitCheckIndex];
                    // TODO: Distance checks can be improved further with a grid cell dictionary system where cells have the size of proximityThreshold, we only check nodes in the neighbouring cells
                    // TODO: Having a high proximityThreshold means that bots will not search the end or corner of corridors/rooms, they can miss loot inside drawers or loot behind interior objects, logic should check if the node has no loot next or visible to it before marking it as visited
                    if (node != null)
                    {
                        if ((ai.transform.position - node.transform.position).sqrMagnitude < proximitySqr && Vector3.Angle(ai.eye.forward, node.transform.position - ai.eye.transform.position) < Const.LETHAL_BOT_FOV && Physics.Linecast(ai.eye.transform.position, node.transform.position + Vector3.up * 0.2f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            unsearchedNodes[visitCheckIndex] = null;
                            unsearchedNodesHasNullRef = true;
                            if (nextTarget == node)
                            {
                                nextTarget = null;
                                selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
                            }
                        }
                    }
                    else
                    {
                        unsearchedNodesHasNullRef = true;
                    }
                    visitCheckIndex--;
                    if (visitCheckIndex < 0)
                    {
                        visitCheckIndex = unsearchedNodes.Count - 1;
                    }
                }
                TrimVisitedNodes();
                if (unsearchedNodes.Count == 0)
                {
                    // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: populated in if (unsearchedNodes.Count == 0)");
                    PopulateNodes();
                }
                lastVisitCheckPos = ai.transform.position;
                yield return null;
            }
            visitInProgress = false;
        }

        private void TrimVisitedNodes()
        {
            if (unsearchedNodesHasNullRef)
            {
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: trimming nodes, nodes count: {unsearchedNodes.Count}");
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: check indexes before: T: {targetCheckIndex}, V: {visitCheckIndex}");
                // We move iterator indexes backwards, this way we can continue iterating through nodes without skipping them or iterating through nodes twice before a full cycle
                for (int i = Mathf.Max(visitCheckIndex, targetCheckIndex) - 1;i >= 0;i--)
                {
                    if (unsearchedNodes[i] == null)
                    {
                        if (targetCheckIndex > i)
                        {
                            targetCheckIndex--;
                        }
                        if (visitCheckIndex > i)
                        {
                            visitCheckIndex--;
                        }
                    }
                }
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: check indexes now: T: {targetCheckIndex}, V: {visitCheckIndex}");
                // int remAmount =
                unsearchedNodes.RemoveAll(x => x == null);
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: nodes removed: {remAmount}");
                // Plugin.LogDebug($"Bot {ai.NpcController.Npc.playerUsername}: nodes count: {unsearchedNodes.Count}");
                unsearchedNodes.TrimExcess();
                unsearchedNodesHasNullRef = false;
            }
        }
    }
}
