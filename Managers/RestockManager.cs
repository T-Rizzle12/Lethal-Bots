using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.NetworkSerializers;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling bot shopping in the game.
    /// </summary>
    public class RestockManager : MonoBehaviour
    {
        public static RestockManager Instance { get; private set; } = null!;

        public LethalBotStockRequirement[] LethalBotStockRequirements = null!;

        private ConfigStockRequirement[] configStockRequirements = null!;

        private void Awake()
        {
            // Prevent multiple instances of LoadoutManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake RestockManager =====================");
        }

        public void InitStockRequirements(ConfigStockRequirement[] configStockRequirements)
        {
            Plugin.LogDebug($"InitStockRequirements, nbStockRequirements {configStockRequirements.Length}");
            LethalBotStockRequirements = new LethalBotStockRequirement[configStockRequirements.Length];
            this.configStockRequirements = configStockRequirements;

            // InitNewStockRequirement
            for (int i = 0; i < configStockRequirements.Length; i++)
            {
                LethalBotStockRequirements[i] = InitNewStockRequirement(i);
            }
        }

        private LethalBotStockRequirement InitNewStockRequirement(int idStockRequirement)
        {
            // Get a config stock requirement
            string name;
            ConfigStockRequirement configStockRequirement;
            if (idStockRequirement >= this.configStockRequirements.Length)
            {
                configStockRequirement = ConfigConst.DEFAULT_CONFIG_STOCK_REQUIREMENT;
            }
            else
            {
                configStockRequirement = this.configStockRequirements[idStockRequirement];
            }

            // Instead of looping through itemsList multiple times, we do it once
            // and put the item names in a dictionary for quick lookups
            var itemLookup = new Dictionary<string, Item>();
            foreach (var item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (item == null) continue;

                if (!itemLookup.ContainsKey(item.itemName))
                {
                    itemLookup[item.itemName] = item;
                }
                else
                {
                    Plugin.LogWarning($"Duplicate item name detected: {item.itemName}");
                }
            }

            // Find actual item in game using the given name!
            // NOTE: We don't actually check if the item can be purchased here,
            // since that could be changed through other mods config settings!
            name = configStockRequirement.itemName;
            Plugin.LogInfo($"RestockManager attempting to register stock requirement for item {name}");
            if (!itemLookup.TryGetValue(name, out Item? foundItem))
            {
                foundItem = null; // Force it null!
            }

            // Lets find all of the required items!
            // Also null check here to maintain backwards compatability
            List<LethalBotStockRequiredItem> requiredItems = new List<LethalBotStockRequiredItem>();
            if (configStockRequirement.requiredItems != null)
            {
                foreach (var requiredItem in configStockRequirement.requiredItems)
                {
                    string requiredItemName = requiredItem.itemName;
                    if (itemLookup.TryGetValue(requiredItemName, out Item item))
                    {
                        requiredItems.Add(new LethalBotStockRequiredItem(item, requiredItem.requiredStock));
                    }
                    else
                    {
                        Plugin.LogWarning($"Failed to find required item with name {requiredItemName}! Is the name misspelled or has the wrong letter case?");
                    }
                }
            }

            // Legacy support for the old requiredItemName.
            #pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrWhiteSpace(configStockRequirement.requiredItemName))
            {
                string requiredItemName = configStockRequirement.requiredItemName;
                if (itemLookup.TryGetValue(requiredItemName, out Item item))
                {
                    requiredItems.Add(new LethalBotStockRequiredItem(item, 1));
                }
                else
                {
                    Plugin.LogWarning($"Failed to find required item with name {requiredItemName}! Is the name misspelled or has the wrong letter case?");
                }
            }
            #pragma warning restore CS0618 // Type or member is obsolete

            // Check if the eco limit has an override set
            bool overrideEcoLimit = configStockRequirement.overrideEcoLimit;
            int ecoLimit = configStockRequirement.ecoLimit;
            if (foundItem == null)
            {
                Plugin.LogWarning($"Failed to find item with name {name}! Is the name misspelled or has the wrong letter case?");
                return new LethalBotStockRequirement(idStockRequirement, null, 0, overrideEcoLimit, ecoLimit, requiredItems); // Give an invalid stock requirement!
            }

            // LethalBotStockRequirement
            Plugin.LogInfo($"RestockManager successfully registered stock requirement for item {name}");
            return new LethalBotStockRequirement(idStockRequirement, foundItem, configStockRequirement.desiredStock, overrideEcoLimit, ecoLimit, requiredItems);
        }

        /// <summary>
        /// Returns the given stock item with the given <paramref name="name"/>
        /// </summary>
        /// <remarks>
        /// This compares the <see cref="LethalBotStockRequirement.Item"/>'s <see cref="Item.itemName"/> with <paramref name="name"/>!
        /// </remarks>
        /// <param name="name"></param>
        /// <returns></returns>
        public LethalBotStockRequirement? GetLethalBotStockRequirementWithName(string name)
        {
            name = name.Trim().ToLower();
            foreach (var stockRequirement in LethalBotStockRequirements)
            {
                if (stockRequirement != null && stockRequirement.Item != null)
                {
                    string loadoutName = stockRequirement.Name.Trim().ToLower();
                    if (loadoutName == name)
                    {
                        return stockRequirement;
                    }
                }
            }

            // If it doesn't exist, just return null
            return null;
        }

        /// <summary>
        /// Returns the given stock item with the given <paramref name="item"/>
        /// </summary>
        /// <remarks>
        /// This just a wrapper function that calls <see cref="GetLethalBotStockRequirementWithName(string)"/> internally.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LethalBotStockRequirement? GetLethalBotStockRequirementWithItem(Item item)
        {
            // Find our stock requirment for this item!
            return item != null ? GetLethalBotStockRequirementWithName(item.itemName) : null;
        }

        public LethalBotStockRequirement this[int index]
        {
            get
            {
                if (index < 0 || index >= LethalBotStockRequirements.Length)
                {
                    throw new IndexOutOfRangeException();
                }
                return LethalBotStockRequirements[index];
            }
        }

        public LethalBotStockRequirement? GetStockRequirementFromIndex(int index)
        {
            if (index < 0 || index > LethalBotStockRequirements.Length)
            {
                return null;
            }
            return LethalBotStockRequirements[index];
        }
    }
}
