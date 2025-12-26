using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using LethalBots.Enums;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Inputs;
using LethalBots.Managers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Patches.GameEnginePatches;
using LethalBots.Patches.MapHazardsPatches;
using LethalBots.Patches.MapPatches;
using LethalBots.Patches.ModPatches.AdditionalNetworking;
using LethalBots.Patches.ModPatches.BetterEmotes;
using LethalBots.Patches.ModPatches.BunkbedRevive;
using LethalBots.Patches.ModPatches.ButteryFixes;
using LethalBots.Patches.ModPatches.LCAlwaysHearActiveWalkie;
using LethalBots.Patches.ModPatches.LethalPhones;
using LethalBots.Patches.ModPatches.LethalProgression;
using LethalBots.Patches.ModPatches.ModelRplcmntAPI;
using LethalBots.Patches.ModPatches.MoreEmotes;
using LethalBots.Patches.ModPatches.Peepers;
using LethalBots.Patches.ModPatches.ReservedItemSlotCore;
using LethalBots.Patches.ModPatches.ReviveCompany;
using LethalBots.Patches.ModPatches.ShowCapacity;
using LethalBots.Patches.ModPatches.TooManyEmotes;
using LethalBots.Patches.ModPatches.Zaprillator;
using LethalBots.Patches.NpcPatches;
using LethalBots.Patches.ObjectsPatches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;
using GameNetcodeStuff;
using PySpeech;

namespace LethalBots
{
    /// <summary>
    /// Main mod plugin class, start everything
    /// </summary>
    [BepInPlugin(ModGUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(Const.CSYNC_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(LethalCompanyInputUtils.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(Const.PYSPEECH_GUID, BepInDependency.DependencyFlags.HardDependency)]
    // Soft Dependencies
    [BepInDependency(Const.REVIVECOMPANY_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.BUNKBEDREVIVE_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.ZAPRILLATOR_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.MOREEMOTES_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.BETTEREMOTES_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.TOOMANYEMOTES_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.MORECOMPANY_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.MODELREPLACEMENT_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.LETHALPHONES_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.SHOWCAPACITY_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.RESERVEDITEMSLOTCORE_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.LETHALPROGRESSION_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.BUTTERYFIXES_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.PEEPERS_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.LETHALMIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.FACILITYMELTDOWN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.NAVMESHINCOMPANY_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Const.LETHALINTERNS_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        // DO NOT use MyPluginInfo.PLUGIN_GUID here! It will use the wrong GUID for the plugin!
        public const string ModGUID = "T-Rizzle." + MyPluginInfo.PLUGIN_NAME;
        public static AssetBundle ModAssets = null!;
        internal static string DirectoryName = null!;
        internal static EnemyType LethalBotNPCPrefab = null!;
        internal static int PluginIrlPlayersCount = 0;
        internal static new ManualLogSource Logger = null!;
        internal static new Configs.Config Config = null!;
        internal static LethalBotsInputs InputActionsInstance = null!;
        internal static bool IsModTooManyEmotesLoaded = false;
        internal static bool IsModModelReplacementAPILoaded = false;
        internal static bool IsModCustomItemBehaviourLibraryLoaded = false;
        internal static bool IsModMoreCompanyLoaded = false;
        internal static bool IsModReviveCompanyLoaded = false;
        internal static bool IsModBunkbedReviveLoaded = false;
        internal static bool IsModLethalMinLoaded = false;
        internal static bool IsModLethalInternsLoaded = false;
        internal static bool IsModFacilityMeltdownLoaded = false;
        internal static bool IsModNavmeshInCompanyLoaded = false;

        private readonly Harmony _harmony = new(ModGUID);

        // ========================
        // Intent Recognition System
        // ========================
        public static readonly Dictionary<BotIntent, string[]> IntentPhrases = new()
        {
            {
                BotIntent.StayOnShip,
                new[]
                {
                    "stay on ship", "stay on the ship", "stay with ship", "man the ship",
                    "guard the ship", "hold the ship", "stay at ship", "remain on ship",
                    "stay with the ship", "guard ship", "hold ship", "stay here", "stay onboard",
                    "stay ship", "be on ship", "man ship", "wait on ship", "wait at ship",
                    "protect the ship", "watch the ship", "stay in the ship", "keep ship"
                }
            },
            {
                BotIntent.GoToShip,
                new[]
                {
                    "go to ship", "return to ship", "head back to ship", "get back to ship",
                    "go back to ship", "go back to the ship", "return to the ship", "head to ship",
                    "withdraw to ship", "retreat to ship", "leave for ship", "going to ship",
                    "get to ship", "on ship", "back to ship", "board the ship"
                }
            },
            {
                BotIntent.StartShip,
                new[]
                {
                    "start the ship", "start ship", "fire up the ship", "pull the lever",
                    "start the boat", "leave now", "launch ship", "take off", "go home",
                    "get out of here", "embark", "ship start", "begin takeoff"
                }
            },
            {
                BotIntent.RequestMonitoring,
                new[]
                {
                    "request monitoring", "monitor me", "watch me", "keep an eye on me",
                    "check my screen", "switch to me", "watch my camera", "monitor my position",
                    "look at me", "tracking me", "track me", "follow my camera", "spectate me"
                }
            },
            {
                BotIntent.RequestTeleport,
                new[]
                {
                    "request teleport", "teleport me", "tp me", "tele me", "bring me back",
                    "get me out", "beam me up", "pull me back", "request tp", "teleport back",
                    "save me", "teleport now", "tele back"
                }
            },
            {
                BotIntent.ClearMonitoring,
                new[]
                {
                    "clear monitoring", "stop monitoring", "cancel monitoring", "stop watching me",
                    "monitoring off", "stop tracking", "clear monitor", "reset monitor",
                    "stop spectating", "stop screen"
                }
            },
            {
                BotIntent.Transmit,
                new[]
                {
                    "transmit", "broadcast", "send message", "signal translate", "tell them",
                    "message crew", "send signal", "signal message"
                }
            },
            {
                BotIntent.Jester,
                new[]
                {
                    "jester", "play jester", "crank jester", "start jester", "jack in the box",
                    "it's cranking", "jester cranking", "jester escape", "run from jester", "jester pop"
                }
            },
            {
                BotIntent.LeaveTerminal,
                new[]
                {
                    "hop off the terminal", "get off the terminal", "get off terminal", "leave terminal",
                    "quit terminal", "exit terminal", "stop using terminal", "terminal off",
                    "move from terminal", "off terminal"
                }
            },
            {
                BotIntent.FollowMe,
                new[]
                {
                    "follow me", "come here", "come with me", "with me", "follow", "walk with me",
                    "stick with me", "stay close", "come", "on me", "tag along", "follow player", "accompany me"
                }
            },
            {
                BotIntent.Explore,
                new[]
                {
                    "go explore", "explore", "find scrap", "lead the way", "search", "go find stuff",
                    "find stuff", "look for scrap", "loot", "go loot", "get scrap", "start searching",
                    "scavenge", "search facility", "look for loot", "gather scrap"
                }
            },
            {
                BotIntent.DropItem,
                new[]
                {
                    "drop item", "drop your item", "drop that", "put it down", "drop", "discard",
                    "leave item", "place item", "drop everything", "empty hands", "drop loot", "drop object"
                }
            },
            {
                BotIntent.ChangeSuit,
                new[]
                {
                    "change suit", "wear my suit", "put on my suit", "change clothes", "wear this suit",
                    "swap suits", "match my suit", "suit up", "use my suit", "change outfit", "wear suit"
                }
            }
        };

        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            input = input.ToLowerInvariant();
            input = Regex.Replace(input, @"[^\w\s]", ""); // Remove punctuation
            input = Regex.Replace(input, @"\s+", " ").Trim(); // Collapse whitespace
            return input;
        }

        public static BotIntent DetectIntent(string rawInput)
        {
            string input = Normalize(rawInput);
            if (string.IsNullOrEmpty(input)) return BotIntent.Unknown;

            var inputTokens = input.Split(' ').ToHashSet();

            float bestScore = 0f;
            BotIntent bestIntent = BotIntent.Unknown;

            foreach (var pair in IntentPhrases)
            {
                foreach (var phrase in pair.Value)
                {
                    var phraseTokens = phrase.Split(' ').ToHashSet();
                    if (phraseTokens.Count == 0) continue;

                    float score = (float)phraseTokens.Intersect(inputTokens).Count() / phraseTokens.Count;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIntent = pair.Key;
                    }
                }
            }

            // Threshold can be tweaked or exposed in config later
            return bestScore >= 0.6f ? bestIntent : BotIntent.Unknown;
        }

        private void Awake()
        {
            var bundleName = "lethalbotnpcmodassets";
            DirectoryName = Path.GetDirectoryName(Info.Location);
            Logger = base.Logger;
            Config = new Configs.Config(base.Config);
            InputActionsInstance = new LethalBotsInputs();

            InitializeNetworkBehaviours();

            // We need to run some code before we register the network prefab
            // Other wise it won't work!

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(DirectoryName, bundleName));
            if (ModAssets == null)
            {
                Logger.LogFatal("Failed to load custom assets.");
                return;
            }

            Object[] test = ModAssets.LoadAllAssets();
            for (int i = 0; i < test.Length; i++)
            {
                Logger.LogInfo($"Found {test[i]} in asset bundle");
                if (test[i] is EnemyType enemy)
                {
                    LethalBotNPCPrefab = enemy;
                    break;
                }
            }

            if (LethalBotNPCPrefab == null)
            {
                Logger.LogFatal("LethalBotNPC prefab failed to load.");
                return;
            }

            foreach (var transform in LethalBotNPCPrefab.enemyPrefab.GetComponentsInChildren<Transform>()
                .Where(x => x.parent != null && x.parent.name == "LethalBotNPCObj"
                            && x.name != "Collision"
                            && x.name != "TurnCompass"
                            && x.name != "CreatureSFX"
                            && x.name != "CreatureVoice")
                .ToList())
            {
                Object.DestroyImmediate(transform.gameObject);
            }

            byte[] value = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(Assembly.GetCallingAssembly().GetName().Name + gameObject.name + bundleName));
            uint newGlobalObjectIdHash = BitConverter.ToUInt32(value, 0);
            Type type = typeof(NetworkObject);
            FieldInfo fieldInfo = type.GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
            var networkObject = LethalBotNPCPrefab.enemyPrefab.GetComponent<NetworkObject>();
            fieldInfo.SetValue(networkObject, newGlobalObjectIdHash);

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(LethalBotNPCPrefab.enemyPrefab);

            InitPluginManager();
            PatchBaseGame();
            PatchOtherMods();
            RegisterVoiceCommands();

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void PatchBaseGame()
        {
            _harmony.PatchAll(typeof(AudioMixerPatch));
            _harmony.PatchAll(typeof(AudioReverbTriggerPatch));
            _harmony.PatchAll(typeof(DebugPatch));
            _harmony.PatchAll(typeof(GameNetworkManagerPatch));
            _harmony.PatchAll(typeof(HUDManagerPatch));
            _harmony.PatchAll(typeof(NetworkSceneManagerPatch));
            _harmony.PatchAll(typeof(NetworkObjectPatch));
            _harmony.PatchAll(typeof(RoundManagerPatch));
            _harmony.PatchAll(typeof(SoundManagerPatch));
            _harmony.PatchAll(typeof(StartOfRoundPatch));

            _harmony.PatchAll(typeof(EnemyAIPatch));
            _harmony.PatchAll(typeof(PlayerControllerBPatch));
            _harmony.PatchAll(typeof(EnemyAICollisionDetect));

            _harmony.PatchAll(typeof(BaboonBirdAIPatch));
            _harmony.PatchAll(typeof(BlobAIPatch));
            _harmony.PatchAll(typeof(BushWolfEnemyPatch));
            _harmony.PatchAll(typeof(ButlerBeesEnemyAIPatch));
            _harmony.PatchAll(typeof(ButlerEnemyAIPatch));
            _harmony.PatchAll(typeof(CaveDwellerAIPatch));
            _harmony.PatchAll(typeof(CentipedeAIPatch));
            _harmony.PatchAll(typeof(CrawlerAIPatch));
            _harmony.PatchAll(typeof(FlowermanAIPatch));
            _harmony.PatchAll(typeof(FlowerSnakeEnemyPatch));
            _harmony.PatchAll(typeof(ForestGiantAIPatch));
            _harmony.PatchAll(typeof(GiantKiwiAIPatch));
            _harmony.PatchAll(typeof(JesterAIPatch));
            _harmony.PatchAll(typeof(MaskedPlayerEnemyPatch));
            _harmony.PatchAll(typeof(MouthDogAIPatch));
            _harmony.PatchAll(typeof(NutcrackerEnemyAIPatch));
            _harmony.PatchAll(typeof(RadMechAIPatch));
            _harmony.PatchAll(typeof(RadMechMissilePatch));
            _harmony.PatchAll(typeof(RedLocustBeesPatch));
            _harmony.PatchAll(typeof(SandSpiderAIPatch));
            _harmony.PatchAll(typeof(SandWormAIPatch));
            _harmony.PatchAll(typeof(SpringManAIPatch));

            _harmony.PatchAll(typeof(LandminePatch));
            _harmony.PatchAll(typeof(QuicksandTriggerPatch));
            _harmony.PatchAll(typeof(SpikeRoofTrapPatch));
            _harmony.PatchAll(typeof(TurretPatch));

            _harmony.PatchAll(typeof(DoorLockPatch));
            _harmony.PatchAll(typeof(InteractTriggerPatch));
            _harmony.PatchAll(typeof(ShipTeleporterPatch));
            _harmony.PatchAll(typeof(VehicleControllerPatch));
            _harmony.PatchAll(typeof(DepositItemsDeskPatch));

            _harmony.PatchAll(typeof(CaveDwellerPhysicsPropPatch));
            _harmony.PatchAll(typeof(DeadBodyInfoPatch));
            _harmony.PatchAll(typeof(GrabbableObjectPatch));
            _harmony.PatchAll(typeof(RagdollGrabbableObjectPatch));
            _harmony.PatchAll(typeof(ShotgunItemPatch));
            _harmony.PatchAll(typeof(StunGrenadeItemPatch));
            _harmony.PatchAll(typeof(TetraChemicalItemPatch));
        }

        private void PatchOtherMods()
        {
            IsModTooManyEmotesLoaded = IsModLoaded(Const.TOOMANYEMOTES_GUID);
            IsModModelReplacementAPILoaded = IsModLoaded(Const.MODELREPLACEMENT_GUID);
            IsModCustomItemBehaviourLibraryLoaded = IsModLoaded(Const.CUSTOMITEMBEHAVIOURLIBRARY_GUID);
            IsModMoreCompanyLoaded = IsModLoaded(Const.MORECOMPANY_GUID);
            IsModReviveCompanyLoaded = IsModLoaded(Const.REVIVECOMPANY_GUID);
            IsModBunkbedReviveLoaded = IsModLoaded(Const.BUNKBEDREVIVE_GUID);
            IsModLethalMinLoaded = IsModLoaded(Const.LETHALMIN_GUID);
            IsModLethalInternsLoaded = IsModLoaded(Const.LETHALINTERNS_GUID);
            IsModFacilityMeltdownLoaded = IsModLoaded(Const.FACILITYMELTDOWN_GUID);
            IsModNavmeshInCompanyLoaded = IsModLoaded(Const.NAVMESHINCOMPANY_GUID);

            bool isModMoreEmotesLoaded = IsModLoaded(Const.MOREEMOTES_GUID);
            bool isModBetterEmotesLoaded = IsModLoaded(Const.BETTEREMOTES_GUID);
            bool isModLethalPhonesLoaded = IsModLoaded(Const.LETHALPHONES_GUID);
            bool isModShowCapacityLoaded = IsModLoaded(Const.SHOWCAPACITY_GUID);
            bool isModReservedItemSlotCoreLoaded = IsModLoaded(Const.RESERVEDITEMSLOTCORE_GUID);
            bool isModLethalProgressionLoaded = IsModLoaded(Const.LETHALPROGRESSION_GUID);
            bool isModLCAlwaysHearWalkieModLoaded = IsModLoaded(Const.LCALWAYSHEARWALKIEMOD_GUID);
            bool isModZaprillatorLoaded = IsModLoaded(Const.ZAPRILLATOR_GUID);
            bool isModButteryFixesLoaded = IsModLoaded(Const.BUTTERYFIXES_GUID);
            bool isModPeepersLoaded = IsModLoaded(Const.PEEPERS_GUID);

            List<string> preloadersFileNames = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(Path.GetFullPath(Paths.PatcherPluginPath), "*.dll", SearchOption.AllDirectories))
            {
                preloadersFileNames.Add(Path.GetFileName(file));
            }

            bool isModAdditionalNetworkingLoaded = IsPreLoaderLoaded(Const.ADDITIONALNETWORKING_DLLFILENAME, preloadersFileNames);

            if (isModMoreEmotesLoaded)
                _harmony.PatchAll(typeof(MoreEmotesPatch));

            if (isModBetterEmotesLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "StartPostfix"),
                    new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.StartPostfix_Prefix)));

                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "UpdatePostfix"),
                    null, null, new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.UpdatePostfix_Transpiler)));

                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "PerformEmotePrefix"),
                    null, null, new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.PerformEmotePrefix_Transpiler)));
            }

            if (IsModTooManyEmotesLoaded)
            {
                _harmony.PatchAll(typeof(EmoteControllerPlayerPatch));
                _harmony.PatchAll(typeof(ThirdPersonEmoteControllerPatch));
            }

            if (IsModModelReplacementAPILoaded)
            {
                _harmony.PatchAll(typeof(BodyReplacementBasePatch));
                _harmony.PatchAll(typeof(ModelReplacementPlayerControllerBPatchPatch));
                _harmony.PatchAll(typeof(ModelReplacementAPIPatch));
            }

            if (isModLethalPhonesLoaded)
            {
                _harmony.PatchAll(typeof(PlayerPhonePatchPatch));
                _harmony.PatchAll(typeof(PhoneBehaviorPatch));
                _harmony.PatchAll(typeof(PlayerPhonePatchLI));
            }

            if (isModAdditionalNetworkingLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("AdditionalNetworking.Patches.Inventory.PlayerControllerBPatch"), "OnStart"),
                    null, null, new HarmonyMethod(typeof(AdditionalNetworkingPatch), nameof(AdditionalNetworkingPatch.Start_Transpiler)));
            }

            if (isModShowCapacityLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ShowCapacity.Patches.PlayerControllerBPatch"), "Update_PreFix"),
                    new HarmonyMethod(typeof(ShowCapacityPatch), nameof(ShowCapacityPatch.Update_PreFix_Prefix)));
            }

            if (IsModReviveCompanyLoaded)
            {
                _harmony.PatchAll(typeof(ReviveCompanyGeneralUtilPatch));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch"), "setHoverTipAndCurrentInteractTriggerPatch"),
                    null, null, new HarmonyMethod(typeof(ReviveCompanyPlayerControllerBPatchPatch), nameof(ReviveCompanyPlayerControllerBPatchPatch.SetHoverTipAndCurrentInteractTriggerPatch_Transpiler)));
            }

            if (IsModBunkbedReviveLoaded)
                _harmony.PatchAll(typeof(BunkbedControllerPatch));

            if (isModZaprillatorLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Zaprillator.Behaviors.RevivablePlayer"), "IShockableWithGun.StopShockingWithGun"),
                    new HarmonyMethod(typeof(RevivablePlayerPatch), nameof(RevivablePlayerPatch.StopShockingWithGun_Prefix)));
            }

            if (isModLethalProgressionLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LethalProgression.Skills.HPRegen"), "HPRegenUpdate"),
                    new HarmonyMethod(typeof(HPRegenPatch), nameof(HPRegenPatch.HPRegenUpdate_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LethalProgression.Skills.Oxygen"), "EnteredWater"),
                    new HarmonyMethod(typeof(OxygenPatch), nameof(OxygenPatch.EnteredWater_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LethalProgression.Skills.Oxygen"), "LeftWater"),
                    new HarmonyMethod(typeof(OxygenPatch), nameof(OxygenPatch.LeftWater_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LethalProgression.Skills.Oxygen"), "ShouldDrown"),
                    new HarmonyMethod(typeof(OxygenPatch), nameof(OxygenPatch.ShouldDrown_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LethalProgression.Skills.Oxygen"), "OxygenUpdate"),
                    new HarmonyMethod(typeof(OxygenPatch), nameof(OxygenPatch.OxygenUpdate_Prefix)));
            }

            if (isModButteryFixesLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ButteryFixes.Patches.Player.BodyPatches"), "DeadBodyInfoPostStart"),
                    new HarmonyMethod(typeof(BodyPatchesPatch), nameof(BodyPatchesPatch.DeadBodyInfoPostStart_Prefix)));
            }

            if (isModPeepersLoaded)
                _harmony.PatchAll(typeof(PeeperAttachHitboxPatch));
        }

        #region Voice Commands - New Fluid Intent-Based System
        private void RegisterVoiceCommands()
        {
            // Optional hint phrases for PySpeech to improve accuracy (not required for recognition)
            string[] hintPhrases = new[]
            {
                "jester", "start the ship", "man the ship", "stay on ship", "go to ship",
                "request monitoring", "request teleport", "clear monitoring", "transmit"
            };
            Speech.RegisterPhrases(hintPhrases);

            FieldInfo bestMatchField = AccessTools.Field(typeof(Speech), "bestMatch");

            void handler(object speechInstance, SpeechEventArgs text)
            {
                if (!Config.AllowVoiceRecognition.Value)
                    return;

                string recognizedText = text.Text ?? "";
                if (!Speech.IsAboveThreshold(hintPhrases, Config.VoiceRecognitionSimilarityThreshold.Value))
                    return;

                if (string.IsNullOrWhiteSpace(recognizedText))
                    return;

                PlayerControllerB? playerControllerB = GameNetworkManager.Instance?.localPlayerController;
                if (playerControllerB == null)
                    return;

                BotIntent intent = DetectIntent(recognizedText);

                if (intent == BotIntent.Unknown)
                    return;

                if (Config.EnableDebugLog.Value)
                {
                    Logger.LogDebug($"[Voice] Recognized: \"{recognizedText}\" → Intent: {intent}");
                }

                // Transmit the raw text (or you could transmit the intent enum if you prefer)
                // Your LethalBotManager will receive this and can re-run DetectIntent on the server/host
                LethalBotManager.Instance?.TransmitVoiceChatAndSync(recognizedText, (int)playerControllerB.playerClientId);
            }

            Speech.RegisterCustomHandler(handler);
        }
        #endregion

        private bool IsModLoaded(string modGUID)
        {
            bool ret = Chainloader.PluginInfos.ContainsKey(modGUID);
            if (ret) LogInfo($"Mod compatibility loaded for mod: GUID {modGUID}");
            return ret;
        }

        private bool IsPreLoaderLoaded(string dllFileName, List<string> fileNames)
        {
            bool ret = fileNames.Contains(dllFileName);
            if (ret) LogInfo($"Mod compatibility loaded for preloader: {dllFileName}");
            return ret;
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    try
                    {
                        var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                        if (attributes.Length > 0)
                            method.Invoke(null, null);
                    }
                    catch { }
                }
            }
        }

        private static void InitPluginManager()
        {
            GameObject gameObject = new GameObject("PluginManager");
            gameObject.AddComponent<PluginManager>();
            PluginManager.Instance.InitManagers();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogDebug(string debugLog)
        {
            if (Plugin.Config.EnableDebugLog.Value)
                Logger.LogDebug(debugLog);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogInfo(string infoLog) => Logger.LogInfo(infoLog);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogWarning(string warningLog) => Logger.LogWarning(warningLog);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogError(string errorLog) => Logger.LogError(errorLog);
    }
}