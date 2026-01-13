using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private HashSet<string> _itemNames = null!;

        public LethalBotLoadout(int idLoadout, string name, Item[] items)
        {
            IdLoadout = idLoadout;
            Name = name;
            Items = items;
            RebuildCache();
        }

        /// <summary>
        /// Helper function that rebuilds the item cache
        /// </summary>
        /// <remarks>
        /// To modders: If you ever edit <see cref="Items"/>, you MUST call this 
        /// function in order for the changes to take effect.
        /// </remarks>
        public void RebuildCache()
        {
            // As much as I wanted to use item ids,
            // they do not work since some items can have the same ids.
            // For Example: All scrap items share the same id of 0.
            _itemNames = Items
                .Where(i => i != null)
                .Select(i => i.itemName).ToHashSet();
        }

        /// <summary>
        /// Helper function that checks if the given grabbable object is in our loadout
        /// </summary>
        /// <param name="grabbableObject">The object to check</param>
        /// <returns>true: this object in in our loadout; otherwise false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGrabbableObjectInLoadout([NotNullWhen(true)] GrabbableObject grabbableObject)
        {
            // Make sure we have a valid object!
            return grabbableObject != null && _itemNames.Contains(grabbableObject.itemProperties.itemName);
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
