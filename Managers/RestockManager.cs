using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.NetworkSerializers;
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
            Plugin.LogDebug("=============== awake ShoppingManager =====================");
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
            string requiredName;
            ConfigStockRequirement configStockRequirement;
            if (idStockRequirement >= this.configStockRequirements.Length)
            {
                configStockRequirement = ConfigConst.DEFAULT_CONFIG_STOCK_REQUIREMENT;
            }
            else
            {
                configStockRequirement = this.configStockRequirements[idStockRequirement];
            }

            name = configStockRequirement.itemName;
            requiredName = configStockRequirement.requiredItemName;

            // Find actual item in game using the given ID!
            // NOTE: We don't actually check if the item can be purchased here,
            // since that could be changed through other mods config settings!
            Item? foundItem = null;
            Item? foundRequiredItem = null;
            foreach (var item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (foundItem != null && foundRequiredItem != null)
                {
                    break;
                }
                if (item != null)
                {
                    string itemName = item.itemName;
                    if (itemName == name)
                    { 
                        foundItem = item; 
                    }
                    if (itemName == requiredName)
                    {
                        foundRequiredItem = item;
                    }
                }
            }

            if (foundItem == null)
            {
                Plugin.LogWarning($"Failed to find item with name {name}! Is the name misspelled or has the wrong letter case?");
                return new LethalBotStockRequirement(idStockRequirement, null, 0, requiredName); // Give an invalid stock requirement!
            }

            // LethalBotLoadout
            return new LethalBotStockRequirement(idStockRequirement, foundItem, configStockRequirement.desiredStock, requiredName);
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
