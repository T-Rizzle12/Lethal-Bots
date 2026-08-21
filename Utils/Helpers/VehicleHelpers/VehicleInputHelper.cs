using LethalBots.Patches.MapPatches;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Helpers.VehicleHelpers
{
    /// <summary>
    /// Helper class for vehicle input related functionality
    /// </summary>
    public class VehicleInputHelper
    {
        /// <summary>
        /// Checks if the bot wants to drive forward
        /// </summary>
        public bool WantsForward => Throttle > 0.0f;
        /// <summary>
        /// Checks if the bot wants to drive backwards
        /// </summary>
        public bool WantsReverse => Throttle < 0.0f;

        /// <summary>
        /// Used in <see cref="VehicleControllerPatch.GetVehicleInput_Prefix(VehicleController)"/> to
        /// actually apply the input, since it only cares about positive values
        /// </summary>
        public float ThrottleMagnitude => Mathf.Abs(Throttle);

        /// <summary>
        /// Grabs the bot's desired steering angle based on the direction the bot wants to drive in
        /// </summary>
        /// <returns></returns>
        public float GetActualSteering() { return IsReversing ? -Steering : Steering; }

        public float Steering;
        public float Throttle;
        public float Brake;
        public bool IsStopping;
        public bool IsReversing;

        /// <summary>
        /// Clears the bot's current input 
        /// </summary>
        public void Zero()
        {
            Steering = 0f;
            Throttle = 0f;
            Brake = 0f;
            IsStopping = false;
        }

        /// <summary>
        /// Calls <see cref="Zero"/> and clears all flags as well
        /// </summary>
        public void Reset()
        {
            Zero();
            IsReversing = false;
        }
    }
}
