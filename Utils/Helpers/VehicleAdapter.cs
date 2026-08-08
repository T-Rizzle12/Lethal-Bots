using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Utils.Helpers.VehicleHelpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

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
        public int NavMeshAgentTypeID => Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID;

        /// <summary>
        /// The maximum speed the bot should attempt to drive at.
        /// </summary>
        protected virtual float MaxDrivingSpeed => 14f;

        /// <summary>
        /// The minimum speed to maintain while making very sharp turns.
        /// </summary>
        protected virtual float MinCornerSpeed => 4f;

        /// <summary>
        /// How far from the destination the bot begins slowing down.
        /// </summary>
        protected virtual float SlowdownDistance => 12f;

        /// <summary>
        /// The distance at which the bot should come to a complete stop.
        /// </summary>
        protected virtual float StopDistance => 2f;

        /// <summary>
        /// Acceptable speed error before applying throttle or brakes.
        /// </summary>
        protected virtual float SpeedTolerance => 0.5f;

        #region Interface Helpers

        Type IVehicleAdapter.VehicleType => typeof(TVehicle);

        bool IVehicleAdapter.CanDrive(VehicleController vehicleController, LethalBotAI lethalBotAI)
        {
            return CanDrive((TVehicle)vehicleController, lethalBotAI);
        }

        void IVehicleAdapter.SetupNavMeshAgent(NavMeshAgent agent, VehicleController vehicleController)
        {
            SetupNavMeshAgent(agent, (TVehicle)vehicleController);
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
        /// Allows you to check if the bot can drive the vehicle or not.
        /// </summary>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns><see langword="true"/> if the bot can drive the vehicle, <see langword="false"/> otherwise</returns>
        public virtual bool CanDrive(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            return !vehicleController.carDestroyed;
        }

        /// <summary>
        /// Allows you to setup the crusier's NavMesh agent with your own custom variables
        /// </summary>
        /// <param name="agent"></param>
        /// <param name="vehicleController"></param>
        public virtual void SetupNavMeshAgent(NavMeshAgent agent, TVehicle vehicleController)
        {
            // Default settings for the NavMeshAgent
            agent.speed = MaxDrivingSpeed;
            agent.angularSpeed = 120f;
            agent.autoBraking = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        /// <summary>
        /// Tells the bot to start the vehicle!
        /// </summary>
        /// <remarks>
        /// This allows you as a modder to implement your own vehicle starting logic based on the vehicle.
        /// </remarks>
        /// <param name="vehicleController">The vehicle the <paramref name="lethalBotAI"/> is trying to start</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public abstract IEnumerator StartCar(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Check if the car is started or not
        /// </summary>
        /// <remarks>
        /// Handles the default Company Crusier by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <returns><see langword="true"/> if the car is started, <see langword="false"/> otherwise</returns>
        public virtual bool IsIgnitionStarted(TVehicle vehicleController)
        {
            return vehicleController.ignitionStarted;
        }

        /// <summary>
        /// Allows you to set how the bot changes gears.
        /// </summary>
        /// <remarks>
        /// I added this cause of the Scanvan! YOUR KEYS ARE IN THE IGNITION!
        /// </remarks>
        /// <param name="vehicleController">The vehicle to change gears for</param>
        /// <param name="changeGearInfo">Information about the gear change</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public abstract IEnumerator ChangeGear(TVehicle vehicleController, TGearRequest changeGearInfo, LethalBotAI lethalBotAI);

        /// <summary>
        /// Finds an open passenger seat in the vehicle.
        /// </summary>
        /// <remarks>
        /// This doesn't include the driver seat, only passenger seats. <br/>
        /// Makes it easy for you to tell the bot about open passenger seats. <br/>
        /// Bots will use this to find a seat to sit in if they are not the driver.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check for open passenger seats</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>The open passenger seat, or null if none is available.</returns>
        public abstract InteractTrigger? FindOpenPassengerSeat(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Finds the driver seat in the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to check for the driver seat</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>The driver seat.</returns>
        public abstract InteractTrigger? GetDriverSeat(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Finds a place for the bot to stand in the trunk of the vehicle.
        /// </summary>
        /// <remarks>
        /// Some vehicles don't have enough seats for all players, 
        /// so this allows you to find a place for the bot to stand in the trunk of the vehicle. <br/>
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check for an open trunk position</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>The open trunk position, or null if none is available.</returns>
        public abstract Vector3? FindOpenTrunkPosition(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Checks if the trunk of the vehicle is open or not.
        /// </summary>
        /// <remarks>
        /// Handles the default Company Crusier by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>true if the trunk is open, false otherwise.</returns>
        public virtual bool IsTrunkOpen(TVehicle vehicleController, LethalBotAI lethalBotAI)
        {
            return vehicleController.backDoorOpen;
        }

        /// <summary>
        /// Allows you to customize how the bot opens the trunk of the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to open the trunk of</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public abstract IEnumerator OpenTrunk(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Allows you to customize how the bot closes the trunk of the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to close the trunk of</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public abstract IEnumerator CloseTrunk(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Allows you to customize how the bot enters the vehicle.
        /// </summary>
        /// <remarks>
        /// This allows you to make it so the bot opens the door, gets in, and closes the door.....etc....
        /// </remarks>
        /// <param name="vehicleController">The vehicle to enter</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <param name="seatTrigger">The seat trigger to use</param>
        /// <returns></returns>
        public abstract IEnumerator EnterVehicle(TVehicle vehicleController, LethalBotAI lethalBotAI, InteractTrigger seatTrigger);

        /// <summary>
        /// Allows you to customize how the bot exits the vehicle.
        /// </summary>
        /// <remarks>
        /// This allows you to make it so the bot opens the door, gets out, and closes the door.....oh and for turning the car off if they are the driver.....etc....
        /// </remarks>
        /// <param name="vehicleController">The vehicle to exit</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public abstract IEnumerator ExitVehicle(TVehicle vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// A coroutine that runs while the bot is driving the vehicle.
        /// </summary>
        /// <remarks>
        /// This is where you can implement your own driving logic for the bot. <br/>
        /// You will be responsible for starting the car, setting the throttle, steering, and braking of the vehicle. <br/>
        /// The bot will use the <paramref name="vehicleAgent"/> to calculate the path to the destination, but you will be responsible for actually driving the vehicle. <br/>
        /// </remarks>
        /// <param name="vehicleController">The vehicle the bot is driving</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <param name="vehicleAgent">The vehicle's <see cref="NavMeshAgent"/></param>
        /// <returns></returns>
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
            agent.nextPosition = vehicle.transform.position;

            // Make sure we have a path to follow, if not, we should brake to a stop.
            if (!agent.hasPath || agent.pathPending || agent.isStopped)
            {
                input.Brake = 1f; // We don't have a path, stop!
                return;
            }

            // Get the current speed of the vehicle.
            float currentSpeed = vehicle.mainRigidbody.linearVelocity.magnitude;

            // Get the target position from the agent.
            Vector3 target = agent.steeringTarget;

            // Convert into the vehicle's local space.
            Vector3 localTarget = vehicle.transform.InverseTransformPoint(target);

            // Get the planar distance to the target (ignoring height).
            float planarDistance = Mathf.Max(new Vector2(localTarget.x, localTarget.z).magnitude, 0.1f);

            // Steering (-1 to 1)
            float desiredSteering = Mathf.Clamp(localTarget.x / planarDistance, -1f, 1f);

            // Smooth steering.
            input.Steering = desiredSteering;

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

            // Apply throttle or brake based on speed error.
            float speedError = desiredSpeed - currentSpeed;
            if (speedError > SpeedTolerance)
            {
                input.Throttle = 1f;
            }
            else if (speedError < -SpeedTolerance)
            {
                input.Brake = 1f;
            }

            // If we are very close to the destination, come to a complete stop.
            if (agent.remainingDistance <= StopDistance)
            {
                input.Zero();
                input.Brake = 1f;
            }
        }
    }
}
