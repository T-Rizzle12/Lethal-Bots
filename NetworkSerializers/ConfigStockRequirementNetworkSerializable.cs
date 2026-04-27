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
        public bool overrideEcoLimit; // Should we not use the eco limit as defined in the config
        public int ecoLimit; // How much money should the bot keep in reserve when thinking about purchasing this item.
        public ConfigStockRequiredItem[] requiredItems; // Don't purchase this item unless we have the required items!

        [Obsolete("requiredItemName has been superceded by requiredItems. You should use it instead. This will be removed in later versions!")]
        public string requiredItemName; // Don't purchase this item unless we have at least one of this item!

        public ConfigStockRequirement()
        {
            itemName = "None";
            desiredStock = 0;
            overrideEcoLimit = false;
            ecoLimit = 0;
            requiredItems = new ConfigStockRequiredItem[0];
            #pragma warning disable CS0618 // Type or member is obsolete
            requiredItemName = "None";
            #pragma warning restore CS0618 // Type or member is obsolete
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemName);
            serializer.SerializeValue(ref desiredStock);
            serializer.SerializeValue(ref overrideEcoLimit);
            serializer.SerializeValue(ref ecoLimit);
            serializer.SerializeValue(ref requiredItems);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Item Name: {itemName}");
            sb.AppendLine($"Desired Stock: {desiredStock}");
            sb.AppendLine($"Override Eco Limit: {overrideEcoLimit}");
            sb.AppendLine($"Eco Limit: {ecoLimit}");
            sb.AppendLine($"Required Items:");
            if (requiredItems == null || requiredItems.Length == 0)
            {
                sb.AppendLine("(None)");
            }
            else
            {
                foreach (var item in requiredItems)
                {
                    sb.AppendLine($"{item}");
                }
            }
            return sb.ToString();
        }
    }

    [Serializable]
    public struct ConfigStockRequiredItem : INetworkSerializable
    {
        public string itemName; // Name of the item we must have.
        public int requiredStock; // How many of this item should be in the crew's possession

        public ConfigStockRequiredItem()
        {
            itemName = string.Empty;
            requiredStock = 0;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemName);
            serializer.SerializeValue(ref requiredStock);
        }

        public override string ToString()
        {
            return $"Item Name: {itemName} and Requred Stock: {requiredStock}";
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
