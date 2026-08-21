using Dusk;
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
        internal Coroutine? driveCruiserCoroutine;
        internal Coroutine? cruiserInteractionCoroutine;
        private bool leaveSeat = false;
        private bool? enableNavAgent = null;
        private CountdownTimer closeDoorInterval = new CountdownTimer();
        internal static Vector3? targetCruiserPosition = null;

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
            // We want the agent enabled
            enableNavAgent = true;

            // Grab our cruiser info
            PlayerControllerB lethalBotController = npcController.Npc;
            if (!VehicleManager.Instance.TryGetVehicleInfo(vehicleController, out IVehicleAdapter? vehicleInfo))
            {
                ai.State = new GetCloseToPlayerState(this);
                return;
            }

            // Can't use a destroyed vehicle
            if (vehicleInfo.IsVehicleDestroyed(vehicleController))
            {
                ai.State = new GetCloseToPlayerState(this);
                return;
            }

            // Check if we are in the cruiser
            //Vector3 entryPointLethalBotCruiser = vehicleController.transform.position + vehicleController.transform.rotation * GetNextRandomEntryPosCruiser();
            Vector3? chosenSpot = GetChosenSpot();
            if (vehicleInfo.IsPlayerInVehicle(vehicleController, lethalBotController, out InteractTrigger? ourSeat) || leaveSeat)
            {
                Plugin.LogInfo($"Bot {lethalBotController.playerUsername} in cruiser!");
                enableNavAgent = false; // Disable the agent, we don't need it anymore
                ai.SetAgent(enabled: false); // Make the change NOW!
                if (leaveSeat || ai.GetVehicleCruiserTargetPlayerIsIn() == null)
                {
                    // Exit vehicle cruiser
                    //ai.SyncTeleportLethalBotVehicle(entryPointLethalBotCruiser, enteringVehicle: false, vehicleController);
                    //vehicleController.SetVehicleCollisionForPlayer(true, lethalBotController);

                    // Wait here
                    ai.StopMoving();

                    if (chosenSeat != null && ourSeat == chosenSeat)
                    {
                        // Stop driving the cruiser
                        StopDriveCruiserCoroutine();

                        // Leave the cruiser
                        leaveSeat = true;
                        if (!IsInteractingWithCruiser())
                        {
                            cruiserInteractionCoroutine = ai.StartCoroutine(vehicleInfo.ExitVehicle(vehicleController, ai));
                        }
                        return;
                    }

                    // Open the trunk if its closed
                    if (chosenSpot.HasValue && !vehicleInfo.IsTrunkOpen(vehicleController, ai))
                    {
                        if (!IsInteractingWithCruiser())
                        {
                            cruiserInteractionCoroutine = ai.StartCoroutine(vehicleInfo.OpenTrunk(vehicleController, ai));
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
                    InteractTrigger? driverSeat = vehicleInfo.GetDriverSeat(vehicleController, ai);
                    if (driverSeat != null && chosenSeat == driverSeat)
                    {
                        // Start driving the cruiser
                        if (IsDrivingCruiser())
                        {
                            // DEBUG: Drive to the main entrance
                            NavMeshAgent cruiserNavMeshAgent = VehicleManager.Instance.CruiserNavMeshAgent;
                            float[] costs = new float[32];
                            for (int i = 0; i < costs.Length; i++)
                            {
                                costs[i] = cruiserNavMeshAgent.GetAreaCost(i);
                            }

                            Vector3 targetPosition = targetCruiserPosition ?? RoundManager.Instance.GetNavMeshPosition(RoundManager.FindMainEntrancePosition(getTeleportPosition: false, getOutsideEntrance: true), sampleRadius: 5f);
                            NavMeshQueryFilter navMeshQueryFilter = new NavMeshQueryFilter() { agentTypeID = cruiserNavMeshAgent.agentTypeID, areaMask = cruiserNavMeshAgent.areaMask, costs = costs };
                            if (LethalBotAI.IsValidPathToTarget(vehicleController.transform.position, targetPosition, navMeshQueryFilter, ref ai.path1, false, out _))
                            {
                                Plugin.LogDebug($"We found a valid path to target {targetPosition}");
                            }
                            else
                            {
                                Plugin.LogDebug($"Failed to find a path to target {targetPosition}");
                                //ai.SendChatMessage("No path to main! :(");
                                //int deathAnimation = Random.Range(1, 3) == 1 ? 9 : 6;
                                //npcController.Npc.KillPlayer(Vector3.zero, deathAnimation: deathAnimation);
                                //Landmine.SpawnExplosion(vehicleController.transform.position, spawnExplosionEffect: true, float.MaxValue, float.MaxValue, int.MaxValue, 50f, goThroughCar: true);
                                //if (vehicleController is ScanVan)
                                //{
                                //    ai.SendChatMessage("HOW THE FUCK DO I DRIVE THIS THING!!!!!!!!!!");
                                //    Landmine.SpawnExplosion(vehicleController.transform.position, spawnExplosionEffect: true, float.MaxValue, float.MaxValue, int.MaxValue, float.MaxValue, goThroughCar: true);
                                //}
                            }

                            if (cruiserNavMeshAgent.destination != targetPosition)
                                cruiserNavMeshAgent.SetDestination(targetPosition);
                        }
                        else
                        {
                            // Setup the cruiser
                            vehicleInfo.SetupNavMeshAgent(VehicleManager.Instance.CruiserNavMeshAgent, vehicleController, ai);

                            // Lets go!!!!!!!!!!!!!!
                            driveCruiserCoroutine = ai.StartCoroutine(vehicleInfo.DriveVehicle(vehicleController, ai, VehicleManager.Instance.CruiserNavMeshAgent));
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
                        if (vehicleInfo.IsTrunkOpen(vehicleController, ai))
                        {
                            if (!IsInteractingWithCruiser())
                            {
                                cruiserInteractionCoroutine = ai.StartCoroutine(vehicleInfo.CloseTrunk(vehicleController, ai));
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
                if (vehicleInfo.IsSeatOccupied(vehicleController, chosenSeat, out PlayerControllerB? currentPlayerInSeat) 
                    && currentPlayerInSeat != lethalBotController)
                {
                    SelectPassengerSeat();
                    return;
                }

                Vector3 chosenSeatPos = GetNearestNavAreaCruiser(chosenSeat.transform.position);
                float distSqrToChosenSeat = (chosenSeatPos - lethalBotController.transform.position).sqrMagnitude;
                if (distSqrToChosenSeat < 10f * 10f) // Interaction range lethalBotController.grabDistance * lethalBotController.grabDistance
                {
                    // Stand here
                    ai.StopMoving();

                    // Enter the vehicle
                    if (!IsInteractingWithCruiser())
                    {
                        cruiserInteractionCoroutine = ai.StartCoroutine(vehicleInfo.EnterVehicle(vehicleController, ai, chosenSeat));
                    }
                }
                else
                {
                    ai.SetDestinationToPositionLethalBotAI(chosenSeatPos);
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

            bool isTrunkOpen = vehicleInfo.IsTrunkOpen(vehicleController, ai);
            ai.SetDestinationToPositionLethalBotAI(isTrunkOpen ? chosenSpot.Value : vehicleController.backDoorContainer.transform.position);
            ai.OrderMoveToDestination();

            // Open the trunk if its closed
            // FIXME: Bots can open the trunk from anywhere on the map.......
            closeDoorInterval.Start(Random.Range(5.0f, 6.0f));
            if (!isTrunkOpen)
            {
                if (!IsInteractingWithCruiser())
                {
                    cruiserInteractionCoroutine = ai.StartCoroutine(vehicleInfo.OpenTrunk(vehicleController, ai));
                }
                return;
            }
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopCruiserInteractionCoroutine();
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
        /// Do we have the right conditions to start this state.
        /// </summary>
        /// <param name="vehicle">The vehicle the bot wants to use</param>
        /// <param name="lethalBotAI">The bot</param>
        /// <returns><see langword="true"/> the bot can use <paramref name="vehicle"/>; otherwise <see langword="false"/></returns>
        public static bool IsPossible(VehicleController vehicle, LethalBotAI lethalBotAI)
        {
            // Grab our cruiser info
            PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
            if (!VehicleManager.Instance.TryGetVehicleInfo(vehicle, out IVehicleAdapter? vehicleInfo))
            {
                return false;
            }

            // Can't use a destroyed vehicle
            if (vehicleInfo.IsVehicleDestroyed(vehicle))
            {
                return false;
            }

            // Check if the bot is already in the vehicle
            if (vehicleInfo.IsPlayerInVehicle(vehicle, lethalBotController, out _))
            {
                return true;
            }

            // Check if the driver seat is open
            PlayerControllerB? playerInSeat;
            InteractTrigger? potentialSeat = vehicleInfo.CanDrive(vehicle, lethalBotAI) ? vehicleInfo.GetDriverSeat(vehicle, lethalBotAI) : null;
            if (potentialSeat != null
                && (!vehicleInfo.IsSeatOccupied(vehicle, potentialSeat, out playerInSeat)
                    || playerInSeat == lethalBotController))
            {
                return true;
            }

            // Find an open passenger seat
            potentialSeat = vehicleInfo.FindOpenPassengerSeat(vehicle, lethalBotAI);
            if (potentialSeat != null && potentialSeat.playerScriptInSpecialAnimation == null
                && (!vehicleInfo.IsSeatOccupied(vehicle, potentialSeat, out playerInSeat)
                    || playerInSeat == lethalBotController))
            {
                return true;
            }

            // Just find a spot in the trunk
            return vehicleInfo.FindOpenTrunkPosition(vehicle, lethalBotAI).HasValue;
        }

        public override bool ShouldUseNavMeshAgent()
        {
            return enableNavAgent ?? base.ShouldUseNavMeshAgent(); // Lets us override the bot's NavMeshAgent so we can use the cruiser's instead
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
        /// Is the bot interacting with the cruiser
        /// </summary>
        /// <returns></returns>
        private bool IsInteractingWithCruiser()
        {
            return cruiserInteractionCoroutine != null;
        }

        private void StopCruiserInteractionCoroutine()
        {
            if (cruiserInteractionCoroutine != null)
            {
                ai.StopCoroutine(cruiserInteractionCoroutine);
                cruiserInteractionCoroutine = null;
            }
        }

        private void StopDriveCruiserCoroutine()
        {
            if (driveCruiserCoroutine != null)
            {
                // Call the drive cleanup function
                if (VehicleManager.Instance.TryGetVehicleInfo(vehicleController, out var vehicleInfo))
                {
                    vehicleInfo.CleanupBotDriver(vehicleController, ai);
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
                PlayerControllerB? playerInSeat;
                InteractTrigger? potentialSeat = vehicleInfo.CanDrive(vehicleController, ai) ? vehicleInfo.GetDriverSeat(vehicleController, ai) : null;
                if (potentialSeat != null 
                    && (!vehicleInfo.IsSeatOccupied(vehicleController, potentialSeat, out playerInSeat) 
                        || playerInSeat == npcController.Npc))
                {
                    Plugin.LogInfo($"Bot Chose Driver Seat");
                    chosenSeat = potentialSeat;
                    return;
                }

                // Find an open passenger seat
                potentialSeat = vehicleInfo.FindOpenPassengerSeat(vehicleController, ai);
                if (potentialSeat != null && potentialSeat.playerScriptInSpecialAnimation == null 
                    && (!vehicleInfo.IsSeatOccupied(vehicleController, potentialSeat, out playerInSeat)
                        || playerInSeat == npcController.Npc))
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

        private Vector3 GetNearestNavAreaCruiser(Vector3 targetPos, bool useCruiserAgentSpecs = false)
        {
            if (useCruiserAgentSpecs)
            {
                NavMeshAgent cruiserNavMeshAgent = VehicleManager.Instance.CruiserNavMeshAgent;
                float[] costs = new float[32];
                for (int i = 0; i < costs.Length; i++)
                {
                    costs[i] = cruiserNavMeshAgent.GetAreaCost(i);
                }
                NavMeshQueryFilter navMeshQueryFilter = new NavMeshQueryFilter() { agentTypeID = cruiserNavMeshAgent.agentTypeID, areaMask = cruiserNavMeshAgent.areaMask, costs = costs };
                if (NavMesh.SamplePosition(targetPos, out var navMeshHit, 5f, navMeshQueryFilter))
                {
                    return navMeshHit.position;
                }
            }
            else
            {
                int areaMask = NavMesh.AllAreas;
                int smallSpaceAreaMask = NavMesh.GetAreaFromName("SmallSpace");
                areaMask &= ~(1 << smallSpaceAreaMask);
                if (NavMesh.SamplePosition(targetPos, out var navMeshHit, 5f, areaMask))
                {
                    return navMeshHit.position;
                }
            }

            return targetPos;
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
