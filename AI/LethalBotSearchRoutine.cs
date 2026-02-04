using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.AI
{
    public class LethalBotSearchRoutine
    {
        public LethalBotAI ai = null!;
        public List<GameObject?> unsearchedNodes { get; set; } = [];
        private Coroutine? searchCoroutine;
        private Coroutine? selectTargetCoroutine;

        public bool searchCenterFollowsAI = true;
        public Vector3 searchCenter;
        public float searchRadius = float.MaxValue;
        public float proximityThreshold = 5f;
        public float minimumPathDistance = 0f;
        public float nodeChance = 0.65f;

        public bool searchInProgress;
        private bool isWaitingTarget;
        private bool isSelectingTarget;
        private GameObject? currentTarget;
        private GameObject? nextTarget;

        private Coroutine? visitNodesCoroutine;
        public bool visitInProgress;
        private int visitCheckIndex;
        private int nullNodesCount;

        public void StartSearch(bool visitOnly = false)
        {
            // Nodes closer than proximityThreshold are going to be invalidated by VisitedNodesCoroutine anyways so we can ignore them
            minimumPathDistance = Mathf.Max(proximityThreshold, minimumPathDistance);
            if (unsearchedNodes.Count <= 0 || unsearchedNodes.Find(x => x != null) == null)
            {
                ClearSearch();
                PopulateNodes();
            }
            if (!searchInProgress && !visitOnly)
            {
                searchCoroutine = ai.StartCoroutine(SearchCoroutine());
            }
            if (proximityThreshold > 0f && !visitInProgress)
            {
                visitNodesCoroutine = ai.StartCoroutine(VisitCoroutine());
            }
        }

        public void StopSearch(bool clearVisited = false, bool clearTarget = true)
        {
            if (searchCoroutine != null) ai.StopCoroutine(searchCoroutine);
            if (visitNodesCoroutine != null) ai.StopCoroutine(visitNodesCoroutine);
            if (selectTargetCoroutine != null) ai.StopCoroutine(selectTargetCoroutine);
            if (clearVisited)
            {
                ClearSearch();
            }
            if (clearTarget)
            {
                currentTarget = null;
                nextTarget = null;
            }
            searchInProgress = false;
            isWaitingTarget = false;
            isSelectingTarget = false;
            visitInProgress = false;
        }

        public Vector3? GetTargetPosition(bool current = true)
        {
            Vector3? returnValue = current ? currentTarget?.transform.position : nextTarget?.transform.position;
            return returnValue;
        }

        public void ClearSearch()
        {
            unsearchedNodes.Clear();
            nullNodesCount = 0;
        }

        private IEnumerator SearchCoroutine()
        {
            searchInProgress = true;
            yield return null;
            while (searchCoroutine != null && ai.IsOwner && ai.NpcController != null)
            {
                if (nextTarget == null || !LethalBotAI.IsValidPathToTarget(ai.transform.position, nextTarget.transform.position, ai.agent.areaMask, ref ai.path1, false, out float _))
                {
                    currentTarget = null;
                    nextTarget = null;
                    isWaitingTarget = true;
                    yield return UpdateSearchCenter();
                    ChooseNextTarget();
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
                    // TODO: Remove only the reachable nodes so we can still remember the already visited nodes from other entrances
                    ClearSearch();
                    PopulateNodes();
                    yield return null;
                    continue;
                }
                isWaitingTarget = false;
                yield return UpdateSearchCenter();
                ChooseNextTarget();
                float proximitySqr = proximityThreshold * proximityThreshold;
                float nextValidCheck = Time.timeSinceLevelLoad + 0.2f;
                float lowestPathDistance = float.MaxValue;
                int stuckChecks = 0;
                // NEEDTOVALIDATE: It is possible to get stuck in interior an navmesh that includes a node under the floor (not sure if that was the actual reason the bot was stuck), saw 4 different bots trying to go for the same position, only happens in some modded interiors though (Scarlet Devil Mansion), for now we are ignoring the .y element when checking distance to destination
                while (searchCoroutine != null && ai.IsOwner && ai.NpcController != null && currentTarget != null)
                {
                    if (ai.agent.velocity.sqrMagnitude < 0.002f)
                    {
                        Vector2 tempPos = new(ai.transform.position.x - currentTarget.transform.position.x,
                                              ai.transform.position.z - currentTarget.transform.position.z);
                        if (tempPos.sqrMagnitude < proximitySqr)
                        {
                            break;
                        }
                    }
                    if (ai.agent.isOnNavMesh && Time.timeSinceLevelLoad > nextValidCheck)
                    {
                        if (LethalBotAI.IsValidPathToTarget(ai.transform.position, currentTarget.transform.position, ai.agent.areaMask, ref ai.path1, true, out float pathDistance) && pathDistance < lowestPathDistance)
                        {
                            stuckChecks = 0;
                            lowestPathDistance = pathDistance - 0.5f;
                        }
                        else
                        {
                            stuckChecks++;
                        }
                        if (stuckChecks >= 20)
                        {
                            if (searchCenterFollowsAI)
                            {
                                nextTarget = null;
                            }
                            break;
                        }
                        nextValidCheck += 0.2f;
                    }
                    yield return null;
                }
            }
            if (searchCoroutine == null || !ai.IsOwner || ai.NpcController == null)
            {
                StopSearch();
            }
        }

        private void PopulateNodes()
        {
            unsearchedNodes = [..ai.allAINodes];
            if (nodeChance < 1f || minimumPathDistance > 0f)
            {
                // The currentSearch.NodeChance random checks favours the deepest nodes in the list
                // When there is a minimum distance and there are no nodes farther away from minimum distance, we accept a random node closer than minimum distance as fallback
                // The shuffle solves the two problems above
                // TODO: Make unsearchedNodes a List with (GameObject node, float weight) tuple, the weight in the tuples are based on how far nodes are from each other, cache node weights in LethalBotManager so weights are processed only once, this should balance random node selection to choose a node based on the density of nodes in an area
                int n = unsearchedNodes.Count;
                while (n > 1)
                {
                    n--;
                    int k = UnityEngine.Random.Range(0,n+1);;
                    (unsearchedNodes[k], unsearchedNodes[n]) = (unsearchedNodes[n], unsearchedNodes[k]);
                }
            }
        }

        private IEnumerator UpdateSearchCenter()
        {
            // We are unable to find nodes if we search for nodes using a position outside navmesh
            while (!ai.agent.isOnNavMesh)
            {
                yield return null;
            }
            Vector3 newSearchCenter = isWaitingTarget ? ai.transform.position : (currentTarget?.transform.position ?? ai.transform.position);
            // The IsValidPathToTarget check here is because if we can't reach searchCenter, we can't reach the nodes reachable from it, so searchCenter becomes our current position
            if (searchCenter != newSearchCenter && (searchCenterFollowsAI || !LethalBotAI.IsValidPathToTarget(ai.transform.position, searchCenter, ai.agent.areaMask, ref ai.path1, true, out float _)))
            {
                searchCenter = newSearchCenter;
                if (isSelectingTarget)
                {
                    ai.StopCoroutine(selectTargetCoroutine);
                    isSelectingTarget = false;
                }
            }
        }

        private void ChooseNextTarget()
        {
            // We only find a new target when we aren't finding one already
            if (!isSelectingTarget)
            {
                selectTargetCoroutine = ai.StartCoroutine(SelectTargetCoroutine());
            }
        }

        private IEnumerator SelectTargetCoroutine()
        {
            isSelectingTarget = true;
            yield return null;
            while (selectTargetCoroutine != null)
            {
                TrimVisitedNodes();
                float closestDist = searchRadius;
                float closestDistSqr = searchRadius * searchRadius;
                GameObject? selectedNode = null;
                for (int i = 0; i < unsearchedNodes.Count; i++)
                {
                    if (i % 10 == 0)
                    {
                        yield return null;
                    }
                    GameObject? node = unsearchedNodes[i];
                    if (node == null || node == currentTarget) // || !ai.State.IsNodeValidForTarget(node)), uncomment and add null checks when it gets used
                    {
                        continue;
                    }
                    if (selectedNode == null || nodeChance >= 1f || UnityEngine.Random.value < nodeChance)
                    {
                        float sqrDistToNode = (searchCenter - node.transform.position).sqrMagnitude;
                        if (sqrDistToNode < closestDistSqr && LethalBotAI.IsValidPathToTarget(searchCenter, node.transform.position, ai.agent.areaMask, ref ai.path1, true, out float pathDistance) && pathDistance < closestDist)
                        {
                            // If framerate is low, bots will wait for the next destination, adding a minimum distance will make the bot walk for more time, so there is more time for the bot to find next destination while walking to the already designed destination, the problem is that bot is less likely to try to go for nodes that are in the end of corridors/rooms.
                            if (pathDistance >= minimumPathDistance)
                            {
                                selectedNode = node;
                                closestDist = pathDistance;
                                closestDistSqr = pathDistance * pathDistance;
                                if (closestDist <= 0f) break;
                            }
                            // We use this node if there are no nodes over minimumPathDistance 
                            else if (selectedNode == null)
                            {
                                selectedNode = node;
                            }

                            // TODO: Simple random node choice? The minimum path distance and this could be useful for bot personalities/identities or plugin config, but not important for now
                            // Simple random node choice isn't used because it favours travelling around nodes in the center of the map even when they are already visited, the bot will only try for end of room/corridor nodes very late in the game
                            // chosenNode = node;
                            // break;
                        }
                    }
                }
                // We try again because selectedNode has been visited while nodes were being processed
                if (selectedNode != null && !unsearchedNodes.Contains(selectedNode))
                {
                    continue;
                }
                if (isWaitingTarget)
                {
                    currentTarget = selectedNode;
                }
                else
                {
                    nextTarget = selectedNode;
                }
                break;
            }
            isSelectingTarget = false;
        }

        private IEnumerator VisitCoroutine()
        {
            visitInProgress = true;
            yield return null;
            Vector3 lastVisitedCheckCenter = ai.transform.position;
            float proximitySqr = proximityThreshold * proximityThreshold;
            float checkDist = proximityThreshold / 2;
            visitCheckIndex = 0;
            while (visitNodesCoroutine != null)
            {
                int checkAmount = (int)Mathf.Lerp(0f, unsearchedNodes.Count, Mathf.Min((ai.transform.position - lastVisitedCheckCenter).magnitude / checkDist, 1.0f));
                for (int i = 0;i < checkAmount;i++)
                {
                    if (visitCheckIndex >= unsearchedNodes.Count)
                    {
                        visitCheckIndex = 0;
                    }
                    GameObject? node = unsearchedNodes[visitCheckIndex];
                    // TODO: Distance checks can ibe improved further with a grid cell dictionary system where cells have the size of proximityThreshold, we only check nodes in the neighbouring cells
                    // TODO: Having a high proximityThreshold means that bots will not search the end or corner of corridors/rooms, they can miss loot inside drawers or loot behind interior objects, logic should check if the node has no loot next or visible to it before marking it as visited
                    if (node != null && (lastVisitedCheckCenter - node.transform.position).sqrMagnitude < proximitySqr && !Physics.Linecast(ai.eye.position, node.transform.position + Vector3.up * 0.10f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                    {
                        unsearchedNodes[visitCheckIndex] = null;
                        nullNodesCount++;
                        if (nextTarget == node)
                        {
                            nextTarget = null;
                            ChooseNextTarget();
                        }
                    }
                    visitCheckIndex++;
                }
                if (!isSelectingTarget)
                {
                    TrimVisitedNodes();
                }
                lastVisitedCheckCenter = ai.transform.position;
                yield return null;
            }
            visitInProgress = false;
        }

        private void TrimVisitedNodes()
        {
            if (unsearchedNodes.Count >= 0)
            {
                int visitedPercent = nullNodesCount / unsearchedNodes.Count;
                if (visitedPercent >= 0.2)
                {
                    visitCheckIndex *= visitedPercent;
                    unsearchedNodes.RemoveAll(x => x == null);
                    unsearchedNodes.TrimExcess();
                    nullNodesCount = 0;
                }
            }
        }
    }
}
