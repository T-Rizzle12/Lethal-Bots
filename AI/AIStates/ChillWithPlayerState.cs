using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
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
            if (npcController.Npc.isInHangarShipRoom)
            {
                // If we are holding an item with a battery, we should charge it!
                if (ChargeHeldItemState.HasItemToCharge(ai, out _))
                {
                    ai.State = new ChargeHeldItemState(this, true);
                    return;
                }

                // Bot drop item
                GrabbableObject? heldItem = ai.HeldItem;
                if (heldItem != null && FindObject(heldItem))
                {
                    ai.DropItem();
                }
                // If we still have stuff in our inventory,
                // we should swap to it and drop it!
                else if (ai.HasGrabbableObjectInInventory(FindObject, out int objectSlot))
                {
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
            if (LethalBotManager.IsInverseTeleporterActive && npcController.Npc.isInHangarShipRoom && !ai.HasSomethingInInventory())
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

        // We are following a player, these messages mean nothing to us!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            return;
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
                    npcController.OrderToLookAtPlayer(position.Value + new Vector3(0, 2.35f, 0));
                }
                else
                {
                    // Looking at player or forward
                    PlayerControllerB? playerToLook = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                    if (playerToLook != null)
                    {
                        npcController.OrderToLookAtPlayer(playerToLook.playerEye.position);
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
                npcController.SetTurnBodyTowardsDirection(ai.targetPlayer.gameplayCamera.transform.forward);
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
                        npcController.OrderToLookAtPosition(hit.point);
                        npcController.SetTurnBodyTowardsDirectionWithPosition(hit.point);
                        return;
                    }
                }

                // Check if looking too far in the distance or at a valid position
                foreach (var hit in raycastHits)
                {
                    if (hit.distance < 0.1f)
                    {
                        npcController.SetTurnBodyTowardsDirection(ai.targetPlayer.gameplayCamera.transform.forward);
                        npcController.OrderToLookForward();
                        return;
                    }

                    PlayerControllerB? player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                    if (player != null && player.playerClientId == StartOfRound.Instance.localPlayerController.playerClientId)
                    {
                        continue;
                    }

                    // Look at position
                    npcController.OrderToLookAtPosition(hit.point);
                    npcController.SetTurnBodyTowardsDirectionWithPosition(hit.point);
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
            return Plugin.Config.DropHeldEquipmentAtShip || LethalBotAI.IsItemScrap(item);
        }
    }
}
