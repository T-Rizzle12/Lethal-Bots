using GameNetcodeStuff;
using LethalBots.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine.AI;

namespace LethalBots.AI.Pathfinding
{
    /// <summary>
    /// Contains information about the context of a lethal bot traversing a <see cref="NavMeshLink"/>. 
    /// This struct is used to pass relevant data to <see cref="INavMeshLinkInfo"/> implementations that handle link traversal logic for lethal bots.
    /// </summary>
    public sealed class LinkTraversalContext
    {
        public LethalBotAI LethalBotAI = null!;
        public PlayerControllerB LethalBotController = null!;
        public NavMeshAgent NavMeshAgent = null!;
        public NavMeshLink NavMeshLink = null!;
        public OffMeshLinkData OffMeshLinkData;
        public INavMeshLinkInfo? LinkTraversalHandler { get; set; }
        public EnumLinkTraversalOutcome TraversalOutcome { get; set; } = EnumLinkTraversalOutcome.InProgress;
        public bool WasTeleported { get; set; } = false;

        public LinkTraversalContext(LethalBotAI lethalBotAI, PlayerControllerB lethalBotController, NavMeshAgent navMeshAgent)
        {
            LethalBotAI = lethalBotAI;
            LethalBotController = lethalBotController;
            NavMeshAgent = navMeshAgent;
            Reset();
        }

        /// <summary>
        /// Returns if this <see cref="LinkTraversalContext"/> is in progress and has a non-null <see cref="INavMeshLinkInfo"/>
        /// </summary>
        /// <returns></returns>
        [MemberNotNullWhen(true, nameof(LinkTraversalHandler))]
        public bool IsValid()
        {
            return LinkTraversalHandler != null && NavMeshLink != null && TraversalOutcome == EnumLinkTraversalOutcome.InProgress;
        }

        /// <summary>
        /// Resets this <see cref="LinkTraversalContext"/> and invalidates it
        /// </summary>
        public void Reset()
        {
            NavMeshLink = null!;
            LinkTraversalHandler = null;
            TraversalOutcome = EnumLinkTraversalOutcome.InProgress;
            OffMeshLinkData = default;
            WasTeleported = false;
        }

        /// <summary>
        /// Sets the context of this <see cref="LinkTraversalContext"/>
        /// </summary>
        /// <param name="navMeshLink"></param>
        /// <param name="offMeshLinkData"></param>
        /// <param name="linkInfo"></param>
        public void SetTraversal(NavMeshLink navMeshLink, OffMeshLinkData offMeshLinkData, INavMeshLinkInfo linkInfo)
        {
            Reset();
            NavMeshLink = navMeshLink;
            OffMeshLinkData = offMeshLinkData;
            LinkTraversalHandler = linkInfo;
        }
    }
}
