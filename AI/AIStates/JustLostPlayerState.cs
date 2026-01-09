using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using System.Collections;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot cannot see the target player and try to reach their last known (seen) position
    /// </summary>
    public class JustLostPlayerState : AIState
    {
        private float lookingAroundTimer;

        /// <summary>
        /// <inheritdoc cref="AIState(AIState)"/>
        /// </summary>
        public JustLostPlayerState(AIState state) : base(state)
        {
            CurrentState = EnumAIStates.JustLostPlayer;
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Looking around for too long, stop the coroutine, the target player is officially lost
            if (lookingAroundTimer > Const.TIMER_LOOKING_AROUND)
            {
                lookingAroundTimer = 0f;
                targetLastKnownPosition = null;

                StopLookingAroundCoroutine();
            }

            // If the looking around timer is started
            // Start of the coroutine for making the bot looking around him
            if (lookingAroundTimer > 0f)
            {
                lookingAroundTimer += ai.AIIntervalTime;
                Plugin.LogDebug($"{ai.NpcController.Npc.playerUsername} Looking around to find player {lookingAroundTimer}");
                ai.StopMoving();

                StartLookingAroundCoroutine();

                CheckLOSForTargetAndGetClose();

                return;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive();
            if (playerController != null)
            {
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // Check for object to grab
            if (ai.HasSpaceInInventory())
            {
                GrabbableObject? grabbableObject = ai.LookingForObjectToGrab();
                if (grabbableObject != null)
                {
                    ai.State = new FetchingObjectState(this, grabbableObject);
                    return;
                }
            }

            // Try to reach target last known position
            if (!targetLastKnownPosition.HasValue)
            {
                ai.State = new SearchingForPlayerState(this);
                return;
            }

            Plugin.LogDebug($"{npcController.Npc.playerUsername} distance to last position {Vector3.Distance(targetLastKnownPosition.Value, npcController.Npc.transform.position)}");
            // If the bot is close enough to the last known position
            float sqrDistanceToTargetLastKnownPosition = (targetLastKnownPosition.Value - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistanceToTargetLastKnownPosition < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
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
                    EntranceTeleport? entrance = ai.IsEntranceCloseForBoth(targetLastKnownPosition.Value, npcController.Npc.transform.position);
                    Vector3? entranceTeleportPos = ai.GetTeleportPosOfEntrance(entrance);
                    if (entranceTeleportPos.HasValue)
                    {
                        Plugin.LogDebug($"======== TeleportLethalBotAndSync {ai.NpcController.Npc.playerUsername} !!!!!!!!!!!!!!! ");
                        ai.StopMoving();
                        ai.SyncTeleportLethalBot(entranceTeleportPos.Value, !entrance?.isEntranceToBuilding ?? !ai.isOutside, entrance);
                        targetLastKnownPosition = ai.targetPlayer.transform.position;
                    }
                    else
                    {
                        // Start looking around
                        if (lookingAroundTimer == 0f)
                        {
                            lookingAroundTimer += ai.AIIntervalTime;
                        }

                        return;
                    }
                }
            }

            // Check if we see the target player
            // Or a new target player if target player is null
            CheckLOSForTargetOrClosestPlayer();

            // Go to the last known position
            bool usingElevator = false;
            bool isPositionNearEntrance = ai.IsPositionNearElevatorEntrance(targetLastKnownPosition.Value);
            if (isPositionNearEntrance && !ai.IsInElevatorStartRoom)
            {
                usingElevator = ai.UseElevator(true);

                // If we are going to use the elevator to go up,
                // we must drop the baby maneater before using the elevator
                if (usingElevator
                && !ai.AreHandsFree()
                && ai.HeldItem is CaveDwellerPhysicsProp)
                {
                    ai.DropItem();
                }
            }
            else if (!isPositionNearEntrance && ai.IsInElevatorStartRoom)
            {
                usingElevator = ai.UseElevator(false);
            }
            else
            {
                ai.SetDestinationToPositionLethalBotAI(targetLastKnownPosition.Value);
                ai.OrderMoveToDestination();
            }

            // Sprint if too far, unsprint if close enough
            if (sqrDistanceToTargetLastKnownPosition < Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION * Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION)
            {
                npcController.OrderToStopSprint();
            }
            else
            {
                npcController.OrderToSprint();
            }

            // Destination after path checking might be not the same now
            targetLastKnownPosition = ai.destination;
            if (!usingElevator && !ai.IsValidPathToTarget(targetLastKnownPosition.Value))
            {
                targetLastKnownPosition = null;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.LosingPlayer,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = false,
                CutCurrentVoiceStateToTalk = true,
                CanRepeatVoiceState = false,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        public override void PlayerHeard(Vector3 noisePosition)
        {
            // Go towards the sound heard
            this.targetLastKnownPosition = noisePosition;
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.HearsPlayer,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = false,
                CutCurrentVoiceStateToTalk = true,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        // We are following a player, these messages mean nothing to us!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            return;
        }

        public override string GetBillboardStateIndicator()
        {
            return "!?";
        }

        /// <summary>
        /// Check if the target player is in line of sight
        /// </summary>
        private void CheckLOSForTargetAndGetClose()
        {
            PlayerControllerB? target = ai.CheckLOSForTarget(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (target != null)
            {
                // Target found
                StopLookingAroundCoroutine();
                targetLastKnownPosition = target.transform.position;

                // Voice
                ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                {
                    VoiceState = EnumVoicesState.LostAndFound,
                    CanTalkIfOtherLethalBotTalk = false,
                    WaitForCooldown = false,
                    CutCurrentVoiceStateToTalk = true,

                    ShouldSync = true,
                    IsLethalBotInside = npcController.Npc.isInsideFactory,
                    AllowSwearing = Plugin.Config.AllowSwearing.Value
                });

                ai.State = new GetCloseToPlayerState(this);
                return;
            }
        }

        private void CheckLOSForTargetOrClosestPlayer()
        {
            if (ai.targetPlayer == null)
            {
                PlayerControllerB? newTarget = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (newTarget != null)
                {
                    // new target
                    ai.SyncAssignTargetAndSetMovingTo(newTarget);
                    if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
                    {
                        ai.ChangeSuitLethalBotServerRpc(npcController.Npc.playerClientId, newTarget.currentSuitID);
                    }
                }
            }
            else
            {
                CheckLOSForTargetAndGetClose();
            }
        }

        /// <summary>
        /// Coroutine for making bot turn his body to look around him
        /// </summary>
        /// <returns></returns>
        protected override IEnumerator LookingAround()
        {
            yield return null;
            while (lookingAroundTimer < Const.TIMER_LOOKING_AROUND)
            {
                float freezeTimeRandom = Random.Range(Const.MIN_TIME_FREEZE_LOOKING_AROUND, Const.MAX_TIME_FREEZE_LOOKING_AROUND);
                float angleRandom = Random.Range(-180, 180);
                npcController.SetTurnBodyTowardsDirection(Quaternion.Euler(0, angleRandom, 0) * npcController.Npc.thisController.transform.forward);
                yield return new WaitForSeconds(freezeTimeRandom);
            }
        }
    }
}
