using DunGen;
using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Patches.GameEnginePatches;
using System;
using System.Collections;
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
            this.currentEnemy = enemyAI;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (this.currentEnemy == null 
                    || this.currentEnemy.isEnemyDead)
                {
                    Plugin.LogWarning("PanikState: currentEnemy is null or dead, cannot start panik state!");
                    ChangeBackToPreviousState();
                    return;
                }
                float? fearRange = ai.GetFearRangeForEnemies(this.currentEnemy);
                if (fearRange.HasValue)
                {
                    // Why run when we can fight back!
                    if (ai.HasCombatWeapon() && ai.CanEnemyBeKilled(this.currentEnemy))
                    {
                        ai.State = new FightEnemyState(this, this.currentEnemy, this.previousAIState);
                        return;
                    }

                    // Find the closest entrance and mark our last pos for stuck checking!
                    targetEntrance = FindClosestEntrance();
                    StartPanikCoroutine(this.currentEnemy, fearRange.Value);
                    if (this.currentEnemy is JesterAI)
                    {
                        wasFleeingJester = true;
                        if ((Time.timeSinceLevelLoad - lastDeclaredJesterTimer) > 30f)
                        {
                            lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                            ai.SendChatMessage("JESTER!!! RUN!!!");
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
            if (currentEnemy == null || currentEnemy.isEnemyDead)
            {
                if (wasFleeingJester)
                {
                    this.currentEnemy = FindNearbyJester();
                    if (this.currentEnemy != null && !this.currentEnemy.isEnemyDead)
                    {
                        return;
                    }
                }
                ChangeBackToPreviousState();
                return;
            }

            float? fearRange = ai.GetFearRangeForEnemies(this.currentEnemy);
            if (!fearRange.HasValue)
            {
                if (wasFleeingJester)
                {
                    this.currentEnemy = FindNearbyJester();
                    return;
                }
                ChangeBackToPreviousState();
                return;
            }

            // Check if another enemy is closer
            EnemyAI? newEnemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (newEnemyAI != null && newEnemyAI != currentEnemy)
            {
                float? newFearRange = ai.GetFearRangeForEnemies(newEnemyAI);
                if (newFearRange.HasValue)
                {
                    this.currentEnemy = newEnemyAI;
                    fearRange = newFearRange.Value;
                    calmDownTimer = 0f;
                    RestartPanikCoroutine(this.currentEnemy, fearRange.Value);
                    if (this.currentEnemy is JesterAI)
                    {
                        wasFleeingJester = true;
                        if ((Time.timeSinceLevelLoad - lastDeclaredJesterTimer) > 30f)
                        {
                            lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                            ai.SendChatMessage("JESTER!!! RUN!!!");
                        }
                    }
                }
                // else no fear range, ignore this enemy, already ignored by CheckLOSForEnemy but hey better be safe
            }

            // Are we waiting for the enemy to leave the entrance?
            if (calmDownTimer > 0f && !this.currentEnemy.isOutside)
            {
                // Check if we should end early!
                ai.StopMoving();
                if (ai.HasScrapInInventory())
                {
                    ai.State = new ReturnToShipState(this);
                }
                else if (previousState == EnumAIStates.ReturnToShip
                    || previousState == EnumAIStates.ChillAtShip)
                {
                    ai.State = new ReturnToShipState(this);
                }
                // Wait outside the door a bit before heading back in,
                // if we have been waiting for a bit give up and head back!
                else if (ShouldReturnToShip())
                {
                    ai.State = new ReturnToShipState(this);
                }
                else if (calmDownTimer > Const.FLEEING_CALM_DOWN_TIME + 60f)
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
            float sqrDistanceToEnemy = (npcController.Npc.transform.position - currentEnemy.transform.position).sqrMagnitude;
            if (this.currentEnemy is not JesterAI &&
                Physics.Linecast(currentEnemy.transform.position, npcController.Npc.gameplayCamera.transform.position,
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
                        this.currentEnemy = FindNearbyJester();
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
                    RestartPanikCoroutine(this.currentEnemy, fearRange.Value);
                }
            }

            // Why run when we can fight back!
            if (ai.HasCombatWeapon() && ai.CanEnemyBeKilled(this.currentEnemy))
            {
                ai.State = new FightEnemyState(this, this.currentEnemy, this.previousAIState);
                return;
            }

            // Look at the enemy if they are a coil head!
            if (this.currentEnemy is SpringManAI || this.currentEnemy is FlowermanAI)
            {
                npcController.OrderToLookAtPosition(this.currentEnemy.eye.position);
                npcController.SetTurnBodyTowardsDirectionWithPosition(this.currentEnemy.eye.position);
            }
            // Ok, there are three state indexes for nutcrackers to date!
            // 0. Patroling
            // 1. Scanning
            // 2. Hunting/Attacking
            else if (this.currentEnemy is NutcrackerEnemyAI && this.currentEnemy.currentBehaviourStateIndex == 1)
            {
                ai.StopMoving(); // Stand still, if we move, the nutcracker will see us!
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
                            RestartPanikCoroutine(this.currentEnemy, fearRange.Value);
                        }
                    }
                }
                else if (distSqrFromEntrance < Const.DISTANCE_NEARBY_ENTRANCE * Const.DISTANCE_NEARBY_ENTRANCE 
                    || this.currentEnemy is JesterAI
                    || !ai.PathIsIntersectedByLineOfSight(targetEntrance.entrancePoint.position, out _, false, true, this.currentEnemy))
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
                CanRepeatVoiceState = this.currentEnemy is not NutcrackerEnemyAI || this.currentEnemy.currentBehaviourStateIndex != 1, // Yeah, its a bit weird for them to keep screaming while standing in place....

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
            if (this.currentEnemy != null)
            { 
                RestartPanikCoroutine(this.currentEnemy, ai.GetFearRangeForEnemies(this.currentEnemy) ?? Const.DISTANCE_FLEEING); 
            }
        }

        // We are fleeing right now, these messages should be queued!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            // Return to the ship when we finish!
            if (message == "return")
            {
                previousAIState = new ReturnToShipState(this);
                return;
            }
            else if (message == "jester")
            {
                // Jester is a special case, we should not panic if we are already panicking!
                lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                if (currentEnemy is JesterAI || ai.isOutside)
                {
                    return;
                }
                EnemyAI? enemyAI = FindNearbyJester();
                if (enemyAI == null)
                {
                    return;
                }
                wasFleeingJester = true;
                this.currentEnemy = enemyAI;
                calmDownTimer = 0f;
                float? fearRange = ai.GetFearRangeForEnemies(this.currentEnemy);
                if (fearRange.HasValue)
                {
                    RestartPanikCoroutine(this.currentEnemy, fearRange.Value);
                    return;
                }
            }
            base.OnSignalTranslatorMessageReceived(message);
        }

        public override void OnPlayerChatMessageReceived(string message, PlayerControllerB playerWhoSentMessage, bool isVoice)
        {
            BotIntent intent = Plugin.DetectIntent(message);
            if (intent == BotIntent.Jester)
            {
                // Jester is a special case, we should not panic if we are already panicking!
                if (currentEnemy is JesterAI || ai.isOutside)
                {
                    return;
                }
                EnemyAI? enemyAI = FindNearbyJester();
                if (enemyAI == null)
                {
                    return;
                }
                wasFleeingJester = true;
                this.currentEnemy = enemyAI;
                calmDownTimer = 0f;
                float? fearRange = ai.GetFearRangeForEnemies(this.currentEnemy);
                if (fearRange.HasValue)
                {
                    RestartPanikCoroutine(this.currentEnemy, fearRange.Value);
                    if ((Time.timeSinceLevelLoad - lastDeclaredJesterTimer) > 30f)
                    {
                        lastDeclaredJesterTimer = Time.timeSinceLevelLoad;
                        ai.SendChatMessage("JESTER!!! RUN!!!");
                    }
                }
                return;
            }
            base.OnPlayerChatMessageReceived(message, playerWhoSentMessage, isVoice);
        }

        public override bool? ShouldBotCrouch()
        {
            return false;
        }

        public override void UseHeldItem()
        {
            // Can't use an item if our hands are empty!
            if (ai.AreHandsFree())
            {
                return;
            }
            GrabbableObject heldItem = ai.HeldItem;
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
            base.UseHeldItem();
        }

        public override string GetBillboardStateIndicator()
        {
            return @"/!\";
        }

        /// <summary>
        /// A class used by ChooseFleeingNodeFromPosition to asses the safety of a node
        /// </summary>
        private sealed class NodeSafety : IComparable<NodeSafety>, IEquatable<NodeSafety>
        {
            public GameObject node;
            public bool isPathOutOfSight;
            public bool isNodeOutOfSight;
            public bool isEnemyCloser;
            public int SafetyScore => (isPathOutOfSight ? 2 : 0) + (isNodeOutOfSight ? 1 : 0) - (isEnemyCloser ? 3 : 0);

            public NodeSafety(GameObject node, bool isPathOutOfSight, bool isNodeOutOfSight, bool isEnemyCloser)
            {
                this.node = node ?? throw new ArgumentNullException(nameof(node));
                this.isPathOutOfSight = isPathOutOfSight;
                this.isNodeOutOfSight = isNodeOutOfSight;
                this.isEnemyCloser = isEnemyCloser;
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

            public override string ToString()
            {
                return $"GameObject {node}, IsPathOutOfSight {isPathOutOfSight}, IsNodeOutOfSight {isNodeOutOfSight}, IsEnemyCloser {isEnemyCloser}";
            }

            public int CompareTo(NodeSafety? other)
            {
                // This is always greater than null
                if (other is null)
                {
                    return 1;
                }

                // Priority: enemy is further > path out of sight > node out of sight
                if (isEnemyCloser != other.isEnemyCloser)
                    return isEnemyCloser ? -1 : 1;

                if (isPathOutOfSight != other.isPathOutOfSight)
                    return isPathOutOfSight ? 1 : -1;

                if (isNodeOutOfSight != other.isNodeOutOfSight)
                    return isNodeOutOfSight ? 1 : -1;

                return 0;
            }

            public bool Equals(NodeSafety? other)
            {
                if (other is null)
                {
                    return false;
                }
                return node == other.node 
                    && isPathOutOfSight == other.isPathOutOfSight 
                    && isNodeOutOfSight == other.isNodeOutOfSight 
                    && isEnemyCloser == other.isEnemyCloser;
            }

            public override bool Equals(object obj)
            {
                return obj is NodeSafety nodeSafety && Equals(nodeSafety);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(node, isPathOutOfSight, isNodeOutOfSight, isEnemyCloser);
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
            Transform enemyTransform = enemy.transform;
            Vector3 enemyPos = enemyTransform.position;
            Vector3 viewPos = enemy.eye?.position ?? enemyPos;
            viewPos += Vector3.up * 0.2f; // Slightly above eye level to avoid ground clipping issues
            float ourDistanceFromEnemy = (enemyTransform.position - npcController.Npc.transform.position).sqrMagnitude;
            float headOffset = npcController.Npc.gameplayCamera.transform.position.y - npcController.Npc.transform.position.y;
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
            float bestNodeDistance = float.MaxValue;
            for (int i = 0; i < ai.allAINodes.Length; i++)
            {
                // Give the main thread a chance to do something else
                if (i % maxAsync == 0)
                {
                    // This feels like too much!
                    //yield return new WaitForSeconds(ai.AIIntervalTime);
                    yield return null;
                }

                // Check if the node is too close to the enemy
                var node = ai.allAINodes[i];
                if (node == null)
                {
                    continue;
                }

                // NEEDTOVALIDATE: Should I use the enemyAI and have it calculate a path to the node?
                // This would be more accurate since the enemy may not be able to path to the node!
                // And it would allow us to make much more accurate decisions!
                NodeSafety nodeSafety = new NodeSafety(node, true, true, false);
                Vector3 nodePos = nodeSafety.GetNodePosition();
                float sqrDistToEnemy = (nodePos - enemyPos).sqrMagnitude;

                // Skip if the node is too close to the enemy
                if (sqrDistToEnemy < fearRange * fearRange)
                {
                    continue;
                }

                // Check the enemy's distance from the node!
                float nodeDistToEnemy = 0f;
                if (enemy != null)
                {
                    // Check if they can even path there!
                    if (enemy.PathIsIntersectedByLineOfSight(nodePos, calculatePathDistance: true, false, false))
                    {
                        nodeDistToEnemy = -1f; // No path, thats REALLY good!
                    }
                    else
                    {
                        nodeDistToEnemy = enemy.pathDistance; // Alright, how far....
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

                    // Now we test if the node is visible to an enemy!
                    Vector3 simulatedHead = nodePos + Vector3.up * headOffset;
                    if (!Physics.Linecast(viewPos, simulatedHead, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        nodeSafety.isNodeOutOfSight = false;
                    }
                }

                // We cache the path distance since we are going to use it!
                // NOTE: We subtract the distance to the enemy since we want to pick a further node from said enemy
                // FIXME: This causes WAY too many problems, I have literally seen bots run IN FRONT of what they were supposed to be fleeing from
                // because the chosen node, although far away from the enemy, was out of sight, but the path wasn't.
                // It only really happens when the bot is out in the open though.
                //float pathDistance = ai.pathDistance - Mathf.Sqrt(sqrDistToEnemy);
                float pathDistance = ai.pathDistance;
                if (nodeDistToEnemy != -1 && pathDistance > nodeDistToEnemy)
                {
                    nodeSafety.isEnemyCloser = true; // We don't want to flee here, enemy is closer!
                }

                // Check if the node is a better candidate
                if (nodeSafety > bestNode || (nodeSafety >= bestNode && pathDistance < bestNodeDistance))
                {
                    bestNode = nodeSafety;
                    bestNodeDistance = pathDistance;
                }
            }

            if (bestNode != null)
            {
                // We found a node to run to!
                Plugin.LogDebug($"Found a node to run to: {bestNode} for {npcController.Npc.playerUsername}!");
                Plugin.LogDebug($"Distance to node: {bestNodeDistance} for {npcController.Npc.playerUsername}!");
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
            if (currentEnemy != null)
            {
                RetreatPos = null;
                panikCoroutine = null;
            }

            // no need for a loop I guess
            /*for (var i = 0; i < nodes.Length; i++)
            {
                Transform nodeTransform = nodes[i].transform;

                if ((nodeTransform.position - enemyTransform.position).sqrMagnitude < fearRange * fearRange)
                {
                    continue;
                }

                if (ai.PathIsIntersectedByLineOfSight(nodeTransform.position, false, true, this.currentEnemy))
                {
                    yield return null;
                    continue;
                }

                retreatPos = nodeTransform.position;
                panikCoroutine = null;
                yield break;
            }

            // If we failed to find a safe path, any will do at this point!
            // no need for a loop I guess
            for (var i = 0; i < nodes.Length; i++)
            {
                Transform nodeTransform = nodes[i].transform;

                if ((nodeTransform.position - enemyTransform.position).sqrMagnitude < fearRange * fearRange)
                {
                    continue;
                }

                if (!ai.IsVaildPathToTarget(nodeTransform.position, false))
                {
                    yield return null;
                    continue;
                }

                retreatPos = nodeTransform.position;
                panikCoroutine = null;
                yield break;
            }*/

        }

        /// <summary>
        /// Changes back to the previous state
        /// </summary>
        protected override void ChangeBackToPreviousState()
        {
            if (previousState == EnumAIStates.SearchingForScrap
                    || (previousState == EnumAIStates.FetchingObject && !ai.IsFollowingTargetPlayer()))
            {
                // If we have some scrap, it might be a good time to bring it back,
                // just in case.....
                if (ai.HasScrapInInventory())
                {
                    ai.State = new ReturnToShipState(this);
                    return;
                }
            }
            base.ChangeBackToPreviousState();
        }

        /// <summary>
        /// Find a an entrance we can path to so we can exit the main building!
        /// </summary>
        /// <remarks>
        /// We check if the bot can path to it since if the bot can't we could go into an infinite loop!
        /// FIXME: We also have to check if the exit position lets the bot reach the ship. Offence is a good example where the bots can't path down the fire exit!
        /// </remarks>
        /// <returns>The closest entrance or else null</returns>
        protected override EntranceTeleport? FindClosestEntrance(Vector3? shipPos = null, EntranceTeleport? entranceToAvoid = null)
        {
            // Don't do this logic if we are outside!
            if (ai.isOutside)
            {
                return null;
            }
            return base.FindClosestEntrance(shipPos, entranceToAvoid);
        }

        private void StartPanikCoroutine(EnemyAI currentEnemy, float fearRange)
        {
            panikCoroutine = ai.StartCoroutine(ChooseFleeingNodeFromPosition(currentEnemy, fearRange));
        }

        private void RestartPanikCoroutine(EnemyAI currentEnemy, float fearRange)
        {
            StopPanikCoroutine();
            StartPanikCoroutine(currentEnemy, fearRange);
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
