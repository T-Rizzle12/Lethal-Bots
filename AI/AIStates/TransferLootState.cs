using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// A state where the bot is assigned to transfer loot from the entrances of the facility back to the players' ship.
    /// </summary>
    public class TransferLootState : AIState
    {
        private float waitTimer;
        private List<EntranceTeleport> checkedEntrances = new List<EntranceTeleport>();

        public TransferLootState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.TransferLoot;
            checkedEntrances.Clear();
        }

        public TransferLootState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.TransferLoot;
            checkedEntrances.Clear();
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                // We are at the company building, change back to previous state
                if (LethalBotManager.AreWeAtTheCompanyBuilding())
                {
                    if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
                    {
                        LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
                    }
                    ChangeBackToPreviousState();
                    return;
                }
                else if (!LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
                {
                    LethalBotManager.Instance.AddPlayerToLootTransferListAndSync(npcController.Npc);
                }
            }
            targetEntrance = FindClosestEntrance(entrancesToAvoid: checkedEntrances);
            waitTimer = 0f;
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Return to the ship if needed!
            if (ShouldReturnToShip())
            {
                LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc); // Remove from loot transfer list, we are done for the day!
                ai.State = new ReturnToShipState(this);
                return;
            }

            // We are at the company building, change back to previous state
            if (LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
                {
                    LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
                }
                ChangeBackToPreviousState();
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
            else
            {
                // If our inventory is full, return to the ship to drop our stuff off
                ai.State = new ReturnToShipState(this);
                return;
            }

            // If we are inside, make the bot go outside so we can transfer loot to the ship
            if (!ai.isOutside)
            {
                ai.State = new ReturnToShipState(this, true); // Tell the state to end if outside
                return;
            }

            // Alright, we are outside, lets head over to the entrance to transfer loot
            if (targetEntrance == null || !targetEntrance.isEntranceToBuilding)
            {
                // Alright, reset the list of checked entrances and find the closest one again
                checkedEntrances.Clear();
                targetEntrance = FindClosestEntrance();
                if (targetEntrance == null || !targetEntrance.isEntranceToBuilding)
                {
                    ai.State = new ReturnToShipState(this); // No entrance found, return to ship
                    return;
                }
            }

            // Find a safe path to the entrance
            StartSafePathCoroutine();

            // Look around for loot and potential enemies
            StartLookingAroundCoroutine();

            // Move towards our target entrance via safe path
            // NOTE: Unlike other states, we rely entirely on the safe path system to avoid danger here.
            // Since we normally wait outside near the entrance, we need to be able to react to danger that may pass by.
            // Safe path system will handle this while we wait for more loot to transfer.
            float sqrMagDistanceToSafePos = (this.safePathPos - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrMagDistanceToSafePos >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                // Alright lets go transfer some loot!
                ai.SetDestinationToPositionLethalBotAI(safePathPos);

                // Sprint if far enough from the ship
                if (!npcController.WaitForFullStamina && sqrMagDistanceToSafePos > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else
                {
                    npcController.OrderToStopSprint();
                }

                ai.OrderMoveToDestination();
                waitTimer = Mathf.Max(waitTimer - ai.AIIntervalTime, 0f); // Slowly decrease wait timer in case we got shoved away from entrance
            }
            else
            {
                // Wait here until its safe to move
                // If we arrived at our target entrance, we will wait until someone drops off more loot
                ai.StopMoving();
                npcController.OrderToStopSprint();

                float sqrMagDistanceToEntrance = (targetEntrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                if (sqrMagDistanceToEntrance <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    // We are at the entrance, wait for loot to arrive
                    waitTimer += ai.AIIntervalTime;
                    if (waitTimer >= Const.TRANSFER_LOOT_MAX_WAIT_TIME)
                    {
                        // No loot at our target entrance, lets check the other entrances
                        // NOTE: Bots will cycle through all entrances before returning to ship.
                        waitTimer = 0f;
                        EntranceTeleport? previousEntrance = targetEntrance;
                        checkedEntrances.Add(previousEntrance); // Mark this entrance as checked
                        if (ai.HasSomethingInInventory())
                        {
                            // We have something in our inventory, return to ship to drop it off
                            ai.State = new ReturnToShipState(this);
                            return;
                        }

                        // Find another entrance to check out
                        targetEntrance = FindClosestEntrance(entrancesToAvoid: checkedEntrances);
                        if (targetEntrance != null 
                            && !checkedEntrances.Contains(targetEntrance) 
                            && previousEntrance != targetEntrance)
                        {
                            // Found another entrance to check out, head over there
                            return;
                        }
                        // No other entrance found
                        else
                        {
                            // Nothing in inventory, reset target entrance and checked list to start over
                            targetEntrance = null;
                            checkedEntrances.Clear(); // Reset checked entrances
                            checkedEntrances.Add(previousEntrance); // Mark previous entrance as checked to avoid immediately going back
                            return;
                        }
                    }
                }
                else
                {
                    // We are not at the entrance yet, reset wait timer
                    // Slowly decrease wait timer in case we got shoved away from entrance
                    waitTimer = Mathf.Max(waitTimer - ai.AIIntervalTime, 0f);
                }
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.FollowingPlayer,
                CanTalkIfOtherLethalBotTalk = false,
                WaitForCooldown = true,
                CutCurrentVoiceStateToTalk = false,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        public override void OnPlayerChatMessageReceived(string message, PlayerControllerB playerWhoSentMessage, bool isVoice)
        {
            // We are already transferring loot, no need to respond to transfer loot messages
            if (message.Contains("transfer loot"))
            {
                return;
            }
            base.OnPlayerChatMessageReceived(message, playerWhoSentMessage, isVoice);
        }

        /// <remarks>
        /// We give the position of the entrance we want a safe path to!<br/>
        /// We return null if we are not outside or our target entrance is null!
        /// </remarks>
        /// <inheritdoc cref="AIState.GetDesiredSafePathPosition"></inheritdoc>
        protected override Vector3? GetDesiredSafePathPosition()
        {
            if (this.targetEntrance == null || !ai.isOutside)
            {
                return null;
            }
            return this.targetEntrance.entrancePoint.position;
        }
    }
}
