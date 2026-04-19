using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using LethalLib.Modules;
using System.Collections;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bots choose to wait at the ship
    /// They may go back in for another loot run if there is time!
    /// </summary>
    public class ChillAtShipState : AIState
    {
        private float chillAtShipTimer;
        private float leavePlanetTimer;
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
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            base.OnExitState(newState);
            npcController.StopPreformingEmote();
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

            // We are not at the ship, we should go back to it!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
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
            if (npcController.Npc.isInHangarShipRoom)
            {
                // If we are the mission controller, go to that state
                PlayerControllerB? missionController = LethalBotManager.Instance.MissionControlPlayer;
                if (missionController == npcController.Npc)
                {
                    ai.State = new MissionControlState(this);
                    return;
                }
                // Bot drop item
                else if (!ai.AreHandsFree() 
                    && FindObject(ai.HeldItem))
                {
                    if (!ai.TurnOffHeldItem())
                        ai.DropItem();
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
                else if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
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
                        LethalBotManager.Instance.MissionControlPlayer = npcController.Npc;
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

            // We wait at the ship for a bit before deciding to go out for more loot
            chillAtShipTimer += ai.AIIntervalTime;
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
            if (chillAtShipTimer > Const.TIMER_CHILL_AT_SHIP)
            {
                // Try to find the closest player to target
                PlayerControllerB? player = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (player != null
                    && !LethalBotManager.Instance.IsPlayerLethalBot(player)
                    && player != LethalBotManager.Instance.MissionControlPlayer
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
                    if (chillAtShipTimer > Const.TIMER_CHILL_AT_SHIP + 2f)
                    {
                        // Last time we were looking for scrap there was a trapped player,
                        // we should grab a key so we can potentially free them!
                        if (LethalBotManager.IsThereATrappedPlayer
                            && !ai.HasKeyInInventory())
                        {
                            GrabbableObject? key = ai.FindItemOnShip(foundItem => foundItem is KeyItem) ?? ai.FindItemOnShip(foundItem => foundItem is LockPicker);
                            if (key != null)
                            {
                                ai.State = new FetchingObjectState(this, key, EnumGrabbableObjectCall.Default, new SearchingForScrapState(this));
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
                            StartMatchLever startMatchLever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
                            if (startMatchLever != null)
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
            // Not really much for the bots to do here at the moment.
            // This will probably be changed in the future.
            return false;
        }

        /// <summary>
        /// Runs the company building logic for this state
        /// </summary>
        /// <returns></returns>
        private bool DoCompanyBuildingLogic()
        {
            if (chillAtShipTimer > Const.TIMER_CHILL_AT_SHIP_AT_COMPANY)
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
                            StartMatchLever startMatchLever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
                            if (startMatchLever != null)
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
