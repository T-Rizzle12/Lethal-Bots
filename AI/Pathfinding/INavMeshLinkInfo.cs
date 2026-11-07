using GameNetcodeStuff;
using NavMeshLib;
using System;
using System.Collections;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.Pathfinding
{
    /// <summary>
    /// An interface for handling <see cref="NavMeshLink"/> traversal information for lethal bots.
    /// </summary>
    public interface INavMeshLinkInfo
    {
        /// <summary>
        /// The <see cref="navMeshLink"/> associated with this link.
        /// </summary>
        public NavMeshLink navMeshLink { get; }

        /// <summary>
        /// Traverses a <see cref="navMeshLink"/> based on the provided <see cref="LinkTraversalContext"/>.
        /// </summary>
        /// <returns></returns>
        public IEnumerator TraverseNavMeshLink(LinkTraversalContext context);

        /// <summary>
        /// Can the bot safely stop traversing the link? 
        /// This is used to determine if the bot can be interrupted while traversing a link.
        /// </summary>
        /// <remarks>
        /// This will be ignored if the bot dies or if <see cref="LethalBotAI.StopOffMeshLinkMovement(bool, bool)"/> 
        /// is called with <c>forceStop = <see langword="true"/></c>.
        /// </remarks>
        /// <returns></returns>
        public bool IsSafeToCancelTraversal(LinkTraversalContext context);
    }
}
