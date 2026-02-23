using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

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
        public Item? Item { get; set; }
        public int RequiredStock { get; }
        public string RequiredItemName { get; }

        public LethalBotStockRequirement(int id, Item? item, int requiredStock, string requiredItemName)
        {
            Id = id;
            Item = item;
            RequiredStock = requiredStock;
            RequiredItemName = requiredItemName;
        }

        public override string ToString()
        {
            return $"Id: {Id}, Item: {Name}, RequiredStock: {RequiredStock}, and RequiredItemName: {RequiredItemName}";
        }
    }
}
