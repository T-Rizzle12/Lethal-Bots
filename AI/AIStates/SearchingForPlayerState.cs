using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using System.Collections;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot has no target player to follow, and is looking for one.
    /// </summary>
    /// <remarks>
    /// The owner of the bot in this state is the last one that owns it before changing to this state, 
    /// the host if no one, just after spawn for example.
    /// We should probably just get rid of this state as the bots are pretty much
    /// self-sufficient and can find their own scrap.
    /// </remarks>
    public class SearchingForPlayerState : AIState
    {
        private float lookForPlayerTimer;
        private Coroutine searchingWanderCoroutine = null!;

        /// <summary>
        /// <inheritdoc cref="AIState(AIState)"/>
        /// </summary>
        public SearchingForPlayerState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.SearchingForPlayer;
        }
        /// <summary>
        /// <inheritdoc cref="AIState(LethalBotAI)"/>
        /// </summary>
        public SearchingForPlayerState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.SearchingForPlayer;
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
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
                    ai.State = new FetchingObjectState(this, grabbableObject);
                    return;
                }
            }

            // If we lose the player outside when returing just head back to the ship
            if (ai.HasScrapInInventory() && ai.isOutside)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Try to find the closest player to target
            PlayerControllerB? player = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (player != null) // new target
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

                // Assign to new target
                ai.SyncAssignTargetAndSetMovingTo(player);
                if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
                {
                    ai.ChangeSuitLethalBotServerRpc(npcController.Npc.playerClientId, player.currentSuitID);
                }
                return;
            }

            // Look for the player for a bit. If we been searching for a while,
            // we should just look for scrap on our own.
            if (lookForPlayerTimer > Const.MAX_TIME_SEARCH_FOR_PLAYER)
            {
                ai.State = new SearchingForScrapState(this);
                return;
            }
            else
            {
                lookForPlayerTimer += ai.AIIntervalTime;
            }

            ai.SetDestinationToPositionLethalBotAI(ai.destination);
            ai.OrderMoveToDestination();

            if (!searchForPlayers.inProgress)
            {
                // Start the coroutine from base game to search for players
                ai.StartSearch(ai.NpcController.Npc.transform.position, searchForPlayers);
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

            ai.State = new JustLostPlayerState(this);
        }

        public override string GetBillboardStateIndicator()
        {
            return "?";
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
