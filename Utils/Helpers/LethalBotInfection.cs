using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Small class to keep track of the infection state of a bot.
    /// </summary>
    /// <remarks>
    /// NetworkSerializable so we can easily sync it across all clients.
    /// </remarks>
    [Serializable]
    public sealed class LethalBotInfection : INetworkSerializable, IEquatable<LethalBotInfection>
    {
        public float showSignsMeter;
        public float timeAtLastHealing;
        public float setPoison;
        public float sprayOnPlayerMeter;
        public float totalTimeSpentInPlants;
        public bool stoodInWeedsLastCheck;
        public float localPlayerImmunityTimer;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref showSignsMeter);
            serializer.SerializeValue(ref timeAtLastHealing);
            serializer.SerializeValue(ref setPoison);
            serializer.SerializeValue(ref sprayOnPlayerMeter);
            serializer.SerializeValue(ref totalTimeSpentInPlants);
            serializer.SerializeValue(ref stoodInWeedsLastCheck);
            serializer.SerializeValue(ref localPlayerImmunityTimer);
        }

        public bool Equals(LethalBotInfection? other)
        {
            return other != null &&
                   showSignsMeter == other.showSignsMeter &&
                   timeAtLastHealing == other.timeAtLastHealing &&
                   setPoison == other.setPoison &&
                   sprayOnPlayerMeter == other.sprayOnPlayerMeter &&
                   totalTimeSpentInPlants == other.totalTimeSpentInPlants &&
                   stoodInWeedsLastCheck == other.stoodInWeedsLastCheck &&
                   localPlayerImmunityTimer == other.localPlayerImmunityTimer;
        }

        public override bool Equals(object obj)
        {
            return obj is LethalBotInfection infection && Equals(infection);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(showSignsMeter, timeAtLastHealing, setPoison, sprayOnPlayerMeter, totalTimeSpentInPlants, stoodInWeedsLastCheck, localPlayerImmunityTimer);
        }

        public static bool operator ==(LethalBotInfection? left, LethalBotInfection? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(LethalBotInfection? left, LethalBotInfection? right)
        {
            return !(left == right);
        }
    }
}
