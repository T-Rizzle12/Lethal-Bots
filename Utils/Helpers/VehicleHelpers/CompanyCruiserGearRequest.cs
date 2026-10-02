using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Utils.Helpers.VehicleHelpers
{
    /// <summary>
    /// Information about a gear change for the Company Cruiser <br/>
    /// <see cref="VehicleController"/> for how its used in the Company Cruiser
    /// </summary>
    public struct CompanyCruiserGearRequest : IChangeGearRequest
    {
        public CarGearShift DesiredGear; // The desired gear to change to. NOTE: This enum is defined for the Company Cruiser
    }
}
