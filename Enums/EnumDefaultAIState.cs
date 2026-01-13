using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Enums
{
    /// <summary>
    /// Enumeration of the default AI state for the bot to assume when spawning
    /// </summary>
    public enum EnumDefaultAIState
    {
        Dynamic,
        FollowPlayer,
        SearchForScrap,
        ShipDuty,
        TransferLoot
    }
}
