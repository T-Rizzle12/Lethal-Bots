using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Enums
{
    /// <summary>
    /// Helper enum for the bot's Physics parent state
    /// </summary>
    public enum EnumLastSyncedPhysicsType
    {
        None = 0, // Player Collision
        Elevator = 1, // Ship
        PhysicsParent = 2, // Physics Parent
        OverridePhysicsParent = 3, // Override Physics Parent
    }
}
