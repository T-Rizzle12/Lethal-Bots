using System;
using System.Collections.Generic;
using System.Text;
using LethalBots.AI;

namespace LethalBots.Enums
{
    /// <summary>
    /// Used by <see cref="LethalBotSearchRoutine.searchCenterFollowsAI"/> to control how the search center is calculated
    /// </summary>
    public enum EnumSearchCenter
    {
        FollowsAI,
        CurrentTarget,
        SetPosition
    }
}
