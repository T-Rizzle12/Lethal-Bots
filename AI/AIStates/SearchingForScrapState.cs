using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// A state where the bot is looking for scrap.
    /// </summary>
    public class SearchingForScrapState : AIState
    {
        private Coroutine? searchingWanderCoroutine = null;
        private bool grabbedLoadout;
        private bool formedGroup;
        private float scrapTimer;
        private float waitForSafePathTimer; // This is how long we have been waiting for a safe path to our target entrance.
        private int entranceAttempts; // This is how many times we spent going into the same entrance!

        public SearchingForScrapState(AIState oldState, EntranceTeleport? entranceToAvoid = null) : base(oldState)
        {
            CurrentState = EnumAIStates.SearchingForScrap;
            grabbedLoadout = false;
            formedGroup = false;
            entranceAttempts = 0;
            if (entranceToAvoid != null)
            {
                // If we are avoiding an entrance, we should set it as the target entrance
                this.targetEntrance = entranceToAvoid;
                waitForSafePathTimer = float.MaxValue; // HACKHACK: Set this to max, so when we start the state, we pick a new entrance!
            }
        }

        public SearchingForScrapState(LethalBotAI ai, EntranceTeleport? entranceToAvoid = null) : base(ai)
        {
            CurrentState = EnumAIStates.SearchingForScrap;
            grabbedLoadout = false;
            formedGroup = false;
            entranceAttempts = 0;
            if (entranceToAvoid != null)
            {
                // If we are avoiding an entrance, we should set it as the target entrance
                this.targetEntrance = entranceToAvoid;
                waitForSafePathTimer = float.MaxValue; // HACKHACK: Set this to max, so when we start the state, we pick a new entrance!
            }
        }

        public override void OnEnterState()
        {
            PlayerControllerB ourController = npcController.Npc;
            if (!hasBeenStarted)
            {
                // We are now searching for scrap and are no longer transferring loot!
                if (LethalBotManager.Instance.LootTransferPlayers.Contains(ourController))
                {
                    LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(ourController);
                }
            }

            // It doesn't matter if we had started the state before,
            // we should always recheck the nearest entrance
            EntranceTeleport? previousEntrance = this.targetEntrance;
            EntranceTeleport? entranceToAvoid = waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH ? previousEntrance : null;
            targetEntrance = FindClosestEntrance(entranceToAvoid: entranceToAvoid);
            entranceAttempts = targetEntrance == previousEntrance ? entranceAttempts : 0;
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // Sell scrap if we are at the company building!
            if (LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                ai.State = new CollectScrapToSellState(this);
                return;
            }

            // Wait for ship to land before doing anything!
            StartOfRound instanceSOR = StartOfRound.Instance;
            if ((npcController.Npc.isInElevator || npcController.Npc.isInHangarShipRoom)
                && !LethalBotManager.AreWeInOrbit(instanceSOR)
                && (LethalBotManager.IsTheShipLeaving(instanceSOR)
                    || !LethalBotManager.IsTheShipLanded(instanceSOR)))
            {
                return;
            }

            // Make sure to grab our loadout before leaving!
            if (!grabbedLoadout)
            {
                grabbedLoadout = true;
                ai.State = new GrabLoadoutState(this);
                return;
            }

            // Create or join our assigned internal group
            if (!formedGroup)
            {
                // Only check this once!
                formedGroup = true;
                int internalGroupID = ai.LethalBotIdentity.GroupID;
                if (internalGroupID != GroupManager.INVALID_GROUP_INDEX && !GroupManager.Instance.IsPlayerInGroup(npcController.Npc))
                {
                    // Only join our predefined group if we are near the group leader
                    if (GroupManager.Instance.DoesInternalGroupExist(ai, out int existingGroupID))
                    {
                        PlayerControllerB? groupLeader = GroupManager.Instance.GetGroupLeader(existingGroupID);
                        if (groupLeader != null)
                        {
                            float sqrHorizontalDistanceWithTarget = Vector3.Scale((groupLeader.transform.position - npcController.Npc.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
                            float sqrVerticalDistanceWithTarget = Vector3.Scale((groupLeader.transform.position - npcController.Npc.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
                            if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR
                                    && sqrVerticalDistanceWithTarget < Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER)
                            {
                                GroupManager.Instance.CreateOrJoinGroupAndSync(npcController.Npc);
                            }
                        }
                    }
                    else
                    {
                        GroupManager.Instance.CreateOrJoinGroupAndSync(npcController.Npc);
                    }
                    return;
                }
            }

            // Start coroutine for wandering
            StartSearchingWanderCoroutine();

            // Start coroutine for looking around
            StartLookingAroundCoroutine();

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
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive();
            if (playerController != null)
            {
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // Check to see if we can heal someone!
            playerController = ai.LookingForPlayerToHeal();
            if (playerController != null)
            {
                ai.State = new HealPlayerState(this, playerController);
                return;
            }

            // Check for object to grab
            int groupID = GroupManager.Instance.GetGroupId(npcController.Npc);
            if (!IsInventoryFull(groupID))
            {
                GrabbableObject? grabbableObject = ai.LookingForObjectToGrab();
                if (grabbableObject != null && ai.HasSpaceInInventory(grabbableObject))
                {
                    scrapTimer = 0f; // Reset this since we found an object!
                    ai.State = new FetchingObjectState(this, grabbableObject);
                    return;
                }
            }
            else
            {
                // If our inventory is full, return to the ship to drop our stuff off
                // Now, lets check if someone is assigned to transfer loot
                bool shouldWalkLootToShip = true;
                if (!ai.isOutside && LethalBotManager.Instance.LootTransferPlayers.Count > 0)
                {
                    shouldWalkLootToShip = false;
                }

                ai.State = new ReturnToShipState(this, !shouldWalkLootToShip);
                return;
            }

            // Use items in our inventory based on the current situation
            SelectBestItemFromInventory();

            // Group logic
            if (groupID != GroupManager.INVALID_GROUP_INDEX)
            {
                // The group leader does their best to make sure no one falls behind.......
                PlayerControllerB? groupLeader = GroupManager.Instance.GetGroupLeader(groupID);
                if (groupLeader == npcController.Npc)
                {
                    // NOTE: We can safely assume that groupLeader is the same as npcController.Npc here
                    PlayerControllerB? straggler = GroupManager.Instance.GetFurthestMemberFromCenter(groupID);
                    if (straggler != null && !ai.AreWeExposed()) // straggler != groupLeader // actually, we allow ourself since the rest of the group may have fallen behind
                    {
                        Vector3 groupCenter = GroupManager.Instance.GetGroupCenter(groupLeader, groupID);
                        float sqrHorizontalDistanceWithTarget = Vector3.Scale((groupCenter - straggler.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
                        float sqrVerticalDistanceWithTarget = Vector3.Scale((groupCenter - straggler.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
                        if (sqrHorizontalDistanceWithTarget > Const.MAX_STRAGGLER_DISTANCE_HOR * Const.MAX_STRAGGLER_DISTANCE_HOR
                            || sqrVerticalDistanceWithTarget > Const.MAX_STRAGGLER_DISTANCE_VER * Const.MAX_STRAGGLER_DISTANCE_VER)
                        {
                            if (ai.searchForScrap.searchInProgress)
                            {
                                // Stop the coroutine while we wait for the player or bot who fell behind
                                ai.searchForScrap.StopSearch();
                            }
                            ai.StopMoving();
                            return;
                        }
                    }
                }
                // Only the group leader should be in the searching for scrap state
                else if (groupLeader != null)
                {
                    // Cheat a little here and let the bot have perfect knowledge of where their group leader is.....
                    float sqrHorizontalDistanceWithTarget = Vector3.Scale((groupLeader.transform.position - npcController.Npc.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
                    float sqrVerticalDistanceWithTarget = Vector3.Scale((groupLeader.transform.position - npcController.Npc.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
                    if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR
                            && sqrVerticalDistanceWithTarget < Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER)
                    {
                        ai.State = new GetCloseToPlayerState(this, groupLeader);
                        return;
                    }
                    else
                    {
                        GroupManager.Instance.RemoveFromCurrentGroupAndSync(npcController.Npc);
                    }
                }
                // This should never happen, but you never know......
                else
                {
                    GroupManager.Instance.RemoveFromCurrentGroupAndSync(npcController.Npc);
                }
            }

            // If we are outside, we need to move inside first!
            if (ai.isOutside)
            {
                // Hold on a minute, we should empty our inventory first!
                // NOTE: If a player leaves scrap by an entrance, the bot will collect
                // as much as they can and return to the ship as a result!
                if (ai.HasScrapInInventory() || (groupID != GroupManager.INVALID_GROUP_INDEX && GroupManager.Instance.DoesGroupHaveScrap(groupID)))
                {
                    // Now, lets check if someone is assigned to transfer loot
                    bool shouldWalkLootToShip = true;
                    if (LethalBotManager.Instance.LootTransferPlayers.Count > 0)
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
                            if (heldItem != null && FindObject(heldItem))
                            {
                                ai.DropItem();
                                LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                                shouldWalkLootToShip = false;
                            }
                            else if (ai.HasGrabbableObjectInInventory(FindObject, out int objectSlot))
                            {
                                ai.SwitchItemSlotsAndSync(objectSlot);
                                shouldWalkLootToShip = false;
                            }
                        }
                    }

                    // Only return if we are supposed to walk the loot to the ship!
                    if (shouldWalkLootToShip)
                    {
                        ai.State = new ReturnToShipState(this);
                    }
                    return;
                }

                // If we don't have an entrance selected we should pick one now!
                if (targetEntrance == null 
                    || waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH 
                    || entranceAttempts > Const.MAX_ENTRANCE_ATTEMPTS)
                {
                    EntranceTeleport? entranceToAvoid = (waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH || entranceAttempts > Const.MAX_ENTRANCE_ATTEMPTS) ? this.targetEntrance : null;
                    targetEntrance = FindClosestEntrance(entranceToAvoid: entranceToAvoid);
                    waitForSafePathTimer = 0f;
                    entranceAttempts = 0;
                    if (targetEntrance == null)
                    {
                        // If we fail to find an entrance we should return to the ship!
                        ai.State = new ReturnToShipState(this);
                        return;
                    }
                }

                // Find a safe path to the entrance
                StartSafePathCoroutine();

                // If we are close enough, we should use the entrance to enter
                float entranceDistSqr = (targetEntrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                if (entranceDistSqr >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    float sqrMagDistanceToSafePos = (this.safePathPos - npcController.Npc.transform.position).sqrMagnitude;
                    if (sqrMagDistanceToSafePos >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                    {
                        // Alright lets go inside!
                        waitForSafePathTimer = Mathf.Max(waitForSafePathTimer - ai.AIIntervalTime, 0f);
                        ai.SetDestinationToPositionLethalBotAI(safePathPos);
                        ai.OrderMoveToDestination();
                    }
                    else
                    {
                        // Wait here until its safe to move
                        waitForSafePathTimer += ai.AIIntervalTime;
                        ai.StopMoving();
                        npcController.OrderToStopSprint();
                    }
                }
                // Check for teleport entrance
                else if (Time.timeSinceLevelLoad - ai.TimeSinceTeleporting > Const.WAIT_TIME_TO_TELEPORT)
                {
                    EntranceTeleport entrance = targetEntrance;
                    Vector3? entranceTeleportPos = ai.GetTeleportPosOfEntrance(entrance);
                    if (entranceTeleportPos.HasValue)
                    {
                        ai.StopMoving();
                        if (!IsEntranceSafe(entrance))
                        {
                            waitForSafePathTimer += ai.AIIntervalTime;
                            return; // We should not use the entrance if the entrance is not safe!
                        }
                        if (LethalBotInteraction == null || LethalBotInteraction.IsCompleted)
                        {
                            ref InteractTrigger interactTrigger = ref PatchesUtil.triggerScriptField.Invoke(entrance);
                            LethalBotInteraction = new LethalBotInteraction(interactTrigger, (lethalBotAI, lethalBotController, _) =>
                            {
                                Plugin.LogDebug($"======== TeleportLethalBotAndSync {lethalBotController.playerUsername} !!!!!!!!!!!!!!! ");
                                lethalBotAI.SyncTeleportLethalBot(entranceTeleportPos.Value, !entrance?.isEntranceToBuilding ?? !lethalBotAI.isOutside, entrance);
                            }, skipOriginalInteract: true);
                        }
                        entranceAttempts++;
                    }
                    else
                    {
                        // HOW DID THIS HAPPEN!!!!
                        ai.State = new ReturnToShipState(this);
                        return;
                    }
                }
            }
            else
            {
                // Don't need this anymore!
                StopSafePathCoroutine();

                // The bot should return after not finding any other scrap for a bit,
                // after all we don't want to lose what we have by leaving too late!
                if (ai.HasScrapInInventory())
                {
                    if (scrapTimer > Const.TIMER_SEARCH_FOR_SCRAP)
                    {
                        // Now, lets check if someone is assigned to transfer loot
                        bool shouldWalkLootToShip = true;
                        if (LethalBotManager.Instance.LootTransferPlayers.Count > 0)
                        {
                            shouldWalkLootToShip = false;
                        }
                        ai.State = new ReturnToShipState(this, !shouldWalkLootToShip);
                        return;
                    }
                    scrapTimer += ai.AIIntervalTime;
                }
                else
                {
                    scrapTimer = 0f;
                }

                // Now that we are inside, lets go find some loot
                // If we need to go down an elevator we should do so!
                MineshaftElevatorController? elevator = LethalBotAI.ElevatorScript;
                if (elevator != null && (ai.IsInElevatorStartRoom || (!elevator.elevatorFinishedMoving || !elevator.elevatorDoorOpen) && ai.IsInsideElevator))
                {
                    if (ai.searchForScrap.searchInProgress)
                    {
                        // Stop the coroutine while we use the elevator
                        ai.searchForScrap.StopSearch();
                    }
                    ai.UseElevator(false);
                }
                else
                {
                    // If there is a player trapped in the facility,
                    // we should unlock all doors we can find to help them out!
                    if (LethalBotManager.IsThereATrappedPlayer)
                    {
                        DoorLock? lockedDoor = ai.UnlockDoorIfNeeded(200f, false);
                        if (lockedDoor != null)
                        {
                            ai.State = new UseKeyOnLockedDoorState(this, lockedDoor);
                            return;
                        }
                    }
                    // If we encounter a locked door, we should unlock it!
                    else
                    {
                        DoorLock? lockedDoor = ai.UnlockDoorIfNeeded(Const.LETHAL_BOT_OBJECT_RANGE, true, Const.LETHAL_BOT_OBJECT_AWARNESS);
                        if (lockedDoor != null)
                        {
                            ai.State = new UseKeyOnLockedDoorState(this, lockedDoor);
                            return;
                        }
                    }

                    // Lets get ourselves some loot
                    Vector3? destination = ai.searchForScrap.GetTargetPosition();
                    if (destination != null)
                    {
                        ai.SetDestinationToPositionLethalBotAI(destination.Value);
                        ai.OrderMoveToDestination();
                    }

                    if (!ai.searchForScrap.searchInProgress)
                    {
                        // Start the coroutine to search for loot
                        ai.searchForScrap.StartSearch();
                    }
                }
            }
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopSearchingWanderCoroutine();
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

        /// <summary>
        /// Simple function that checks if the give <paramref name="item"/> is scrap.
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected override bool FindObject(GrabbableObject item)
        {
            return LethalBotAI.IsItemScrap(item) && (!ai.IsGrabbableObjectInLoadout(item) || ai.HasDuplicateLoadoutItems(item, out _)); // Found a scrap item, great, we want to drop it!
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

        /// <remarks>
        /// We ignore the intital danger check if we are close to the entrance!
        /// </remarks>
        /// <inheritdoc cref="AIState.ShouldIgnoreInitialDangerCheck"></inheritdoc>
        protected override bool ShouldIgnoreInitialDangerCheck()
        {
            if (this.targetEntrance != null)
            {
                float distSqrToEntrance = (this.targetEntrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                return distSqrToEntrance < Const.DISTANCE_NEARBY_ENTRANCE * Const.DISTANCE_NEARBY_ENTRANCE;
            }
            return base.ShouldIgnoreInitialDangerCheck();
        }

        /// <summary>
        /// Checks if the bot's inventory is full.
        /// </summary>
        /// <remarks>
        /// This considers the bot's group as well.
        /// </remarks>
        /// <returns></returns>

        private bool IsInventoryFull(int? groupID)
        {
            groupID ??= GroupManager.Instance.GetGroupId(npcController.Npc);
            if (groupID != GroupManager.INVALID_GROUP_INDEX)
            {
                List<PlayerControllerB> groupMembers = GroupManager.Instance.GetGroupMembers(groupID.Value);
                GrabbableObject[]? itemSlots = null;
                foreach (PlayerControllerB member in groupMembers)
                {
                    // Make sure this member is valid
                    if (member == null) continue;

                    // Grab this member's inventory
                    itemSlots = member.ItemSlots;
                    int inventorySize = LethalBotAI.GetInventorySize(member, itemSlots);
                    for (int i = 0; i < inventorySize; i++)
                    {
                        if (itemSlots[i] == null)
                        {
                            return false; // Someone has space in their inventory.
                        }
                    }
                }

                // Group is full, we should head back now.
                return true;
            }

            return !ai.HasSpaceInInventory();
        }

        /// <summary>
        /// Coroutine for when searching, alternate between sprinting and walking
        /// </summary>
        /// <remarks>
        /// The other coroutine <see cref="LethalBotSearchRoutine.StartSearch"><c>LethalBotSearchRoutine.StartSearch</c></see>, already take care of choosing node to walk to.
        /// </remarks>
        /// <returns></returns>
        private IEnumerator SearchingWander()
        {
            yield return null;
            while (ai.State != null
                    && ai.State == this)
            {
                float sprintTimeRandom = Random.Range(Const.MIN_TIME_SPRINT_SEARCH_WANDER, Const.MAX_TIME_SPRINT_SEARCH_WANDER);
                npcController.OrderToSprint();
                yield return new WaitForSeconds(sprintTimeRandom);

                sprintTimeRandom = Random.Range(Const.MIN_TIME_SPRINT_SEARCH_WANDER, Const.MAX_TIME_SPRINT_SEARCH_WANDER);
                npcController.OrderToStopSprint();
                yield return new WaitForSeconds(sprintTimeRandom);
            }

            searchingWanderCoroutine = null;
        }

        private void StartSearchingWanderCoroutine()
        {
            if (this.searchingWanderCoroutine == null)
            {
                this.searchingWanderCoroutine = ai.StartCoroutine(this.SearchingWander());
            }
        }

        private void StopSearchingWanderCoroutine()
        {
            if (this.searchingWanderCoroutine != null)
            {
                ai.StopCoroutine(this.searchingWanderCoroutine);
                this.searchingWanderCoroutine = null;
            }
        }
    }
}
