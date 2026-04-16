using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.GameEnginePatches;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot just saw a dangerous enemy (see: <see cref="LethalBotAI.GetFearRangeForEnemies"><c>LethalBotAI.GetFearRangeForEnemies</c></see>).
    /// The bot try to flee by choosing a far away node from the enemy.
    /// </summary>
    public class PanikState : AIState
    {
        private float findEntranceTimer;
        private float calmDownTimer;
        private float breakLOSTimer;
        private const float declareJesterCooldownTime = 30f;
        private float lastDeclaredJesterTimer;
        private bool wasFleeingJester;
        private Vector3? _retreatPos = null;
        private Vector3? RetreatPos
        {
            set
            { 
                Vector3? newPos = value;
                if (newPos.HasValue)
                {
                    _retreatPos = RoundManager.Instance.GetNavMeshPosition(newPos.Value, RoundManager.Instance.navHit, 2.7f);
                }
                else
                {
                    _retreatPos = null;
                }
            }
            get => _retreatPos;
        }

        /// <summary>
        /// Constructor for PanikState
        /// </summary>
        /// <param name="oldState"></param>
        /// <param name="enemyAI">EnemyAI to flee</param>
        public PanikState(AIState oldState, EnemyAI enemyAI) : base(oldState)
        {
            CurrentState = EnumAIStates.Panik;

            Plugin.LogDebug($"{npcController.Npc.playerUsername} enemy seen {enemyAI.enemyType.enemyName}");
            this.CurrentEnemy = enemyAI;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (this.CurrentEnemy == null 
                    || this.CurrentEnemy.isEnemyDead)
                {
                    Plugin.LogWarning("PanikState: CurrentEnemy is null or dead, cannot start panik state!");
                    ChangeBackToPreviousState();
                    return;
                }
                float? fearRange = ai.GetFearRangeForEnemies(this.CurrentEnemy);
                if (fearRange.HasValue)
                {
                    // Why run when we can fight back!
                    if (ai.HasCombatWeapon() && ai.CanEnemyBeKilled(this.CurrentEnemy))
                    {
                        ai.State = new FightEnemyState(this, this.CurrentEnemy, this.previousAIState);
                        return;
                    }

                    // Find the closest entrance and mark our last pos for stuck checking!
                    targetEntrance = FindClosestEntrance();

                    // Make us back away at first until the panik coroutine can find a safe path.
                    const float fallbackDistance = 20f;
                    Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - this.CurrentEnemy.transform.position + Vector3.up * 0.2f);
                    ray.direction = new Vector3(ray.direction.x, 0f, ray.direction.z);
                    Vector3 pos = (!Physics.Raycast(ray, out RaycastHit hit, fallbackDistance, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) ? ray.GetPoint(fallbackDistance) : hit.point;
                    RetreatPos = pos;
                    StartPanikCoroutine(this.CurrentEnemy, fearRange.Value);
                    if (this.CurrentEnemy is JesterAI)
                    {
                        wasFleeingJester = true;
                        if ((Time.timeSinceLevelLoad - lastDeclaredJesterTimer) > declareJesterCooldownTime)
                        {
                            lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                            ai.SendChatMessage("JESTER!!! RUN!!!", true);
                        }
                    }
                }
                else
                {
                    ChangeBackToPreviousState();
                    return;
                }
            }
            base.OnEnterState();
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
        public override void DoAI()
        {
            if (CurrentEnemy == null || CurrentEnemy.isEnemyDead)
            {
                if (wasFleeingJester)
                {
                    this.CurrentEnemy = FindNearbyJester();
                    if (this.CurrentEnemy != null && !this.CurrentEnemy.isEnemyDead)
                    {
                        return;
                    }
                }
                ChangeBackToPreviousState();
                return;
            }

            float? fearRange = ai.GetFearRangeForEnemies(this.CurrentEnemy);
            if (!fearRange.HasValue)
            {
                if (wasFleeingJester)
                {
                    this.CurrentEnemy = FindNearbyJester();
                    return;
                }
                ChangeBackToPreviousState();
                return;
            }

            // Check if another enemy is closer
            EnemyAI? newEnemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (newEnemyAI != null && newEnemyAI != CurrentEnemy)
            {
                float? newFearRange = ai.GetFearRangeForEnemies(newEnemyAI);
                if (newFearRange.HasValue)
                {
                    this.CurrentEnemy = newEnemyAI;
                    fearRange = newFearRange.Value;
                    calmDownTimer = 0f;
                    RestartPanikCoroutine(this.CurrentEnemy, fearRange.Value);
                    if (this.CurrentEnemy is JesterAI)
                    {
                        wasFleeingJester = true;
                        if ((Time.timeSinceLevelLoad - lastDeclaredJesterTimer) > declareJesterCooldownTime)
                        {
                            lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                            ai.SendChatMessage("JESTER!!! RUN!!!", true);
                        }
                    }
                }
                // else no fear range, ignore this enemy, already ignored by CheckLOSForEnemy but hey better be safe
            }

            // Are we waiting for the enemy to leave the entrance?
            if (calmDownTimer > 0f && !this.CurrentEnemy.isOutside)
            {
                // Check if we should end early!
                ai.StopMoving();

                // Now, lets check if someone is assigned to transfer loot
                bool shouldWalkLootToShip = true;
                if (LethalBotManager.Instance.LootTransferPlayers.Count > 0)
                {
                    shouldWalkLootToShip = false;
                }
                if (ai.HasScrapInInventory())
                {
                    ai.State = new ReturnToShipState(this, !shouldWalkLootToShip);
                }
                else if (previousState == EnumAIStates.ReturnToShip
                    || previousState == EnumAIStates.ChillAtShip)
                {
                    ai.State = new ReturnToShipState(this, !shouldWalkLootToShip);
                }
                // Wait outside the door a bit before heading back in,
                // if we have been waiting for a bit give up and head back!
                else if (ShouldReturnToShip())
                {
                    ai.State = new ReturnToShipState(this);
                }
                else if (calmDownTimer > Const.FLEEING_CALM_DOWN_TIME + Const.WAIT_TIME_FOR_SAFE_PATH)
                {
                    ai.State = new SearchingForScrapState(this, targetEntrance);
                }
                else if (calmDownTimer > Const.FLEEING_CALM_DOWN_TIME 
                    && IsEntranceSafe(targetEntrance, true))
                {
                    ChangeBackToPreviousState();
                }
                else
                {
                    calmDownTimer += ai.AIIntervalTime;
                }
                return;
            }

            // Check to see if the bot can see the enemy, or enemy has line of sight to bot
            float sqrDistanceToEnemy = (npcController.Npc.transform.position - CurrentEnemy.transform.position).sqrMagnitude;
            if (this.CurrentEnemy is not JesterAI &&
                Physics.Linecast(CurrentEnemy.transform.position, npcController.Npc.gameplayCamera.transform.position,
                                 StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) 
                && sqrDistanceToEnemy > Const.DISTANCE_FLEEING_NO_LOS * Const.DISTANCE_FLEEING_NO_LOS)
            {
                // If line of sight broke
                // and the bot is far enough when the enemy can not see him
                if (breakLOSTimer > Const.FLEEING_BREAK_LOS_TIME)
                {
                    // Don't forget we still need to get out of there!
                    if (wasFleeingJester)
                    {
                        this.CurrentEnemy = FindNearbyJester();
                        return;
                    }
                    ChangeBackToPreviousState();
                    return;
                }
                else
                {
                    breakLOSTimer += ai.AIIntervalTime;
                }
            }
            else
            {
                breakLOSTimer = 0f;
            }
            // Enemy still has line of sight of bot

            // Far enough from enemy
            if (sqrDistanceToEnemy > fearRange * fearRange)
            {
                // Don't forget we still need to get out of there!
                if (wasFleeingJester)
                {
                    this.CurrentEnemy = FindNearbyJester();
                    return;
                }
                ChangeBackToPreviousState();
                return;
            }
            // Enemy still too close

            // If enemy still too close, and destination reached, restart the panic routine
            if (panikCoroutine == null)
            {
                if (!RetreatPos.HasValue
                    || (RetreatPos.Value - npcController.Npc.transform.position).sqrMagnitude < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION
                    || !ai.IsValidPathToTarget(RetreatPos.Value, false))
                {
                    RestartPanikCoroutine(this.CurrentEnemy, fearRange.Value);
                }
            }

            // Why run when we can fight back!
            if (ai.HasCombatWeapon() && ai.CanEnemyBeKilled(this.CurrentEnemy))
            {
                ai.State = new FightEnemyState(this, this.CurrentEnemy, this.previousAIState);
                return;
            }

            // Check if we are countering an enemy
            if (CounterEnemy(this.CurrentEnemy))
            {
                // Custom logic doesn't want the movement code to run.
                return;
            }

            // If we are nearby an entrance we should flee out of it!
            if (findEntranceTimer > Const.FLEEING_UPDATE_ENTRANCE)
            {
                findEntranceTimer = 0f;
                targetEntrance = FindClosestEntrance();
            }
            else
            {
                findEntranceTimer += ai.AIIntervalTime;
            }

            // Flee out the entrance if we are close enough to it!
            if (targetEntrance != null)
            {
                // If we are close enough, we should use the entrance to leave
                float distSqrFromEntrance = (targetEntrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                if (distSqrFromEntrance < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    // Check for teleport entrance
                    if (!ai.AreHandsFree() && ai.HeldItem is CaveDwellerPhysicsProp)
                    {
                        // We must drop the maneater baby before we use the entrance!
                        ai.DropItem();
                        return;
                    }
                    else if (Time.timeSinceLevelLoad - ai.TimeSinceTeleporting > Const.WAIT_TIME_TO_TELEPORT)
                    {
                        Vector3? entranceTeleportPos = ai.GetTeleportPosOfEntrance(targetEntrance);
                        if (entranceTeleportPos.HasValue)
                        {
                            Plugin.LogDebug($"======== TeleportLethalBotAndSync {ai.NpcController.Npc.playerUsername} !!!!!!!!!!!!!!! ");
                            ai.StopMoving();
                            ai.SyncTeleportLethalBot(entranceTeleportPos.Value, !this.targetEntrance?.isEntranceToBuilding ?? !ai.isOutside, this.targetEntrance);
                            calmDownTimer = ai.AIIntervalTime;
                            if (ai.targetPlayer != null && ai.targetPlayer.isInsideFactory)
                            {
                                // If we use the entrance to go outside, we should set the last known position to the entrance teleport position
                                // This makes us use the entrance again so we can follow the player back inside
                                previousStateUpdate.TargetLastKnownPosition = entranceTeleportPos.Value;
                            }
                        }
                        else
                        {
                            targetEntrance = null;
                            findEntranceTimer = 0f;
                            RestartPanikCoroutine(this.CurrentEnemy, fearRange.Value);
                        }
                    }
                }
                else if (distSqrFromEntrance < Const.DISTANCE_NEARBY_ENTRANCE * Const.DISTANCE_NEARBY_ENTRANCE 
                    || this.CurrentEnemy is JesterAI
                    || !ai.PathIsIntersectedByLineOfSight(targetEntrance.entrancePoint.position, out _, false, true, this.CurrentEnemy))
                {
                    Plugin.LogDebug("Safe path to nearby entrance, setting retreat pos to entrance!");
                    StopPanikCoroutine();

                    // If we need to go up an elevator we should do so!
                    if (LethalBotAI.ElevatorScript != null && !ai.IsInElevatorStartRoom && !ai.IsValidPathToTarget(targetEntrance.entrancePoint.position, false))
                    {
                        // Use elevator returns a bool if the can successfully use the elevator
                        ai.UseElevator(true);
                        RetreatPos = ai.destination;
                    }
                    else
                    {
                        RetreatPos = targetEntrance.entrancePoint.position;
                    }
                }
            }

            // Update our destination if needed!
            if (RetreatPos.HasValue)
            {
                ai.SetDestinationToPositionLethalBotAI(RetreatPos.Value);
            }

            // Sprint of course
            npcController.OrderToSprint();
            ai.OrderMoveToDestination();
            //retreatPos = ai.destination; // OrderMoveToDestination may change the final destination! 
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopPanikCoroutine();
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // If we used an entrance to go outside, we wait a bit before entering again!
            if (calmDownTimer > 0f)
            {
                return;
            }

            // Priority state
            // Stop talking and voice new state
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.RunningFromMonster,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = false,
                CutCurrentVoiceStateToTalk = true,
                CanRepeatVoiceState = this.CurrentEnemy is not NutcrackerEnemyAI || this.CurrentEnemy.currentBehaviourStateIndex != 1, // Yeah, its a bit weird for them to keep screaming while standing in place....

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        /// <summary>
        /// <inheritdoc cref="AIState.OnBotStuck"/>
        /// </summary>
        /// <remarks>
        /// If the bot is stuck, we should reset the panik coroutine and try to find a new path to flee.
        /// </remarks>
        public override void OnBotStuck()
        {
            base.OnBotStuck();
            if (this.CurrentEnemy != null)
            { 
                RestartPanikCoroutine(this.CurrentEnemy, ai.GetFearRangeForEnemies(this.CurrentEnemy) ?? Const.DISTANCE_FLEEING); 
            }
        }

        /// <inheritdoc cref="AIState.RegisterChatCommands"/>
        public static new void RegisterChatCommands()
        {
            // We are paniking right now, ignore default chat commands
            ChatCommandsManager.RegisterIgnoreDefaultForState<PanikState>();

            // Jester is a special case, we should not panic if we are already panicking!
            ChatCommandsManager.RegisterCommandForState<PanikState>(new ChatCommand(Const.JESTER_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                PanikState panikState = (PanikState)state;
                if (panikState.CurrentEnemy is JesterAI || lethalBotAI.isOutside)
                {
                    return true;
                }
                EnemyAI? enemyAI = panikState.FindNearbyJester();
                if (enemyAI == null)
                {
                    return true;
                }
                panikState.wasFleeingJester = true;
                panikState.CurrentEnemy = enemyAI;
                panikState.calmDownTimer = 0f;
                float? fearRange = lethalBotAI.GetFearRangeForEnemies(panikState.CurrentEnemy);
                if (fearRange.HasValue)
                {
                    panikState.RestartPanikCoroutine(panikState.CurrentEnemy, fearRange.Value);
                    if ((Time.timeSinceLevelLoad - panikState.lastDeclaredJesterTimer) > declareJesterCooldownTime)
                    {
                        panikState.lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                        lethalBotAI.SendChatMessage("JESTER!!! RUN!!!", true);
                    }
                }
                return true;
            }));
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are fleeing right now, these messages should be queued!
            ChatCommandsManager.RegisterCommandForState<PanikState>(new ChatCommand(Const.RETURN_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                // Return to the ship when we finish running away!
                PanikState panikState = (PanikState)state;
                panikState.previousAIState = new ReturnToShipState(state);
                return true;
            }));

            ChatCommandsManager.RegisterCommandForState<PanikState>(new ChatCommand(Const.JESTER_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                // Jester is a special case, we should not panic if we are already panicking!
                PanikState panikState = (PanikState)state;
                panikState.lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                if (panikState.CurrentEnemy is JesterAI || lethalBotAI.isOutside)
                {
                    return true;
                }
                EnemyAI? enemyAI = panikState.FindNearbyJester();
                if (enemyAI == null)
                {
                    return true;
                }
                panikState.wasFleeingJester = true;
                panikState.CurrentEnemy = enemyAI;
                panikState.calmDownTimer = 0f;
                float? fearRange = lethalBotAI.GetFearRangeForEnemies(panikState.CurrentEnemy);
                if (fearRange.HasValue)
                {
                    panikState.RestartPanikCoroutine(panikState.CurrentEnemy, fearRange.Value);
                    return true;
                }
                return true;
            }));
        }

        public override bool? ShouldBotCrouch()
        {
            return false;
        }

        public override void UseHeldItem()
        {
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem is CaveDwellerPhysicsProp caveDwellerGrabbableObject)
            {
                // Drop the Maneater since we may flee out of the facility,
                // and it could be a problem if we are outside with it!
                CaveDwellerAI? caveDwellerAI = caveDwellerGrabbableObject.caveDwellerScript;
                if (caveDwellerAI == null || !caveDwellerAI.babyCrying)
                {
                    ai.DropItem();
                    return;
                }
            }
            else if (heldItem is NoisemakerProp)
            {
                return; // Nope, not the time for this......
            }
            base.UseHeldItem();
        }

        public override string GetBillboardStateIndicator()
        {
            return @"/!\";
        }

        /// <summary>
        /// Helper function that was made to make it easier for bots to counter enemies
        /// </summary>
        /// <remarks>
        /// <paramref name="CurrentEnemy"/> only exists to allow other modders the ability to add custom logic for their enemies!
        /// </remarks>
        /// <param name="CurrentEnemy"></param>
        /// <returns><see langword="true"/> if we should skip movement logic; otherwise <see langword="false"/></returns>
        private bool CounterEnemy(EnemyAI CurrentEnemy)
        {
            // Look at the enemy if they are a coil head!
            if (CurrentEnemy is SpringManAI || CurrentEnemy is FlowermanAI || CurrentEnemy is PumaAI)
            {
                npcController.OrderToLookAtPosition(CurrentEnemy.NetworkObject, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime);
            }
            // Ok, there are three state indexes for nutcrackers to date!
            // 0. Patroling
            // 1. Scanning
            // 2. Hunting/Attacking
            else if (CurrentEnemy is NutcrackerEnemyAI && CurrentEnemy.currentBehaviourStateIndex == 1)
            {
                ai.StopMoving(); // Stand still, if we move, the nutcracker will see us!
                return true;
            }
            return false;
        }

        /// <summary>
        /// A class used by ChooseFleeingNodeFromPosition to asses the safety of a node
        /// </summary>
        private sealed class NodeSafety : IComparable<NodeSafety>, IEquatable<NodeSafety>
        {
            // The node associated with this safety object
            public GameObject node;
            public float fearRange;

            // These affect the safety score of the node
            public bool isPathOutOfSight; // true is good; false is bad
            public bool isNodeOutOfSight; // true is good; false is bad
            public float minPathDistanceToEnemy; // Larger numbers are better!

            // These are the distance between the node and the chosen target
            public float enemyPathDistance; // further away is better
            public float botPathDistance; // closer is better

            public NodeSafety(GameObject node, float fearRange, bool isPathOutOfSight, bool isNodeOutOfSight)
            {
                this.node = node ?? throw new ArgumentNullException(nameof(node));
                this.fearRange = fearRange;
                this.isPathOutOfSight = isPathOutOfSight;
                this.isNodeOutOfSight = isNodeOutOfSight;
            }

            /// <summary>
            /// Returns the position of <see cref="node"/>
            /// </summary>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Vector3 GetNodePosition()
            {
                return node.transform.position;
            }

            /// <summary>
            /// Returns the current safety score of this <see cref="NodeSafety"/> object
            /// </summary>
            /// <returns></returns>
            public float GetSafetyScore()
            {
                float score = 0f;

                // Check the fear range
                if (minPathDistanceToEnemy < fearRange)
                {
                    score -= 100f; // Not good, really dislike this node!
                }
                else
                {
                    // Path safety is king
                    score += minPathDistanceToEnemy * 2.5f;
                }

                // Visibility matters, but not infinitely
                if (isPathOutOfSight) score += 15f;
                if (isNodeOutOfSight) score += 10f;

                // Enemy distance
                score += Mathf.Min(enemyPathDistance, fearRange) * 1.2f;

                // Prefer closer nodes, but lightly
                score -= botPathDistance * 0.6f;

                return score;
            }


            public override string ToString()
            {
                return $"GameObject: {node}, IsPathOutOfSight: {isPathOutOfSight}, IsNodeOutOfSight: {isNodeOutOfSight}, MinPathDistanceToEnemy: {minPathDistanceToEnemy}, EnemyPathDistance: {enemyPathDistance}, BotPathDistance: {botPathDistance}";
            }

            public int CompareTo(NodeSafety? other)
            {
                // This is always greater than null
                if (other is null)
                {
                    return 1;
                }

                // Compare the two safety scores
                float a = this.GetSafetyScore();
                float b = other.GetSafetyScore();

                // If they are equal, consider them the same!
                if (Mathf.Approximately(a, b))
                    return 0;

                // Check to see which score is higher!
                return a > b ? 1 : -1;
            }

            public bool Equals(NodeSafety? other)
            {
                if (other is null)
                {
                    return false;
                }
                return node == other.node 
                    && fearRange == other.fearRange
                    && isPathOutOfSight == other.isPathOutOfSight 
                    && isNodeOutOfSight == other.isNodeOutOfSight 
                    && minPathDistanceToEnemy == other.minPathDistanceToEnemy 
                    && enemyPathDistance == other.enemyPathDistance
                    && botPathDistance == other.botPathDistance;
            }

            public override bool Equals(object obj)
            {
                return obj is NodeSafety nodeSafety && Equals(nodeSafety);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(node, fearRange, isPathOutOfSight, isNodeOutOfSight, minPathDistanceToEnemy, enemyPathDistance, botPathDistance);
            }

            public static bool operator <(NodeSafety? left, NodeSafety? right) 
            { 
                if (left is null)
                {
                    return right is not null;
                }
                return left.CompareTo(right) < 0; 
            }

            public static bool operator >(NodeSafety? left, NodeSafety? right)
            {
                if (left is null)
                {
                    return false;
                }
                return left.CompareTo(right) > 0;
            }

            public static bool operator <=(NodeSafety? left, NodeSafety? right) 
            {
                if (left is null)
                {
                    return right is null;
                }
                return left.CompareTo(right) <= 0;
            }
            public static bool operator >=(NodeSafety? left, NodeSafety? right) 
            {
                if (left is null)
                {
                    return right is null;
                }
                return left.CompareTo(right) >= 0;
            }

            public static bool operator ==(NodeSafety? left, NodeSafety? right) 
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null) return false;
                return left.Equals(right); 
            }

            public static bool operator !=(NodeSafety? left, NodeSafety? right) 
            {
                return !(left == right); 
            }
        }

        /// <summary>
        /// Coroutine to find the closest node after some distance (see: <see cref="LethalBotAI.GetFearRangeForEnemies"><c>LethalBotAI.GetFearRangeForEnemies</c></see>).
        /// In other word, find a path node to flee from the enemy.
        /// </summary>
        /// <remarks>
        /// Or should I say an attempt to code it.
        /// </remarks>
        /// <param name="enemy">Position of the enemy</param>
        /// <returns></returns>
        private IEnumerator ChooseFleeingNodeFromPosition(EnemyAI enemy, float fearRange)
        {
            Plugin.LogDebug($"Start panik coroutine for {npcController.Npc.playerUsername}!");
            // FIXME: This relies on Elucian Distance rather than travel distance, this should be fixed!
            /*var nodes = ai.allAINodes.OrderBy(node =>
            {
                float distanceToNode = (node.transform.position - this.ai.transform.position).sqrMagnitude;
                float distanceToEnemy = (node.transform.position - enemyTransform.position).sqrMagnitude;
                return distanceToNode - distanceToEnemy; // Minimize distance to node, maximize distance to enemy
            }).ToArray();*/
            //yield return null;

            /// This is mostly the same as the <see cref="FlowermanAI.maxAsync"/>
            /// This makes bots much more responsive when picking a spot to flee to
            /// We do less async calculations the further we are from our target enemy!
            Vector3 ourPos = npcController.Npc.transform.position;
            Transform enemyTransform = enemy.transform;
            Vector3 enemyPos = enemyTransform.position;
            Vector3 viewPos = enemy.eye != null ? enemy.eye.position : enemyPos;
            viewPos += Vector3.up * 0.2f; // Slightly above eye level to avoid ground clipping issues
            float ourDistanceFromEnemy = (enemyTransform.position - ourPos).sqrMagnitude;
            float headOffset = npcController.Npc.gameplayCamera.transform.position.y - ourPos.y;
            int maxAsync;
            if (ourDistanceFromEnemy < 16f * 16f)
            {
                maxAsync = 25; // Was changed from 100 to 25 for optimization reasons!
            }
            else if (ourDistanceFromEnemy < 40f * 40f)
            {
                maxAsync = 15;
            }
            else
            {
                maxAsync = 5; // Was 4, but 5 feels like a better number!
            }

            // We don't use an foreach loop here as we want to be able to customize the cooldown
            NodeSafety? bestNode = null;
            for (int i = 0; i < ai.allAINodes.Length; i++)
            {
                // Give the main thread a chance to do something else
                var node = ai.allAINodes[i];
                if (i % maxAsync == 0)
                {
                    // This feels like too much!
                    //yield return new WaitForSeconds(ai.AIIntervalTime);
                    yield return null;
                }

                // Check if the node is too close to the enemy
                if (node == null)
                {
                    continue;
                }

                // Skip if the node is too close to the enemy
                NodeSafety nodeSafety = new NodeSafety(node, fearRange, true, true);
                Vector3 nodePos = nodeSafety.GetNodePosition();
                if ((nodePos - ourPos).sqrMagnitude < fearRange * fearRange)
                {
                    continue;
                }

                if (enemy != null)
                {
                    // Check if they can even path there and their distance from the node!
                    if (enemy.PathIsIntersectedByLineOfSight(nodePos, calculatePathDistance: true, false, false))
                    {
                        nodeSafety.enemyPathDistance = float.MaxValue; // No path, thats REALLY good!
                    }
                    else
                    {
                        nodeSafety.enemyPathDistance = enemy.pathDistance; // Alright, how far....
                    }
                }

                // Check if the node is in line of sight of the enemy
                if (ai.PathIsIntersectedByLineOfSight(nodePos, out bool isPathVaild, true, true, enemy))
                {
                    // Check if we can use this node as a fallback
                    nodeSafety.isPathOutOfSight = false;
                    if (!isPathVaild)
                    {
                        // Skip if the node has no path
                        continue;
                    }
                }

                // Update out distance to the node
                nodeSafety.botPathDistance = ai.pathDistance;

                // Check if our path goes anywhere near the enemy we are fleeing from
                float minDist = float.MaxValue;
                foreach (var corner in ai.path1.corners)
                {
                    // TODO: Make this pick the closest point on the path.
                    // Use the same stuff safe path uses.
                    minDist = Math.Min(minDist, (corner - enemyPos).sqrMagnitude);
                }

                // Update the closest part of the path to the enemy we are fleeing from
                nodeSafety.minPathDistanceToEnemy = minDist;

                // Make sure our path actualy leads us further away from out target
                if (nodeSafety.minPathDistanceToEnemy <= ourDistanceFromEnemy * 0.9f)
                {
                    continue;
                }

                // Now we test if the node is visible to an enemy!
                Vector3 simulatedHead = nodePos + Vector3.up * headOffset;
                if (!Physics.Linecast(viewPos, simulatedHead, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    nodeSafety.isNodeOutOfSight = false;
                }

                // Check if the node is a better candidate
                if (bestNode == null || nodeSafety > bestNode)
                {
                    bestNode = nodeSafety;
                }
            }

            if (bestNode != null)
            {
                // We found a node to run to!
                Plugin.LogDebug($"Found a node to run to: {bestNode} for {npcController.Npc.playerUsername}!");
                Plugin.LogDebug($"Distance to node: {bestNode.botPathDistance} for {npcController.Npc.playerUsername}!");
                Plugin.LogDebug($"Distance to enemy: {Vector3.Distance(bestNode.GetNodePosition(), enemyTransform.position)} for {npcController.Npc.playerUsername}!");
                RetreatPos = bestNode.GetNodePosition();
                ai.SetDestinationToPositionLethalBotAI(RetreatPos.Value);
                ai.OrderMoveToDestination();
                panikCoroutine = null;
                yield break;
            }

            // We somehow failed to find a place to run to, pick again next AI think!
            Plugin.LogDebug($"Failed to find a node to run to for {npcController.Npc.playerUsername}!");
            yield return new WaitForEndOfFrame();
            if (CurrentEnemy != null)
            {
                RetreatPos = null;
                panikCoroutine = null;
            }

        }

        /// <summary>
        /// Changes back to the previous state
        /// </summary>
        protected override void ChangeBackToPreviousState()
        {
            if (previousState == EnumAIStates.SearchingForScrap
                    || (previousState == EnumAIStates.FetchingObject 
                        && (ai.targetPlayer == null
                        || !ai.targetPlayer.isPlayerControlled
                        || ai.targetPlayer.isPlayerDead)))
            {
                // If we have some scrap, it might be a good time to bring it back,
                // just in case.....
                if (ai.HasScrapInInventory())
                {
                    // Now, lets check if someone is assigned to transfer loot
                    bool shouldWalkLootToShip = true;
                    if (LethalBotManager.Instance.LootTransferPlayers.Count > 0)
                    {
                        shouldWalkLootToShip = false;
                    }
                    ai.State = new ReturnToShipState(this, !shouldWalkLootToShip, new SearchingForScrapState(this));
                    return;
                }
            }
            base.ChangeBackToPreviousState();
        }

        protected override EntranceTeleport? FindClosestEntrance(EntranceTeleport? entranceToAvoid, Vector3? shipPos = null)
        {
            // Don't do this logic if we are outside!
            if (ai.isOutside)
            {
                return null;
            }
            return base.FindClosestEntrance(entranceToAvoid, shipPos);
        }

        protected override EntranceTeleport? FindClosestEntrance(Vector3? shipPos = null, HashSet<EntranceTeleport>? entrancesToAvoid = null)
        {
            // Don't do this logic if we are outside!
            if (ai.isOutside)
            {
                return null;
            }
            return base.FindClosestEntrance(shipPos, entrancesToAvoid);
        }

        private void StartPanikCoroutine(EnemyAI CurrentEnemy, float fearRange)
        {
            panikCoroutine = ai.StartCoroutine(ChooseFleeingNodeFromPosition(CurrentEnemy, fearRange));
        }

        private void RestartPanikCoroutine(EnemyAI CurrentEnemy, float fearRange)
        {
            StopPanikCoroutine();
            StartPanikCoroutine(CurrentEnemy, fearRange);
        }

        private void StopPanikCoroutine()
        {
            if (panikCoroutine != null)
            {
                ai.StopCoroutine(panikCoroutine);
                panikCoroutine = null;
            }
        }
    }
}
