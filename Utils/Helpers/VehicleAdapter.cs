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

        /// <summary>
        /// The maximum speed the bot should attempt to drive at.
        /// </summary>
        protected virtual float MaxDrivingSpeed => 14f; // Was 28f, but it was TOO FAST

        /// <summary>
        /// The bare minimum speed the bot should attempt to drive at.
        /// </summary>
        protected virtual float MinDrivingSpeed => 2f;

        /// <summary>
        /// The minimum speed to maintain while making very sharp turns.
        /// </summary>
        protected virtual float MinCornerSpeed => 4f;

        /// <summary>
        /// The cruiser's speed before we should change gears
        /// </summary>
        protected virtual float ChangeGearSpeed => 4f;

        /// <summary>
        /// How far from the destination the bot begins slowing down.
        /// </summary>
        protected virtual float SlowdownDistance => 12f;

        /// <summary>
        /// The distance at which the bot should come to a complete stop.
        /// </summary>
        protected virtual float StopDistance => 4f;

        /// <summary>
        /// Acceptable speed error before applying throttle or brakes.
        /// </summary>
        protected virtual float SpeedTolerance => 0.5f;

        /// <summary>
        /// Acceptable steering error before attempting to  
        /// </summary>
        protected virtual float SteeringTolerance => 0.1f;

        /// <summary>
        /// The angle the max angle <typeparamref name="TVehicle"/> can turn
        /// </summary>
        protected virtual float SteeringAngle => 45f;

        /// <summary>
        /// The minimum heading error at which the bot will consider reversing
        /// instead of making a large forward turn.
        /// </summary>
        protected virtual float ReverseEnterAngle => 120f;

        /// <summary>
        /// The minimum heading error at which the bot will consider to stop reversing.
        /// </summary>
        protected virtual float ReverseExitAngle => 75f;

        #region Interface Helpers

        Type IVehicleAdapter.VehicleType => typeof(TVehicle);

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
            return !IsVehicleDestroyed(vehicleController);
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
            agent.radius = 4; // 5;
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
            areaMask &= ~(1 << smallSpaceAreaMask);
            agent.areaMask = areaMask;

            // Update area costs
            int waterAreaMask = NavMesh.GetAreaFromName("Water");
            agent.SetAreaCost(waterAreaMask, 5f);

            // Try not to use Enemy Only area
            int enemyOnlyArea = NavMesh.GetAreaFromName("EnemiesOnly");
            agent.SetAreaCost(enemyOnlyArea, 100f);

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

        /// <inheritdoc cref="IVehicleAdapter.CleanupBotDriver(VehicleController, LethalBotAI)"/>
        public virtual void CleanupBotDriver(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            // HACKHACK: Renable NavMeshCollider
            // TODO: Make a better way of doing this
            // FIXME: So, apparently Zeekerss left behind a NavMeshObstacle on the Player,
            // since players get parented to the cruiser, this causes my code to accidently renable them as well.
            // We we have to loop through and prevent that from happening.
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
            // The agent is only a navigation brain, not the actual mover.
            //int areaMask = NavMesh.AllAreas;
            //int smallSpaceAreaMask = NavMesh.GetAreaFromName("SmallSpace");
            //areaMask &= ~(1 << smallSpaceAreaMask);
            //Vector3 vehiclePosition = RoundManager.Instance.GetNavMeshPosition(vehicle.transform.position, sampleRadius: 2.7f, areaMask: areaMask);
            agent.nextPosition = vehicle.mainRigidbody.transform.position; 
            //agent.gameObject.transform.position = vehiclePosition;

            // Make sure we have a path to follow, if not, we should brake to a stop.
            if (!agent.hasPath || agent.pathPending || agent.isStopped)
            {
                input.Brake = 1f; // We don't have a path, stop!
                input.IsStopping = true;
                return;
            }

            // Get the current speed of the vehicle.
            Vector3 currentVelocity = vehicle.mainRigidbody.velocity;

            // Get the target position from the agent.
            // TODO: Improve Steering code as bots either use too much or too little
            //Vector3 target = (vehicle.mainRigidbody.transform.position - agent.desiredVelocity); //agent.steeringTarget;
            //Vector3 target = (agent.steeringTarget - vehicle.mainRigidbody.transform.position);
            Vector3 target = agent.desiredVelocity;
            target.y = 0f;
            target.Normalize();

            // Convert to local space (relative to the cruiser forward direction)
            //Vector3 localTarget = vehicle.mainRigidbody.transform.InverseTransformDirection(target);
            //localTarget.y = 0f;
            //localTarget.Normalize();

            // Check the current steering direction
            Vector3 vehicleForward = vehicle.mainRigidbody.transform.forward;
            vehicleForward.y = 0f;
            vehicleForward.Normalize();

            float currentSpeed = Vector3.Dot(currentVelocity, vehicleForward); // currentVelocity.magnitude

            // Get the planar distance to the target (ignoring height).
            //float planarDistance = Mathf.Max(new Vector2(localTarget.x, localTarget.z).magnitude, 0.1f);

            // Cacluate the vehicle's horizontal right direction.
            // This gives us the local X axis without using the vehicle's pitch/roll.
            //Vector3 vehicleRight = Vector3.Cross(Vector3.up, vehicleForward);

            //// Calculate the target direction in our horizontal vehicle-local space.
            //Vector3 localTarget = new Vector3(
            //    Vector3.Dot(target, vehicleRight),
            //    0f,
            //    Vector3.Dot(target, vehicleForward)
            //);
            //localTarget.Normalize();

            // Build a yaw-only rotation for the vehicle.
            // Ignore pitch and roll caused by terrain.
            float vehicleYaw = vehicle.mainRigidbody.transform.eulerAngles.y;

            Quaternion yawRotation = Quaternion.Euler(0f, vehicleYaw, 0f);

            // Convert the desired direction into yaw-local space.
            Vector3 localTarget = Quaternion.Inverse(yawRotation) * target;
            localTarget.y = 0f;
            localTarget.Normalize();

            // Calculate the target angle
            float targetAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

            // Steering (-1 to 1)
            //float targetSteering = Mathf.Clamp(localTarget.x / planarDistance, -1f, 1f);
            // Full steering at this angle.
            float targetSteering = Mathf.Clamp(targetAngle / SteeringAngle, -1f, 1f);
            //float vehicleSteeringInputNormalised = vehicle.steeringInput / 3f;
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

            Plugin.LogDebug(
                $"VehiclePos: {vehicle.mainRigidbody.position} | " +
                $"VehicleForward: {vehicle.mainRigidbody.transform.forward} | " +
                $"SteeringTarget: {agent.steeringTarget} | " +
                $"AgentDestination: {agent.destination} | " +
                $"AgentPathEndPosition: {agent.pathEndPosition} | " +
                $"ToTarget: {target} | " +
                $"LocalTarget: {localTarget} | " +
                $"Angle: {targetAngle:F2} | " +
                $"Steering: {targetSteering:F2} | " +
                $"Velocity: {vehicle.mainRigidbody.velocity}"
            );

            // Adjust speed based on turn angle and distance to target.
            float steeringAmount = Mathf.Abs(desiredSteering);

            // Square it so speed falls off smoothly.
            float steeringCurve = steeringAmount * steeringAmount;
            float desiredSpeed = Mathf.Lerp(MaxDrivingSpeed, MinCornerSpeed, steeringCurve);

            // Begin slowing near the destination.
            if (agent.remainingDistance < SlowdownDistance)
            {
                float factor = Mathf.InverseLerp(StopDistance, SlowdownDistance, agent.remainingDistance);
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
            if (agent.remainingDistance <= StopDistance)
            {
                input.Zero();
                input.Brake = 1f;
                input.IsStopping = true;
            }
        }
    }
}
