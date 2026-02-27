using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace LethalBots.NetworkSerializers
{
    [Serializable]
    public struct ConfigStockRequirement : INetworkSerializable
    {
        public string itemName; // Name of the item we intent to keep stock of. Used by the shopping manager to relay information to the mission controller!
        public int desiredStock; // How much of this item do we want to keep in the ship
        public string requiredItemName; // Don't purchase this item unless we have at least one of this item!

        public ConfigStockRequirement()
        {
            itemName = "None";
            desiredStock = 0;
            requiredItemName = "None";
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemName);
            serializer.SerializeValue(ref desiredStock);
            serializer.SerializeValue(ref requiredItemName);
        }

        public override string ToString()
        {
            return $"Item Name: {itemName}, Desired Stock: {desiredStock}, and Required Item Name: {requiredItemName}";
        }
    }

    public struct ConfigStockRequirementNetworkSerializable : INetworkSerializable
    {
        public ConfigStockRequirement[] ConfigStockRequirements;

        // INetworkSerializable
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ConfigStockRequirements);
        }
    }
}
