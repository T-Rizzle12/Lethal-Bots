using System;
using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;

namespace LethalBots.Enums
{
    /// <summary>
    /// Enumeration representing the possible outcomes of a lethal bot traversing a <see cref="NavMeshLink"/>.
    /// </summary>
    public enum EnumLinkTraversalOutcome
    {
        InProgress,
        Success,
        Failure
    }
}
