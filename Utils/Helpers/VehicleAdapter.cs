using Dusk;
using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils.Helpers.VehicleHelpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Abstract class that allows you to customize how bots interact with vehicles.
    /// </summary>
    /// <remarks>
    /// This is a generic class that allows you control on how bots interact with vehicles.
    /// </remarks>
    public abstract class VehicleAdapter<TVehicle, TGearRequest> : IVehicleAdapter
        where TVehicle : VehicleController
        where TGearRequest : IChangeGearRequest
    {
        public virtual int NavMeshAgentTypeID => Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID;

        public virtual float MaxDrivingSpeed { protected set; get; } = 14f; // Was 28f, but it was TOO FAST

        public virtual float MinDrivingSpeed => 2f;

        public virtual float MinCornerSpeed => 4f;

        public virtual float ChangeGearSpeed => 4f;

        public virtual float SlowdownDistance => 12f;

        public virtual float StopDistance => 4f;

        public virtual float SpeedTolerance => 0.5f;

        public virtual float SteeringAngle => 45f;

        public virtual float ReverseEnterAngle => 120f;

        public virtual float ReverseExitAngle => 75f;

        #region Interface Helpers

        Type IVehicleAdapter.VehicleType => typeof(TVehicle);

        float IVehicleAdapter.MaxDrivingSpeed { get => MaxDrivingSpeed; set => MaxDrivingSpeed = value; }

        float IVehicleAdapter.MinDrivingSpeed => MinDrivingSpeed;

        float IVehicleAdapter.MinCornerSpeed => MinCornerSpeed;

        float IVehicleAdapter.ChangeGearSpeed => ChangeGearSpeed;

        float IVehicleAdapter.SlowdownDistance => SlowdownDistance;

        float IVehicleAdapter.StopDistance => StopDistance;

        float IVehicleAdapter.SpeedTolerance => SpeedTolerance;

        bool IVehicleAdapter.CanDrive(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return CanDrive((TVehicle)vehicleController, lethalBotAI);
        }

        bool IVehicleAdapter.IsVehicleDestroyed(VehicleController vehicleController)
        {
            return IsVehicleDestroyed((TVehicle)vehicleController);
        }

        bool IVehicleAdapter.IsSeatOccupied(VehicleController vehicleController, InteractTrigger seatTrigger, [NotNullWhen(true)] out PlayerControllerB? playerInSeat)
        {
            return IsSeatOccupied((TVehicle)vehicleController, seatTrigger, out playerInSeat);
        }

        bool IVehicleAdapter.IsPlayerInVehicle(VehicleController vehicleController, PlayerControllerB playerInVehicle, out InteractTrigger? seatTrigger)
        {
            return IsPlayerInVehicle((TVehicle)vehicleController, playerInVehicle, out seatTrigger);
        }

        void IVehicleAdapter.SetupNavMeshAgent(NavMeshAgent agent, VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            SetupNavMeshAgent(agent, (TVehicle)vehicleController, lethalBotAI);
        }

        void IVehicleAdapter.SetAreaCostsForCruiser(NavMeshAgent agent, VehicleController vehicleController)
        {
            SetAreaCostsForCruiser(agent, (TVehicle)vehicleController);
        }

        void IVehicleAdapter.CleanupBotDriver(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            CleanupBotDriver((TVehicle)vehicleController, lethalBotAI);
        }

        IEnumerator IVehicleAdapter.StartCar(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return StartCar((TVehicle)vehicleController, lethalBotAI);
        }

        bool IVehicleAdapter.IsIgnitionStarted(VehicleController vehicleController)
        {
            return IsIgnitionStarted((TVehicle)vehicleController);
        }

        IEnumerator IVehicleAdapter.ChangeGear(VehicleController vehicleController, IChangeGearRequest changeGearInfo, LethalBotAI lethalBotAI)
        {
            return ChangeGear((TVehicle)vehicleController, (TGearRequest)changeGearInfo, lethalBotAI);
        }

        InteractTrigger? IVehicleAdapter.FindOpenPassengerSeat(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return FindOpenPassengerSeat((TVehicle)vehicleController, lethalBotAI);
        }

        InteractTrigger? IVehicleAdapter.GetDriverSeat(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return GetDriverSeat((TVehicle)vehicleController, lethalBotAI);
        }

        Vector3? IVehicleAdapter.FindOpenTrunkPosition(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return FindOpenTrunkPosition((TVehicle)vehicleController, lethalBotAI);
        }

        bool IVehicleAdapter.IsTrunkOpen(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return IsTrunkOpen((TVehicle)vehicleController, lethalBotAI);
        }

        IEnumerator IVehicleAdapter.OpenTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return OpenTrunk((TVehicle)vehicleController, lethalBotAI);
        }

        IEnumerator IVehicleAdapter.CloseTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return CloseTrunk((TVehicle)vehicleController, lethalBotAI);
        }

        IEnumerator IVehicleAdapter.EnterVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, InteractTrigger seatTrigger)
        {
            return EnterVehicle((TVehicle)vehicleController, lethalBotAI, seatTrigger);
        }

        IEnumerator IVehicleAdapter.ExitVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return ExitVehicle((TVehicle)vehicleController, lethalBotAI);
        }

        IEnumerator IVehicleAdapter.DriveVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, NavMeshAgent vehicleAgent)
        {
            return DriveVehicle((TVehicle)vehicleController, lethalBotAI, vehicleAgent);
        }

        void IVehicleAdapter.UpdateVehicleInput(VehicleController vehicle, LethalBotAI bot, NavMeshAgent agent, ref VehicleInputHelper input)
        {
            UpdateVehicleInput((TVehicle)vehicle, bot, agent, ref input);
        }

        #endregion

        /// <summary>
        /// This tells the given <paramref name="lethalBotAI"/> that your coroutine has finished.
        /// </summary>
        /// <remarks>
        /// WARNING: YOU MUST CALL THIS WHEN ANY OF YOUR COROUTINES FINISH OR ELSE THE BOT WILL ENTER AN INFINTE LOOP
        /// </remarks>
        /// <param name="lethalBotAI"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void EndCoroutine(LethalBotAI lethalBotAI)
        {
            if (lethalBotAI.State is UseCruiserState useCruiserState)
            {
                useCruiserState.cruiserInteractionCoroutine = null;
            }
        }

        /// <inheritdoc cref="IVehicleAdapter.CanDrive(VehicleController, LethalBotAI)"/>
        public virtual bool CanDrive(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            return !IsVehicleDestroyed(vehicleController) && Plugin.Config.AllowDrivingCruiser.Value;
        }

        /// <inheritdoc cref="IVehicleAdapter.IsVehicleDestroyed(VehicleController)"/>
        public virtual bool IsVehicleDestroyed(TVehicle vehicleController)
        {
            return vehicleController.carDestroyed;
        }

        /// <inheritdoc cref="IVehicleAdapter.IsSeatOccupied(VehicleController, InteractTrigger, out PlayerControllerB?)"/>
        public abstract bool IsSeatOccupied(TVehicle vehicleController, InteractTrigger seatTrigger, [NotNullWhen(true)] out PlayerControllerB? playerInSeat);

        /// <inheritdoc cref="IVehicleAdapter.IsPlayerInVehicle(VehicleController, PlayerControllerB, out InteractTrigger?)"/>
        public abstract bool IsPlayerInVehicle(VehicleController vehicleController, PlayerControllerB playerToCheck, out InteractTrigger? seatTrigger);

        /// <inheritdoc cref="IVehicleAdapter.SetupNavMeshAgent(NavMeshAgent, VehicleController, LethalBotAI)"/>
        public virtual void SetupNavMeshAgent(NavMeshAgent agent, TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            // Default settings for the NavMeshAgent
            agent.agentTypeID = NavMeshAgentTypeID;
            agent.enabled = true;
            agent.baseOffset = 1.9f;
            agent.radius = 2f; // Was 4.7f, but was causing issues;
            agent.height = 4.5f;
            agent.speed = MaxDrivingSpeed;
            agent.angularSpeed = 120f;
            agent.acceleration = float.MaxValue;
            agent.autoBraking = false;
            agent.updatePosition = false;
            agent.updateRotation = false;

            // Update NavMesh areaMask
            int areaMask = NavMesh.AllAreas;
            int smallSpaceAreaMask = NavMesh.GetAreaFromName("SmallSpace");
            int playerShipAreaMask = NavMesh.GetAreaFromName("PlayerShip");
            int mediumSpaceAreaMask = NavMesh.GetAreaFromName("MediumSpace");
            int climbAreaMask = NavMesh.GetAreaFromName("Climb");
            areaMask &= ~(1 << smallSpaceAreaMask) | ~(1 << playerShipAreaMask) | ~(1 << mediumSpaceAreaMask) | ~(1 << climbAreaMask);
            agent.areaMask = areaMask;

            // Update area costs
            SetAreaCostsForCruiser(agent, vehicleController);

            // Disable default NavMeshObstacle
            foreach (var obstacle in vehicleController.GetComponentsInChildren<NavMeshObstacle>())
            {
                if (obstacle != null)
                {
                    obstacle.enabled = false;
                }
            }

            // Attach the agent to the cruiser
            agent.Warp(vehicleController.transform.position);
            //agent.transform.position = vehicleController.transform.position;
            //agent.transform.SetParent(vehicleController.transform, worldPositionStays: true);
            //agent.transform.localPosition = new Vector3(0f, -2f, 0f);

            // Reset bot input
            lethalBotAI.NpcController.vehicleInput.Reset();
        }

        /// <inheritdoc cref="IVehicleAdapter.SetAreaCostsForCruiser(NavMeshAgent, VehicleController)"/>
        public virtual void SetAreaCostsForCruiser(NavMeshAgent agent, VehicleController vehicleController)
        {
            // Update area costs
            int waterAreaMask = NavMesh.GetAreaFromName("Water");
            agent.SetAreaCost(waterAreaMask, 5f);

            // Try not to use Enemy Only area
            int enemyOnlyArea = NavMesh.GetAreaFromName("EnemiesOnly");
            agent.SetAreaCost(enemyOnlyArea, 100f);
        }

        /// <inheritdoc cref="IVehicleAdapter.CleanupBotDriver(VehicleController, LethalBotAI)"/>
        public virtual void CleanupBotDriver(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            // HACKHACK: Renable NavMeshCollider
            // TODO: Make a better way of doing this
            // FIXME: So, apparently Zeekerss left behind a NavMeshObstacle on the Player,
            // since players get parented to the cruiser, this causes my code to accidently renable them as well.
            // We we have to loop through and prevent that from happening.
            // TO MODDERS: Unlike me, you have the advantage of caching your NavMeshObstacle............please, make use of it.......
            HashSet<NavMeshObstacle> navMeshObstacles = new HashSet<NavMeshObstacle>();
            PlayerControllerB[] allPlayerScripts = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < allPlayerScripts.Length; i++)
            {
                PlayerControllerB playerControllerB = allPlayerScripts[i];
                if (playerControllerB != null)
                {
                    NavMeshObstacle[] playerNavMeshObstacles = playerControllerB.GetComponentsInChildren<NavMeshObstacle>();
                    for (int j = 0; j < playerNavMeshObstacles.Length; j++)
                    {
                        navMeshObstacles.Add(playerNavMeshObstacles[j]);
                    }
                }
            }

            // Renable NavMeshObstacle
            foreach (var obstacle in vehicleController.GetComponentsInChildren<NavMeshObstacle>())
            {
                if (obstacle != null && !navMeshObstacles.Contains(obstacle))
                {
                    obstacle.enabled = true;
                }
            }

            // Disable the CruiserNavMeshAgent
            VehicleManager.Instance.CruiserNavMeshAgent.enabled = false;

            // Reset bot input
            lethalBotAI.NpcController.vehicleInput.Reset();
        }

        /// <inheritdoc cref="IVehicleAdapter.StartCar(VehicleController, LethalBotAI)"/>
        public abstract IEnumerator StartCar(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Check if the car is started or not
        /// </summary>
        /// <remarks>
        /// Handles the default Company Cruiser by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <returns><see langword="true"/> if the car is started, <see langword="false"/> otherwise</returns>
        public virtual bool IsIgnitionStarted(TVehicle vehicleController)
        {
            return vehicleController.ignitionStarted;
        }

        /// <inheritdoc cref="IVehicleAdapter.ChangeGear(VehicleController, IChangeGearRequest, LethalBotAI)"/>
        public abstract IEnumerator ChangeGear(TVehicle vehicleController, TGearRequest changeGearInfo, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.FindOpenPassengerSeat(VehicleController, LethalBotAI)"/>
        public abstract InteractTrigger? FindOpenPassengerSeat(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.GetDriverSeat(VehicleController, LethalBotAI)"/>
        public abstract InteractTrigger? GetDriverSeat(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.FindOpenTrunkPosition(VehicleController, LethalBotAI)"/>
        public abstract Vector3? FindOpenTrunkPosition(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Checks if the trunk of the vehicle is open or not.
        /// </summary>
        /// <remarks>
        /// Handles the default Company Cruiser by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>true if the trunk is open, false otherwise.</returns>
        public virtual bool IsTrunkOpen(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            return vehicleController.backDoorOpen;
        }

        /// <inheritdoc cref="IVehicleAdapter.OpenTrunk(VehicleController, LethalBotAI)"/>
        public abstract IEnumerator OpenTrunk(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.CloseTrunk(VehicleController, LethalBotAI)"/>
        public abstract IEnumerator CloseTrunk(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.EnterVehicle(VehicleController, LethalBotAI, InteractTrigger)"/>
        public abstract IEnumerator EnterVehicle(TVehicle vehicleController, LethalBotAI lethalBotAI, InteractTrigger seatTrigger);

        /// <inheritdoc cref="IVehicleAdapter.ExitVehicle(VehicleController, LethalBotAI)"/>
        public abstract IEnumerator ExitVehicle(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <inheritdoc cref="IVehicleAdapter.DriveVehicle(VehicleController, LethalBotAI, NavMeshAgent)"/>
        public abstract IEnumerator DriveVehicle(TVehicle vehicleController, LethalBotAI lethalBotAI, NavMeshAgent vehicleAgent);

        /// <summary>
        /// Allows you to customize how the bot updates the vehicle input while driving.
        /// </summary>
        /// <param name="vehicle">The vehicle the bot is driving</param>
        /// <param name="bot">The bot's <see cref="LethalBotAI"/></param>
        /// <param name="agent">The vehicle's <see cref="NavMeshAgent"/></param>
        /// <param name="input">The vehicle input to update</param>
        protected virtual void UpdateVehicleInput(TVehicle vehicle, LethalBotAI bot, NavMeshAgent agent, ref VehicleInputHelper input)
        {
            // Default to no input.
            input.Zero();

            // Keep the agent synced with the actual vehicle position.
            // The agent is only for pathfinding, not the actual mover.
            Rigidbody mainRigidbody = vehicle.mainRigidbody;
            agent.nextPosition = mainRigidbody.transform.position;

            // Make sure we have a path to follow, if not, we should brake to a stop.
            if (!agent.hasPath || agent.pathPending || agent.isStopped)
            {
                input.Brake = 1f; // We don't have a path, stop!
                input.IsStopping = true;
                return;
            }

            // Get the current velocity of the vehicle.
            Vector3 currentVelocity = mainRigidbody.velocity;

            // Get the target position from the agent.
            Vector3 target = agent.desiredVelocity; // This includes collision avoidance as well
            target.y = 0f;
            target.Normalize();

            // Check the current steering direction
            Vector3 vehicleForward = mainRigidbody.transform.forward;
            vehicleForward.y = 0f;
            vehicleForward.Normalize();

            // Calculate the current signed speed of the cruiser
            float currentSpeed = Vector3.Dot(currentVelocity, vehicleForward); // currentVelocity.magnitude

            // Build a yaw-only rotation for the vehicle.
            // Ignore pitch and roll caused by terrain.
            float vehicleYaw = mainRigidbody.transform.eulerAngles.y;
            Quaternion yawRotation = Quaternion.Euler(0f, vehicleYaw, 0f);

            // Convert the desired direction into yaw-local space.
            Vector3 localTarget = Quaternion.Inverse(yawRotation) * target;
            localTarget.y = 0f;
            localTarget.Normalize();

            // Calculate the target angle
            float targetAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

            // Steering (-1 to 1)
            float targetSteering = Mathf.Clamp(targetAngle / SteeringAngle, -1f, 1f);
            float desiredSteering = targetSteering;

            // Check our set tolerance
            //if (Mathf.Abs(targetSteering - vehicleSteeringInputNormalised) < SteeringTolerance)
            //{
            //    desiredSteering = vehicleSteeringInputNormalised; // Keep at it
            //}
            //else
            //{
            //    desiredSteering = targetSteering; // Update our steering angle
            //}

            // Check is we want to be reversing instead of driving forward
            if (!input.IsReversing 
                && Mathf.Abs(targetAngle) >= ReverseEnterAngle)
            {
                input.IsReversing = true; // Tell the input class we want to move backwards
            }
            else if (input.IsReversing 
                && Mathf.Abs(targetAngle) <= ReverseExitAngle)
            {
                input.IsReversing = false; // Tell the input class we want to move forwards
            }

            // Smooth steering.
            input.Steering = desiredSteering;

            //Plugin.LogDebug(
            //    $"VehiclePos: {mainRigidbody.position} | " +
            //    $"VehicleForward: {mainRigidbody.transform.forward} | " +
            //    $"SteeringTarget: {agent.steeringTarget} | " +
            //    $"AgentDestination: {agent.destination} | " +
            //    $"AgentPathEndPosition: {agent.pathEndPosition} | " +
            //    $"ToTarget: {target} | " +
            //    $"LocalTarget: {localTarget} | " +
            //    $"Angle: {targetAngle:F2} | " +
            //    $"Steering: {targetSteering:F2} | " +
            //    $"Velocity: {mainRigidbody.velocity}"
            //);

            // Adjust speed based on turn angle and distance to target.
            float steeringAmount = Mathf.Abs(desiredSteering);

            // Grab our max speed
            float maxSpeed = MaxDrivingSpeed;
            if (bot.State is UseCruiserState useCruiserState && useCruiserState.ShouldSpeed)
            {
                maxSpeed *= 2f;
            }

            // Square it so speed falls off smoothly.
            float steeringCurve = steeringAmount * steeringAmount;
            float desiredSpeed = Mathf.Lerp(maxSpeed, MinCornerSpeed, steeringCurve);

            // Begin slowing near the destination.
            float remainingDistance = agent.remainingDistance;
            if (remainingDistance < SlowdownDistance)
            {
                float factor = Mathf.InverseLerp(StopDistance, SlowdownDistance, remainingDistance);
                desiredSpeed *= factor;
            }

            // Don't drive slower than our minimum speed
            desiredSpeed = Mathf.Max(desiredSpeed, MinDrivingSpeed);

            // Consider our current move direction
            float currentSpeedMagnitude = Mathf.Abs(currentSpeed);
            bool movingWrongDirection = currentSpeedMagnitude > SpeedTolerance 
                && (input.IsReversing ? currentSpeed > 0f : currentSpeed < 0f);

            // Apply throttle or brake based on speed error.
            float speedError = desiredSpeed - currentSpeedMagnitude;
            if (movingWrongDirection)
            {
                // We're moving opposite the desired direction.
                // Brake until we're stopped.
                input.Brake = 1f;
            }
            else if (speedError > SpeedTolerance)
            {
                // We're moving in the correct direction but too slowly.
                input.Throttle = input.IsReversing ? -1f : 1f;
            }
            else if (speedError < -SpeedTolerance)
            {
                // We're moving in the correct direction but too quickly.
                input.Brake = 1f;
            }

            // If we are very close to the destination, come to a complete stop.
            if (remainingDistance <= StopDistance)
            {
                input.Zero();
                input.Brake = 1f;
                input.IsStopping = true;
            }
        }
    }
}
