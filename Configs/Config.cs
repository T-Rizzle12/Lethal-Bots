using BepInEx;
using BepInEx.Configuration;
using CSync.Extensions;
using CSync.Lib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.NetworkSerializers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LethalBots.Configs
{
    // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
    // Csync https://lethal.wiki/dev/apis/csync/usage-guide

    /// <summary>
    /// Config class, manage parameters editable by the player (irl)
    /// </summary>
    public class Config : SyncedConfig2<Config>
    {
        // Bot settings
        [SyncedEntryField] public SyncedEntry<int> MaxBotsAllowedToSpawn;
        public ConfigEntry<int> MaxAnimatedBots;

        // Identity  
        [SyncedEntryField] public SyncedEntry<bool> SpawnIdentitiesRandomly;

        // Behaviour       
        [SyncedEntryField] public SyncedEntry<bool> FollowCrouchWithPlayer;
        [SyncedEntryField] public SyncedEntry<bool> ChangeSuitAutoBehaviour;
        [SyncedEntryField] public SyncedEntry<bool> TeleportWhenUsingLadders;
        [SyncedEntryField] public SyncedEntry<bool> SellAllScrapOnShip;
        [SyncedEntryField] public SyncedEntry<bool> DropHeldEquipmentAtShip;
        [SyncedEntryField] public SyncedEntry<bool> GrabItemsNearEntrances;
        [SyncedEntryField] public SyncedEntry<bool> GrabBeesNest;
        [SyncedEntryField] public SyncedEntry<bool> GrabDeadBodies;
        [SyncedEntryField] public SyncedEntry<bool> GrabManeaterBaby;
        [SyncedEntryField] public SyncedEntry<bool> AdvancedManeaterBabyAI;
        [SyncedEntryField] public SyncedEntry<bool> GrabWheelbarrow;
        [SyncedEntryField] public SyncedEntry<bool> GrabShoppingCart;

        // Teleporters
        [SyncedEntryField] public SyncedEntry<bool> TeleportedBotDropItems;

        // Voices
        public ConfigEntry<int> Talkativeness;
        public ConfigEntry<bool> AllowSwearing;

        // Debug
        public ConfigEntry<bool> EnableDebugLog;

        // Config identities
        public ConfigIdentities ConfigIdentities;

        public Config(ConfigFile cfg) : base(MyPluginInfo.PLUGIN_GUID)
        {
            cfg.SaveOnConfigSet = false;

            // Bots
            MaxBotsAllowedToSpawn = cfg.BindSyncedEntry(ConfigConst.ConfigSectionMain,
                                           "Max amount of bots that can spawn",
                                           defaultValue: ConfigConst.DEFAULT_MAX_BOTS_AVAILABLE,
                                           new ConfigDescription("Be aware of possible performance problems when more than ~16 bots spawned",
                                                                 new AcceptableValueRange<int>(ConfigConst.MIN_BOTS_AVAILABLE, ConfigConst.MAX_BOTS_AVAILABLE)));

            MaxAnimatedBots = cfg.Bind(ConfigConst.ConfigSectionMain,
                                   "Max animated bots at once",
                                   defaultValue: ConfigConst.MAX_BOTS_AVAILABLE,
                                   new ConfigDescription("Set the maximum of bots that can be animated at the same time (if heavy lag occurs when looking at a lot of bots) (client only)",
                                                         new AcceptableValueRange<int>(1, ConfigConst.MAX_BOTS_AVAILABLE)));

            // Identities
            SpawnIdentitiesRandomly = cfg.BindSyncedEntry(ConfigConst.ConfigSectionIdentities,
                                              "Randomness of identities",
                                              defaultVal: false,
                                              "Spawn the bot with random identities from the file rather than in order?");

            // Behaviour
            FollowCrouchWithPlayer = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Crouch with player",
                                               defaultVal: true,
                                               "Should the bot crouch like the player is crouching? (NOTE: This will not affect the dynamic crouching AI!)");

            ChangeSuitAutoBehaviour = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Options for automaticaly switch suit",
                                               defaultVal: false,
                                               "Should the bot automatically switch to the same suit as the player who they are assigned to?");

            TeleportWhenUsingLadders = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Teleport when using ladders",
                                               defaultVal: false,
                                               "Should the bot just teleport and bypass any animations when using ladders? (Useful if you think the bot tends to get stuck on them!)");

            SellAllScrapOnShip = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Sell all scrap on ship",
                                               defaultVal: false,
                                               "Should the bot sell all scrap on the ship? If false, bots will use advanced AI to only sell to quota! (NOTE: This is useful if you have a mod such as quota rollover and the like!)");

            DropHeldEquipmentAtShip = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Drop held equipment at ship",
                                               defaultVal: false,
                                               "Should the bot drop all equipment its holding when at the ship? If false, bots will hold onto equipment, such as shovels! (NOTE: This doesn't affect bot if it returns to the ship on its own!)");

            GrabItemsNearEntrances = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                               "Grab items near entrances",
                                               defaultVal: true,
                                               "Should the bot grab the items near main entrance and fire exits?");

            GrabBeesNest = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                    "Grab bees nests",
                                    defaultVal: false,
                                    "Should the bot try to grab bees nests? (NOTE: Bots will sell them regardless if this is true or false!)");

            GrabDeadBodies = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                      "Grab dead bodies",
                                      defaultVal: true,
                                      "Should the bot try to grab dead bodies? (NOTE: The bot at the terminal will still teleport them back to the ship!))");

            GrabManeaterBaby = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                      "Grab the baby maneater",
                                      defaultVal: true,
                                      "Is the bot allowed to grab the baby maneater? (NOTE: The bots do have AI for taking care of the baby maneater, but it's very basic!)");

            AdvancedManeaterBabyAI = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                      "Advanced baby maneater AI",
                                      defaultVal: false,
                                      "Should the bot use advanced AI for taking care of the baby maneater? (WARNING: This is experimental and may cause issues!)");

            GrabWheelbarrow = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                      "Grab the wheelbarrow",
                                      defaultVal: false,
                                      "Should the bot try to grab the wheelbarrow (mod)?");

            GrabShoppingCart = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehaviour,
                                      "Grab the shopping cart",
                                      defaultVal: false,
                                      "Should the bot try to grab the shopping cart (mod)?");

            // Teleporters
            TeleportedBotDropItems = cfg.BindSyncedEntry(ConfigConst.ConfigSectionTeleporters,
                                                            "Inverse Teleported bots drop items when teleporting",
                                                            defaultVal: true,
                                                            "Should the bot drop their items when inverse teleporting?");

            // Voices
            Talkativeness = cfg.Bind(ConfigConst.ConfigSectionVoices,
                                     "Talkativeness (Client only)",
                                     defaultValue: (int)VoicesConst.DEFAULT_CONFIG_ENUM_TALKATIVENESS,
                                     new ConfigDescription("0: No talking | 1: Shy | 2: Normal | 3: Talkative | 4: Can't stop talking",
                                                     new AcceptableValueRange<int>(Enum.GetValues(typeof(EnumTalkativeness)).Cast<int>().Min(),
                                                                                   Enum.GetValues(typeof(EnumTalkativeness)).Cast<int>().Max())));

            AllowSwearing = cfg.Bind(ConfigConst.ConfigSectionVoices,
                                     "Swear words (Client only)",
                                     defaultValue: false,
                                     "Allow the use of swear words in bots voice lines ?");

            // Debug
            EnableDebugLog = cfg.Bind(ConfigConst.ConfigSectionDebug,
                                      "EnableDebugLog  (Client only)",
                                      defaultValue: false,
                                      "Enable the debug logs used for this mod.");

            ClearUnusedEntries(cfg);
            cfg.SaveOnConfigSet = true;

            // Config identities
            CopyDefaultConfigIdentitiesJson();
            ReadAndLoadConfigIdentitiesFromUser();

            ConfigManager.Register(this);
        }

        private void LogDebugInConfig(string debugLog)
        {
            if (!EnableDebugLog.Value)
            {
                return;
            }
            Plugin.Logger.LogDebug(debugLog);
        }

        private void ClearUnusedEntries(ConfigFile cfg)
        {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }

        private void CopyDefaultConfigIdentitiesJson()
        {
            try
            {
                string directoryPath = Utility.CombinePaths(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
                Directory.CreateDirectory(directoryPath);

                string json = ReadJsonResource("LethalBots.Configs.ConfigIdentities.json");
                using (StreamWriter outputFile = new StreamWriter(Utility.CombinePaths(directoryPath, ConfigConst.FILE_NAME_CONFIG_IDENTITIES_DEFAULT)))
                {
                    outputFile.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error while CopyDefaultConfigIdentitiesJson ! {ex}");
            }
        }

        private string ReadJsonResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void ReadAndLoadConfigIdentitiesFromUser()
        {
            string json;
            string path = "No path yet";

            try
            {
                path = Utility.CombinePaths(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID, ConfigConst.FILE_NAME_CONFIG_IDENTITIES_USER);
                // Try to read user config file
                if (File.Exists(path))
                {
                    Plugin.Logger.LogInfo("User identities file found ! Reading...");
                    using (StreamReader r = new StreamReader(path))
                    {
                        json = r.ReadToEnd();
                    }

                    ConfigIdentities = JsonUtility.FromJson<ConfigIdentities>(json);
                    if (ConfigIdentities.configIdentities == null)
                    {
                        Plugin.Logger.LogWarning($"Unknown to read identities from file at {path}");
                    }
                }
                else
                {
                    Plugin.Logger.LogInfo("No user identities file found. Reading default identities...");
                    path = "LethalBots.Configs.ConfigIdentities.json";
                    json = ReadJsonResource(path);
                    ConfigIdentities = JsonUtility.FromJson<ConfigIdentities>(json);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Error while ReadAndLoadConfigIdentitiesFromUser ! {e}");
                json = "No json, see exception above.";
            }

            if (ConfigIdentities.configIdentities == null)
            {
                Plugin.Logger.LogWarning($"A problem occured while retrieving identities from config file ! continuing with no identities... json used : \n{json}");
                ConfigIdentities = new ConfigIdentities() { configIdentities = new ConfigIdentity[0] };
            }
            else
            {
                Plugin.Logger.LogInfo($"Loaded {ConfigIdentities.configIdentities.Length} identities from file : {path}");
                foreach (ConfigIdentity configIdentity in ConfigIdentities.configIdentities)
                {
                    LogDebugInConfig($"{configIdentity.ToString()}");
                }
            }
        }
    }
}