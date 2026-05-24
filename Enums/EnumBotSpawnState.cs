using System;

namespace LethalBots.Enums
{
    /// <summary>
    /// Enumeration made to represent the spawn state of a bot between the server and the client!
    /// </summary>
    [Serializable]
    public enum EnumBotSpawnState 
    {
        Unknown, 
        NotSpawned,
        Spawned 
    }
}
