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
        public List<GameObject> unsearchedNodes { get; set; } = [];
        public HashSet<GameObject> visitedNodes { get; set; } = []; // visitedNodes is a HashSet that holds visited nodes to remove from unsearchedNodes, we can't remove nodes from unsearchedNodes while it is being processed, so we hold unsearchedNodes here until they aren't being processed anymore, thinking about renaming it to searchedNodes
        private Coroutine? searchCoroutine = null;
        private Coroutine? chooseTargetCoroutine = null;
        private Coroutine? visitedNodesCoroutine = null;

        public bool searchCenterFollowsAI = true;
        public Vector3 searchCenter;
        public float searchRadius = float.MaxValue;
        public float proximityThreshold = 5f;
        public float minimumPathDistance = 0f;
        public float nodeChance = 0.65f;

        public bool searchInProgress;
        public bool visitInProgress;
        private bool isWaitingTarget;
        private bool isChoosingTarget;
        private bool hasChosenTarget;
        private GameObject? currentTarget;
        private GameObject? nextTarget;

        public void StartSearch(bool visitOnly = false)
        {
            // Nodes closer than proximityThreshold are going to be invalidated by VisitedNodesCoroutine anyways so we can ignore them
            minimumPathDistance = Mathf.Max(proximityThreshold, minimumPathDistance);
        	if (unsearchedNodes.Count - visitedNodes.Count <= 0)
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
                visitedNodesCoroutine = ai.StartCoroutine(VisitedNodesCoroutine());
            }
        }

        public void StopSearch(bool clearVisited = false, bool keepTarget = false)
        {
            if (searchCoroutine != null) ai.StopCoroutine(searchCoroutine);
            if (visitedNodesCoroutine != null) ai.StopCoroutine(visitedNodesCoroutine);
            if (chooseTargetCoroutine != null) ai.StopCoroutine(chooseTargetCoroutine);
            if (clearVisited)
            {
                ClearSearch();
            }
            if (!keepTarget)
            {
                currentTarget = null;
                nextTarget = null;
            }
            searchInProgress = false;
            visitInProgress = false;
            isWaitingTarget = false;
            isChoosingTarget = false;
            hasChosenTarget = false;
        }

        public Vector3? GetTargetPosition(bool current = true)
        {
            Vector3? returnValue = current ? currentTarget?.transform.position : nextTarget?.transform.position;
            return returnValue;
        }

        public void ClearSearch()
        {
            unsearchedNodes.Clear();
            visitedNodes.Clear();
        }

        private IEnumerator SearchCoroutine()
        {
            searchInProgress = true;
            yield return null;
            while (CanProcess(searchCoroutine))
            {
                if (nextTarget == null || !LethalBotAI.IsValidPathToTarget(ai.transform.position, nextTarget.transform.position, ai.agent.areaMask, ref ai.path1, false, out float _))
                {
                    currentTarget = null;
                    nextTarget = null;
                    isWaitingTarget = true;
                    yield return UpdateSearchCenter();
                    ChooseNextTarget();
                    while (!hasChosenTarget)
                    {
                        yield return null;
                    }
                }
                else
                {
                    currentTarget = nextTarget;
                }
                isWaitingTarget = false;
                if (currentTarget == null)
                {
                    // TODO: Remove only the reachable nodes so we can still remember the already visited nodes from other entrances
                    ClearSearch();
                    PopulateNodes();
                    yield return null;
                    continue;
                }
                yield return UpdateSearchCenter();
                ChooseNextTarget();
                float proximitySqr = proximityThreshold * proximityThreshold;
                float nextValidCheck = Time.timeSinceLevelLoad + 0.2f;
                float lowestPathDistance = float.MaxValue;
                int stuckChecks = 0;
                // NEEDTOVALIDATE: It is possible to get stuck in interior an navmesh that includes a node under the floor (not sure if that was the actual reason the bot was stuck), saw 4 different bots trying to go for the same position, only happens in some modded interiors though (Scarlet Devil Mansion), for now we are ignoring the .y element when checking distance to destination
                while (CanProcess(searchCoroutine) && currentTarget != null)
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
            if (!CanProcess(searchCoroutine))
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
                isChoosingTarget = false;
                hasChosenTarget = false;
                if (chooseTargetCoroutine != null && isChoosingTarget)
                {
                    ai.StopCoroutine(chooseTargetCoroutine);
                }
            }
        }

        private void ChooseNextTarget()
        {
            // We only find a new target when we aren't finding one already
            if (!isChoosingTarget)
            {
                chooseTargetCoroutine = ai.StartCoroutine(ChooseTargetCoroutine());
            }
        }

        private IEnumerator ChooseTargetCoroutine()
        {
            isChoosingTarget = true;
            hasChosenTarget = false;
            yield return null;
            while (CanProcess(chooseTargetCoroutine)) // && ai.State != null)
            {
                unsearchedNodes.RemoveAll(x => visitedNodes.Contains(x));
                visitedNodes.Clear();
                float closestDist = searchRadius;
                float closestDistSqr = searchRadius * searchRadius;
                int iterAmount = 10;
                GameObject? selectedNode = null;
                for (int i = 0; i < unsearchedNodes.Count; i++)
                {
                    // TODO: When a new node is chosen, increase iterAmount depending on closestDist (lower closestDist = higher iterAmount, CPU time used is lower because we are pathfinding only to closer nodes)?
                    if (i % iterAmount == 0)
                    {
                        yield return null;
                        if (!CanProcess(chooseTargetCoroutine)) // || ai.State == null)
                        {
                            isChoosingTarget = false;
                            chooseTargetCoroutine = null;
                            yield break;
                        }
                    }
                    GameObject? node = unsearchedNodes[i];
                    if (node == null || node == currentTarget) continue; // || !ai.State.IsNodeValidForTarget(node)), uncomment when it gets used
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
                            // Fallback to nodes closer than minimumPathDistance when there are no selected node
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
                if (selectedNode != null && visitedNodes.Contains(selectedNode))
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
                hasChosenTarget = true;
                break;
            }
            isChoosingTarget = false;
            chooseTargetCoroutine = null;
        }

        private IEnumerator VisitedNodesCoroutine()
        {
            visitInProgress = true;
            yield return null;
            Vector3 visitedCheckCenter = ai.transform.position;
            Vector3 eyePosition = ai.eye.position;
            float proximitySqr = proximityThreshold * proximityThreshold;
            float checkDist = proximityThreshold / 2;
            int currentIndex = 0;
            while (CanProcess(visitedNodesCoroutine))
            {
                int reachIndex = (int)Mathf.Lerp(0f, unsearchedNodes.Count, Mathf.Min((ai.transform.position - visitedCheckCenter).magnitude / checkDist, 1.0f));
                for (int i = currentIndex; i < reachIndex; i++)
                {
                    var node = unsearchedNodes[i];
                    // TODO: Distance checks can be improved further with a grid cell dictionary system where cells have the size of proximityThreshold, we only check nodes in the neighbouring cells
                    // TODO: Having a high proximityThreshold means that bots will not search the end or corner of corridors/rooms, they can miss loot inside drawers or loot behind interior objects, logic should check if the node has no loot next or visible to it
                    if (!visitedNodes.Contains(node) && node != currentTarget && (visitedCheckCenter - node.transform.position).sqrMagnitude < proximitySqr && !Physics.Linecast(eyePosition, node.transform.position + Vector3.up * 0.10f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                    {
                        visitedNodes.Add(node);
                        if (nextTarget == node)
                        {
                            nextTarget = null;
                            ChooseNextTarget();
                        }
                    }
                }
                currentIndex = Math.Max(currentIndex, reachIndex);
                if (currentIndex >= unsearchedNodes.Count)
                {
                    currentIndex = 0;
                    visitedCheckCenter = ai.transform.position;
                    eyePosition = ai.eye.position;
                }
                yield return null;
            }
            visitInProgress = false;
        }

        private bool CanProcess(Coroutine? coroutine)
        {
            return coroutine != null && ai.IsOwner && ai.NpcController != null;
        }
    }
}
