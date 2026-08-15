using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using LethalBots.Utils.Helpers.VehicleHelpers;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace LethalBots.AI.AIStates
{
    public class UseCruiserState : AIState
    {
        private VehicleController vehicleController;
        private InteractTrigger? chosenSeat;
        private Vector3? chosenSpot;
        public Coroutine? driveCruiserCoroutine;
        public Coroutine? crusierInteractionCoroutine;
        private bool leaveSeat = false;
        private CountdownTimer closeDoorInterval = new CountdownTimer();

        public UseCruiserState(AIState state, VehicleController vehicleController) : base(state)
        {
            CurrentState = EnumAIStates.UseCruiser;

            this.vehicleController = vehicleController;
        }

        public UseCruiserState(LethalBotAI ai, VehicleController vehicleController) : base(ai)
        {
            CurrentState = EnumAIStates.UseCruiser;

            this.vehicleController = vehicleController;
        }

        public override void OnEnterState()
        {
            SelectPassengerSeat(); // Select where we want to sit
            if (chosenSeat == null && !chosenSpot.HasValue)
            {
                ChangeBackToPreviousState();
                return;
            }

            base.OnEnterState();
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
        public override void DoAI()
        {
            // Grab our crusier info
            PlayerControllerB lethalBotController = npcController.Npc;
            if (!VehicleManager.Instance.TryGetVehicleInfo(vehicleController, out IVehicleAdapter? vehicleAdapter))
            {
                ai.State = new GetCloseToPlayerState(this);
                return;
            }

            // TODO: Implement a way for bots to sit in the passenger seat!
            //Vector3 entryPointLethalBotCruiser = vehicleController.transform.position + vehicleController.transform.rotation * GetNextRandomEntryPosCruiser();
            Vector3? chosenSpot = GetChosenSpot();
            if (leaveSeat || lethalBotController.inVehicleAnimation || vehicleController.physicsRegion.physicsTransform == lethalBotController.physicsParent)
            {
                Plugin.LogInfo($"Bot {lethalBotController.playerUsername} in cruiser!");
                ai.SetAgent(enabled: false);
                if (leaveSeat || ai.GetVehicleCruiserTargetPlayerIsIn() == null)
                {
                    // Exit vehicle cruiser
                    //ai.SyncTeleportLethalBotVehicle(entryPointLethalBotCruiser, enteringVehicle: false, vehicleController);
                    //vehicleController.SetVehicleCollisionForPlayer(true, lethalBotController);

                    // Wait here
                    ai.StopMoving();

                    if (chosenSeat != null && lethalBotController.currentTriggerInAnimationWith == chosenSeat)
                    {
                        // Stop driving the crusier
                        StopDriveCruiserCoroutine();

                        // Leave the cruiser
                        leaveSeat = true;
                        if (!IsInteractingWithCrusier())
                        {
                            crusierInteractionCoroutine = ai.StartCoroutine(vehicleAdapter.ExitVehicle(vehicleController, ai));
                        }
                        return;
                    }

                    // Open the trunk if its closed
                    if (chosenSpot.HasValue && !vehicleAdapter.IsTrunkOpen(vehicleController, ai))
                    {
                        if (!IsInteractingWithCrusier())
                        {
                            crusierInteractionCoroutine = ai.StartCoroutine(vehicleAdapter.OpenTrunk(vehicleController, ai));
                        }
                        return;
                    }

                    ai.State = new GetCloseToPlayerState(this);
                    return;
                }

                // Seat Logic
                if (chosenSeat != null)
                {
                    // Wait here
                    Plugin.LogInfo("Seat Code!");
                    ai.StopMoving();

                    // Are we the driver
                    InteractTrigger? driverSeat = vehicleAdapter.GetDriverSeat(vehicleController, ai);
                    if (driverSeat != null && chosenSeat == driverSeat)
                    {
                        // Start driving the cruiser
                        if (IsDrivingCruiser())
                        {
                            // DEBUG: Drive to the main entrance
                            NavMeshAgent crusierNavMeshAgent = VehicleManager.Instance.CruiserNavMeshAgent;
                            crusierNavMeshAgent.SetDestination(RoundManager.FindMainEntrancePosition(getTeleportPosition: false, getOutsideEntrance: true));
                        }
                        else
                        {
                            // Setup the cruiser
                            vehicleAdapter.SetupNavMeshAgent(VehicleManager.Instance.CruiserNavMeshAgent, vehicleController);

                            // Lets go!!!!!!!!!!!!!!
                            driveCruiserCoroutine = ai.StartCoroutine(vehicleAdapter.DriveVehicle(vehicleController, ai, VehicleManager.Instance.CruiserNavMeshAgent));
                        }
                    }
                    else
                    {
                        // We are a passenger, don't do anything else
                        // TODO: Maybe make the bot look around randomly?
                        StopDriveCruiserCoroutine();
                    }
                }
                // Trunk Logic
                else if (chosenSpot.HasValue)
                {
                    // Close the trunk behind us
                    Plugin.LogInfo("Trunk Code!");
                    if (!closeDoorInterval.HasStarted() || closeDoorInterval.Elapsed())
                    {
                        // Close the trunk if its open
                        closeDoorInterval.Start(Random.Range(1.5f, 2.5f));
                        if (vehicleAdapter.IsTrunkOpen(vehicleController, ai))
                        {
                            if (!IsInteractingWithCrusier())
                            {
                                crusierInteractionCoroutine = ai.StartCoroutine(vehicleAdapter.CloseTrunk(vehicleController, ai));
                            }
                            return;
                        }
                    }

                    // Stop Moving if we are close enough
                    float distSqrToChosenSpot = (chosenSpot.Value - lethalBotController.transform.position).sqrMagnitude;
                    if (distSqrToChosenSpot < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                    {
                        ai.StopMoving();
                    }
                    else
                    {
                        ai.SetDestinationToPositionLethalBotAI(chosenSpot.Value);
                        ai.OrderMoveToDestination();
                    }
                }

                // Stay in vehicle with target player
                return;
            }

            // Bot still not in vehicle

            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Seat Logic
            if (chosenSeat != null)
            {
                // Seat was taken, pick another
                PlayerControllerB? currentPlayerInSeat = chosenSeat.playerScriptInSpecialAnimation;
                if (currentPlayerInSeat != null && currentPlayerInSeat != lethalBotController)
                {
                    SelectPassengerSeat();
                    return;
                }

                float distSqrToChosenSeat = (chosenSeat.transform.position - lethalBotController.transform.position).sqrMagnitude;
                if (distSqrToChosenSeat < 10f * 10f) // Interaction range lethalBotController.grabDistance * lethalBotController.grabDistance
                {
                    // Stand here
                    ai.StopMoving();

                    // Enter the vehicle
                    if (!IsInteractingWithCrusier())
                    {
                        crusierInteractionCoroutine = ai.StartCoroutine(vehicleAdapter.EnterVehicle(vehicleController, ai, chosenSeat));
                    }
                }
                else
                {
                    ai.SetDestinationToPositionLethalBotAI(chosenSeat.transform.position);
                    ai.OrderMoveToDestination();
                }
                return;
            }

            // Teleport to cruiser and enter vehicle
            // Place bot in random spot
            if (!chosenSpot.HasValue)
            {
                ai.State = new GetCloseToPlayerState(this);
                return;
            }
            ai.SetDestinationToPositionLethalBotAI(chosenSpot.Value);
            ai.OrderMoveToDestination();

            // Open the trunk if its closed
            // FIXME: Bots can open the trunk from anywhere on the map.......
            closeDoorInterval.Start(Random.Range(5.0f, 6.0f));
            if (!vehicleAdapter.IsTrunkOpen(vehicleController, ai))
            {
                if (!IsInteractingWithCrusier())
                {
                    crusierInteractionCoroutine = ai.StartCoroutine(vehicleAdapter.OpenTrunk(vehicleController, ai));
                }
                return;
            }
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopCrusierInteractionCoroutine();
            StopDriveCruiserCoroutine();
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
            SignalTranslatorCommandsManager.RegisterIgnoreDefaultForState<UseCruiserState>();
        }

        /// <summary>
        /// Is the bot driving the cruiser
        /// </summary>
        /// <returns></returns>
        private bool IsDrivingCruiser()
        {
            return driveCruiserCoroutine != null;
        }

        /// <summary>
        /// Is the bot interacting with the crusier
        /// </summary>
        /// <returns></returns>
        private bool IsInteractingWithCrusier()
        {
            return crusierInteractionCoroutine != null;
        }

        private void StopCrusierInteractionCoroutine()
        {
            if (crusierInteractionCoroutine != null)
            {
                ai.StopCoroutine(crusierInteractionCoroutine);
                crusierInteractionCoroutine = null;
            }
        }

        private void StopDriveCruiserCoroutine()
        {
            if (driveCruiserCoroutine != null)
            {
                // HACKHACK: Renable NavMeshCollider and NavMeshSurface.
                // TODO: Make a better way of doing this
                foreach (var obstacle in vehicleController.GetComponentsInChildren<NavMeshObstacle>())
                {
                    if (obstacle != null)
                    {
                        obstacle.enabled = true;
                    }
                }

                foreach (var navMeshSurface in vehicleController.GetComponentsInChildren<NavMeshSurface>())
                {
                    if (navMeshSurface != null)
                    {
                        navMeshSurface.enabled = true;
                    }
                }

                ai.StopCoroutine(driveCruiserCoroutine);
                driveCruiserCoroutine = null;
            }
        }

        /// <summary>
        /// Used by the bot to find a seat to sit in
        /// </summary>
        private void SelectPassengerSeat()
        {
            // Clear old values
            chosenSeat = null;
            chosenSpot = null;

            // Grab the information about our current vehicle
            IVehicleAdapter? vehicleInfo = VehicleManager.Instance.GetVehicleInfo(vehicleController);
            if (vehicleInfo != null)
            {
                // Check if the driver seat is open
                InteractTrigger? potentialSeat = vehicleInfo.CanDrive(vehicleController, ai) ? vehicleInfo.GetDriverSeat(vehicleController, ai) : null;
                if (potentialSeat != null && potentialSeat.playerScriptInSpecialAnimation == null)
                {
                    Plugin.LogInfo($"Bot Chose Driver Seat");
                    chosenSeat = potentialSeat;
                    return;
                }

                // Find an open passenger seat
                potentialSeat = vehicleInfo.FindOpenPassengerSeat(vehicleController, ai);
                if (potentialSeat != null && potentialSeat.playerScriptInSpecialAnimation == null)
                {
                    Plugin.LogInfo($"Bot Chose Passenger Seat");
                    chosenSeat = potentialSeat;
                    return;
                }

                // Just find a spot in the trunk
                Plugin.LogInfo($"Bot Chose Trunk");
                chosenSpot = vehicleInfo.FindOpenTrunkPosition(vehicleController, ai);
            }
        }

        private Vector3? GetChosenSpot()
        {
            return chosenSpot.HasValue ? vehicleController.transform.position + vehicleController.transform.rotation * chosenSpot.Value : null;
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
