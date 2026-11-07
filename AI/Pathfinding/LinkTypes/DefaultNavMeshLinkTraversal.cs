using GameNetcodeStuff;
using LethalBots.Enums;
using NavMeshLib.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.Pathfinding.LinkTypes
{
    /// <summary>
    /// This class implements the <see cref="INavMeshLinkInfo"/> interface for handling traversal of any links 
    /// not covered by other <see cref="INavMeshLinkInfo"/> objects in the navigation mesh.
    /// </summary>
    public class DefaultNavMeshLinkTraversal : INavMeshLinkInfo
    {
        public float height = 0.6f; // Default is 0.6f

        [SerializeField]
        private NavMeshLink _navMeshLink = null!;

        /// <inheritdoc cref="INavMeshLinkInfo.navMeshLink"/>
        public NavMeshLink navMeshLink { set => _navMeshLink = value; get => _navMeshLink; }

        public DefaultNavMeshLinkTraversal(NavMeshLink navMeshLink)
        {
            this.navMeshLink = navMeshLink;
        }

        public IEnumerator TraverseNavMeshLink(LinkTraversalContext context)
        {
            // Cache our context information
            LethalBotAI lethalBotAI = context.LethalBotAI;
            PlayerControllerB lethalBotController = context.LethalBotController;
            NavMeshAgent agent = context.NavMeshAgent;
            NavMeshLink link = context.NavMeshLink;
            OffMeshLinkData data = context.OffMeshLinkData;

            // Mark the NavMeshLink as completed, so other agents can use it.....we will handle the actual movement
            UnityEngine.AI.NavMeshAgent navMeshAgent = context.NavMeshAgent;
            Plugin.LogDebug($"Old Speed: {navMeshAgent.speed}");
            Vector3 oldPos = navMeshAgent.transform.position;
            navMeshAgent.CompleteOffMeshLink(); // Clear its current OffMeshLink
            navMeshAgent.transform.position = oldPos; // CompleteOffMeshLink moves the NavMeshAgent to the end of the link....we undo that here
            navMeshAgent.Warp(oldPos);
            Plugin.LogDebug($"New Speed: {navMeshAgent.speed}");

            // Convert the NavMeshLink endpoints to world space
            Vector3 linkStart = link.transform.position + link.transform.rotation * link.startPoint;
            Vector3 linkEnd = link.transform.position + link.transform.rotation * link.endPoint;

            // Determine which endpoint we're traveling toward
            bool movingForward = (data.endPos - linkEnd).sqrMagnitude < (data.endPos - linkStart).sqrMagnitude;
            Vector3 targetPos = movingForward ? linkEnd : linkStart;
            Vector3 targetLocal = movingForward ? link.endPoint : link.startPoint;

            // Define our "local" variables for the traversal logic
            Vector3 startPos = lethalBotAI.transform.position;
            Vector3 endPos = targetPos + Vector3.up * agent.baseOffset;
            float normalizedTime = 0f;

            // Calculate duration from speed
            Vector3 flatStart = Vector3.ProjectOnPlane(startPos, Vector3.up);
            Vector3 flatEnd = Vector3.ProjectOnPlane(endPos, Vector3.up);
            float horizontalDistance = Vector3.Distance(flatStart, flatEnd);
            float duration = horizontalDistance / Mathf.Max(agent.speed, 1f); // Even if agent speed is zero, we always move

            Plugin.LogDebug($"DefaultNavMeshLinkTraversal: Beginning off mesh link movement. {data.valid}; {data.activated}; {lethalBotAI.IsOwner}");
            while (normalizedTime < 1f && data.valid && lethalBotAI.IsOwner)
            {
                // If we were teleported, fail our current OffMeshLink movement
                if (context.WasTeleported)
                {
                    context.TraversalOutcome = EnumLinkTraversalOutcome.Failure;
                    yield break;
                }
                targetPos = link.transform.position + link.transform.rotation * targetLocal;
                endPos = targetPos + Vector3.up * agent.baseOffset; // Link end position could be moving (like a moving platform), so we need to update it every frame!
                float num = height * 4f * (normalizedTime - normalizedTime * normalizedTime);
                Plugin.LogDebug($"DefaultNavMeshLinkTraversal: Moving on off mesh link; time: {normalizedTime}; y: {num}; speed: {agent.speed}");
                Vector3 newPos = Vector3.Lerp(startPos, endPos, normalizedTime) + num * Vector3.up;
                lethalBotAI.transform.position = newPos;
                agent.transform.position = newPos;
                lethalBotController.transform.position = newPos;
                lethalBotAI.NpcController.SetTurnBodyTowardsDirectionWithPosition(endPos);
                normalizedTime += Time.deltaTime / duration;
                yield return null;
            }

            // If we were teleported, fail our current OffMeshLink movement
            if (context.WasTeleported)
            {
                context.TraversalOutcome = EnumLinkTraversalOutcome.Failure;
                yield break;
            }
            lethalBotAI.TeleportAgentAIAndBody(endPos, skipNavMeshCheck: true, affectLinkTraversals: false); // We should already be on the NavMesh......
            Plugin.LogDebug($"DefaultNavMeshLinkTraversal: Completed off mesh link without interruption, position: {lethalBotAI.transform.position}");
            context.TraversalOutcome = EnumLinkTraversalOutcome.Success;
        }

        public bool IsSafeToCancelTraversal(LinkTraversalContext context)
        {
            return true; // Its always safe to cancel this traversal
        }
    }
}
