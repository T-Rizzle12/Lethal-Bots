using NavMeshLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.Pathfinding
{
    /// <summary>
    /// Helper functions for use in objects derived from <see cref="INavMeshLinkInfo"/>
    /// </summary>
    public static class NavMeshLinkInfoExtentsions
    {
        /// <summary>
        /// Directly sets the bot's <see cref="NavMeshAgent.destination"/> without
        /// disrupting what was set by the AI states.
        /// </summary>
        /// <remarks>
        /// NOTE: <see cref="LethalBotAI.OrderMoveToDestination"/> will skip its SetDestination calls while
        /// <see cref="LethalBotAI.IsUsingOffMeshLink"/> returns true.
        /// </remarks>
        /// <param name="navMeshLinkInfo"></param>
        /// <param name="context"></param>
        /// <param name="destination"></param>
        public static void MoveBotOnLink(this INavMeshLinkInfo navMeshLinkInfo, in LinkTraversalContext context, Vector3 destination)
        {
            NavMeshAgent agent = context.LethalBotAI.agent;
            agent.SetDestination(RoundManager.Instance.GetNavMeshPosition(destination, agent.GetNavMeshQueryFilter(), sampleRadius: 2.7f));
            context.LethalBotAI.OrderMoveToDestination();
        }
    }
}
