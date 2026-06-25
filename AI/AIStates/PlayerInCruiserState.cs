using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LethalBots.AI.AIStates
{
    public class PlayerInCruiserState : AIState
    {
        private VehicleController? vehicleController;

        public PlayerInCruiserState(AIState state, VehicleController vehicleController) : base(state)
        {
            CurrentState = EnumAIStates.PlayerInCruiser;

            this.vehicleController = vehicleController;
        }

        public PlayerInCruiserState(LethalBotAI ai, VehicleController? vehicleController) : base(ai)
        {
            CurrentState = EnumAIStates.PlayerInCruiser;

            this.vehicleController = vehicleController;
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
        public override void DoAI()
        {
            PlayerControllerB lethalBotController = npcController.Npc;
            ai.SetAgent(enabled: false);
            if (vehicleController == null)
            {
                ai.State = new GetCloseToPlayerState(this);
                return;
            }

            // TODO: Implement a way for bots to sit in the passenger seat!
            Vector3 entryPointLethalBotCruiser = vehicleController.transform.position + vehicleController.transform.rotation * GetNextRandomEntryPosCruiser();

            if (npcController.IsControllerInCruiser)
            {
                if (ai.GetVehicleCruiserTargetPlayerIsIn() == null)
                {
                    // Exit vehicle cruiser
                    ai.SyncTeleportLethalBotVehicle(entryPointLethalBotCruiser, enteringVehicle: false, vehicleController);
                    vehicleController.SetVehicleCollisionForPlayer(true, lethalBotController);

                    ai.State = new GetCloseToPlayerState(this);
                    return;
                }

                // Stay in vehicle with target player
                return;
            }

            // Bot still not in vehicle

            // No target player, search for one
            // Or Target is not available anymore
            if (ai.targetPlayer == null
                || !ai.PlayerIsTargetable(ai.targetPlayer))
            {
                ai.State = new SearchingForPlayerState(this);
                return;
            }

            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Teleport to cruiser and enter vehicle
            // Place bot in random spot
            Vector3 lethalBotPassengerPos = vehicleController.transform.position + vehicleController.transform.rotation * GetNextRandomInCruiserPos();
            ai.SyncTeleportLethalBotVehicle(lethalBotPassengerPos, enteringVehicle: true, vehicleController);

            // random rotation
            float angleRandom = Random.Range(-180f, 180f);
            npcController.UpdateNowTurnBodyTowardsDirection(Quaternion.Euler(0, angleRandom, 0) * lethalBotController.thisController.transform.forward);

            // Crouch or not
            float crouchRancom = Random.Range(0f, 1f);
            if (crouchRancom > 0.5f
                && !lethalBotController.isCrouching)
            {
                npcController.OrderToToggleCrouch();
            }

            // Stop animations
            npcController.StopAnimations();

            // Chill
            npcController.OrderToStopMoving();
            return;
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.EnteringCruiser,
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
            // We are following a player, these messages mean nothing to us!
            SignalTranslatorCommandsManager.RegisterIgnoreDefaultForState<PlayerInCruiserState>();
        }

        private Vector3 GetNextRandomInCruiserPos()
        {
            float x = Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.x, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.x);
            float y = Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.y, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.y);
            float z = Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.z, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.z);

            return new Vector3(x, y, z);
        }

        private Vector3 GetNextRandomEntryPosCruiser()
        {
            float x = Random.Range(Const.POS1_ENTRY_LETHALBOT_CRUISER.x, Const.POS2_ENTRY_LETHALBOT_CRUISER.x);

            return new Vector3(x, Const.POS1_ENTRY_LETHALBOT_CRUISER.y, Const.POS1_ENTRY_LETHALBOT_CRUISER.z);
        }
    }
}
