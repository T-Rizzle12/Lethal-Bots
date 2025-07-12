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
        private Coroutine? lookingAroundCoroutine = null;
        private Coroutine? trappedPlayerCheckCoroutine = null;
        private float scrapTimer;
        private float waitForSafePathTimer;
        public static bool IsThereATrappedPlayer { private set; get; }

        public SearchingForScrapState(AIState oldState, EntranceTeleport? entranceToAvoid = null) : base(oldState)
        {
            CurrentState = EnumAIStates.SearchingForScrap;
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
            if (entranceToAvoid != null)
            {
                // If we are avoiding an entrance, we should set it as the target entrance
                this.targetEntrance = entranceToAvoid;
                waitForSafePathTimer = float.MaxValue; // HACKHACK: Set this to max, so when we start the state, we pick a new entrance!
            }
        }

        public override void OnEnterState()
        {
            // It doesn't matter if we had started the state before,
            // we should always recheck the nearest entrance
            EntranceTeleport? entranceToAvoid = waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH ? this.targetEntrance : null;
            targetEntrance = FindClosestEntrance(entranceToAvoid: entranceToAvoid);
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
                ai.State = new ReturnToShipState(this);
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
                    ai.State = new ReturnToShipState(this);
                    return;
                }

                // If we don't have an entrace selected we should pick one now!
                if (targetEntrance == null 
                    || waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH)
                {
                    EntranceTeleport? entranceToAvoid = waitForSafePathTimer > Const.WAIT_TIME_FOR_SAFE_PATH ? this.targetEntrance : null;
                    targetEntrance = FindClosestEntrance(entranceToAvoid: entranceToAvoid);
                    waitForSafePathTimer = 0f;
                    if (targetEntrance == null)
                    {
                        // If we fail to find an entrance we should return to the ship!
                        ai.State = new ReturnToShipState(this);
                        return;
                    }
                }

                // Find a safe path to the entrance
                StartSafePathCoroutine();

                // Don't need this anymore!
                StopTrappedPlayerCoroutine();

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

                // Start checking for trapped players!
                StartTrappedPlayerCoroutine();

                // The bot should return after not finding any other scrap for a bit,
                // after all we don't want to lose what we have by leaving too late!
                if (ai.HasScrapInInventory())
                {
                    if (scrapTimer > Const.TIMER_SEARCH_FOR_SCRAP)
                    {
                        ai.State = new ReturnToShipState(this);
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
                if (ai.ElevatorScript != null && ai.IsInElevatorStartRoom)
                {
                    if (searchForScrap.inProgress)
                    {
                        // Stop the coroutine while we use the elevator
                        ai.StopSearch(searchForScrap);
                    }
                    ai.UseElevator(false);
                }
                else
                {
                    // If there is a player trapped in the facility,
                    // we should unlock all doors we can find to help them out!
                    if (IsThereATrappedPlayer)
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
            StopLookingAroundCoroutine();
            StopTrappedPlayerCoroutine();
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
        /// A function that checks if the player can path to the exit!
        /// </summary>
        /// <remarks>
        /// This is basically and advanced call to <see cref="NavMesh.CalculatePath(Vector3, Vector3, int, NavMeshPath)"/> with mutliple checks
        /// to make sure the path is complete!
        /// </remarks>
        /// <param name="player">The player to test</param>
        /// <returns>true: if there is a valid path, false: if there is no valid path</returns>
        private bool CanPlayerPathToExit(PlayerControllerB player)
        {
            // Setup some variables
            Vector3 startPosition = RoundManager.Instance.GetNavMeshPosition(player.transform.position, RoundManager.Instance.navHit, 2.7f);
            LethalBotAI? isPlayerBot = LethalBotManager.Instance.GetLethalBotAI(player);
            bool isOutside = isPlayerBot != null ? isPlayerBot.isOutside : !player.isInsideFactory;
            int areaMask = isPlayerBot != null ? isPlayerBot.agent.areaMask : NavMesh.AllAreas;
            NavMeshPath path = new NavMeshPath();
            foreach (var entrance in ai.EntrancesTeleportArray)
            {
                if (((isOutside && entrance.isEntranceToBuilding)
                        || (!isOutside && !entrance.isEntranceToBuilding)) 
                    && entrance.FindExitPoint())
                {
                    // Check if we can create a path there first!
                    Vector3 exitPosition = RoundManager.Instance.GetNavMeshPosition(entrance.entrancePoint.position, RoundManager.Instance.navHit, 2.7f);
                    if (!IsValidPathToEntrance(startPosition, exitPosition, areaMask, ref path, entrance))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Helper function that checks if we can path to the main entrance!
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="exitPosition"></param>
        /// <param name="areaMask"></param>
        /// <param name="path"></param>
        /// <param name="targetEntrance"></param>
        /// <returns></returns>
        private bool IsValidPathToEntrance(Vector3 startPosition, Vector3 exitPosition, int areaMask, ref NavMeshPath path, EntranceTeleport targetEntrance)
        {
            // Check if we can path to the entrance!
            if (!LethalBotAI.IsValidPathToTarget(startPosition, exitPosition, areaMask, ref path))
            {
                // Check if this is the front entrance if we need to use an elevator
                if (IsFrontEntrance(targetEntrance) && ai.ElevatorScript != null)
                {
                    // Check if we can path to the bottom of the elevator
                    if (LethalBotAI.IsValidPathToTarget(startPosition, ai.ElevatorScript.elevatorBottomPoint.position, areaMask, ref path))
                    {
                        return true;
                    }

                    // Check if they are inside the elevator!
                    return (startPosition - ai.ElevatorScript.elevatorInsidePoint.position).sqrMagnitude < 2f * 2f;
                }
                return false;
            }
            return true;
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

        // Should this be in the main AI instead?
        private IEnumerator trappedPlayerCheck()
        {
            yield return null;
            while (ai.State != null
                    && ai.State.GetAIState() == EnumAIStates.SearchingForScrap)
            {
                // Check if there is a player trapped in the facility
                bool foundTrappedPlayer = false;
                StartOfRound instance = StartOfRound.Instance;
                for (int i = 0; i < instance.allPlayerScripts.Length; i++)
                {
                    PlayerControllerB player = instance.allPlayerScripts[i];
                    if (player.isPlayerControlled && !player.isPlayerDead && player.isInsideFactory)
                    {
                        // Check if the player is trapped
                        if (!CanPlayerPathToExit(player))
                        {
                            foundTrappedPlayer = true;
                            break;
                        }
                        yield return null; // Give the main thread a chance to do something else
                    }
                }

                IsThereATrappedPlayer = foundTrappedPlayer;

                yield return new WaitForSeconds(Const.TIMER_CHECK_FOR_TRAPPED_PLAYER);
            }

            trappedPlayerCheckCoroutine = null;
        }

        private void StartTrappedPlayerCoroutine()
        {
            if (trappedPlayerCheckCoroutine == null)
            {
                trappedPlayerCheckCoroutine = ai.StartCoroutine(trappedPlayerCheck());
            }
        }

        private void StopTrappedPlayerCoroutine()
        {
            if (trappedPlayerCheckCoroutine != null)
            {
                ai.StopCoroutine(trappedPlayerCheckCoroutine);
                trappedPlayerCheckCoroutine = null;
            }
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
                    && ai.State.GetAIState() == EnumAIStates.SearchingForScrap)
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

        /// <summary>
        /// Coroutine for making bot turn his body to look around him
        /// </summary>
        /// <returns></returns>
        private IEnumerator LookingAround()
        {
            yield return null;
            while (ai.State != null
                    && ai.State.GetAIState() == EnumAIStates.SearchingForScrap)
            {
                float freezeTimeRandom = Random.Range(Const.MIN_TIME_SEARCH_LOOKING_AROUND, Const.MAX_TIME_SEARCH_LOOKING_AROUND);
                float angleRandom = Random.Range(0f, 360f);

                // Only look around if we are already not doing so!
                if (npcController.LookAtTarget.IsLookingForward())
                {
                    // Convert angle to world position for looking
                    // Convert to local space (relative to the bot's forward direction)
                    Vector3 lookDirection = Quaternion.Euler(0, angleRandom, 0) * Vector3.forward;
                    float minLookDistance = 2f; // TODO: Move these into the Const class!
                    float maxLookDistance = 8f;
                    float lookDistance = Random.Range(minLookDistance, maxLookDistance); // Hardcoded for now
                    Vector3 lookAtPoint = npcController.Npc.gameplayCamera.transform.position + lookDirection * lookDistance;

                    // Ensure bot doesn’t look at unreachable areas (optional raycast check)
                    if (Physics.Raycast(npcController.Npc.thisController.transform.position, lookDirection, out RaycastHit hit, lookDistance))
                    {
                        lookAtPoint = hit.point; // Adjust to the first obstacle it hits
                    }

                    // Use OrderToLookAtPosition as SetTurnBodyTowardsDirection can be overriden!
                    npcController.OrderToLookAtPosition(lookAtPoint);
                }
                yield return new WaitForSeconds(freezeTimeRandom);
            }

            lookingAroundCoroutine = null;
        }

        private void StartLookingAroundCoroutine()
        {
            if (this.lookingAroundCoroutine == null)
            {
                this.lookingAroundCoroutine = ai.StartCoroutine(this.LookingAround());
            }
        }

        private void StopLookingAroundCoroutine()
        {
            if (this.lookingAroundCoroutine != null)
            {
                ai.StopCoroutine(this.lookingAroundCoroutine);
                this.lookingAroundCoroutine = null;
            }
        }
    }
}
