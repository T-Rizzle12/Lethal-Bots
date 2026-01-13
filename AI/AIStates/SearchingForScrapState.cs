using DunGen;
using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System.Collections;
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
        private float scrapTimer;
        private float waitForSafePathTimer; // This is how long we have been waiting for a safe path to our target entrance.
        private int entranceAttempts; // This is how many times we spent going into the same entrance!

        public SearchingForScrapState(AIState oldState, EntranceTeleport? entranceToAvoid = null) : base(oldState)
        {
            CurrentState = EnumAIStates.SearchingForScrap;
            grabbedLoadout = false;
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
            if (!hasBeenStarted)
            {
                // We are now searching for scrap and are no longer transferring loot!
                if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
                {
                    LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
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
            if ((npcController.Npc.isInElevator || npcController.Npc.isInHangarShipRoom)
                && (StartOfRound.Instance.shipIsLeaving
                    || !StartOfRound.Instance.shipHasLanded))
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

            // Check for object to grab
            if (ai.HasSpaceInInventory())
            {
                GrabbableObject? grabbableObject = ai.LookingForObjectToGrab();
                if (grabbableObject != null)
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

            // If we are outside, we need to move inside first!
            if (ai.isOutside)
            {
                // Hold on a minute, we should empty our inventory first!
                // NOTE: If a player leaves scrap by an entrance, the bot will collect
                // as much as they can and return to the ship as a result!
                if (ai.HasScrapInInventory())
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
                    Vector3? entranceTeleportPos = ai.GetTeleportPosOfEntrance(targetEntrance);
                    if (entranceTeleportPos.HasValue)
                    {
                        Plugin.LogDebug($"======== TeleportLethalBotAndSync {ai.NpcController.Npc.playerUsername} !!!!!!!!!!!!!!! ");
                        ai.StopMoving();
                        if (!IsEntranceSafe(targetEntrance))
                        {
                            waitForSafePathTimer += ai.AIIntervalTime;
                            return; // We should not use the entrance if the entrance is not safe!
                        }
                        ai.SyncTeleportLethalBot(entranceTeleportPos.Value, !this.targetEntrance?.isEntranceToBuilding ?? !ai.isOutside, this.targetEntrance);
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
                if (LethalBotAI.ElevatorScript != null && ai.IsInElevatorStartRoom)
                {
                    if (searchForScrap.inProgress)
                    {
                        // Stop the coroutine while we use the elevator
                        ai.StopSearch(searchForScrap, false);
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
                    ai.SetDestinationToPositionLethalBotAI(ai.destination);
                    ai.OrderMoveToDestination();

                    if (!searchForScrap.inProgress)
                    {
                        // Start the coroutine from base game to search for loot
                        ai.StartSearch(npcController.Npc.transform.position, searchForScrap);
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

        /// <summary>
        /// Coroutine for making bot turn his body to look around him
        /// </summary>
        /// <returns></returns>
        //protected override IEnumerator LookingAround()
        //{
        //    yield return null;
        //    while (ai.State != null
        //            && ai.State == this)
        //    {
        //        float freezeTimeRandom = Random.Range(Const.MIN_TIME_SEARCH_LOOKING_AROUND, Const.MAX_TIME_SEARCH_LOOKING_AROUND);
        //        float angleRandom = Random.Range(0f, 360f);

        //        // Only look around if we are already not doing so!
        //        if (npcController.LookAtTarget.IsLookingForward())
        //        {
        //            // Convert angle to world position for looking
        //            // Convert to local space (relative to the bot's forward direction)
        //            Vector3 lookDirection = Quaternion.Euler(0, angleRandom, 0) * Vector3.forward;
        //            float minLookDistance = 2f; // TODO: Move these into the Const class!
        //            float maxLookDistance = 8f;
        //            float lookDistance = Random.Range(minLookDistance, maxLookDistance); // Hardcoded for now
        //            Vector3 lookAtPoint = npcController.Npc.gameplayCamera.transform.position + lookDirection * lookDistance;

        //            // Ensure bot doesn’t look at unreachable areas (optional raycast check)
        //            if (Physics.Raycast(npcController.Npc.thisController.transform.position, lookDirection, out RaycastHit hit, lookDistance, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        //            {
        //                lookAtPoint = hit.point; // Adjust to the first obstacle it hits
        //            }

        //            // Use OrderToLookAtPosition as SetTurnBodyTowardsDirection can be overriden!
        //            npcController.OrderToLookAtPosition(lookAtPoint);
        //        }
        //        yield return new WaitForSeconds(freezeTimeRandom);
        //    }

        //    lookingAroundCoroutine = null;
        //}

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
        /// Coroutine for when searching, alternate between sprinting and walking
        /// </summary>
        /// <remarks>
        /// The other coroutine <see cref="EnemyAI.StartSearch"><c>EnemyAI.StartSearch</c></see>, already take care of choosing node to walk to.
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
