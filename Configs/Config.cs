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
        [SyncedEntryField] public SyncedEntry<bool> AllowBotsToChat;
        [SyncedEntryField] public SyncedEntry<bool> AllowMissionControlTeleport;
        [SyncedEntryField] public SyncedEntry<bool> StartShipChatCommandProtection;
        [SyncedEntryField] public SyncedEntry<bool> AutoMissionControl;
        [SyncedEntryField] public SyncedEntry<float> ReturnToShipTime;
        [SyncedEntryField] public SyncedEntry<bool> TeleportWhenUsingLadders;
        [SyncedEntryField] public SyncedEntry<bool> SellAllScrapOnShip;
        [SyncedEntryField] public SyncedEntry<bool> DropHeldEquipmentAtShip;
        [SyncedEntryField] public SyncedEntry<bool> ShouldKillEverything;
        [SyncedEntryField] public SyncedEntry<bool> GrabItemsNearEntrances;
        [SyncedEntryField] public SyncedEntry<bool> GrabBeesNest;
        [SyncedEntryField] public SyncedEntry<bool> GrabDeadBodies;
        [SyncedEntryField] public SyncedEntry<bool> GrabDockedApparatus;
        [SyncedEntryField] public SyncedEntry<bool> TakeCareOfManeaterBaby;
        [SyncedEntryField] public SyncedEntry<bool> AdvancedManeaterBabyAI;
        [SyncedEntryField] public SyncedEntry<bool> GrabWheelbarrow;
        [SyncedEntryField] public SyncedEntry<bool> GrabShoppingCart;

        // Teleporters
        [SyncedEntryField] public SyncedEntry<bool> TeleportedBotDropItems;

        // Voice Recognition
        public ConfigEntry<bool> AllowVoiceRecognition;
        public ConfigEntry<float> VoiceRecognitionSimilarityThreshold;

        // Voices
        public ConfigEntry<int> Talkativeness;
        public ConfigEntry<bool> AllowSwearing;
        [SyncedEntryField] public SyncedEntry<bool> AllowTalkingWhileDead;

        // Debug
        public ConfigEntry<bool> EnableDebugLog;

        // Config identities
        public ConfigIdentities ConfigIdentities;
        public ConfigLoadouts ConfigLoadouts;

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
                                   "Max animated bots at once (Client only)",
                                   defaultValue: ConfigConst.MAX_BOTS_AVAILABLE,
                                   new ConfigDescription("Set the maximum of bots that can be animated at the same time (if heavy lag occurs when looking at a lot of bots) (client only)",
                                                         new AcceptableValueRange<int>(1, ConfigConst.MAX_BOTS_AVAILABLE)));

            // Identities
            SpawnIdentitiesRandomly = cfg.BindSyncedEntry(ConfigConst.ConfigSectionIdentities,
                                              "Randomness of identities",
                                              defaultVal: false,
                                              "Spawn the bot with random identities from the file rather than in order?");

            // Behavior
            FollowCrouchWithPlayer = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Crouch with player",
                                               defaultVal: true,
                                               "Should the bot crouch like the player is crouching? (NOTE: This will not affect the dynamic crouching AI!)");

            ChangeSuitAutoBehaviour = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Options for automaticaly switch suit",
                                               defaultVal: false,
                                               "Should the bot automatically switch to the same suit as the player who they are assigned to?");

            AllowBotsToChat = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Allow using chat",
                                               defaultVal: true,
                                               "Should the bot be allowed to use the chat? (NOTE: This is useful if you don't want bots spamming the chat! Also this doesn't affect bots calling out jesters!)");

            AllowMissionControlTeleport = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                                "Allow the mission controller to teleport living players",
                                                defaultVal: true,
                                                "Should the bot who is the active mission controller be allowed to teleport living players. (NOTE: This doesn't affect dead body teleportation or if a player specifically request to be teleported!)");

            StartShipChatCommandProtection = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                                "Start the ship chat command protection",
                                                defaultVal: true,
                                                "Should non-host players be allowed to tell the mission controller bot to start the ship? (NOTE: Bots will allow non-host players to start the ship if the host is dead regardless of this option!)");

            AutoMissionControl = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                                "Allow automatic mission control assignment",
                                                defaultVal: true,
                                                "Should bots that are chilling at the ship automatically assume the mission control state if the current mission controller is not set or dead?");

            ReturnToShipTime = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                                "Return to ship time",
                                                defaultValue: 0.63f,
                                                new ConfigDescription("At what time should bots automatically return to the ship. This has to be a normalized value. TIP: 10:00 PM is about 0.9f and the time the ship auto leaves at is 0.996f!", 
                                                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            TeleportWhenUsingLadders = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Teleport when using ladders",
                                               defaultVal: false,
                                               "Should the bot just teleport and bypass any animations when using ladders? (Useful if you think the bot tends to get stuck on them!)");

            SellAllScrapOnShip = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Sell all scrap on ship",
                                               defaultVal: false,
                                               "Should the bot sell all scrap on the ship? If false, bots will use advanced AI to only sell to quota! (NOTE: This is useful if you have a mod such as quota rollover and the like!)");

            DropHeldEquipmentAtShip = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Drop held equipment at ship",
                                               defaultVal: false,
                                               "Should the bot drop all equipment its holding when at the ship? If false, bots will hold onto equipment, such as shovels! (NOTE: This doesn't affect bot loadouts or if it returns to the ship on its own!)");

            ShouldKillEverything = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                                "Should bots attempt to kill everything",
                                                defaultVal: false,
                                                "Should bots attempt to kill every enemy in the game, even if they can't be killed normally?");

            GrabItemsNearEntrances = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                               "Grab items near entrances",
                                               defaultVal: true,
                                               "Should the bot grab the items near main entrance and fire exits?");

            GrabBeesNest = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                    "Grab bees nests",
                                    defaultVal: false,
                                    "Should the bot try to grab bees nests? (NOTE: Bots will sell them regardless if this is true or false!)");

            GrabDeadBodies = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Grab dead bodies",
                                      defaultVal: true,
                                      "Should the bot try to grab dead bodies? (NOTE: The bot at the terminal will still teleport them back to the ship!))");

            GrabDockedApparatus = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Grab docked apparatus",
                                      defaultVal: true,
                                      "Is the bot allowed to grab docked apparatuses?");

            TakeCareOfManeaterBaby = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Take care of baby maneater",
                                      defaultVal: true,
                                      "Is the bot allowed to calm down the baby maneater if its crying?");

            AdvancedManeaterBabyAI = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Advanced baby maneater AI",
                                      defaultVal: false,
                                      "Should the bot use advanced AI for taking care of the baby maneater? (WARNING: This is experimental and may cause issues!)");

            GrabWheelbarrow = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Grab the wheelbarrow",
                                      defaultVal: false,
                                      "Should the bot try to grab the wheelbarrow (mod)?");

            GrabShoppingCart = cfg.BindSyncedEntry(ConfigConst.ConfigSectionBehavior,
                                      "Grab the shopping cart",
                                      defaultVal: false,
                                      "Should the bot try to grab the shopping cart (mod)?");

            // Teleporters
            TeleportedBotDropItems = cfg.BindSyncedEntry(ConfigConst.ConfigSectionTeleporters,
                                                            "Inverse Teleported bots drop items when teleporting",
                                                            defaultVal: true,
                                                            "Should the bot drop their items when inverse teleporting?");

            // Voice Recognition
            AllowVoiceRecognition = cfg.Bind(ConfigConst.ConfigSectionVoiceRecognition,
                                            "Enable bot Voice Recognition (Client only)",
                                            defaultValue: true,
                                            "Should the bots be able to hear what you say in voice chat. (It would only be used for the voice commands!)");

            VoiceRecognitionSimilarityThreshold = cfg.Bind(ConfigConst.ConfigSectionVoiceRecognition,
                                                           "Voice recognition similarity threshold (Client only)",
                                                           defaultValue: 0.8f,
                                                           new ConfigDescription("This is the level of accuracy that would be used by PySpeech when the bot attempts to determine if the given message was a valid chat command. (Higher numbers means a higher level of accuracy is needed!)", 
                                                                            new AcceptableValueRange<float>(0.0f, 1.0f)));

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

            AllowTalkingWhileDead = cfg.BindSyncedEntry(ConfigConst.ConfigSectionVoices,
                                                        "Allow bots to talk while dead",
                                                        defaultVal: true,
                                                        "Are bots that are dead allowed to talk? (NOTE: Only other dead players can hear them!)");

            // Debug
            EnableDebugLog = cfg.Bind(ConfigConst.ConfigSectionDebug,
                                      "EnableDebugLog  (Client only)",
                                      defaultValue: true,
                                      "Enable the debug logs used for this mod.");

            ClearUnusedEntries(cfg);
            cfg.SaveOnConfigSet = true;

            // Config identities
            CopyDefaultConfigIdentitiesJson();
            ReadAndLoadConfigIdentitiesFromUser();

            // Config loadouts
            CopyDefaultConfigLoadoutsJson();
            ReadAndLoadConfigLoadoutsFromUser();

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

        private void CopyDefaultConfigLoadoutsJson()
        {
            try
            {
                string directoryPath = Utility.CombinePaths(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
                Directory.CreateDirectory(directoryPath);

                string json = ReadJsonResource("LethalBots.Configs.ConfigLoadouts.json");
                using (StreamWriter outputFile = new StreamWriter(Utility.CombinePaths(directoryPath, ConfigConst.FILE_NAME_CONFIG_LOADOUTS_DEFAULT)))
                {
                    outputFile.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error while CopyDefaultConfigLoadoutsJson ! {ex}");
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

        private void ReadAndLoadConfigLoadoutsFromUser()
        {
            string json;
            string path = "No path yet";
            ConfigLoadouts defaultLoadouts = new ConfigLoadouts() { configLoadouts = null! };
            ConfigLoadouts userLoadouts = new ConfigLoadouts() { configLoadouts = null! };
            ConfigLoadouts = new ConfigLoadouts() { configLoadouts = null! };
            bool defaultLoadoutsLoaded = false;
            bool userLoadoutsLoaded = false;

            try
            {
                path = Utility.CombinePaths(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID, ConfigConst.FILE_NAME_CONFIG_LOADOUTS_USER);
                // Try to read user config file
                if (File.Exists(path))
                {
                    Plugin.Logger.LogInfo("User loadout file found! Reading...");
                    using (StreamReader r = new StreamReader(path))
                    {
                        json = r.ReadToEnd();
                    }


                    userLoadouts = JsonUtility.FromJson<ConfigLoadouts>(json);
                    if (userLoadouts.configLoadouts == null)
                    {
                        Plugin.Logger.LogWarning($"Unknown to read loadouts from file at {path}");
                    }
                    else
                    {
                        userLoadoutsLoaded = true;
                        Plugin.Logger.LogInfo($"Loaded {userLoadouts.configLoadouts.Length} identities from file : {path}");
                    }

                    Plugin.Logger.LogInfo("Reading default loadouts...");
                    path = "LethalBots.Configs.ConfigLoadouts.json";
                    json = ReadJsonResource(path);
                    defaultLoadouts = JsonUtility.FromJson<ConfigLoadouts>(json);
                    if (defaultLoadouts.configLoadouts != null)
                    {
                        defaultLoadoutsLoaded = true;
                        Plugin.Logger.LogInfo($"Loaded {defaultLoadouts.configLoadouts.Length} identities from file : {path}");
                    }
                }
                else
                {
                    Plugin.Logger.LogInfo("No user loadout file found. Reading only default loadouts...");
                    path = "LethalBots.Configs.ConfigLoadouts.json";
                    json = ReadJsonResource(path);
                    defaultLoadouts = JsonUtility.FromJson<ConfigLoadouts>(json);
                    if (defaultLoadouts.configLoadouts != null)
                    {
                        defaultLoadoutsLoaded = true;
                        Plugin.Logger.LogInfo($"Loaded {defaultLoadouts.configLoadouts.Length} identities from file : {path}");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Error while ReadAndLoadConfigLoadoutsFromUser ! {e}");
                json = "No json, see exception above.";
            }

            // Now make a combined version
            if (userLoadouts.configLoadouts != null)
            {
                // Check if default was properly loaded
                if (defaultLoadouts.configLoadouts != null)
                {
                    // Now we merge the two files together
                    List<ConfigLoadout> loadouts = new List<ConfigLoadout>();
                    List<string> takenNames = new List<string>();
                    foreach (var loadout in userLoadouts.configLoadouts)
                    {
                        // User defined loadouts take priority
                        loadouts.Add(loadout);
                        if (!takenNames.Contains(loadout.name))
                        {
                            takenNames.Add(loadout.name);
                        }
                    }

                    // Now we are onto the default, if the user overwrote the default, just ignore them!
                    foreach (var loadout in defaultLoadouts.configLoadouts)
                    {
                        // Make sure that name is open!
                        if (!takenNames.Contains(loadout.name))
                        {
                            loadouts.Add(loadout);
                        }
                    }

                    ConfigLoadouts.configLoadouts = loadouts.ToArray();
                }
                else
                {
                    ConfigLoadouts = userLoadouts;
                }
            }
            else if (defaultLoadouts.configLoadouts != null)
            {
                ConfigLoadouts = defaultLoadouts;
            }

            if (ConfigLoadouts.configLoadouts == null)
            {
                Plugin.Logger.LogWarning($"A problem occured while retrieving loadouts from config file! continuing with no loadouts... json used : \n{json}");
                ConfigLoadouts = new ConfigLoadouts() { configLoadouts = new ConfigLoadout[0] };
            }
            else
            {
                Plugin.Logger.LogInfo($"Loaded {ConfigLoadouts.configLoadouts.Length} loadouts from files! Default loaded: {defaultLoadoutsLoaded} and User Loaded: {userLoadoutsLoaded}");
                foreach (ConfigLoadout configIdentity in ConfigLoadouts.configLoadouts)
                {
                    LogDebugInConfig($"{configIdentity.ToString()}");
                }
            }
        }
    }
}