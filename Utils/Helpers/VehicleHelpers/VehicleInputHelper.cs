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
        public bool WantsForward => Throttle > 0.0f;
        public bool WantsReverse => Throttle < -0.0f;
        public float ThrottleMagnitude => Mathf.Abs(Throttle);

        public float GetActualSteering() { return Throttle > 0f ? Steering : -Steering; }

        public float Steering;
        public float Throttle;
        public float Brake;
        public bool IsStopping;

        public void Zero()
        {
            Steering = 0f;
            Throttle = 0f;
            Brake = 0f;
            IsStopping = false;
        }
    }
}
