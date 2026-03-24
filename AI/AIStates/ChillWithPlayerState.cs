using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using Steamworks.Ugc;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// The state when the AI is close to the owner player
    /// </summary>
    /// <remarks>
    /// When close to the player, the chill state makes the bot stop moving and looking at them,
    /// check for items to grab or enemies to flee, waiting for the player to move. 
    /// </remarks>
    public class ChillWithPlayerState : AIState
    {
        private CountdownTimer entranceDropTimer = new CountdownTimer();

        /// <summary>
        /// Represents the distance between the body of bot (<c>PlayerControllerB</c> position) and the target player (owner of bot), 
        /// only on axis x and z, y at 0, and squared
        /// </summary>
        private float SqrHorizontalDistanceWithTarget
        {
            get
            {
                return Vector3.Scale((ai.targetPlayer.transform.position - npcController.Npc.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
            }
        }

        /// <summary>
        /// Represents the distance between the body of bot (<c>PlayerControllerB</c> position) and the target player (owner of bot), 
        /// only on axis y, x and z at 0, and squared
        /// </summary>
        private float SqrVerticalDistanceWithTarget
        {
            get
            {
                return Vector3.Scale((ai.targetPlayer.transform.position - npcController.Npc.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
            }
        }

        /// <summary>
        /// <inheritdoc cref="AIState(LethalBotAI)"/>
        /// </summary>
        public ChillWithPlayerState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.ChillWithPlayer;
        }

        /// <summary>
        /// <inheritdoc cref="AIState(AIState)"/>
        /// </summary>
        public ChillWithPlayerState(AIState state) : base(state)
        {
            CurrentState = EnumAIStates.ChillWithPlayer;
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

            // Check for object to grab
            // Or drop in ship room
            bool canInverseTeleport = true;
            if (npcController.Npc.isInHangarShipRoom)
            {
                // If we are holding an item with a battery, we should charge it!
                if (ChargeHeldItemState.HasItemToCharge(ai, out _))
                {
                    canInverseTeleport = false;
                    ai.State = new ChargeHeldItemState(this, true);
                    return;
                }

                // Bot drop item
                GrabbableObject? heldItem = ai.HeldItem;
                if (heldItem != null && FindObject(heldItem))
                {
                    canInverseTeleport = false;
                    if (!ai.TurnOffHeldItem())
                        ai.DropItem();
                }
                // If we still have stuff in our inventory,
                // we should swap to it and drop it!
                else if (ai.HasGrabbableObjectInInventory(FindObject, out int objectSlot))
                {
                    canInverseTeleport = false;
                    ai.SwitchItemSlotsAndSync(objectSlot);
                }
            }
            else if (ai.HasSpaceInInventory())
            {
                GrabbableObject? grabbableObject = ai.LookingForObjectToGrab();
                if (grabbableObject != null)
                {
                    ai.State = new FetchingObjectState(this, grabbableObject);
                    return;
                }
                if (!ai.searchForScrap.visitInProgress && !ai.isOutside)
                {
                    ai.searchForScrap.StartVisit();
                }
            }
            else if (ai.searchForScrap.visitInProgress)
            {
                ai.searchForScrap.StopSearch();
            }

            // If we are in a group, only follow the group leader
            int groupID = GroupManager.Instance.GetGroupId(npcController.Npc);
            if (groupID != GroupManager.INVALID_GROUP_INDEX)
            {
                PlayerControllerB? groupLeader = GroupManager.Instance.GetGroupLeader(groupID);
                if (groupLeader != null)
                {
                    if (groupLeader == npcController.Npc)
                    {
                        ai.State = new SearchingForScrapState(this);
                        return;
                    }
                    else if (ai.targetPlayer != groupLeader)
                    {
                        ai.targetPlayer = groupLeader;
                        targetLastKnownPosition = groupLeader.transform.position;
                    }
                }
                // This should never happen, but if it does......
                else
                {
                    GroupManager.Instance.RemoveFromCurrentGroupAndSync(npcController.Npc);
                }
            }

            // If the player we were following is dropping off loot at an entrance, then lets do the same!
            if (ai.isOutside && (!entranceDropTimer.HasStarted() || entranceDropTimer.Elapsed()) && ai.HasScrapInInventory())
            {
                // Now, lets check if someone is assigned to transfer loot
                // NOTE: If the player we are following is transfering loot, then we don't drop ours!
                entranceDropTimer.Start(0.5f); // Only do this every half a second!
                if (LethalBotManager.Instance.LootTransferPlayers.Count > 0 
                    && !LethalBotManager.Instance.LootTransferPlayers.Contains(ai.targetPlayer))
                {
                    bool areWeNearbyEntrance = false;
                    foreach (EntranceTeleport entrance in LethalBotAI.EntrancesTeleportArray)
                    {
                        if (entrance.isEntranceToBuilding
                            && (entrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude < Const.DISTANCE_NEARBY_ENTRANCE * Const.DISTANCE_NEARBY_ENTRANCE)
                        {
                            areWeNearbyEntrance = true;
                            break;
                        }
                    }

                    if (areWeNearbyEntrance)
                    {
                        // Stop moving while we drop our items
                        ai.StopMoving();
                        npcController.OrderToStopSprint();

                        GrabbableObject? heldItem = ai.HeldItem;
                        if (heldItem != null && DropScrapAtEntrance(heldItem))
                        {
                            ai.DropItem();
                            LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                        }
                        else if (ai.HasGrabbableObjectInInventory(DropScrapAtEntrance, out int objectSlot))
                        {
                            ai.SwitchItemSlotsAndSync(objectSlot);
                        }
                    }
                }
                return;
            }

            VehicleController? vehicleController = ai.GetVehicleCruiserTargetPlayerIsIn();
            if (vehicleController != null)
            {
                ai.State = new PlayerInCruiserState(this, vehicleController);
                return;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive();
            if (playerController != null)
            {
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // Select and use items based on our current situation, if needed
            SelectBestItemFromInventory();

            // Update target last known position
            PlayerControllerB? playerTarget = ai.CheckLOSForTarget(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (playerTarget != null)
            {
                targetLastKnownPosition = ai.targetPlayer.transform.position;
            }

            // Target too far, get close to him
            // note: not the same distance to compare in horizontal or vertical distance
            if (SqrHorizontalDistanceWithTarget > Const.DISTANCE_CLOSE_ENOUGH_HOR * Const.DISTANCE_CLOSE_ENOUGH_HOR
                || SqrVerticalDistanceWithTarget > Const.DISTANCE_CLOSE_ENOUGH_VER * Const.DISTANCE_CLOSE_ENOUGH_VER)
            {
                npcController.OrderToLookForward();
                ai.State = new GetCloseToPlayerState(this);
                return;
            }

            // Is the inverse teleporter on, we should use it!
            if (LethalBotManager.IsInverseTeleporterActive && npcController.Npc.isInHangarShipRoom && canInverseTeleport)
            {
                ai.State = new UseInverseTeleporterState(this);
                return;
            }

            // Set where the bot should look
            SetBotLookAt();

            // Chill
            ai.StopMoving();

            // Emotes
            npcController.MimicEmotes(ai.targetPlayer);
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

        public override void PlayerHeard(Vector3 noisePosition)
        {
            // Look at origin of sound
            SetBotLookAt(noisePosition);
        }

        /// <inheritdoc cref="AIState.RegisterChatCommands"/>
        public static new void RegisterChatCommands()
        {
            ChatCommandsManager.RegisterCommandForState<ChillWithPlayerState>(new ChatCommand(Const.GEAR_UP_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                lethalBotAI.State = new GrabLoadoutState(state);
                return true;
            }));
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are following a player, these messages mean nothing to us!
            SignalTranslatorCommandsManager.RegisterIgnoreDefaultForState<ChillWithPlayerState>();
        }

        private void SetBotLookAt(Vector3? position = null)
        {
            if (Plugin.InputActionsInstance.MakeBotLookAtPosition.IsPressed())
            {
                LookAtWhatPlayerPointingAt();
            }
            else
            {
                if (position.HasValue)
                {
                    npcController.OrderToLookAtPosition(position.Value + new Vector3(0, 2.35f, 0), priority: EnumLookAtPriority.HIGH_PRIORITY, 1.0f);
                }
                else
                {
                    // Looking at player or forward
                    PlayerControllerB? playerToLook = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                    if (playerToLook != null)
                    {
                        npcController.OrderToLookAtPlayer(playerToLook);
                    }
                    else
                    {
                        npcController.OrderToLookForward();
                    }
                }
            }
        }

        private void LookAtWhatPlayerPointingAt()
        {
            // Look where the target player is looking
            Ray interactRay = new Ray(ai.targetPlayer.gameplayCamera.transform.position, ai.targetPlayer.gameplayCamera.transform.forward);
            RaycastHit[] raycastHits = Physics.RaycastAll(interactRay);
            if (raycastHits.Length == 0)
            {
                npcController.OrderToLookForward();
            }
            else
            {
                // Check if looking at a player/bot
                foreach (var hit in raycastHits)
                {
                    PlayerControllerB? player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                    if (player != null
                        && player.playerClientId != StartOfRound.Instance.localPlayerController.playerClientId)
                    {
                        npcController.OrderToLookAtPosition(hit.point, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime);
                        return;
                    }
                }

                // Check if looking too far in the distance or at a valid position
                foreach (var hit in raycastHits)
                {
                    if (hit.distance < 0.1f)
                    {
                        npcController.OrderToLookForward();
                        return;
                    }

                    PlayerControllerB? player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                    if (player != null && player.playerClientId == StartOfRound.Instance.localPlayerController.playerClientId)
                    {
                        continue;
                    }

                    // Look at position
                    npcController.OrderToLookAtPosition(hit.point, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime);
                    break;
                }
            }
        }

        /// <summary>
        /// Find and drop items that are no longer needed
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        protected override bool FindObject(GrabbableObject item)
        {
            if (ai.IsGrabbableObjectInLoadout(item) && !ai.HasDuplicateLoadoutItems(item, out _))
            {
                return false;
            }
            return Plugin.Config.DropHeldEquipmentAtShip || LethalBotAI.IsItemScrap(item);
        }

        /// <summary>
        /// Simple function that checks if the give <paramref name="item"/> is scrap.
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected bool DropScrapAtEntrance(GrabbableObject item)
        {
            return LethalBotAI.IsItemScrap(item) && (!ai.IsGrabbableObjectInLoadout(item) || ai.HasDuplicateLoadoutItems(item, out _)); // Found a scrap item, great, we want to drop it!
        }
    }
}
