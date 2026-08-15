using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using LethalBots.Utils.Helpers.VehicleHelpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.Utils.Vehicles
{
    /// <summary>
    /// Information about the vanilla Company Cruiser and how to interact with it
    /// </summary>
    public class CompanyCruiserInfo : VehicleAdapter<VehicleController, CompanyCruiserGearRequest>
    {
        public override IEnumerator StartCar(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            // If the car is already started, we don't need to do anything
            if (IsIgnitionStarted(vehicleController))
            {
                // Let the bot know we finished
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // Since the Company Cruiser has a chance to fail to start the engine, we loop until the engine has started
            PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
            while (!IsIgnitionStarted(vehicleController))
            {
                // Start the car and wait until it is started or 3 seconds have passed
                // NOTE: Pass local player through here, since it stops the same code from running twice for clients.
                float startTime = Time.timeSinceLevelLoad;
                if (vehicleController.keyIgnitionCoroutine != null)
                {
                    vehicleController.StopCoroutine(vehicleController.keyIgnitionCoroutine);
                }
                vehicleController.keyIgnitionCoroutine = vehicleController.StartCoroutine(vehicleController.TryIgnition(isLocalDriver: true));
                vehicleController.TryIgnitionServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, vehicleController.keyIsInIgnition);
                yield return new WaitUntil(() => IsIgnitionStarted(vehicleController) || (Time.timeSinceLevelLoad - startTime) > 3f);

                // Now if we failed to start the engine, end our current attempt and try again later.
                // The cruiser has a chance to fail to start the engine, so we need to handle that case.
                if (!IsIgnitionStarted(vehicleController))
                {
                    if (vehicleController.keyIsInIgnition)
                    {
                        lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 3);
                    }
                    else
                    {
                        lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 0);
                    }

                    vehicleController.carEngine1AudioActive = false;
                    vehicleController.CancelIgnitionAnimation();
                    vehicleController.CancelTryIgnitionServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, vehicleController.keyIsInIgnition);
                    yield return new WaitForSeconds(1f);
                }
            }

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }

        public override IEnumerator ChangeGear(VehicleController vehicleController, CompanyCruiserGearRequest changeGearInfo, LethalBotAI lethalBotAI)
        {
            // Don't change gear if the desired gear is the same as the current gear
            CarGearShift desiredGear = changeGearInfo.DesiredGear;
            if (desiredGear == vehicleController.gear)
            {
                // Let the bot know we finished
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // Look at the gear shift and change the gear
            // FIXME: This causes bots to not properly change gears, its an internal issue with the bot's vision code.
            //lethalBotAI.NpcController.OrderToLookAtPosition(vehicleController.gearStickAnimator.transform.position, EnumLookAtPriority.MAXIMUM_PRIORITY, maxBodyFOV: 20);
            //yield return new WaitUntil(() => lethalBotAI.NpcController.LookAtTarget.IsHeadAimingOnTarget() && lethalBotAI.NpcController.LookAtTarget.hasBeenSightedIn);

            // Change the gear and sync it across the network
            // NOTE: This immediately changes the gear and syncs it across the network
            vehicleController.ShiftToGearAndSync((int)desiredGear);
            yield return null;

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }

        public override InteractTrigger? FindOpenPassengerSeat(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            InteractTrigger passengerSeat = vehicleController.passengerSeatTrigger;
            return passengerSeat != null && passengerSeat.interactable && vehicleController.currentPassenger == null ? passengerSeat : null;
        }

        public override Vector3? FindOpenTrunkPosition(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            // TODO: Implement proper trunk position finding logic for the Company Cruiser
            float x = UnityEngine.Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.x, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.x);
            float y = UnityEngine.Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.y, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.y);
            float z = UnityEngine.Random.Range(Const.FIRST_CORNER_LETHALBOT_IN_CRUISER.z, Const.SECOND_CORNER_LETHALBOT_IN_CRUISER.z);

            return new Vector3(x, y, z);
        }

        public override InteractTrigger? GetDriverSeat(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return vehicleController.driverSeatTrigger;
        }

        public override IEnumerator OpenTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            // Check if the trunk is already open, if so, we don't need to do anything
            if (IsTrunkOpen(vehicleController, lethalBotAI))
            {
                // Let the bot know we finished
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // For the Vanilla Cruiser, it holds the open and close triggers in the back door container.
            // I just need to find them.
            Transform openTrigger = vehicleController.backDoorContainer.transform.Find("OpenTrigger");
            if (openTrigger != null)
            {
                float startTime = Time.timeSinceLevelLoad;
                openTrigger.GetComponent<InteractTrigger>().Interact(lethalBotAI.NpcController.Npc.thisPlayerBody);
                yield return new WaitUntil(() => IsTrunkOpen(vehicleController, lethalBotAI) || (Time.timeSinceLevelLoad - startTime) > 5f);
            }

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }

        public override IEnumerator CloseTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            // Check if the trunk is already closed, if so, we don't need to do anything
            if (!IsTrunkOpen(vehicleController, lethalBotAI))
            {
                // Let the bot know we finished
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // For the Vanilla Cruiser, it holds the open and close triggers in the back door container.
            // I just need to find them.
            Transform closedTrigger = vehicleController.backDoorContainer.transform.Find("ClosedTrigger");
            if (closedTrigger != null)
            {
                float startTime = Time.timeSinceLevelLoad;
                closedTrigger.GetComponent<InteractTrigger>().Interact(lethalBotAI.NpcController.Npc.thisPlayerBody);
                yield return new WaitUntil(() => !IsTrunkOpen(vehicleController, lethalBotAI) || (Time.timeSinceLevelLoad - startTime) > 5f);
            }

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }

        public override IEnumerator DriveVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, NavMeshAgent vehicleAgent)
        {
            // Kinda hard to drive a vehicle if the engine isn't started!
            if (!IsIgnitionStarted(vehicleController))
            {
                // Start the car. StartCar will handle the case where the car fails to start and will keep trying until it starts.
                yield return StartCar(vehicleController, lethalBotAI);
            }

            // Drive the vehicle we are using, the loop must be MANUALLY ended
            // by the AIState that is managing this.
            while (true)
            {
                // If the car is no longer started, get it back online
                if (!IsIgnitionStarted(vehicleController))
                {
                    // Start the car. StartCar will handle the case where the car fails to start and will keep trying until it starts.
                    yield return StartCar(vehicleController, lethalBotAI);
                }

                // Update the crusier's NavMeshAgent with the current speed and acceleration of the vehicle
                vehicleAgent.speed = vehicleController.mainRigidbody.velocity.magnitude;
                vehicleAgent.acceleration = vehicleController.carAcceleration;

                // Drive the vehicle using the NavMeshAgent
                ref VehicleInputHelper input = ref lethalBotAI.NpcController.vehicleInput;
                UpdateVehicleInput(vehicleController, lethalBotAI, vehicleAgent, ref input);

                // Check what gear our vehicle is in and change it if necessary
                CarGearShift currentGear = vehicleController.gear;
                if (input.Throttle > 0f && currentGear != CarGearShift.Drive)
                {
                    input.Throttle = 0f; // Prevent the vehicle from moving forward while we are changing gears
                    yield return ChangeGear(vehicleController, new CompanyCruiserGearRequest { DesiredGear = CarGearShift.Drive }, lethalBotAI);
                }
                else if (input.Throttle < 0f && currentGear != CarGearShift.Reverse)
                {
                    input.Throttle = 0f; // Prevent the vehicle from moving forward while we are changing gears
                    // Don't change to park if we are at high speed, we need to slow down first.
                    if (vehicleAgent.speed <= MinCornerSpeed)
                    {
                        yield return ChangeGear(vehicleController, new CompanyCruiserGearRequest { DesiredGear = CarGearShift.Reverse }, lethalBotAI);
                    }
                    else
                    {
                        input.Brake = 1f; // Apply brakes to slow down the vehicle before changing gears
                    }
                }
                else if (input.Brake > 0f && currentGear != CarGearShift.Park)
                {
                    // Don't change to park if we are at high speed, we need to slow down first.
                    if (input.IsStopping)
                    {
                        yield return ChangeGear(vehicleController, new CompanyCruiserGearRequest { DesiredGear = CarGearShift.Park }, lethalBotAI);
                    }
                }

                yield return null;
            }
        }

        public override IEnumerator EnterVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, InteractTrigger seatTrigger)
        {
            // Kinda hard to enter a vehicle if we don't have a seat to enter!
            if (seatTrigger == null)
            {
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // Find out which seat we are trying to enter
            InteractTrigger? driverSeat = GetDriverSeat(vehicleController, lethalBotAI);
            if (driverSeat == null)
            {
                Plugin.LogWarning($"[CompanyCruiserInfo] No driver seat found for vehicle {vehicleController.name}. Cannot enter vehicle.");
                EndCoroutine(lethalBotAI);
                yield break;
            }

            // Check which seat we are using and open the appropriate door if necessary
            PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
            if (seatTrigger == driverSeat)
            {
                // Make sure the seat isn't already occupied
                if (vehicleController.currentDriver != null)
                {
                    Plugin.LogWarning($"[CompanyCruiserInfo] Driver seat is already occupied for vehicle {vehicleController.name}. Cannot enter vehicle.");
                    EndCoroutine(lethalBotAI);
                    yield break;
                }

                // Looking at the source code, true is for open and false if for closed.
                if (!vehicleController.driverSideDoor.boolValue)
                {
                    // Open the driver side door and wait until it is open or 5 seconds have passed
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.driverSideDoorTrigger.Interact(lethalBotController.thisPlayerBody);
                    yield return new WaitForSeconds(3f); // vehicleController.passengerSideDoor.boolValue can be set too early, so wait a hot second
                    yield return new WaitUntil(() => vehicleController.driverSideDoor.boolValue || (Time.timeSinceLevelLoad - startTime) > 5f);
                }

                // Make sure the seat isn't already occupied
                // NOTE: This is a double check in case the seat was occupied while we were opening the door
                if (vehicleController.currentDriver != null)
                {
                    Plugin.LogWarning($"[CompanyCruiserInfo] Driver seat is already occupied for vehicle {vehicleController.name}. Cannot enter vehicle.");
                    EndCoroutine(lethalBotAI);
                    yield break;
                }

                // Actually enter the vehicle
                driverSeat.Interact(lethalBotController.thisPlayerBody);
                if (vehicleController.driverSideDoor.boolValue)
                {
                    vehicleController.driverSideDoor.TriggerAnimation(lethalBotController);
                }

                // Same logic as the base game
                // We handle the stuff here since we don't want to call some of the base game stuff
                vehicleController.currentDriver = lethalBotController;
                lethalBotController.playerBodyAnimator.SetFloat("animationSpeed", 0.5f);
                if (vehicleController.ignitionStarted)
                {
                    lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 1);
                }
                else
                {
                    lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 0);
                }

                vehicleController.SetVehicleCollisionForPlayer(setEnabled: false, lethalBotController);
                vehicleController.SetPlayerInControlOfVehicleServerRpc((int)lethalBotController.playerClientId);
            }
            else
            {
                // Make sure the seat isn't already occupied
                if (vehicleController.currentPassenger != null)
                {
                    Plugin.LogWarning($"[CompanyCruiserInfo] Passenger seat is already occupied for vehicle {vehicleController.name}. Cannot enter vehicle.");
                    EndCoroutine(lethalBotAI);
                    yield break;
                }

                // Looking at the source code, true is for open and false if for closed.
                if (!vehicleController.passengerSideDoor.boolValue)
                {
                    // Open the driver side door and wait until it is open or 5 seconds have passed
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.passengerSideDoorTrigger.Interact(lethalBotController.thisPlayerBody);
                    yield return new WaitForSeconds(3f); // vehicleController.passengerSideDoor.boolValue can be set too early, so wait a hot second
                    yield return new WaitUntil(() => vehicleController.passengerSideDoor.boolValue || (Time.timeSinceLevelLoad - startTime) > 5f);
                }

                // Make sure the seat isn't already occupied
                // NOTE: This is a double check in case the seat was occupied while we were opening the door
                if (vehicleController.currentPassenger != null)
                {
                    Plugin.LogWarning($"[CompanyCruiserInfo] Passenger seat is already occupied for vehicle {vehicleController.name}. Cannot enter vehicle.");
                    EndCoroutine(lethalBotAI);
                    yield break;
                }

                // Base game, thanks to our patches, handles this just fine so no special logic is needed here.
                seatTrigger.Interact(lethalBotController.thisPlayerBody);
            }

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }

        public override IEnumerator ExitVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            // Find out which seat we are sitting in
            InteractTrigger? driverSeat = GetDriverSeat(vehicleController, lethalBotAI);
            if (driverSeat == null)
            {
                Plugin.LogWarning($"[CompanyCruiserInfo] No driver seat found for vehicle {vehicleController.name}. Cannot leave vehicle.");
                EndCoroutine(lethalBotAI);
                yield break;
            }

            PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
            if (driverSeat.playerScriptInSpecialAnimation == lethalBotController)
            {
                // Make sure the vehicle is in park
                if (vehicleController.gear != CarGearShift.Park)
                {
                    yield return ChangeGear(vehicleController, new CompanyCruiserGearRequest { DesiredGear = CarGearShift.Park }, lethalBotAI);
                }

                // Shut off the engine.
                if (IsIgnitionStarted(vehicleController))
                {
                    if (vehicleController.keyIgnitionCoroutine != null)
                    {
                        vehicleController.StopCoroutine(vehicleController.keyIgnitionCoroutine);
                    }

                    // NOTE: Pass local player through here, since it stops the same code from running twice for clients.
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.keyIgnitionCoroutine = vehicleController.StartCoroutine(vehicleController.RemoveKey());
                    lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 6);
                    vehicleController.RemoveKeyFromIgnitionServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId);
                    yield return new WaitUntil(() => vehicleController.keyIgnitionCoroutine == null || (Time.timeSinceLevelLoad - startTime) > 5f);
                }

                // Carbon Copy of ExitDriverSideSeat, but modified to work for the bots
                lethalBotController.playerBodyAnimator.SetInteger("SA_CarAnim", 0);
                int num = vehicleController.CanExitCar(passengerSide: true);
                if (num != -1)
                {
                    lethalBotController.TeleportPlayer(vehicleController.driverSideExitPoints[num].position);
                    EndCoroutine(lethalBotAI);
                    yield break;
                }
                if (!vehicleController.driverSideDoor.boolValue)
                {
                    vehicleController.driverSideDoor.TriggerAnimation(lethalBotController);
                }
                lethalBotController.TeleportPlayer(vehicleController.driverSideExitPoints[1].position);

                // Wait a second to make sure the player is out of the vehicle before closing the door
                yield return new WaitForSeconds(1f);

                // Close the door if it is open
                if (vehicleController.driverSideDoor.boolValue)
                {
                    // Close the driver side door and wait until it is closed or 5 seconds have passed
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.driverSideDoorTrigger.Interact(lethalBotController.thisPlayerBody);
                    yield return new WaitUntil(() => !vehicleController.driverSideDoor.boolValue || (Time.timeSinceLevelLoad - startTime) > 5f);
                }
            }
            else if (vehicleController.passengerSeatTrigger.playerScriptInSpecialAnimation == lethalBotController)
            {
                // Looking at the source code, true is for open and false if for closed.
                if (!vehicleController.passengerSideDoor.boolValue)
                {
                    // Open the passenger side door and wait until it is open or 5 seconds have passed
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.passengerSideDoorTrigger.Interact(lethalBotController.thisPlayerBody);
                    yield return new WaitForSeconds(3f); // vehicleController.passengerSideDoor.boolValue can be set too early, so wait a hot second
                    yield return new WaitUntil(() => vehicleController.passengerSideDoor.boolValue || (Time.timeSinceLevelLoad - startTime) > 5f);
                }

                // Carbon Copy of ExitPassengerSideSeat, but modified to work for the bots
                int num = vehicleController.CanExitCar(passengerSide: false);
                if (num != -1)
                {
                    lethalBotController.TeleportPlayer(vehicleController.passengerSideExitPoints[num].position);
                }
                else
                {
                    lethalBotController.TeleportPlayer(vehicleController.passengerSideExitPoints[1].position);
                }

                // Just in case
                vehicleController.passengerSeatTrigger.interactable = true;
                vehicleController.currentPassenger = null;
                vehicleController.SetVehicleCollisionForPlayer(setEnabled: true, vehicleController.currentPassenger);

                // Wait a second to make sure the player is out of the vehicle before closing the door
                yield return new WaitForSeconds(1f);

                // Close the door if it is open
                if (vehicleController.passengerSideDoor.boolValue)
                {
                    // Close the passenger side door and wait until it is closed or 5 seconds have passed
                    float startTime = Time.timeSinceLevelLoad;
                    vehicleController.passengerSideDoorTrigger.Interact(lethalBotController.thisPlayerBody);
                    yield return new WaitUntil(() => !vehicleController.passengerSideDoor.boolValue || (Time.timeSinceLevelLoad - startTime) > 5f);
                }
            }

            // Let the bot know we finished
            EndCoroutine(lethalBotAI);
        }
    }
}
