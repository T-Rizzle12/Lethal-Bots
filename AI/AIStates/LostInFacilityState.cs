using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.AIStates
{
    public class LostInFacilityState : AIState
    {
        private Coroutine searchingWanderCoroutine = null!;
        private float findEntranceTimer;
        private readonly LethalBotSearchRoutine stuckSearch = new();

        public LostInFacilityState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.LostInFacility;
            stuckSearch.searchCenterFollowsBot = true;
            stuckSearch.nodeChance = 0.65f;
        }

        public LostInFacilityState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.LostInFacility;
            stuckSearch.searchCenterFollowsBot = true;
            stuckSearch.nodeChance = 0.65f;
        }

        public override void DoAI()
        {
            // Start coroutine for wandering
            StartSearchingWanderCoroutine();

            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive();
            if (playerController != null)
            {
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // TODO: If bot finds a key, bot should drop least valuable item in inventory and pick the key up

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

            if (ai.isOutside)
            {
                // If we are outside, we should not be in this state
                Plugin.LogError($"Bot {npcController.Npc.playerUsername} is outside but in LostInFacilityState. This should not happen!");
                ChangeBackToPreviousState();
                return;
            }

            // If there is an entrance we can path to, we will change state to exit the facility\
            if (findEntranceTimer > Const.FLEEING_UPDATE_ENTRANCE)
            {
                findEntranceTimer = 0f;
                EntranceTeleport? potentalExit = FindClosestEntrance();
                if (potentalExit != null)
                {
                    ChangeBackToPreviousState();
                    return;
                }
            }
            else
            {
                findEntranceTimer += ai.AIIntervalTime; // Don't do this every frame, we could lag the game if we do!
            }

            // Find a door that might help us escape!!!!
            DoorLock? lockedDoor = ai.UnlockDoorIfNeeded(200f, false);
            if (lockedDoor != null)
            {
                findEntranceTimer = float.MaxValue; // Make sure we check as soon as we finish!
                ai.State = new UseKeyOnLockedDoorState(this, lockedDoor);
                return;
            }

            ai.SetDestinationToPositionLethalBotAI(ai.destination);
            ai.OrderMoveToDestination();

            if (!ai.searchForScrap.inProgress && ai.HasSpaceInInventory())
            {
                // All we can do now is search for loot or keys
                ai.StopSearch(stuckSearch, false);
                ai.StartSearch(ai.searchForScrap);
            }
            // Because the bot's inventory is full, the bot will ignore the loot it is passing by, when the bot frees space in inventory the bot should be able to revisit those areas again for loot, that's why we use stuckSearch instead of ai.searchForScrap
            else if (ai.agent.isOnNavMesh && !stuckSearch.inProgress)
            {
                ai.StopSearch(ai.searchForScrap, false);
                ai.StartSearch(stuckSearch);
            }
        }

        public override void StopAllCoroutines()
        {
            ai.StopSearch(stuckSearch, false);
            base.StopAllCoroutines();
            StopSearchingWanderCoroutine();
        }

        /// <remarks>
        /// In this state, we do not want to only use the front entrance,
        /// </remarks>
        /// <inheritdoc cref="AIState.ShouldOnlyUseFrontEntrance"></inheritdoc>
        protected override bool ShouldOnlyUseFrontEntrance()
        {
            return false;
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.Lost,
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
                float freezeTimeRandom = Random.Range(Const.MIN_TIME_SPRINT_SEARCH_WANDER, Const.MAX_TIME_SPRINT_SEARCH_WANDER);
                npcController.OrderToSprint();
                yield return new WaitForSeconds(freezeTimeRandom);

                freezeTimeRandom = Random.Range(Const.MIN_TIME_SPRINT_SEARCH_WANDER, Const.MAX_TIME_SPRINT_SEARCH_WANDER);
                npcController.OrderToStopSprint();
                yield return new WaitForSeconds(freezeTimeRandom);
            }
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
            }
        }
    }
}
