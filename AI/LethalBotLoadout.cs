using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.AI
{
    /// <summary>
    /// Represents a loadout for a Lethal Bot to use, this includes its name and the ingame item refrences
    /// </summary>
    /// <remarks>
    /// This class holds all of the important information about a loadout. We use Item properties refrences since it allows me
    /// to find the exact type of item we need. The loadout does nothing on its own and 
    /// is meant to store repeated data for normal and debugging purposes.
    /// </remarks>
    public class LethalBotLoadout
    {
        public int IdLoadout { get; }
        public string Name { get; }
        public Item[] Items { get; set; }

        public LethalBotLoadout(int idLoadout, string name, Item[] items)
        {
            IdLoadout = idLoadout;
            Name = name;
            Items = items;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"IdLoadout: {IdLoadout}, Name: {Name}");
            sb.AppendLine("Items:");
            if (Items.Length == 0)
            {
                sb.AppendLine("(None)");
            }
            else
            {
                foreach (Item item in Items)
                {
                    if (item == null)
                    {
                        sb.AppendLine("(null Item)");
                        continue;
                    }
                    sb.AppendLine($"Name: {item.itemName} with ID: {item.itemId}");
                }
            }
            return sb.ToString();
        }
    }
}
