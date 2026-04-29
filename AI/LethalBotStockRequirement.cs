using LethalBots.NetworkSerializers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Netcode;

namespace LethalBots.AI
{
    /// <summary>
    /// Represents a stock requirement for a Lethal Bot to use, this includes its name and the ingame item refrences
    /// </summary>
    /// <remarks>
    /// This class holds all of the important information about a item to keep in stock on the ship. 
    /// We use Item properties refrences since it allows me to find the exact type of item we need. 
    /// The stock requirement does nothing on its own and is meant to store repeated data for normal and debugging purposes.
    /// </remarks>
    public class LethalBotStockRequirement
    {
        public int Id { get; }
        public string Name 
        { 
            get
            {
                return Item?.itemName ?? "Invalid Item";
            }
        }
        public Item? Item { get; }
        public int RequiredStock { get; }
        public bool OverrideEcoLimit { get; }
        public int EcoLimit { get; }
        public IReadOnlyList<LethalBotStockRequiredItem> RequiredItems { get; }

        public LethalBotStockRequirement(int id, Item? item, int requiredStock, bool overrideEcoLimit, int ecoLimit, List<LethalBotStockRequiredItem> requiredItems)
        {
            Id = id;
            Item = item;
            RequiredStock = requiredStock;
            OverrideEcoLimit = overrideEcoLimit;
            EcoLimit = ecoLimit;
            RequiredItems = requiredItems;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Id: {Id}");
            sb.AppendLine($"Item: {Name}");
            sb.AppendLine($"Required Stock: {RequiredStock}");
            sb.AppendLine($"Override Eco Limit: {OverrideEcoLimit}");
            sb.AppendLine($"Eco Limit: {EcoLimit}");
            sb.AppendLine($"Required Items:");
            if (RequiredItems.Count == 0)
            {
                sb.AppendLine("(None)");
            }
            else
            {
                foreach (var item in RequiredItems)
                {
                    sb.AppendLine($"{item}");
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a required item the crew must have before making the purchase, this includes its name and the ingame item refrences
    /// </summary>
    /// <remarks>
    /// This class holds all of the important information about a item to keep in stock on the ship. 
    /// We use Item properties refrences since it allows me to find the exact type of item we need. 
    /// The required item does nothing on its own and is meant to store repeated data for normal and debugging purposes.
    /// </remarks>
    public class LethalBotStockRequiredItem
    {
        public string Name
        {
            get
            {
                return Item?.itemName ?? "Invalid Item";
            }
        }
        public Item? Item { get; } // The item we must have.
        public int RequiredStock { get; } // How many of this item should be in the crew's possession

        public LethalBotStockRequiredItem(Item item, int requiredStock)
        {
            Item = item;
            RequiredStock = requiredStock;
        }

        public override string ToString()
        {
            return $"Item Name: {Name} and Requred Stock: {RequiredStock}";
        }
    }
}
