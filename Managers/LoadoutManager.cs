using LethalBots.AI;
using LethalBots.Configs;
using LethalBots.Constants;
using LethalBots.NetworkSerializers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling bot loadouts in the game.
    /// </summary>
    public class LoadoutManager : MonoBehaviour
    {
        public static LoadoutManager Instance { get; private set; } = null!;

        public LethalBotLoadout[] LethalBotLoadouts = null!;

        private ConfigLoadout[] configLoadouts = null!;

        private void Awake()
        {
            // Prevent multiple instances of LoadoutManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake LoadoutManager =====================");
        }

        public void InitLoadouts(ConfigLoadout[] configLoadouts)
        {
            Plugin.LogDebug($"InitLoadouts, nbLoadouts {configLoadouts.Length}");
            LethalBotLoadouts = new LethalBotLoadout[configLoadouts.Length];
            this.configLoadouts = configLoadouts;

            // InitNewIdentity
            for (int i = 0; i < configLoadouts.Length; i++)
            {
                LethalBotLoadouts[i] = InitNewLoadout(i);
            }
        }

        private LethalBotLoadout InitNewLoadout(int idLoadout)
        {
            // Get a config loadout
            string name;
            ConfigLoadout configLoadout;
            if (idLoadout >= this.configLoadouts.Length)
            {
                configLoadout = ConfigConst.DEFAULT_CONFIG_LOADOUT;
                name = string.Format(configLoadout.name, idLoadout);
            }
            else
            {
                configLoadout = this.configLoadouts[idLoadout];
                name = configLoadout.name;
            }

            // Find actual item in game using the given ID!
            List<string> loadoutItems = configLoadout.itemNames.ToList();
            List<Item> foundLoadoutItems = new List<Item>();
            foreach (var item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (item != null 
                    && !foundLoadoutItems.Contains(item) 
                    && loadoutItems.Contains(item.itemName))
                {
                    foundLoadoutItems.Add(item);
                    loadoutItems.Remove(item.itemName);
                    if (loadoutItems.Count == 0)
                    {
                        break;
                    }
                }
            }

            // LethalBotLoadout
            return new LethalBotLoadout(idLoadout, name, foundLoadoutItems.ToArray());
        }

        public LethalBotLoadout GetLethalBotLoadoutWithName(string name)
        {
            name = name.ToLower().Trim();
            foreach (var loadout in LethalBotLoadouts)
            {
                string loadoutName = loadout.Name.ToLower().Trim();
                if (loadoutName == name)
                {
                    return loadout;
                }
            }

            // Log if we failed
            if (name != "empty")
            { 
                Plugin.LogWarning($"LoadoutManager failed to find loadout with name {name}! Using default: Empty!");
                return GetLethalBotLoadoutWithName("Empty"); // NOTE: Prevents us from recreating the empty loadout!
            }

            // If the default loadout of empty doesn't exist yet, create it now!
            // NOTE: You may ask why I don't do this when the mod starts and thats because if every bot has a vaild loadout,
            // why waste the memory storing the default!
            int defaultIndex = LethalBotLoadouts.Length;
            Array.Resize(ref LethalBotLoadouts, defaultIndex + 1);
            Plugin.LogInfo("LoadoutManager creating default loadout!");

            // Create and add the default loadout to the Loadouts list!
            ConfigLoadout configLoadout = ConfigConst.DEFAULT_CONFIG_LOADOUT;
            const int idLoadout = -1;
            string defaultName = configLoadout.name;
            Item[] defaultItems = new Item[0];
            LethalBotLoadout defaultLoadout = new LethalBotLoadout(idLoadout, defaultName, defaultItems);
            LethalBotLoadouts[defaultIndex] = defaultLoadout;
            return defaultLoadout;
        }

        public LethalBotLoadout this[int index]
        {
            get
            {
                if (index < 0 || index >= LethalBotLoadouts.Length)
                {
                    throw new IndexOutOfRangeException();
                }
                return LethalBotLoadouts[index];
            }
        }

        public LethalBotLoadout? GetLoadoutFromIndex(int index)
        {
            if (index < 0 || index > LethalBotLoadouts.Length)
            {
                return null;
            }
            return LethalBotLoadouts[index];
        }
    }
}
