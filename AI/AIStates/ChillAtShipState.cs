using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using LethalLib.Modules;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static LethalLib.Modules.ContentLoader;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bots choose to wait at the ship
    /// They may go back in for another loot run if there is time!
    /// </summary>
    public class ChillAtShipState : AIState
    {
        private static readonly HashSet<int> allowedSeats = new HashSet<int>()
        {
            29, // Sofa Chair
            32 // Electric Chair
        };

        private IntervalTimer chillAtShipTimer = new IntervalTimer();
        private float leavePlanetTimer;
        private SeatTarget? targetSeat = null;

        public ChillAtShipState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.ChillAtShip;
        }
        public ChillAtShipState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.ChillAtShip;
        }

        public override void OnEnterState()
        {
            // Unless we are a group leader, entering this state means we are chilling at the ship by ourself now
            PlayerControllerB ourController = npcController.Npc;
            int groupID = GroupManager.Instance.GetGroupId(ourController);
            if (groupID != GroupManager.INVALID_GROUP_INDEX && GroupManager.Instance.GetGroupLeader(groupID) != ourController)
            {
                GroupManager.Instance.RemoveFromCurrentGroupAndSync(ourController);
            }
            targetSeat = null;
            chillAtShipTimer.Reset();
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            base.OnExitState(newState);
            npcController.StopPreformingEmote();
            if (targetSeat?.seatTrigger != null)
            {
                targetSeat.seatTrigger.CancelAnimationExternally(); // Stop using the seat
            }
            targetSeat = null;
        }

        public override void DoAI()
        {
            // Check for enemies
            PlayerControllerB lethalBotController = npcController.Npc;
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // We are not at the ship, we should go back to it!
            if (!lethalBotController.isInElevator && !lethalBotController.isInHangarShipRoom)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // If we are holding an item with a battery, we should charge it!
            if (ChargeHeldItemState.HasItemToCharge(ai, out _))
            {
                ai.State = new ChargeHeldItemState(this, true, new ReturnToShipState(this));
                return;
            }

            // If we are holding anything we should drop it
            bool canInverseTeleport = true;
            if (lethalBotController.isInHangarShipRoom)
            {
                // If we are the mission controller, go to that state
                PlayerControllerB? missionController = LethalBotManager.Instance.MissionControlPlayer;
                if (missionController == lethalBotController)
                {
                    ai.State = new MissionControlState(this);
                    return;
                }
                // Bot drop item
                else if (!ai.AreHandsFree() 
                    && FindObject(ai.HeldItem))
                {
                    if (!ai.TurnOffHeldItem())
                        lethalBotController.DiscardHeldObject();
                    canInverseTeleport = false;
                }
                // If we still have stuff in our inventory,
                // we should swap to it and drop it!
                else if (ai.HasGrabbableObjectInInventory(FindObject, out int objectSlot))
                {
                    ai.SwitchItemSlotsAndSync(objectSlot);
                    canInverseTeleport = false;
                }
                // If we are transferring loot, go back to that state
                else if (LethalBotManager.Instance.LootTransferPlayers.Contains(lethalBotController))
                {
                    // We finished dropping our stuff off, go back to transferring loot!
                    if (previousAIState is TransferLootState)
                    {
                        ChangeBackToPreviousState(); // We were transferring loot before, go back to it
                    }
                    else
                    {
                        ai.State = new TransferLootState(this); // Go to transferring loot state
                    }
                    return;
                }
                // If there is no mission controller, or its dead, we should be it!
                else if (Plugin.Config.AllowBotsInOrbit.Value || !StartOfRound.Instance.shipIsLeaving)
                {
                    if ((missionController == null || !missionController.isPlayerControlled || missionController.isPlayerDead) 
                        && (Plugin.Config.AllowBotsInOrbit.Value || !LethalBotManager.AreWeInOrbit())
                        && Plugin.Config.AutoMissionControl.Value)
                    {
                        LethalBotManager.Instance.MissionControlPlayer = lethalBotController;
                        canInverseTeleport = false;
                    }
                }
            }

            // HACKHACK: Piggyback off of canInverseTeleport before we can look for players to revive!
            if (canInverseTeleport)
            {
                // Does someone need to be saved?
                PlayerControllerB? playerController = ai.LookingForPlayerToRevive(true, true);
                if (playerController != null)
                {
                    ai.State = new RescueAndReviveState(this, playerController);
                    return;
                }

                // Were there items recently purchased?
                if (CollectPurchasedItemsState.IsPossible())
                {
                    ai.State = new CollectPurchasedItemsState(this);
                    return;
                }

                // Does someone need to be healed?
                playerController = ai.LookingForPlayerToHeal(true, true);
                if (playerController != null)
                {
                    ai.State = new HealPlayerState(this, playerController);
                    return;
                }
            }

            // Is the inverse teleporter on, we should use it!
            if (LethalBotManager.IsInverseTeleporterActive 
                && canInverseTeleport)
            {
                ai.State = new UseInverseTeleporterState(this);
                return;
            }

            // Run logic based on what phase of the game we are in.
            if (LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                if (DoCompanyBuildingLogic())
                {
                    return;
                }
            }
            else if (LethalBotManager.AreWeInOrbit())
            {
                if (DoOrbitLogic())
                {
                    return;
                }
            }
            else
            {
                if (DoStandardLogic())
                {
                    return;
                }
            }

            // Chill
            ai.StopMoving();

            // Emotes
            npcController.PerformRandomEmote();
        }

        public override bool? ShouldBotCrouch()
        {
            bool? originalResult = base.ShouldBotCrouch();
            if (originalResult.HasValue)
            {
                return originalResult;
            }
            return false;
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.Chilling,
                CanTalkIfOtherLethalBotTalk = false,
                WaitForCooldown = true,
                CutCurrentVoiceStateToTalk = false,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are chilling at the ship, this message means nothing to us!
            SignalTranslatorCommandsManager.RegisterCommandForState<ChillAtShipState>(new SignalTranslatorCommand(Const.RETURN_COMMAND, (state, lethalBotAI, message) =>
            {
                return true;
            }));
        }

        /// <summary>
        /// Runs the default logic for this state.
        /// </summary>
        /// <returns></returns>
        private bool DoStandardLogic()
        {
            if (chillAtShipTimer.IsGreaterThan(Plugin.Config.ChillAtShipTime))
            {
                // Try to find the closest player to target
                PlayerControllerB? player = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (player != null
                    && !LethalBotManager.Instance.IsPlayerLethalBot(player)
                    && player != LethalBotManager.Instance.MissionControlPlayer
                    && !LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc)
                    && !GroupManager.Instance.IsPlayerGroupLeader(npcController.Npc, out _)) // new target
                {
                    // Don't compromise the ship by being loud!
                    if (!ai.CheckProximityForEyelessDogs())
                    {
                        // Play voice
                        ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                        {
                            VoiceState = EnumVoicesState.LostAndFound,
                            CanTalkIfOtherLethalBotTalk = true,
                            WaitForCooldown = false,
                            CutCurrentVoiceStateToTalk = true,
                            CanRepeatVoiceState = false,

                            ShouldSync = true,
                            IsLethalBotInside = npcController.Npc.isInsideFactory,
                            AllowSwearing = Plugin.Config.AllowSwearing.Value
                        });
                    }

                    // We are following a human player, leave our current group or join theirs!
                    GroupManager.Instance.CreateOrJoinGroupWithMembersAndSync(player, new PlayerControllerB[] { npcController.Npc });

                    // Assign to new target
                    ai.SyncAssignTargetAndSetMovingTo(player);
                    if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
                    {
                        ai.ChangeSuitLethalBotServerRpc(npcController.Npc.playerClientId, player.currentSuitID);
                    }
                    return true;
                }

                // If its getting late out, we should stay at the ship!
                if (!ShouldReturnToShip())
                {
                    // So, we are done chilling and didn't find a player to follow, so lets go in by ourselves
                    // A player can press their +use key on us to make us follow them!
                    if (chillAtShipTimer.IsGreaterThan(Plugin.Config.ChillAtShipTime + 2f))
                    {
                        // Last time we were looking for scrap there was a trapped player,
                        // we should grab a key so we can potentially free them!
                        if (LethalBotManager.IsThereATrappedPlayer
                            && !ai.HasKeyInInventory())
                        {
                            GrabbableObject? key = ai.FindItemOnShip(foundItem => foundItem is KeyItem) ?? ai.FindItemOnShip(foundItem => foundItem is LockPicker);
                            if (key != null)
                            {
                                ai.State = new FetchingObjectState(this, key, changeToOnEnd: new SearchingForScrapState(this));
                                return true;
                            }
                        }
                        ai.State = new SearchingForScrapState(this);
                        return true;
                    }
                }
                else if (LethalBotManager.Instance.AreAllHumanPlayersDead()
                && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                {
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                    {
                        StartOfRound instanceSOR = npcController.Npc.playersManager;
                        if ((LethalBotManager.IsTheShipLanded(instanceSOR) || LethalBotManager.AreWeInOrbit(instanceSOR))
                            && !LethalBotManager.IsTheShipLeaving(instanceSOR))
                        {
                            if (SingletonManager.StartMatchLevel.TryGet(out StartMatchLever? startMatchLever))
                            {
                                ai.PullShipLever(startMatchLever);
                            }
                            //npcController.Npc.playersManager.ShipLeaveAutomatically(true);
                        }
                    }
                    else
                    {
                        leavePlanetTimer += ai.AIIntervalTime;
                    }
                }
                else
                {
                    leavePlanetTimer = 0f;
                }
            }
            return false;
        }

        /// <summary>
        /// Runs the orbit logic for this state
        /// </summary>
        /// <returns></returns>
        private bool DoOrbitLogic()
        {
            // If we have a chosen seat, we should move to sit in it
            if (targetSeat != null)
            {
                // Make sure the seat is still valid
                var parentObject = targetSeat.parentObject;
                if (parentObject == null || parentObject.disableObject)
                {
                    targetSeat = null;
                    return false;
                }

                // Check if we are in our target seat
                PlayerControllerB lethalBotController = npcController.Npc;
                InteractTrigger seatTrigger = targetSeat.seatTrigger;
                if (seatTrigger.playerScriptInSpecialAnimation == lethalBotController)
                {
                    return true; // We do nothing more
                }

                // Move to our target seat
                float sqrDistFromSeat = (seatTrigger.playerPositionNode.position - lethalBotController.transform.position).sqrMagnitude;
                if (sqrDistFromSeat > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    // Move, now!
                    ai.OrderMoveToDestination();
                    ai.SetDestinationToPositionLethalBotAI(seatTrigger.playerPositionNode.position);
                }
                else
                {
                    // Interact with the seat
                    if (!ai.IsLethalBotInteracting() 
                        && !ai.StartInteraction(seatTrigger))
                    {
                        targetSeat = null; // If we failed for some reason, give up
                    }
                }

                // Block default movement
                return true;
            }
            // Have a chance to sit in random furniture
            else if (chillAtShipTimer.IsGreaterThan(Const.TIMER_CHILL_AT_SHIP))
            {
                chillAtShipTimer.Reset(); // Reset the Timer

                // Do we choose to sit in something
                // For now a 20% chance
                if (UnityEngine.Random.Range(1, 101) < 20)
                {
                    // Find all of the purchased objects on the ship
                    StartOfRound instanceSOR = StartOfRound.Instance;
                    PlaceableShipObject[] placeableShipObjects = Object.FindObjectsByType<PlaceableShipObject>(FindObjectsSortMode.None);
                    for (int i = 0; i < placeableShipObjects.Length; i++)
                    {
                        // Make sure its valid
                        var shipObject = placeableShipObjects[i];
                        if (shipObject != null 
                            && allowedSeats.Contains(shipObject.unlockableID))
                        {
                            // Make sure its root object is valid
                            var parentObject = shipObject.parentObject;
                            if (parentObject != null 
                                && !parentObject.disableObject)
                            {
                                // Check if this object is a seat
                                InteractTrigger[] interactTriggers = parentObject.gameObject.GetComponentsInChildren<InteractTrigger>();
                                for (int j = 0; j < interactTriggers.Length; j++)
                                {
                                    // Check if this object has a seat interact trigger
                                    var trigger = interactTriggers[j];
                                    if (trigger != null 
                                        && trigger.stopAnimationManually
                                        && trigger.interactable
                                        && trigger.playerScriptInSpecialAnimation == null)
                                    {
                                        targetSeat = new SeatTarget(shipObject, trigger); // Found one!
                                        break;
                                    }
                                }

                                // End early if we found a seat
                                if (targetSeat != null)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private sealed class SeatTarget
        {
            public PlaceableShipObject shipObject;
            public AutoParentToShip? parentObject => shipObject != null ? shipObject.parentObject : null;
            public InteractTrigger seatTrigger;

            public SeatTarget(PlaceableShipObject shipObject, InteractTrigger seatTrigger)
            {
                this.shipObject = shipObject;
                this.seatTrigger = seatTrigger;
            }
        }

        /// <summary>
        /// Runs the company building logic for this state
        /// </summary>
        /// <returns></returns>
        private bool DoCompanyBuildingLogic()
        {
            if (chillAtShipTimer.IsGreaterThan(Const.TIMER_CHILL_AT_SHIP_AT_COMPANY))
            {
                // If we are at the company building, we should sell!
                if (ai.LookingForObjectsToSell(true) != null || LethalBotManager.AreThereItemsOnDesk())
                {
                    ai.State = new CollectScrapToSellState(this);
                    return true;
                }
                else if (LethalBotManager.Instance.AreAllHumanPlayersDead()
                && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                {
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                    {
                        StartOfRound instanceSOR = npcController.Npc.playersManager;
                        if ((LethalBotManager.IsTheShipLanded(instanceSOR) || LethalBotManager.AreWeInOrbit(instanceSOR))
                            && !LethalBotManager.IsTheShipLeaving(instanceSOR))
                        {
                            if (SingletonManager.StartMatchLevel.TryGet(out StartMatchLever? startMatchLever))
                            {
                                ai.PullShipLever(startMatchLever);
                            }
                        }
                    }
                    else
                    {
                        leavePlanetTimer += ai.AIIntervalTime;
                    }
                }
                else
                {
                    leavePlanetTimer = 0f;
                }
            }
            return false;
        }
    }
}
