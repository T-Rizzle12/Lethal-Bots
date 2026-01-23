using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
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
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;
using GameNetcodeStuff;
using PySpeech;
using LethalBots.Patches.ModPatches.LCVR;

namespace LethalBots
{
    /// <summary>
    /// Main mod plugin class, start everything
    /// </summary>
    [BepInPlugin(ModGUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    // HardDependencies
    [BepInDependency(LethalLib.Plugin.ModGUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(Const.CSYNC_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(LethalCompanyInputUtils.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(Const.PYSPEECH_GUID, BepInDependency.DependencyFlags.HardDependency)]
    // SoftDependencies
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
    [BepInDependency(LCVR.Plugin.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        // Please don't use the MyPluginInfo class for the GUID, my mod is
        // registered as this GUID, and it is used in the Harmony patching
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
        internal static bool IsModZaprillatorLoaded = false;
        internal static bool IsModLethalMinLoaded = false;
        internal static bool IsModLethalInternsLoaded = false;
        internal static bool IsModFacilityMeltdownLoaded = false;
        internal static bool IsModNavmeshInCompanyLoaded = false;
        internal static bool IsModReservedItemSlotCoreLoaded = false;
        private readonly Harmony _harmony = new(ModGUID);

        private void Awake()
        {
            var bundleName = "lethalbotnpcmodassets";
            DirectoryName = Path.GetDirectoryName(Info.Location);

            Logger = base.Logger;

            Config = new Configs.Config(base.Config);
            InputActionsInstance = new LethalBotsInputs();

            // This should be ran before Network Prefabs are registered.
            InitializeNetworkBehaviours();

            // Load mod assets from unity
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(DirectoryName, bundleName));
            if (ModAssets == null)
            {
                Logger.LogFatal($"Unknown to load custom assets.");
                return;
            }

            // Load bot prefab
            LethalBotNPCPrefab = Plugin.ModAssets.LoadAsset<EnemyType>("LethalBotNPC");
            if (LethalBotNPCPrefab == null)
            {
                Logger.LogInfo("Failed to directly load LethalBotNPC, manually searching asset bundle instead!");

                Object[] test = Plugin.ModAssets.LoadAllAssets();
                for (int i = 0; i < test.Length; i++)
                {
                    Logger.LogInfo($"Found {test[i]} in asset bundle");
                    if (test[i] is EnemyType enemy)
                    {
                        LethalBotNPCPrefab = enemy;
                        break;
                    }
                }
            }
            if (LethalBotNPCPrefab == null)
            {
                Logger.LogFatal($"LethalBotNPC prefab failed to load.");
                return;
            }
            foreach (var transform in LethalBotNPCPrefab.enemyPrefab.GetComponentsInChildren<Transform>()
                                                               .Where(x => x.parent != null && x.parent.name == "LethalBotNPCObj"
                                                                                            //&& x.name != "ScanNode"
                                                                                            //&& x.name != "MapDot"
                                                                                            && x.name != "Collision"
                                                                                            && x.name != "TurnCompass"
                                                                                            && x.name != "CreatureSFX"
                                                                                            && x.name != "CreatureVoice"
                                                                                            )
                                                               .ToList())
            {
                Object.DestroyImmediate(transform.gameObject);
            }

            // randomize GlobalObjectIdHash
            byte[] value = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(Assembly.GetCallingAssembly().GetName().Name + gameObject.name + bundleName));
            uint newGlobalObjectIdHash = BitConverter.ToUInt32(value, 0);
            Type type = typeof(NetworkObject);
            FieldInfo fieldInfo = type.GetField("GlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance);
            var networkObject = LethalBotNPCPrefab.enemyPrefab.GetComponent<NetworkObject>();
            fieldInfo.SetValue(networkObject, newGlobalObjectIdHash);

            // Register the network prefab with the randomized GlobalObjectIdHash
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(LethalBotNPCPrefab.enemyPrefab);

            InitPluginManager();

            PatchBaseGame();

            PatchOtherMods();

            RegisterVoiceCommands();

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void PatchBaseGame()
        {
            // Game engine
            _harmony.PatchAll(typeof(AudioMixerPatch));
            _harmony.PatchAll(typeof(AudioReverbTriggerPatch));
            _harmony.PatchAll(typeof(DebugPatch));
            _harmony.PatchAll(typeof(GameNetworkManagerPatch));
            _harmony.PatchAll(typeof(HUDManagerPatch));
            _harmony.PatchAll(typeof(NetworkSceneManagerPatch));
            _harmony.PatchAll(typeof(NfgoPlayerPatch));
            _harmony.PatchAll(typeof(NetworkObjectPatch));
            _harmony.PatchAll(typeof(RoundManagerPatch));
            _harmony.PatchAll(typeof(SoundManagerPatch));
            _harmony.PatchAll(typeof(StartOfRoundPatch));

            // Npc
            _harmony.PatchAll(typeof(EnemyAIPatch));
            _harmony.PatchAll(typeof(PlayerControllerBPatch));
            _harmony.PatchAll(typeof(EnemyAICollisionDetect));

            // Enemies
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

            // Map hazards
            _harmony.PatchAll(typeof(LandminePatch));
            _harmony.PatchAll(typeof(QuicksandTriggerPatch));
            _harmony.PatchAll(typeof(SpikeRoofTrapPatch));
            _harmony.PatchAll(typeof(TurretPatch));

            // Map
            _harmony.PatchAll(typeof(AnimatedObjectFloatSetterPatch));
            _harmony.PatchAll(typeof(CompanyMonsterCollisionDetectPatch));
            _harmony.PatchAll(typeof(DoorLockPatch));
            _harmony.PatchAll(typeof(InteractTriggerPatch));
            _harmony.PatchAll(typeof(OutOfBoundsTriggerPatch));
            _harmony.PatchAll(typeof(ShipTeleporterPatch));
            _harmony.PatchAll(typeof(VehicleControllerPatch));
            _harmony.PatchAll(typeof(DepositItemsDeskPatch));

            // Objects
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
            // -----------------------
            // Are these mods loaded ?
            IsModTooManyEmotesLoaded = IsModLoaded(Const.TOOMANYEMOTES_GUID);
            IsModModelReplacementAPILoaded = IsModLoaded(Const.MODELREPLACEMENT_GUID);
            IsModCustomItemBehaviourLibraryLoaded = IsModLoaded(Const.CUSTOMITEMBEHAVIOURLIBRARY_GUID);
            IsModMoreCompanyLoaded = IsModLoaded(Const.MORECOMPANY_GUID);
            IsModReviveCompanyLoaded = IsModLoaded(Const.REVIVECOMPANY_GUID);
            IsModBunkbedReviveLoaded = IsModLoaded(Const.BUNKBEDREVIVE_GUID);
            IsModZaprillatorLoaded = IsModLoaded(Const.ZAPRILLATOR_GUID);
            IsModLethalMinLoaded = IsModLoaded(Const.LETHALMIN_GUID);
            IsModLethalInternsLoaded = IsModLoaded(Const.LETHALINTERNS_GUID);
            IsModFacilityMeltdownLoaded = IsModLoaded(Const.FACILITYMELTDOWN_GUID);
            IsModNavmeshInCompanyLoaded = IsModLoaded(Const.NAVMESHINCOMPANY_GUID);
            IsModReservedItemSlotCoreLoaded = IsModLoaded(Const.RESERVEDITEMSLOTCORE_GUID);

            bool isModMoreEmotesLoaded = IsModLoaded(Const.MOREEMOTES_GUID);
            bool isModBetterEmotesLoaded = IsModLoaded(Const.BETTEREMOTES_GUID);
            bool isModLethalPhonesLoaded = IsModLoaded(Const.LETHALPHONES_GUID);
            bool isModShowCapacityLoaded = IsModLoaded(Const.SHOWCAPACITY_GUID);
            bool isModLethalProgressionLoaded = IsModLoaded(Const.LETHALPROGRESSION_GUID);
            bool isModLCAlwaysHearWalkieModLoaded = IsModLoaded(Const.LCALWAYSHEARWALKIEMOD_GUID);
            bool isModButteryFixesLoaded = IsModLoaded(Const.BUTTERYFIXES_GUID);
            bool isModPeepersLoaded = IsModLoaded(Const.PEEPERS_GUID);
            bool isModLethalCompanyVRLoaded = IsModLoaded(LCVR.Plugin.PLUGIN_GUID);

            // -------------------
            // Read the preloaders
            List<string> preloadersFileNames = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(Path.GetFullPath(Paths.PatcherPluginPath), "*.dll", SearchOption.AllDirectories))
            {
                preloadersFileNames.Add(Path.GetFileName(file));
            }
            // Are these preloaders loaded ?
            bool isModAdditionalNetworkingLoaded = IsPreLoaderLoaded(Const.ADDITIONALNETWORKING_DLLFILENAME, preloadersFileNames);

            // -----------------------------
            // Compatibility with other mods
            if (isModMoreEmotesLoaded)
            {
                _harmony.PatchAll(typeof(MoreEmotesPatch));
            }
            if (isModBetterEmotesLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "StartPostfix"),
                               new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.StartPostfix_Prefix)));
                //_harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "UpdatePrefix"),
                               //new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.UpdatePrefix_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "UpdatePostfix"),
                               null,
                               null,
                               new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.UpdatePostfix_Transpiler)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("BetterEmote.Patches.EmotePatch"), "PerformEmotePrefix"),
                               null,
                               null,
                               new HarmonyMethod(typeof(BetterEmotesPatch), nameof(BetterEmotesPatch.PerformEmotePrefix_Transpiler)));
                _harmony.PatchAll(typeof(BetterEmotesCustomAnimationObjectsPatch));
            }
            if (IsModTooManyEmotesLoaded)
            {
                _harmony.PatchAll(typeof(EmoteControllerPlayerPatch));
                _harmony.PatchAll(typeof(ThirdPersonEmoteControllerPatch));
            }
            if (IsModModelReplacementAPILoaded)
            {
                //_harmony.PatchAll(typeof(BodyReplacementBasePatch));
                _harmony.PatchAll(typeof(ModelReplacementPlayerControllerBPatchPatch));
                _harmony.PatchAll(typeof(ModelReplacementAPIPatch));
                //_harmony.PatchAll(typeof(ManagerBasePatch)); // This gives me an error, commenting out for now
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
                               null,
                               null,
                               new HarmonyMethod(typeof(AdditionalNetworkingPatch), nameof(AdditionalNetworkingPatch.Start_Transpiler)));
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
                               null,
                               null,
                               new HarmonyMethod(typeof(ReviveCompanyPlayerControllerBPatchPatch), nameof(ReviveCompanyPlayerControllerBPatchPatch.SetHoverTipAndCurrentInteractTriggerPatch_Transpiler)));
            }
            if (IsModBunkbedReviveLoaded)
            {
                _harmony.PatchAll(typeof(BunkbedControllerPatch));
            }
            if (IsModZaprillatorLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Zaprillator.Behaviors.RevivablePlayer"), "IShockableWithGun.StopShockingWithGun"),
                               new HarmonyMethod(typeof(RevivablePlayerPatch), nameof(RevivablePlayerPatch.StopShockingWithGun_Prefix)));
            }
            /*if (isModReservedItemSlotCoreLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ReservedItemSlotCore.Patches.PlayerPatcher"), "InitializePlayerControllerLate"),
                               new HarmonyMethod(typeof(PlayerPatcherPatch), nameof(PlayerPatcherPatch.InitializePlayerControllerLate_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ReservedItemSlotCore.Patches.PlayerPatcher"), "CheckForChangedInventorySize"),
                               new HarmonyMethod(typeof(PlayerPatcherPatch), nameof(PlayerPatcherPatch.CheckForChangedInventorySize_Prefix)));
            }*/
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
            /*if (isModLCAlwaysHearWalkieModLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LCAlwaysHearWalkieMod.Patches.PlayerControllerBPatch"), "alwaysHearWalkieTalkiesPatch"),
                               null,
                               null,
                               new HarmonyMethod(typeof(LCAlwaysHearActiveWalkiePatch), nameof(LCAlwaysHearActiveWalkiePatch.alwaysHearWalkieTalkiesPatch_Transpiler)));
            }*/
            if (isModButteryFixesLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ButteryFixes.Patches.Player.BodyPatches"), "DeadBodyInfoPostStart"),
                               new HarmonyMethod(typeof(BodyPatchesPatch), nameof(BodyPatchesPatch.DeadBodyInfoPostStart_Prefix)));
            }
            if (isModPeepersLoaded)
            {
                _harmony.PatchAll(typeof(PeeperAttachHitboxPatch));
            }
            if (isModLethalCompanyVRLoaded)
            {
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LCVR.Patches.Spectating.SpectatorPlayerPatches"), "BeforePlayerDeath"),
                               new HarmonyMethod(typeof(LCVRPatchesPatch), nameof(LCVRPatchesPatch.BeforePlayerDeath_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LCVR.Patches.Spectating.SpectatorPlayerPatches"), "OnPlayerDeath"),
                               new HarmonyMethod(typeof(LCVRPatchesPatch), nameof(LCVRPatchesPatch.OnPlayerDeath_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LCVR.Patches.Items.ShotgunItemPatches"), "DisplaySafetyPatch"),
                               new HarmonyMethod(typeof(LCVRPatchesPatch), nameof(LCVRPatchesPatch.DisplaySafetyPatch_Prefix)));
                _harmony.Patch(AccessTools.Method(AccessTools.TypeByName("LCVR.Patches.PlayerControllerPatches"), "AfterDamagePlayer"),
                               new HarmonyMethod(typeof(LCVRPatchesPatch), nameof(LCVRPatchesPatch.AfterDamagePlayer_Prefix)));
            }
        }

        #region Voice Commands

        private void RegisterVoiceCommands()
        {
            // TODO: Revamp the chat command system to be more modular and easier to add new commands
            // Until then, we have to register all of the commands here manually
            string[] ValidCommands = new string[]
            {
                "jester",
                "start the ship",
                "hop off the terminal",
                "request monitoring",
                "request teleport",
                "clear monitoring",
                "man the ship",
                "transmit", // FIXME: This command doesn't work due to how speech recognition works, a fix will be made later
                "transfer loot",
                "gear up"
            };

            // Register valid phrases for speech recognition
            Speech.RegisterPhrases(ValidCommands);

            // Create a handler for recognized speech events
            FieldInfo bestMatchField = AccessTools.Field(typeof(Speech), "bestMatch");
            void handler(object speechInstance, SpeechEventArgs text)
            {
                // Don't do this if the local client has disabled voice recognition
                if (!Config.AllowVoiceRecognition.Value)
                {
                    return;
                }

                // The local player gets to determine which model to use for their voice recognition.
                // Our job is to broadcast that to all other players so their bots can respond accordingly.
                PlayerControllerB? playerControllerB = GameNetworkManager.Instance?.localPlayerController;
                if (playerControllerB != null && Speech.IsAboveThreshold(ValidCommands, Config.VoiceRecognitionSimilarityThreshold.Value))
                {
                    LethalBotManager.Instance?.TransmitVoiceChatAndSync((string)bestMatchField.GetValue(null), (int)playerControllerB.playerClientId);
                }
            }

            Speech.RegisterCustomHandler(handler);
        }

        #endregion

        private bool IsModLoaded(string modGUID)
        {
            bool ret = Chainloader.PluginInfos.ContainsKey(modGUID);
            if (ret)
            {
                LogInfo($"Mod compatibility loaded for mod : GUID {modGUID}");
            }

            //foreach (var a in Chainloader.PluginInfos)
            //{
            //    LogDebug($"{a.Key}");
            //}

            return ret;
        }

        private bool IsPreLoaderLoaded(string dllFileName, List<string> fileNames)
        {
            bool ret = fileNames.Contains(dllFileName);
            if (ret)
            {
                LogInfo($"Mod compatibility loaded for preloader : {dllFileName}");
            }

            return ret;
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
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
                        {
                            method.Invoke(null, null);
                        }
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
            {
                Logger.LogDebug(debugLog);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogInfo(string infoLog)
        {
            Logger.LogInfo(infoLog);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogWarning(string warningLog)
        {
            Logger.LogWarning(warningLog);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogError(string errorLog)
        {
            Logger.LogError(errorLog);
        }
    }
}