using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot holds the current position and does not move.
    /// </summary>
    /// <remarks>
    /// Bots auto return if the ship is close to auto leaving the level.
    /// </remarks>
    public class HoldPositionState : AIState
    {
        Vector3 holdPostion;

        public HoldPositionState(AIState oldState, Vector3 positionToHold) : base(oldState)
        {
            CurrentState = EnumAIStates.HoldPosition;
            holdPostion = positionToHold;
        }

        public HoldPositionState(LethalBotAI ai, Vector3 positionToHold) : base(ai)
        {
            CurrentState = EnumAIStates.HoldPosition;
            holdPostion = positionToHold;
        }

        public override void DoAI()
        {
            // Start coroutine for looking around
            StartLookingAroundCoroutine();

            // Check for enemies
            PlayerControllerB lethalBotController = npcController.Npc;
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
            if (ai.HasSpaceInInventory())
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

            // Use items in our inventory based on the current situation
            SelectBestItemFromInventory();

            // We should move to our hold position if we are not already there!
            float sqrDistFromCharger = (holdPostion - lethalBotController.transform.position).sqrMagnitude;
            if (sqrDistFromCharger > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                ai.SetDestinationToPositionLethalBotAI(holdPostion);
                if (!npcController.WaitForFullStamina && sqrDistFromCharger > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else if (npcController.WaitForFullStamina || sqrDistFromCharger < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
                {
                    npcController.OrderToStopSprint();
                }
                ai.OrderMoveToDestination();
            }
            else
            {
                // We don't need to move now!
                ai.StopMoving();
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.Waiting,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = true,
                CutCurrentVoiceStateToTalk = false,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        //public override bool ShouldReturnToShip()
        //{
        //    PlayerControllerB? followTarget = ai.targetPlayer;
        //    if (followTarget != null && followTarget.isPlayerControlled && !followTarget.isPlayerDead) 
        //    {
        //        return false; // If the bot is following a player, it should not return to the ship.
        //    }

        //    return base.ShouldReturnToShip(); // Otherwise, use the base logic to determine if it should return to the ship.
        //}
    }
}
