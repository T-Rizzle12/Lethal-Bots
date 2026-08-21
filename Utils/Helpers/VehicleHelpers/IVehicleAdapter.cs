using Dusk;
using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.Utils.Helpers.VehicleHelpers
{
    /// <summary>
    /// Helper interface for use in <see cref="VehicleAdapter{TVehicle, TGearRequest}"/>
    /// </summary>
    public interface IVehicleAdapter
    {
        /// <summary>
        /// The type of this vehicle
        /// </summary>
        public Type VehicleType { get; }

        /// <summary>
        /// Custom Agent ID for the vehicle <br/>
        /// Lethal Bots defines a custom Cruiser NavSettings by default: <see cref="Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID"/>
        /// </summary>
        /// <remarks>
        /// WARNING: IF YOU WANT CUSTOM NAVMESH SETTING FOR YOUR VEHICLE, YOU ARE RESPONSIBLE FOR CREATING CUSTOM AGENT TYPES FOR YOUR CRUISER <br/>
        /// The default Cruiser NavSettings should work for MOST vehicles
        /// </remarks>
        public int NavMeshAgentTypeID { get; }

        /// <summary>
        /// Allows you to check if the bot can drive the vehicle or not.
        /// </summary>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns><see langword="true"/> if the bot can drive the vehicle, <see langword="false"/> otherwise</returns>
        public bool CanDrive(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Checks if the given <paramref name="vehicleController"/> is destroyed
        /// </summary>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <returns><see langword="true"/> if the <paramref name="vehicleController"/> is destroyed; otherwise <see langword="false"/></returns>
        public bool IsVehicleDestroyed(VehicleController vehicleController);

        /// <summary>
        /// Checks if the given <paramref name="seatTrigger"/> is occupied
        /// </summary>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="seatTrigger">The seat to check</param>
        /// <param name="playerInSeat">The player who is sitting in the seat. Null if no-one is in the seat</param>
        /// <returns><see langword="true"/> if the <paramref name="seatTrigger"/> is occupied; otherwise <see langword="false"/></returns>
        public bool IsSeatOccupied(VehicleController vehicleController, InteractTrigger seatTrigger, [NotNullWhen(true)] out PlayerControllerB? playerInSeat);

        /// <summary>
        /// Checks if the given player is in the vehicle
        /// </summary>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="playerToCheck">The player to check</param>
        /// <param name="seatTrigger">The seat <paramref name="playerToCheck"/> was sitting in. May be null if player is standing on vehicle</param>
        /// <returns><see langword="true"/> if the player is sitting in the vehicle; otherwise <see langword="false"/></returns>
        public bool IsPlayerInVehicle(VehicleController vehicleController, PlayerControllerB playerToCheck, out InteractTrigger? seatTrigger);

        /// <summary>
        /// Allows you to setup the Cruiser's NavMesh agent with your own custom variables
        /// </summary>
        /// <param name="agent">The <see cref="NavMeshAgent"/> the bot will be using</param>
        /// <param name="vehicleController">The vehicle the bot intents to drive</param>
        /// <param name="lethalBotAI">The bot who is prepping to use the vehicle</param>
        public void SetupNavMeshAgent(NavMeshAgent agent, VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Called when a bot has decided it doesn't want to drive anymore
        /// </summary>
        /// <param name="vehicleController">The vehicle to cleanup</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        public void CleanupBotDriver(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Tells the bot to start the vehicle!
        /// </summary>
        /// <remarks>
        /// This allows you as a modder to implement your own vehicle starting logic based on the vehicle.
        /// </remarks>
        /// <param name="vehicleController">The vehicle the <paramref name="lethalBotAI"/> is trying to start</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public IEnumerator StartCar(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Check if the car is started or not
        /// </summary>
        /// <remarks>
        /// Handles the default Company Cruiser by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <returns><see langword="true"/> if the car is started, <see langword="false"/> otherwise</returns>
        public bool IsIgnitionStarted(VehicleController vehicleController);

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
        public IEnumerator ChangeGear(VehicleController vehicleController, IChangeGearRequest changeGearInfo, LethalBotAI lethalBotAI);

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
        public InteractTrigger? FindOpenPassengerSeat(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Finds the driver seat in the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to check for the driver seat</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>The driver seat.</returns>
        public InteractTrigger? GetDriverSeat(VehicleController vehicleController, LethalBotAI lethalBotAI);

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
        public Vector3? FindOpenTrunkPosition(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Checks if the trunk of the vehicle is open or not.
        /// </summary>
        /// <remarks>
        /// Handles the default Company Cruiser by default.
        /// </remarks>
        /// <param name="vehicleController">The vehicle to check</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns>true if the trunk is open, false otherwise.</returns>
        public bool IsTrunkOpen(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Allows you to customize how the bot opens the trunk of the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to open the trunk of</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public IEnumerator OpenTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI);

        /// <summary>
        /// Allows you to customize how the bot closes the trunk of the vehicle.
        /// </summary>
        /// <param name="vehicleController">The vehicle to close the trunk of</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public IEnumerator CloseTrunk(VehicleController vehicleController, LethalBotAI lethalBotAI);

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
        public IEnumerator EnterVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, InteractTrigger seatTrigger);

        /// <summary>
        /// Allows you to customize how the bot exits the vehicle.
        /// </summary>
        /// <remarks>
        /// This allows you to make it so the bot opens the door, gets out, and closes the door.....oh and for turning the car off if they are the driver.....etc....
        /// </remarks>
        /// <param name="vehicleController">The vehicle to exit</param>
        /// <param name="lethalBotAI">The bot's <see cref="LethalBotAI"/></param>
        /// <returns></returns>
        public IEnumerator ExitVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI);

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
        public IEnumerator DriveVehicle(VehicleController vehicleController, LethalBotAI lethalBotAI, NavMeshAgent vehicleAgent);

        /// <summary>
        /// Allows you to customize how the bot updates the vehicle input while driving.
        /// </summary>
        /// <param name="vehicle">The vehicle the bot is driving</param>
        /// <param name="bot">The bot's <see cref="LethalBotAI"/></param>
        /// <param name="agent">The vehicle's <see cref="NavMeshAgent"/></param>
        /// <param name="input">The vehicle input to update</param>
        protected void UpdateVehicleInput(VehicleController vehicle, LethalBotAI bot, NavMeshAgent agent, ref VehicleInputHelper input);

    }
}
