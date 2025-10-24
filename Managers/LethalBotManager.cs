using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.AI.AIStates;
using LethalBots.Configs;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.NetworkSerializers;
using LethalBots.Patches.GameEnginePatches;
using LethalBots.Patches.MapPatches;
using LethalBots.Patches.ModPatches.ModelRplcmntAPI;
using LethalBots.Patches.NpcPatches;
using Steamworks.ServerList;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Random = System.Random;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager responsible for spawning, initializing, managing bots and synchronize clients.
    /// </summary>
    /// <remarks>
    /// For spawning bots, the managers uses open player slots in the <c>allPlayerScripts</c>, <c>allPlayerObjects</c> 
    /// from <see cref="ConfigConst.MAX_BOTS_AVAILABLE"><c>ConfigConst.MAX_BOTS_AVAILABLE</c></see>.<br/>
    /// An bot is a <c>PlayerControllerB</c> with an <c>LethalBotAI</c>, both attached to the <c>GameObject</c> of the playerController.<br/>
    /// So the manager instantiate new playerControllers (body) and spawn on server new AI (brain) and link them together.<br/>
    /// Other methods in class can retrieve the brain from the body index and vice versa with the use of arrays.<br/>
    /// <br/>
    /// Important points:<br/>
    /// The <c>PlayerControllerB</c> instantiated for bots do not spawn on server, they are synchronized in each client.<br/>
    /// This means that the <c>PlayerControllerB</c> of an bot is never owned, only the <c>LethalBotAI</c> associated is.<br/>
    /// The patches for the original game code need to always look for an <c>LethalBotAI</c> associated with <c>PlayerControllerB</c> they encounter.<br/>
    /// Typically, everything that happens to the player owner of his body (real player), should function the same to the body of an bot owned by this player,
    /// the local player.<br/>
    /// <br/>
    /// Note: To be compatible with MoreCompany, the manager need to keep reference of the number of "real" players initialize by the game and the mod (MoreCompany)
    /// Typically 4 (base game) + 28 (default from MoreCompany)<br/>
    /// MoreCompany resize arrays in the same way, after each scene load, so quite a number of time, the manager execute after MoreCompany and resize the arrays to the
    /// right size : 4 + 28 (default for LethalBot)<br/>
    /// </remarks>
    public class LethalBotManager : NetworkBehaviour
    {
        public static LethalBotManager Instance { get; private set; } = null!;

        /// <summary>
        /// Size of allPlayerScripts, AllPlayerObjects, for all player controllers
        /// </summary>
        public int AllEntitiesCount;
        /// <summary>
        /// Number of actually connected players, used for the DepositItemDeskPatch!
        /// </summary>
        public int AllRealPlayersCount { private set; get; }
        /// <summary>
        /// Integer corresponding to the first player controller associated with an <see cref="LethalInternship.AI.InternAI"/> in StartOfRound.Instance.allPlayerScripts
        /// </summary>
        public int IndexBeginOfInterns
        {
            get
            {
                if (!Plugin.IsModLethalInternsLoaded)
                {
                    return int.MaxValue;
                }
                return LethalInternship.Managers.InternManager.Instance?.IndexBeginOfInterns ?? int.MaxValue;
            }
        }
        /// <summary>
        /// BotSpawnState used to check when a bot spawns, so living and connected players can update!
        /// </summary>
        public NetworkVariable<EnumBotSpawnState> botSpawned = new NetworkVariable<EnumBotSpawnState>(EnumBotSpawnState.Unknown);
        /// <summary>
        /// Bool used to check when a bot has died or been revived, so living and connected players can update!
        /// </summary>
        public NetworkVariable<bool> sendPlayerCountUpdate = new NetworkVariable<bool>(false);
        /// <summary>
        /// Bool used when the inital bot spawn happens so we don't spam the clients with updates!
        /// </summary>
        public NetworkVariable<bool> isSpawningBots = new NetworkVariable<bool>(false);
        private static PlayerControllerB? _hostPlayerScript;
        /// <summary>
        /// Public property used to return the host player object!
        /// </summary>
        public static PlayerControllerB? HostPlayerScript 
        { 
            get
            {
                if (_hostPlayerScript == null || !_hostPlayerScript.isHostPlayerObject)
                {
                    foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (player != null && player.isHostPlayerObject)
                        {
                            _hostPlayerScript = player;
                            break;
                        }
                    }
                }
                return _hostPlayerScript;
            }
        }
        /// <summary>
        /// This is the human player or bot who is set to stay and monitor the others on the ship!
        /// </summary>
        /// <remarks>
        /// WARNING: If the value is set on a client (not the host), please wait a short time before
        /// accessing the stored value, as it must be sent to the server and synced back.
        /// This does NOT apply when running as the host, since the host acts as the server.
        /// </remarks>
        public PlayerControllerB? MissionControlPlayer
        {
            set
            {
                if (value != null)
                {
                    SetMissionControllerAndSync(value);
                }
                else
                {
                    SetMissionControllerAndSync(default);
                }
            }
            get
            {
                if (_missionControlPlayer.Value.TryGet(out PlayerControllerB player))
                {
                    return player;
                }
                else
                {
                    return null;
                }
            }
        }
        private NetworkVariable<NetworkBehaviourReference> _missionControlPlayer = new NetworkVariable<NetworkBehaviourReference>(writePerm: NetworkVariableWritePermission.Server);
        /// <summary>
        /// This is the last reported time of day from the last <see cref="MissionControlPlayer"/>.
        /// </summary>
        public static DayMode lastReportedTimeOfDay = DayMode.Dawn;
        public static DepositItemsDesk? _companyDesk;
        /// <summary>
        /// Returns the <see cref="DepositItemsDesk"/> instance in the scene, if it exists.<br/>
        /// </summary>
        public static DepositItemsDesk? CompanyDesk 
        {
            get
            {
                if (_companyDesk == null)
                {
                    _companyDesk = UnityEngine.Object.FindObjectOfType<DepositItemsDesk>();
                }
                return _companyDesk;
            }
        }
        public VehicleController? VehicleController;
        public List<IBodyReplacementBase> ListBodyReplacementOnDeadBodies { private set; get; } = new List<IBodyReplacementBase>();

        public Dictionary<EnemyAI, INoiseListener> DictEnemyAINoiseListeners = new Dictionary<EnemyAI, INoiseListener>();

        private LethalBotAI[] AllLethalBotAIs = null!; // new LethalBotAI[50] So far the largest size a modded lobby I have seen is 50, this will resize as needed to not waste memory!
        private List<int> AllBotPlayerIndexs = new List<int>();
        private List<EndOfGameBotStats> AllBotEndOfGameStats = new List<EndOfGameBotStats>();

        private Coroutine registerItemsCoroutine = null!;
        private Coroutine? spawnLethalBotsAtShipCoroutine = null;
        private Coroutine BeamOutLethalBotsCoroutine = null!;
        private Coroutine? trappedPlayerCheckCoroutine = null;
        /// <summary>
        /// Returns if the inverse teleporter is active or not.<br/>
        /// Used by the <see cref="LethalBotAI"/> to check if they should use it to teleport in or not.
        /// </summary>
        public static bool IsInverseTeleporterActive 
        { 
            private set;
            get; 
        }
        /// <summary>
        /// Returns if there is a trapped player in the facility.</br>
        /// Use by the <see cref="LethalBotAI"/> to check if they should bring a key 
        /// and look for locked doors to free them.
        /// </summary>
        public static bool IsThereATrappedPlayer 
        { 
            private set; 
            get; 
        }
        private ClientRpcParams ClientRpcParams = new ClientRpcParams();

        private static float nextCheckForShipSafety;
        private static bool _isShipCompromised;
        private float nextCheckForAliveHumanPlayers;
        private bool _areAllHumanPlayersDead;
        private float nextCheckForAllPlayersOnShip;
        private bool _areAllPlayersOnTheShip;
        private float timerAnimationCulling;
        private float timerNoAnimationAfterLag;
        private LethalBotAI[] lethalBotsInFOV = null!; // new LethalBotAI[50]
        private float timerUpdatePlayerCount;

        private float timerRegisterAINoiseListener;
        private List<EnemyAI> ListEnemyAINonNoiseListeners = new List<EnemyAI>();
        private static Dictionary<string, LethalBotThreat> DictionaryLethalBotThreats = new Dictionary<string, LethalBotThreat>();
        public static List<GameObject> grabbableObjectsInMap = new List<GameObject>();
        public Dictionary<string, int> DictTagSurfaceIndex = new Dictionary<string, int>();

        private float timerSetLethalBotInElevator;
        private float timerUpdateOwnershipOfBotInventory;

        /// <summary>
        /// Initialize instance,
        /// repopulate pool of bots if LethalBotManager reset when loading game
        /// </summary>
        private void Awake()
        {
            // Prevent multiple instances of the main bot manager
            if (Instance != null && Instance != this)
            {
                if (Instance.IsSpawned && Instance.IsServer)
                {
                    Instance.NetworkObject.Despawn(destroy: true);
                }
                else
                {
                    Destroy(Instance.gameObject);
                }
            }

            Instance = this;
            Plugin.Config.InitialSyncCompleted += Config_InitialSyncCompleted;
            Plugin.LogDebug($"Client {NetworkManager.LocalClientId}, MaxBotsAllowedToSpawn before CSync {Plugin.Config.MaxBotsAllowedToSpawn.Value}");
            Plugin.LogDebug($"Saved instance: {Instance}, This object: {this}");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!base.NetworkManager.IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    // Destory Local manager
                    Destroy(Instance.gameObject);
                }
                Instance = this;
            }
        }

        private void Config_InitialSyncCompleted(object sender, EventArgs e)
        {
            if (IsHost)
            {
                return;
            }

            Plugin.LogDebug($"Client {NetworkManager.LocalClientId}, ManagePoolOfBots after CSync, MaxBotsAllowedToSpawn {Plugin.Config.MaxBotsAllowedToSpawn.Value}");
            ManagePoolOfBots();
        }

        private void FixedUpdate()
        {
            RegisterAINoiseListener(Time.fixedDeltaTime);
        }

        private void RegisterAINoiseListener(float deltaTime)
        {
            timerRegisterAINoiseListener += deltaTime;
            if (timerRegisterAINoiseListener < 1f)
            {
                return;
            }

            timerRegisterAINoiseListener = 0f;
            RoundManager instanceRM = RoundManager.Instance;
            foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
            {
                if (ListEnemyAINonNoiseListeners.Contains(spawnedEnemy))
                {
                    continue;
                }
                else if (DictEnemyAINoiseListeners.ContainsKey(spawnedEnemy))
                {
                    continue;
                }

                INoiseListener noiseListener;
                if (spawnedEnemy.gameObject.TryGetComponent<INoiseListener>(out noiseListener))
                {
                    Plugin.LogDebug($"new enemy noise listener, spawnedEnemy {spawnedEnemy}");
                    DictEnemyAINoiseListeners.Add(spawnedEnemy, noiseListener);
                }
                else
                {
                    Plugin.LogDebug($"new enemy not noise listener, spawnedEnemy {spawnedEnemy}");
                    ListEnemyAINonNoiseListeners.Add(spawnedEnemy);
                }
            }
        }

        /// <summary>
        /// Coppied from <see cref="GrabbableObject.Start"><c>GrabbableObject.Start</c></see>
        /// </summary>
        /// <param name="newItem"></param>
        public void GrabbableObjectSpawned(GrabbableObject newItem)
        {
            // Don't add it again!
            if (grabbableObjectsInMap.Contains(newItem.gameObject))
            {
                return;
            }

            // Lets make sure the bots don't attempt to grab dead bodies as soon as a player is killed!
            if (newItem is RagdollGrabbableObject)
            {
                //grabbableObjectsInMap.Add(newItem.gameObject);
                LethalBotAI.DictJustDroppedItems[newItem] = Time.realtimeSinceStartup;
            }
            // HACKHACK: Make shotguns spawned by nutcrackers be added to the list!
            /*else if (!newItem.itemProperties.isScrap || newItem is ShotgunItem)
            {
                grabbableObjectsInMap.Add(newItem.gameObject);
            }*/

            // Above code was from HordingBugAI, but it's not needed here!
            // After all we are player bots!
            grabbableObjectsInMap.Add(newItem.gameObject);
        }

        public void RegisterItems()
        {
            if (registerItemsCoroutine == null)
            {
                registerItemsCoroutine = StartCoroutine(RegisterItemsCoroutine());
            }
        }

        private IEnumerator RegisterItemsCoroutine()
        {
            grabbableObjectsInMap.Clear();
            yield return null;

            GrabbableObject[] array = Object.FindObjectsOfType<GrabbableObject>();
            Plugin.LogDebug($"Bot register grabbable object, found : {array.Length}");
            foreach (var grabbableObject in array)
            {
                grabbableObjectsInMap.Add(grabbableObject.gameObject);
                yield return null;
            }

            registerItemsCoroutine = null!;
            yield break;
        }

        private void Start()
        {
            // Identities
            IdentityManager.Instance.InitIdentities(Plugin.Config.ConfigIdentities.configIdentities);

            // Bot objects
            if (Plugin.PluginIrlPlayersCount > 0)
            {
                // only resize if irl players not 0, which means we already tried to populate pool of bots
                // But the manager somehow reset
                ManagePoolOfBots();
            }

            // Load data from save
            SaveManager.Instance.LoadAllDataFromSave();

            // Init footstep surfaces tags
            DictTagSurfaceIndex.Clear();
            for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
            {
                DictTagSurfaceIndex.Add(StartOfRound.Instance.footstepSurfaces[i].surfaceTag, i);
            }

            // Register default threats
            DictionaryLethalBotThreats.Clear();

            // Static value threats
            RegisterThreat("Crawler", 20f, 10f, 20f);
            RegisterThreat("Bunker Spider", 20f, 10f, 20f);
            RegisterThreat("ForestGiant", 30f, 10f, 40f);
            RegisterThreat("Earth Leviathan", 10f, null, 15f);
            RegisterThreat("Nutcracker", 15f, 10f, 15f);
            RegisterThreat("ImmortalSnail", 10f, null, 10f);
            RegisterThreat("Clay Surgeon", 15f, null, 10f);
            RegisterThreat("Flowerman", 10f, null, 5f);
            RegisterThreat("Bush Wolf", 15f, 10f, 15f);
            RegisterThreat("Puffer", 2f, null, 2f);
            RegisterThreat("Red Locust Bees", 10f, null, 15f);
            RegisterThreat("Butler Bees", 20f, null, 15f);
            RegisterThreat("Blob", 10f, null, 10f);
            RegisterThreat("Baboon hawk", 10f, 5f, 10f);

            // Dynamic behavior threats
            RegisterThreat("RadMech",
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 20f : null,
                fq => 40f // Always 40 for pathfinding
            );

            RegisterThreat("T-rex",
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null,
                fq => 40f // Always 40 for pathfinding
            );

            RegisterThreat("Maneater",
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 2 ? 30f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null
            );

            RegisterThreat("Jester",
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? float.MaxValue : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 && (fq.PlayerToCheck == null || fq.PlayerToCheck.isInsideFactory) ? float.MaxValue : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? null : 20f // Always 20 for pathfinding, unless they are winding up, then we need to move NOW!
            );

            RegisterThreat("MouthDog",
                fq => fq.EnemyAI is MouthDogAI dog && dog.suspicionLevel >= 9 ? 20f : 5f,
                fq => fq.EnemyAI is MouthDogAI dog && dog.suspicionLevel >= 9 ? 20f : null,
                fq => fq.EnemyAI is MouthDogAI dog && dog.suspicionLevel > 1 ? 30f : 20f // Increase the danger range if they are angry!
            );

            RegisterThreat("Centipede",
                fq => 
                {
                    if (fq.EnemyAI is CentipedeAI c && c.clingingToPlayer != null)
                    {
                        return c.clingingToPlayer == fq.Bot ? 1f : 15f;
                    }
                    return fq.EnemyAI.currentBehaviourStateIndex > 1 ? 15f : 1f;
                },
                fq => fq.EnemyAI is CentipedeAI c && c.clingingToPlayer == fq.PlayerToCheck ? float.MaxValue : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 1 ||
                      (fq.EnemyAI is CentipedeAI c && c.clingingToPlayer != null) ? 15f : 1f
            );

            RegisterThreat("Spring",
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 20f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 10f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 20f : null
            );

            RegisterThreat("Butler",
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 20f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 10f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 20f : null
            );

            RegisterThreat("Hoarding bug",
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 20f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 5f : null,
                fq => fq.EnemyAI.currentBehaviourStateIndex == 2 ? 20f : null
            );

            RegisterThreat("Masked",
                fq =>
                {
                    bool aware = fq.EnemyAI is MaskedPlayerEnemy masked && fq.Bot is LethalBotAI lethalBotAI && lethalBotAI.DictKnownMasked.TryGetValue(masked, out bool known) && known;
                    return (aware || fq.EnemyAI.creatureAnimator.GetBool("HandsOut")) ? 30f : 15f;
                },
                _ => 8f, // Always 8 for mission control
                fq =>
                {
                    bool aware = fq.EnemyAI is MaskedPlayerEnemy masked && fq.Bot is LethalBotAI lethalBotAI && lethalBotAI.DictKnownMasked.TryGetValue(masked, out bool known) && known;
                    return (aware || fq.EnemyAI.creatureAnimator.GetBool("HandsOut")) ? 30f : 15f;
                }
            );

            // TODO: Improve this as I study the AI!
            RegisterThreat("GiantKiwi", 
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : null, 
                _ => null,
                fq => fq.EnemyAI.currentBehaviourStateIndex > 0 ? 30f : 15f
            );

            // Girl behavior (currently commented out for fixing bugs)
            // DictionaryLethalBotThreats["Girl"] = new LethalBotThreat("Girl",
            //     fq => fq.EnemyAI is DressGirlAI ghostGirl && ghostGirl.hauntingPlayer == NpcController.Npc && false && ghostGirl.currentBehaviourStateIndex > 0 ? 30f : null,
            //     _ => null,  // No value for mission control
            //     _ => null   // No value for pathfinding
            // );

            // Girl is always ignored (for now), so skip or:
            RegisterThreat("Girl", (float?)null, (float?)null, (float?)null);
        }

        private void Update()
        {
            UpdateAnimationsCulling();

            timerUpdatePlayerCount += Time.deltaTime;
            StartOfRound instanceSOR = StartOfRound.Instance;
            if (timerUpdatePlayerCount > 0.5f)
            {
                timerUpdatePlayerCount = 0f;
                if (!isSpawningBots.Value && sendPlayerCountUpdate.Value 
                    && (IsServer || IsHost))
                {
                    // Check and update the amount of dead and living players
                    int livingPlayerCount = 0;
                    foreach (PlayerControllerB playerControllerB in instanceSOR.allPlayerScripts)
                    {
                        if (playerControllerB.isPlayerControlled && !playerControllerB.isPlayerDead)
                        {
                            livingPlayerCount++;
                        }
                    }
                    SendNewPlayerCountServerRpc(instanceSOR.connectedPlayersAmount, livingPlayerCount, AllRealPlayersCount);
                }
            }

            // Start checking for trapped player once the ship has landed
            // and we are not in the lobby!
            if (instanceSOR != null && !instanceSOR.inShipPhase && instanceSOR.shipHasLanded)
            {
                StartTrappedPlayerCoroutine();
            }
            else
            {
                StopTrappedPlayerCoroutine();
            }
        }

        /// <summary>
        /// Check how many player slots are available and how many we can use!
        /// </summary>
        public void ManagePoolOfBots()
        {
            StartOfRound instance = StartOfRound.Instance;

            if (instance.allPlayerObjects[3].gameObject == null)
            {
                Plugin.LogInfo("No player objects initialized in game, aborting bots initializations.");
                return;
            }

            if (Plugin.PluginIrlPlayersCount == 0)
            {
                Plugin.PluginIrlPlayersCount = instance.allPlayerObjects.Length;
                Plugin.LogDebug($"PluginIrlPlayersCount = {Plugin.PluginIrlPlayersCount}");
            }

            int irlPlayersCount = Plugin.PluginIrlPlayersCount;

            // Initialize back ups
            if (AllLethalBotAIs == null || lethalBotsInFOV == null)
            {
                AllLethalBotAIs = new LethalBotAI[irlPlayersCount];
                lethalBotsInFOV = new LethalBotAI[irlPlayersCount];
            }
            else if (AllLethalBotAIs.Length != irlPlayersCount)
            {
                Array.Resize(ref AllLethalBotAIs, irlPlayersCount);
                Array.Resize(ref lethalBotsInFOV, irlPlayersCount);
            }

            // We save this so we can deterine how many player slots are available without needing to grab the entire table.
            AllEntitiesCount = irlPlayersCount;

            // Need to populate pool of bots?
            if (instance.allPlayerScripts.Length == AllEntitiesCount)
            {
                // the arrays have not been resize between round
                Plugin.LogInfo($"Pool of bots ok. The arrays have not been resized, PluginIrlPlayersCount: {Plugin.PluginIrlPlayersCount}, arrays length: {instance.allPlayerScripts.Length}");
                return;
            }

            Plugin.LogInfo($"Pool of bots not ok. The arrays have been resized, PluginIrlPlayersCount: {Plugin.PluginIrlPlayersCount}, arrays length: {instance.allPlayerScripts.Length}");
            // Bots
            //UpdateSoundManagerWithInterns(irlPlayersAndInternsCount);
        }

        public void ResetIdentities()
        {
            IdentityManager.Instance.InitIdentities(Plugin.Config.ConfigIdentities.configIdentities);
        }

        // TODO: Do we even need this since the bots use the player slots?
        /*private void UpdateSoundManagerWithInterns(int irlPlayersAndInternsCount)
        {
            SoundManager instanceSM = SoundManager.Instance;

            Array.Resize(ref instanceSM.playerVoicePitchLerpSpeed, irlPlayersAndInternsCount);
            Array.Resize(ref instanceSM.playerVoicePitchTargets, irlPlayersAndInternsCount);
            Array.Resize(ref instanceSM.playerVoicePitches, irlPlayersAndInternsCount);
            Array.Resize(ref instanceSM.playerVoiceVolumes, irlPlayersAndInternsCount);

            // From moreCompany
            for (int i = IndexBeginOfInterns; i < irlPlayersAndInternsCount; i++)
            {
                instanceSM.playerVoicePitchLerpSpeed[i] = 3f;
                instanceSM.playerVoicePitchTargets[i] = 1f;
                instanceSM.playerVoicePitches[i] = 1f;
                instanceSM.playerVoiceVolumes[i] = 0.5f;
            }

            ResizePlayerVoiceMixers(irlPlayersAndInternsCount);
        }

        // TODO: Do we even need this!?
        public void ResizePlayerVoiceMixers(int irlPlayersAndInternsCount)
        {
            // From moreCompany
            SoundManager instanceSM = SoundManager.Instance;
            Array.Resize<AudioMixerGroup>(ref instanceSM.playerVoiceMixers, irlPlayersAndInternsCount);
            AudioMixerGroup audioMixerGroup = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().FirstOrDefault((AudioMixerGroup x) => x.name.StartsWith("VoicePlayer"));
            for (int i = IndexBeginOfInterns; i < irlPlayersAndInternsCount; i++)
            {
                instanceSM.playerVoiceMixers[i] = audioMixerGroup;
            }
        }*/

        private void RemovePlayerModelReplacement(PlayerControllerB lethalBotController)
        {
            RemovePlayerModelReplacement(lethalBotController.GetComponent<ModelReplacement.BodyReplacementBase>());
        }

        private void RemovePlayerModelReplacement(object bodyReplacementBase)
        {
            Object.DestroyImmediate((ModelReplacement.BodyReplacementBase)bodyReplacementBase);
        }

        private void RemoveCosmetics(PlayerControllerB lethalBotController)
        {
            MoreCompany.Cosmetics.CosmeticApplication componentInChildren = lethalBotController.gameObject.GetComponentInChildren<MoreCompany.Cosmetics.CosmeticApplication>();
            if (componentInChildren != null)
            {
                Plugin.LogDebug("clear cosmetics");
                componentInChildren.RefreshAllCosmeticPositions();
                componentInChildren.ClearCosmetics();
            }
        }

        #region Bot Fear Manager

        /// <summary>
        /// Get the threat range for a given threat name
        /// </summary>
        /// <param name="fearQuery"></param>
        /// <returns>The fear range based on the given query</returns>
        public static float? GetFearRangeForEnemy(LethalBotFearQuery fearQuery)
        {
            // Make sure we have a valid enemyAI
            if (fearQuery.EnemyAI == null)
            {
                Plugin.LogWarning($"GetFearRangeForEnemy: EnemyAI is null");
                return null;
            }
            string threatName = fearQuery.EnemyAI.enemyType.enemyName;
            if (DictionaryLethalBotThreats.TryGetValue(threatName, out LethalBotThreat threatInfo))
            {
                return threatInfo.GetFearRangeForEnemy(fearQuery);
            }
            return null;
        }

        /// <summary>
        /// Helper function for registering threats that don't need any special logic
        /// </summary>
        /// <param name="name">Name of the threat this should the same as the one in <see cref="EnemyAI.enemyType"/></param>
        /// <param name="panik">Bot panik range</param>
        /// <param name="mission">Bot teleport player range</param>
        /// <param name="path">Bot Avoid LOS range</param>
        public static void RegisterThreat(string name, float? panik, float? mission, float? path)
        {
            DictionaryLethalBotThreats.Add(name, new LethalBotThreat(
                name,
                _ => panik,
                _ => mission,
                _ => path
            ));
        }

        /// <summary>
        /// Helper function for registering threats that need special logic
        /// </summary>
        /// <param name="name">Name of the threat this should the same as the one in <see cref="EnemyAI.enemyType"/></param>
        /// <param name="panik">Bot panik function</param>
        /// <param name="mission">Bot teleport player function</param>
        /// <param name="path">Bot Avoid LOS function</param>
        public static void RegisterThreat(string name, Func<LethalBotFearQuery, float?> panik, Func<LethalBotFearQuery, float?> mission, Func<LethalBotFearQuery, float?> path)
        {
            DictionaryLethalBotThreats.Add(name, new LethalBotThreat(
                name,
                panik,
                mission,
                path
            ));
        }

        /// <summary>
        /// Unregisters a threat by its name.
        /// </summary>
        /// <param name="name">The name of the threat to unregister.</param>
        public static void UnRegisterThreat(string name)
        {
            if (DictionaryLethalBotThreats.ContainsKey(name))
            {
                DictionaryLethalBotThreats.Remove(name);
            }
        }

        #endregion

        #region Spawn Bot

        /// <summary>
        /// Rpc method on server spawning network object from bot prefab and calling the client
        /// </summary>
        /// <param name="spawnPosition">Where the bots will spawn</param>
        /// <param name="yRot">Rotation of the bots when spawning</param>
        /// <param name="isOutside">Spawning outside or inside the facility (used for initializing AI Nodes)</param>
        [ServerRpc(RequireOwnership = false)]
        public void SpawnLethalBotServerRpc(SpawnLethalBotParamsNetworkSerializable spawnLethalBotsParamsNetworkSerializable)
        {
            if (AllLethalBotAIs == null || AllLethalBotAIs.Length == 0)
            {
                Plugin.LogError($"Fatal error : client #{NetworkManager.LocalClientId} no bots initialized ! Please check for previous errors in the console");
                botSpawned.Value = EnumBotSpawnState.NotSpawned;
                return;
            }

            int identityID = -1;
            // Get selected identities
            int[] selectedIdentities = IdentityManager.Instance.GetIdentitiesToDrop();
            if (selectedIdentities.Length > 0)
            {
                identityID = selectedIdentities[0];
            }
            else
            {
                LethalBotIdentity? selectedIdentity = IdentityManager.Instance.GetIdentiyFromIndex(IdentityManager.Instance.GetNewIdentityToSpawn());
                if (selectedIdentity != null)
                {
                    identityID = selectedIdentity.IdIdentity;
                }
            }


            if (identityID < 0)
            {
                Plugin.LogInfo($"Try to spawn bot, no more bot identities available.");
                botSpawned.Value = EnumBotSpawnState.NotSpawned;
                return;
            }

            IdentityManager.Instance.LethalBotIdentities[identityID].Status = EnumStatusIdentity.Spawned;
            spawnLethalBotsParamsNetworkSerializable.LethalBotIdentityID = identityID;
            SpawnLethalBotServer(spawnLethalBotsParamsNetworkSerializable);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnThisLethalBotServerRpc(int identityID, SpawnLethalBotParamsNetworkSerializable spawnLethalBotsParamsNetworkSerializable)
        {
            if (AllLethalBotAIs == null || AllLethalBotAIs.Length == 0)
            {
                Plugin.LogError($"Fatal error : client #{NetworkManager.LocalClientId} no bots initialized ! Please check for previous errors in the console.");
                return;
            }

            if (identityID < 0)
            {
                Plugin.LogInfo($"Unknown to spawn specific bot identity with id {identityID}.");
                return;
            }

            spawnLethalBotsParamsNetworkSerializable.LethalBotIdentityID = identityID;
            SpawnLethalBotServer(spawnLethalBotsParamsNetworkSerializable);
        }

        private void SpawnLethalBotServer(SpawnLethalBotParamsNetworkSerializable spawnLethalBotsParamsNetworkSerializable)
        {
            int indexNextPlayerObject = spawnLethalBotsParamsNetworkSerializable.IndexNextPlayerObject ?? GetNextAvailablePlayerObject();
            if (indexNextPlayerObject < 0)
            {
                Plugin.LogInfo($"No more bots can be spawned at the same time, see Max Server size value : {Plugin.PluginIrlPlayersCount}");
                if (IsServer || IsHost)
                    botSpawned.Value = EnumBotSpawnState.NotSpawned;
                return;
            }

            NetworkObject networkObject;
            LethalBotAI lethalBotAI = AllLethalBotAIs[indexNextPlayerObject];
            if (lethalBotAI != null)
            {
                // Use lethalBot if exists
                networkObject = AllLethalBotAIs[indexNextPlayerObject].NetworkObject;
            }
            else
            {
                // Or spawn one (server only)
                GameObject lethalBotPrefab = Object.Instantiate<GameObject>(Plugin.LethalBotNPCPrefab.enemyPrefab);
                lethalBotAI = lethalBotPrefab.GetComponent<LethalBotAI>();
                Plugin.LogDebug($"{lethalBotPrefab}");
                Plugin.LogDebug($"{lethalBotAI}");
                AllLethalBotAIs[indexNextPlayerObject] = lethalBotAI;

                networkObject = lethalBotPrefab.GetComponentInChildren<NetworkObject>();
                networkObject.Spawn(true);
            }

            // Get an identity for the bot
            lethalBotAI.LethalBotIdentity = IdentityManager.Instance.LethalBotIdentities[spawnLethalBotsParamsNetworkSerializable.LethalBotIdentityID];
            int suitID;
            if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
            {
                suitID = GameNetworkManager.Instance.localPlayerController.currentSuitID;
            }
            else
            {
                suitID = lethalBotAI.LethalBotIdentity.SuitID ?? lethalBotAI.LethalBotIdentity.GetRandomSuitID();
            }

            // Send to client to spawn bot
            spawnLethalBotsParamsNetworkSerializable.IndexNextLethalBot = indexNextPlayerObject;
            spawnLethalBotsParamsNetworkSerializable.IndexNextPlayerObject = indexNextPlayerObject;
            spawnLethalBotsParamsNetworkSerializable.LethalBotIdentityID = lethalBotAI.LethalBotIdentity.IdIdentity;
            spawnLethalBotsParamsNetworkSerializable.SuitID = suitID;
            SpawnLethalBotClientRpc(networkObject, spawnLethalBotsParamsNetworkSerializable);
        }

        /// <summary>
        /// Get the index of the next <c>PlayerControllerB</c> not controlled and ready to be hooked to an <c>LethalBotAI</c>
        /// </summary>
        /// <returns></returns>
        private int GetNextAvailablePlayerObject()
        {
            StartOfRound instance = StartOfRound.Instance;
            int playerArraySize = instance.allPlayerScripts.Length;
            int maxPlayerIndex = Mathf.Clamp(Plugin.IsModLethalInternsLoaded && IndexBeginOfInterns > 0 ? IndexBeginOfInterns : playerArraySize, 0, playerArraySize);
            for (int i = 0; i < maxPlayerIndex; i++)
            {
                PlayerControllerB player = instance.allPlayerScripts[i];
                if (!player.isPlayerControlled 
                    && !player.isPlayerDead)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Client side, when receiving <c>NetworkObjectReference</c> for the <c>LethalBotAI</c> spawned on server,
        /// adds it to its corresponding arrays
        /// </summary>
        /// <param name="networkObjectReferenceLethalBotAI"><c>NetworkObjectReference</c> for the <c>LethalBotAI</c> spawned on server</param>
        /// <param name="indexNextLethalBot">Corresponding index in <c>AllLethalBotAIs</c> for the body <c>GameObject</c> at another index in <c>allPlayerObjects</c></param>
        /// <param name="indexNextPlayerObject">Corresponding index in <c>allPlayerObjects</c> for the body of bot</param>
        /// <param name="spawnPosition">Where the bots will spawn</param>
        /// <param name="yRot">Rotation of the bots when spawning</param>
        /// <param name="isOutside">Spawning outside or inside the facility (used for initializing AI Nodes)</param>
        [ClientRpc]
        private void SpawnLethalBotClientRpc(NetworkObjectReference networkObjectReferenceLethalBotAI,
                                          SpawnLethalBotParamsNetworkSerializable spawnParamsNetworkSerializable)
        {
            Plugin.LogInfo($"Client receive RPC to spawn bot... position : {spawnParamsNetworkSerializable.SpawnPosition}, yRot: {spawnParamsNetworkSerializable.YRot}");

            if (AllLethalBotAIs == null || AllLethalBotAIs.Length == 0)
            {
                Plugin.LogError($"Fatal error : client #{NetworkManager.LocalClientId} no bots initialized ! Please check for previous errors in the console");
                if (IsServer || IsHost)
                    botSpawned.Value = EnumBotSpawnState.NotSpawned;
                return;
            }

            // Get lethalBot from server
            networkObjectReferenceLethalBotAI.TryGet(out NetworkObject networkObjectLethalBotAI);
            LethalBotAI lethalBotAI = networkObjectLethalBotAI.gameObject.GetComponent<LethalBotAI>();
            AllLethalBotAIs[spawnParamsNetworkSerializable.IndexNextLethalBot] = lethalBotAI;

            // Check for identites correctness
            if (spawnParamsNetworkSerializable.LethalBotIdentityID >= IdentityManager.Instance.LethalBotIdentities.Length)
            {
                IdentityManager.Instance.ExpandWithNewDefaultIdentities(numberToAdd: 1);
            }

            InitLethalBotSpawning(lethalBotAI,
                               spawnParamsNetworkSerializable);
        }

        /// <summary>
        /// Initialize bot by initializing body (<c>PlayerControllerB</c>) and brain (<c>LethalBotAI</c>) to default values.
        /// Attach the brain to the body, attach <c>LethalBotAI</c> <c>Transform</c> to the <c>GameObject</c> of the <c>PlayerControllerB</c>.
        /// </summary>
        /// <param name="lethalBotAI"><c>LethalBotAI</c> to initialize</param>
        /// <param name="indexNextPlayerObject">Corresponding index in <c>allPlayerObjects</c> for the body of bot</param>
        /// <param name="spawnPosition">Where the bots will spawn</param>
        /// <param name="yRot">Rotation of the bots when spawning</param>
        /// <param name="isOutside">Spawning outside or inside the facility (used for initializing AI Nodes)</param>
        private void InitLethalBotSpawning(LethalBotAI lethalBotAI,
                                        SpawnLethalBotParamsNetworkSerializable spawnParamsNetworkSerializable)
        {
            StartOfRound instance = StartOfRound.Instance;
            LethalBotIdentity lethalBotIdentity = IdentityManager.Instance.LethalBotIdentities[spawnParamsNetworkSerializable.LethalBotIdentityID];

            // Make sure we have a vaild player index
            if (!spawnParamsNetworkSerializable.IndexNextPlayerObject.HasValue)
            {
                Plugin.LogError($"Fatal error : client #{NetworkManager.LocalClientId} no indexNextPlayerObject in SpawnLethalBotParamsNetworkSerializable ! Please check for previous errors in the console");
                return;
            }

            GameObject objectParent = instance.allPlayerObjects[spawnParamsNetworkSerializable.IndexNextPlayerObject.Value];
            objectParent.transform.position = spawnParamsNetworkSerializable.SpawnPosition;
            objectParent.transform.rotation = Quaternion.Euler(new Vector3(0f, spawnParamsNetworkSerializable.YRot, 0f));

            PlayerControllerB lethalBotController = instance.allPlayerScripts[spawnParamsNetworkSerializable.IndexNextPlayerObject.Value];
            lethalBotController.playerUsername = lethalBotIdentity.Name;
            lethalBotController.isPlayerDead = false;
            lethalBotController.isPlayerControlled = true;
            lethalBotController.transform.localScale = Vector3.one;
            lethalBotController.playerSteamId = 0; // Set SteamId to 0 since the game code considers that invalid
            lethalBotController.playerClientId = (ulong)spawnParamsNetworkSerializable.IndexNextPlayerObject;
            lethalBotController.actualClientId = lethalBotController.playerClientId + Const.LETHAL_BOT_ACTUAL_ID_OFFSET;
            lethalBotController.playerActions = new PlayerActions();
            lethalBotController.health = 100;
            DisableLethalBotControllerModel(objectParent, lethalBotController, enable: true, disableLocalArms: true);
            lethalBotController.isInsideFactory = !spawnParamsNetworkSerializable.IsOutside;
            lethalBotController.isMovementHindered = 0;
            lethalBotController.hinderedMultiplier = 1f;
            lethalBotController.criticallyInjured = false;
            lethalBotController.bleedingHeavily = false;
            lethalBotController.activatingItem = false;
            lethalBotController.twoHanded = false;
            lethalBotController.inSpecialInteractAnimation = false;
            lethalBotController.freeRotationInInteractAnimation = false;
            lethalBotController.disableSyncInAnimation = false;
            lethalBotController.disableLookInput = false;
            lethalBotController.inAnimationWithEnemy = null;
            lethalBotController.holdingWalkieTalkie = false;
            lethalBotController.speakingToWalkieTalkie = false;
            lethalBotController.isSinking = false;
            lethalBotController.isUnderwater = false;
            lethalBotController.sinkingValue = 0f;
            lethalBotController.sourcesCausingSinking = 0;
            lethalBotController.isClimbingLadder = false;
            lethalBotController.setPositionOfDeadPlayer = false;
            lethalBotController.mapRadarDotAnimator.SetBool(Const.MAPDOT_ANIMATION_BOOL_DEAD, false);
            lethalBotController.externalForceAutoFade = Vector3.zero;
            lethalBotController.voiceMuffledByEnemy = false;
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_LIMP, false);
            lethalBotController.climbSpeed = Const.CLIMB_SPEED;
            lethalBotController.usernameBillboardText.enabled = true;
            AccessTools.Field(typeof(PlayerControllerB), "updatePositionForNewlyJoinedClient").SetValue(lethalBotController, true);

            // CleanLegsFromMoreEmotesMod
            CleanLegsFromMoreEmotesMod(lethalBotController);

            // lethalBot
            lethalBotAI.BotId = Array.IndexOf(AllLethalBotAIs, lethalBotAI);
            lethalBotAI.creatureAnimator = lethalBotController.playerBodyAnimator;
            lethalBotAI.NpcController = new NpcController(lethalBotController);
            lethalBotAI.eye = lethalBotController.GetComponentsInChildren<Transform>().First(x => x.name == "PlayerEye");
            lethalBotAI.LethalBotIdentity = lethalBotIdentity;
            lethalBotAI.LethalBotIdentity.Hp = spawnParamsNetworkSerializable.Hp == 0 ? 100 : spawnParamsNetworkSerializable.Hp;
            lethalBotAI.LethalBotIdentity.SuitID = spawnParamsNetworkSerializable.SuitID;
            lethalBotAI.LethalBotIdentity.Status = EnumStatusIdentity.Spawned;
            lethalBotAI.SetEnemyOutside(spawnParamsNetworkSerializable.IsOutside);

            // Plug ai on bot body
            lethalBotAI.enabled = false;
            lethalBotAI.NetworkObject.AutoObjectParentSync = false;
            lethalBotAI.transform.parent = objectParent.transform;
            lethalBotAI.NetworkObject.AutoObjectParentSync = true;
            lethalBotAI.enabled = true;

            objectParent.SetActive(true);

            // Unsuscribe from events to prevent double trigger
            PlayerControllerBPatch.OnDisable_ReversePatch(lethalBotController);

            // Destroy dead body of identity
            if (spawnParamsNetworkSerializable.ShouldDestroyDeadBody)
            {
                if (Plugin.IsModModelReplacementAPILoaded
                    && lethalBotIdentity.BodyReplacementBase != null)
                {
                    RemovePlayerModelReplacement(lethalBotIdentity.BodyReplacementBase);
                    lethalBotIdentity.BodyReplacementBase = null;
                }
                if (lethalBotIdentity.DeadBody != null)
                {
                    Object.Destroy(lethalBotIdentity.DeadBody.gameObject);
                    lethalBotIdentity.DeadBody = null;
                }
            }
            // Remove deadbody on controller
            if (lethalBotController.deadBody != null)
            {
                lethalBotController.deadBody = null;
            }

            // Switch suit
            lethalBotAI.ChangeSuitLethalBot(lethalBotController.playerClientId, lethalBotAI.LethalBotIdentity.SuitID.Value, true);

            // Show model replacement
            if (Plugin.IsModModelReplacementAPILoaded)
            {
                lethalBotAI.HideShowModelReplacement(show: true);
            }

            // Radar name update
            foreach (var radarTarget in instance.mapScreen.radarTargets)
            {
                if (radarTarget != null
                    && radarTarget.transform == lethalBotController.transform)
                {
                    radarTarget.name = lethalBotController.playerUsername;
                    break;
                }
            }

            // FIXME: This creates bugs for some reason, the cause is unknown!
            // HACKHACK: We raise the connected players amount and number of living players so enemies consider them!
            // This also makes it so if all human players die, the bots can continue without them!
            /*StartOfRound instanceSOR = StartOfRound.Instance;
            int oldPlayerCount = instanceSOR.connectedPlayersAmount;
            int oldLivingPlayerCount = instanceSOR.livingPlayers;
            instanceSOR.connectedPlayersAmount += 1;
            instanceSOR.livingPlayers += 1;

            Plugin.LogDebug($"Old Num of Connected Players {oldPlayerCount}");
            Plugin.LogDebug($"Connected Players now {instanceSOR.connectedPlayersAmount}");
            Plugin.LogDebug($"Old Num of Living Players now {oldLivingPlayerCount}");
            Plugin.LogDebug($"Living Players now {instanceSOR.livingPlayers}");*/

            // Send player count update
            if (IsServer || IsHost)
            {
                // Make sure we sync the bot's level
                HUDManager.Instance.SyncPlayerLevelServerRpc((int)lethalBotController.playerClientId, lethalBotAI.LethalBotIdentity.Level, true);
                if (isSpawningBots.Value)
                {
                    botSpawned.Value = EnumBotSpawnState.Spawned;
                }
                else
                {
                    sendPlayerCountUpdate.Value = true;
                }
            }

            Plugin.LogDebug($"++ Bot with body {lethalBotController.playerClientId} with identity spawned: {lethalBotIdentity.ToString()}");
            lethalBotAI.Init(spawnParamsNetworkSerializable.enumSpawnAnimation);
        }

        /// <summary>
        /// Manual DisablePlayerModel, for compatibility with mod LethalPhones, does not trigger patch of DisablePlayerModel in LethalPhones
        /// </summary>
        public void DisableLethalBotControllerModel(GameObject lethalBotObject, PlayerControllerB lethalBotController, bool enable = false, bool disableLocalArms = false)
        {
            SkinnedMeshRenderer[] componentsInChildren = lethalBotObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var component in componentsInChildren)
            {
                component.enabled = enable;
            }
            if (disableLocalArms)
            {
                lethalBotController.thisPlayerModelArms.enabled = false;
            }
        }

        private void CleanLegsFromMoreEmotesMod(PlayerControllerB lethalBotController)
        {
            GameObject? gameObject = lethalBotController.playerBodyAnimator.transform.Find("FistPersonLegs")?.gameObject;
            if (gameObject != null)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername}: Cleaning legs from more emotes");
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        #endregion

        /// <summary>
        /// Tells all of the bots a message from the signal translator arrived!
        /// </summary>
        public void LethalBotsRespondToSignalTranslator(string message)
        {
            // Make the message case insensitive!
            message = message.Trim().ToLower();

            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled
                    || lethalBotAI.State == null)
                {
                    continue;
                }

                Plugin.LogDebug($"Bot {lethalBotAI.NpcController.Npc.playerUsername} recevied message {message}!");
                lethalBotAI.State.OnSignalTranslatorMessageReceived(message);
            }
        }

        /// <summary>
        /// Tells all of the bots in range a chat message arrived!
        /// </summary>
        public void LethalBotsRespondToChatMessage(string message, int playerId)
        {
            // Make the message case insensitive!
            message = message.Trim().ToLower();

            StartOfRound playersManager = StartOfRound.Instance;
            PlayerControllerB playerWhoSentMessage = playersManager.allPlayerScripts[playerId];
            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled
                    || lethalBotAI.State == null)
                {
                    continue;
                }

                // Don't allow dead players to chat with the living!
                if (lethalBotAI.NpcController.Npc.isPlayerDead != playerWhoSentMessage.isPlayerDead)
                {
                    continue;
                }

                // We don't care about our own messages!
                if (lethalBotAI.NpcController.Npc == playerWhoSentMessage)
                {
                    continue;
                }

                bool flag = lethalBotAI.NpcController.Npc.holdingWalkieTalkie && playerWhoSentMessage.holdingWalkieTalkie;
                if ((lethalBotAI.NpcController.Npc.transform.position - playerWhoSentMessage.transform.position).sqrMagnitude <= 25f * 25f || flag)
                {
                    Plugin.LogDebug($"Bot {lethalBotAI.NpcController.Npc.playerUsername} heard message {message} from {playerWhoSentMessage.playerUsername}!");
                    Plugin.LogDebug($"Bot { (flag ? "does" : "doesn't") } have a walkie-talkie!");
                    lethalBotAI.State.OnPlayerChatMessageRecevied(message, playerWhoSentMessage);
                }
            }
        }

        #region Company Building Helpers

        /// <summary>
        /// Checks if the Navmesh In Company mod is loaded!
        /// </summary>
        /// <returns>
        /// true: if we can spawn at the company, false: if we can not
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanBotsSpawnAtCompanyBuilding()
        {
            if (Plugin.IsModNavmeshInCompanyLoaded)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if we are at the company building!
        /// </summary>
        /// <returns>
        /// true: if we are at the company, false: if we are not
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreWeAtTheCompanyBuilding()
        {
            if (StartOfRound.Instance.currentLevel.levelID == Const.COMPANY_BUILDING_MOON_ID)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Helper function that checks if there are items on the company desk!
        /// </summary>
        /// <returns>true: if there are items on the desk, false: if there are no items on the desk</returns>
        public static bool AreThereItemsOnDesk()
        {
            if (!AreWeAtTheCompanyBuilding())
            {
                return false;
            }

            return GetNumberOfItemsOnDesk() > 0;
        }

        /// <summary>
        /// Helper function that returns the number of items on the company desk.
        /// </summary>
        /// <returns>the number of items on the company desk, return 0 if null or not at the company building</returns>
        public static int GetNumberOfItemsOnDesk()
        {
            if (!AreWeAtTheCompanyBuilding())
            {
                return 0;
            }
            DepositItemsDesk? companyDesk = CompanyDesk;
            if (companyDesk != null)
            {
                return companyDesk.deskObjectsContainer.GetComponentsInChildren<GrabbableObject>().Length;
            }
            return 0;
        }

        /// <summary>
        /// Helper function that returns the value of items on the company desk.
        /// </summary>
        /// <returns>The total value of all items on the desk!</returns>
        public static int GetValueOfItemsOnDesk()
        {
            if (!AreWeAtTheCompanyBuilding())
            {
                return 0;
            }
            DepositItemsDesk? companyDesk = CompanyDesk;
            if (companyDesk != null)
            {
                int value = 0;
                GrabbableObject[] grabbableObjects = companyDesk.deskObjectsContainer.GetComponentsInChildren<GrabbableObject>();
                foreach (GrabbableObject grabbableObject in grabbableObjects)
                {
                    if (grabbableObject == null)
                    {
                        continue;
                    }
                    value += grabbableObject.scrapValue;
                }
                return value;
            }
            return 0;
        }

        /// <summary>
        /// Helper function that checks if we have fulfilled the profit quota.
        /// </summary>
        /// <remarks>
        /// This also includes the value of items on the desk.
        /// </remarks>
        /// <returns>true: if we have reached the profit quota. false: if we haven't yet!</returns>
        public static bool HaveWeFulfilledTheProfitQuota()
        {
            // Check if the host wants the bots to fulfill the profit quota
            // or sell everything on entire ship
            if (Plugin.Config.SellAllScrapOnShip.Value)
            {
                // We never technically fulfill the profit quota, so the bots will never stop selling scrap
                Plugin.LogDebug("HaveWeFulfilledTheProfitQuota: SellAllScrapOnShip is enabled, returning false.");
                return false;
            }

            TimeOfDay timeOfDay = TimeOfDay.Instance;
            if (timeOfDay == null)
            {
                Plugin.LogWarning("HaveWeFulfilledTheProfitQuota: TimeOfDay instance is null, returning false.");
                return false;
            }

            // Check if the fulfilled quota is greater than or equal to the profit quota
            int fulfilledQuota = timeOfDay.quotaFulfilled + GetValueOfItemsOnDesk();
            Plugin.LogDebug($"HaveWeFulfilledTheProfitQuota: Quota fulfilled: {fulfilledQuota}, Profit quota: {timeOfDay.profitQuota}");
            return fulfilledQuota >= timeOfDay.profitQuota;
        }

        #endregion

        #region Lethal Bots XP

        /// <summary>
        /// Called in <see cref="StartOfRoundPatch.EndOfGameClientRpc_PostFix(StartOfRound, int, bool)"/> to update the XP of all lethal bots
        /// </summary>
        /// <remarks>
        /// We do this here since this is right after the round ends and we have all the data
        /// We only update the XP of lethal bots that are owned by the local player,
        /// since this is the client rpc that is called for all clients
        /// </remarks>
        /// <param name="instanceSOR"></param>
        /// <param name="stats"></param>
        /// <param name="localPlayerWasMostProfitable"></param>
        public void UpdateLethalBotsXP(StartOfRound instanceSOR, EndOfGameStats stats, bool localPlayerWasMostProfitable = false)
        {
            // We need to find the most profitable player or bot
            HUDManager instanceHUD = HUDManager.Instance;
            PlayerControllerB? mostProfitablePlayer = null;
            if (!localPlayerWasMostProfitable)
            {
                // If the local player was not the most profitable, we need to find the most profitable player
                int mostProfitablePlayerIndex = -1;
                int mostScrapCollected = 0;
                for (int i = 0; i < stats.allPlayerStats.Length; i++)
                {
                    PlayerStats player = stats.allPlayerStats[i];
                    if (mostProfitablePlayerIndex == -1 || player.profitable > mostScrapCollected)
                    {
                        mostProfitablePlayerIndex = i;
                        mostScrapCollected = player.profitable;
                    }
                }

                // If we found the most profitable player, set it
                if (mostProfitablePlayerIndex != -1 && mostScrapCollected > 50)
                {
                    mostProfitablePlayer = instanceSOR.allPlayerScripts[mostProfitablePlayerIndex];
                }
            }

            // Now we can update the XP of all bots owned by the local player
            foreach (EndOfGameBotStats botStats in AllBotEndOfGameStats)
            {
                // Make sure we have a valid lethal bot identity
                if (botStats == null || botStats.Identity == null)
                {
                    continue;
                }

                // Update the XP of the bot
                PlayerControllerB lethalBotController = botStats.BotController;
                int currentBotXP = botStats.Identity.XP ?? 0;
                int XPGain = GetLethalBotLevel(!botStats.IsAlive, lethalBotController == mostProfitablePlayer, instanceSOR.allPlayersDead);
                int targetXPLevel = Mathf.Max(currentBotXP + XPGain, 0);

                // Now update the bot's level if it has changed
                int currentLevel = 0;
                for (int i = 0; i < instanceHUD.playerLevels.Length; i++)
                {
                    PlayerLevel playerLevel = instanceHUD.playerLevels[i];
                    if (targetXPLevel >= playerLevel.XPMin && targetXPLevel < playerLevel.XPMax)
                    {
                        currentLevel = i;
                        break;
                    }

                    if (i == instanceHUD.playerLevels.Length - 1)
                    {
                        currentLevel = i;
                    }
                }

                // Update the bot's identity with the new XP and level
                botStats.Identity.XP = targetXPLevel;
                botStats.Identity.Level = currentLevel;
                Plugin.LogDebug($"Updated XP for bot {lethalBotController.playerUsername} to {botStats.Identity.XP}");

                // Now update all other clients with the new XP and level
                instanceHUD.SyncPlayerLevelServerRpc((int)lethalBotController.playerClientId, currentLevel, true);
                UpdateLethalBotsXPServerRpc((int)lethalBotController.playerClientId, targetXPLevel, currentLevel);
            }

            // Clear out the table in case this is called again somehow
            AllBotEndOfGameStats.Clear();
        }

        /// <summary>
        /// This is the same as <see cref="HUDManager.SetPlayerLevel(bool, bool, bool)"/>,
        /// but it returns the XP gain instead of setting it on the local player.
        /// </summary>
        /// <param name="isDead"></param>
        /// <param name="mostProfitable"></param>
        /// <param name="allPlayersDead"></param>
        /// <returns></returns>
        private static int GetLethalBotLevel(bool isDead, bool mostProfitable, bool allPlayersDead)
        {
            int num = 0;
            num = ((!isDead) ? (num + 10) : (num - 3));
            if (mostProfitable)
            {
                num += 15;
            }

            if (allPlayersDead)
            {
                num -= 5;
            }

            if (num > 0)
            {
                Plugin.LogInfo($"XP gain before scaling to scrap returned: {num}");
                Plugin.LogInfo($"{(float)RoundManager.Instance.scrapCollectedInLevel / RoundManager.Instance.totalScrapValueInLevel}");
                float num2 = (float)RoundManager.Instance.scrapCollectedInLevel / RoundManager.Instance.totalScrapValueInLevel;
                Plugin.LogInfo($"{num2}");
                num = (int)((float)num * num2);
            }

            return num;
        }

        /// <summary>
        /// Server RPC to update the XP and Level of a lethal bot for all clients.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="newIdentityXP"></param>
        /// <param name="newIdentityLevel"></param>
        [ServerRpc(RequireOwnership = false)]
        public void UpdateLethalBotsXPServerRpc(int playerId, int newIdentityXP, int newIdentityLevel)
        {
            UpdateLethalBotsXPClientRpc(playerId, newIdentityXP, newIdentityLevel);
        }

        /// <summary>
        /// Updates the experience points (XP) and level of a lethal bot on the client.
        /// </summary>
        /// <remarks>
        /// This method is invoked as a client RPC to synchronize the lethal bot's XP and level
        /// across clients. If the specified player ID does not correspond to a valid player controller, a warning is
        /// logged.
        /// </remarks>
        /// <param name="playerId">The unique identifier of the player controlling the lethal bot.</param>
        /// <param name="newIdentityXP">The new XP value to assign to the lethal bot's identity.</param>
        /// <param name="newIdentityLevel">The new level value to assign to the lethal bot's identity.</param>
        [ClientRpc]
        private void UpdateLethalBotsXPClientRpc(int playerId, int newIdentityXP, int newIdentityLevel)
        {
            Plugin.LogDebug($"UpdateLethalBotsXPClientRpc: {playerId}, {newIdentityXP}, {newIdentityLevel}");
            PlayerControllerB? lethalBotController = playerId < StartOfRound.Instance.allPlayerScripts.Length ? StartOfRound.Instance.allPlayerScripts[playerId] : null;
            if (lethalBotController != null)
            {
                LethalBotAI? lethalBotAI = GetLethalBotAI(lethalBotController);
                if (lethalBotAI != null)
                {    
                    LethalBotIdentity lethalBotIdentity = lethalBotAI.LethalBotIdentity;
                    lethalBotIdentity.XP = newIdentityXP;
                    lethalBotIdentity.Level = newIdentityLevel;
                    Plugin.LogDebug($"Updated XP for bot {lethalBotController.playerUsername} to {lethalBotIdentity.XP}");
                }
            }
            else
            {
                Plugin.LogWarning($"Player Controller with ID {playerId} not found!");
            }
        }

        #endregion

        #region SpawnLethalBotsAtShip

        /// <summary>
        /// Spawn lethal bots from ship after landing
        /// </summary>
        public void SpawnLethalBotsAtShip()
        {
            // No bots on the company building moon unless there is a mod that adds navmesh there!
            if (AreWeAtTheCompanyBuilding() && !CanBotsSpawnAtCompanyBuilding())
            {
                return;
            }

            // Only spawn bots if we are the server or host
            if (!base.IsServer && !base.IsHost)
            {
                return;
            }

            // Only run one instance of this coroutine at a time!
            if (spawnLethalBotsAtShipCoroutine != null)
            {
                StopCoroutine(spawnLethalBotsAtShipCoroutine);
            }
            spawnLethalBotsAtShipCoroutine = StartCoroutine(SpawnLethalBotsCoroutine());
        }

        private IEnumerator SpawnLethalBotsCoroutine()
        {
            yield return null;
            //int nbLethalBotsToSpawn = IdentityManager.Instance.GetNbIdentitiesToDrop();
            //for (int i = 0; i < nbLethalBotsToSpawn; i++)
            int nbSpawnedBots = 0;
            int nbBotsToSpawn = Plugin.Config.MaxBotsAllowedToSpawn;

            // NEEDTOVALIDATE: Should I cache the amount of living players and old player counts
            // This would be the most reliable way of reseting them back, but would break with Late Company
            // since it would not consider late joining players......
            StartOfRound instanceSOR = StartOfRound.Instance;
            int newPlayerCount = instanceSOR.connectedPlayersAmount;
            int newLivingPlayerCount = instanceSOR.livingPlayers;
            isSpawningBots.Value = true;
            Plugin.LogDebug("Preparing to spawn bots!");
            for (int i = 0; i < AllLethalBotAIs.Length; i++)
            {
                Plugin.LogDebug($"AI at index {i}: {AllLethalBotAIs[i]}");
            }
            for (int i = nbBotsToSpawn; i > 0; i--)
            {
                int nextPlayerIndex = GetNextAvailablePlayerObject();
                if (nextPlayerIndex < 0)
                {
                    Plugin.LogWarning("No available player objects to spawn any more lethal bots!");
                    break;
                }

                PlayerControllerB player = instanceSOR.allPlayerScripts[nextPlayerIndex];
                if (!player.isPlayerControlled && !player.isPlayerDead)
                {
                    botSpawned.Value = EnumBotSpawnState.Unknown; // Reset before calling RPC
                    SpawnLethalBotServerRpc(new SpawnLethalBotParamsNetworkSerializable()
                    {
                        enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimationIfDead,
                        SpawnPosition = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(instanceSOR, nextPlayerIndex, simpleTeleport: false),
                        YRot = 0,
                        IsOutside = true
                    });

                    float startTime = Time.realtimeSinceStartup;
                    yield return new WaitUntil(() => botSpawned.Value != EnumBotSpawnState.Unknown || (Time.realtimeSinceStartup - startTime) > 5f);

                    // If we timeout log the incident!
                    if (botSpawned.Value == EnumBotSpawnState.Unknown)
                    {
                        Plugin.LogWarning("Failed to get response from the host!");
                    }
                    else if (botSpawned.Value == EnumBotSpawnState.Spawned)
                    {
                        // NOTE: We do this here as other clients will have a spawn miscount if we don't!
                        // HACKHACK: We raise the connected players amount and number of living players so enemies consider them!
                        // This also makes it so if all human players die, the bots can continue without them!
                        newPlayerCount += 1;
                        newLivingPlayerCount += 1;
                        nbSpawnedBots++;
                    }
                    else if (botSpawned.Value == EnumBotSpawnState.NotSpawned)
                    {
                        Plugin.LogError("Failed to spawn bot on the host!");
                    }
                    // NEEDTOVALIDATE: Should this be faster!
                    //yield return new WaitForSeconds(0.3f);
                }
            }
            Plugin.LogDebug("Finished spawning bots!");
            for (int i = 0; i < AllLethalBotAIs.Length; i++)
            {
                Plugin.LogDebug($"AI at index {i}: {AllLethalBotAIs[i]}");
            }
            SendNewPlayerCountServerRpc(newPlayerCount, newLivingPlayerCount, GameNetworkManager.Instance.connectedPlayers);
            isSpawningBots.Value = false;

            // Only set the starting revives if the mod is loaded!
            if (Plugin.IsModReviveCompanyLoaded)
            { 
                SetStartingRevivesReviveCompany();
            }

            HUDManager.Instance.DisplayTip("Finished Spawning Bots!", string.Format("{0} bots were spawned!", nbSpawnedBots), false, false, "LC_Tip1");
            spawnLethalBotsAtShipCoroutine = null;
        }

        /// <summary>
        /// This is how the host updated the number of living and connected players
        /// after bots spawn in.
        /// </summary>
        /// <remarks>
        /// This is done so bots are considered human players for intents and purposes!
        /// </remarks>
        /// <param name="numConnectedPlayers"></param>
        /// <param name="numLivingPlayers"></param>
        /// <param name="numRealPlayers"></param>
        /// <param name="rpcParams"></param>
        [ServerRpc(RequireOwnership = false)]
        public void SendNewPlayerCountServerRpc(int numConnectedPlayers, int numLivingPlayers, int numRealPlayers, ServerRpcParams rpcParams = default)
        {
            // Allow only the host to update these values
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                Plugin.LogWarning($"Unauthorized client {rpcParams.Receive.SenderClientId} attempted to update player counts!");
                return;
            }

            StartOfRound instanceSOR = StartOfRound.Instance;
            int oldPlayerCount = instanceSOR.connectedPlayersAmount;
            int oldLivingPlayerCount = instanceSOR.livingPlayers;

            instanceSOR.connectedPlayersAmount = numConnectedPlayers;
            instanceSOR.livingPlayers = numLivingPlayers;

            // Only the host can update the real player count
            AllRealPlayersCount = numRealPlayers;

            Plugin.LogInfo($"[Server] Old Num of Connected Players {oldPlayerCount} -> {instanceSOR.connectedPlayersAmount}");
            Plugin.LogInfo($"[Server] Old Num of Living Players {oldLivingPlayerCount} -> {instanceSOR.livingPlayers}");
            Plugin.LogInfo($"[Server] Real Players Count: {AllRealPlayersCount}");

            // Send the updated values to all clients
            sendPlayerCountUpdate.Value = false;
            SendNewPlayerCountClientRpc(numConnectedPlayers, numLivingPlayers, numRealPlayers);
        }

        /// <summary>
        /// This function updated the living and connected player counts
        /// for the game to use!
        /// </summary>
        /// <param name="numConnectedPlayers"></param>
        /// <param name="numLivingPlayers"></param>
        /// <param name="numRealPlayers"></param>
        [ClientRpc]
        private void SendNewPlayerCountClientRpc(int numConnectedPlayers, int numLivingPlayers, int numRealPlayers)
        {
            // The host already updated these values, no need to do it again
            if (IsServer || IsHost)
            {
                return;
            }

            Plugin.LogInfo("[Client] Received updated player counts!");

            StartOfRound instanceSOR = StartOfRound.Instance;
            int oldPlayerCount = instanceSOR.connectedPlayersAmount;
            int oldLivingPlayerCount = instanceSOR.livingPlayers;

            instanceSOR.connectedPlayersAmount = numConnectedPlayers;
            instanceSOR.livingPlayers = numLivingPlayers;
            AllRealPlayersCount = numRealPlayers;

            Plugin.LogInfo($"[Client] Old Num of Connected Players {oldPlayerCount} -> {instanceSOR.connectedPlayersAmount}");
            Plugin.LogInfo($"[Client] Old Num of Living Players {oldLivingPlayerCount} -> {instanceSOR.livingPlayers}");
            Plugin.LogInfo($"[Client] Real Players Count: {AllRealPlayersCount}");
        }

        #endregion

        #region Teleporters

        public void TeleportOutLethalBots(ShipTeleporter teleporter,
                                       Random shipTeleporterSeed)
        {
            if (this.BeamOutLethalBotsCoroutine != null)
            {
                base.StopCoroutine(this.BeamOutLethalBotsCoroutine);
            }
            this.BeamOutLethalBotsCoroutine = base.StartCoroutine(this.BeamOutLethalBots(teleporter, shipTeleporterSeed));
        }

        private IEnumerator BeamOutLethalBots(ShipTeleporter teleporter,
                                           Random shipTeleporterSeed)
        {
            IsInverseTeleporterActive = true;
            yield return new WaitForSeconds(5f);

            if (StartOfRound.Instance.inShipPhase)
            {
                IsInverseTeleporterActive = false;
                yield break;
            }

            Vector3 positionLethalBot;
            Vector3 teleportPos = default(Vector3);
            Vector3 nodePos;
            AudioReverbPresets audioReverbPresets = Object.FindObjectOfType<AudioReverbPresets>();
            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.IsSpawned
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || lethalBotAI.NpcController.Npc.isPlayerDead
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled)
                {
                    continue;
                }

                positionLethalBot = lethalBotAI.NpcController.Npc.transform.position;
                if (lethalBotAI.NpcController.Npc.deadBody != null)
                {
                    positionLethalBot = lethalBotAI.NpcController.Npc.deadBody.bodyParts[5].transform.position;
                }

                if ((positionLethalBot - teleporter.teleportOutPosition.position).sqrMagnitude > 2f * 2f)
                {
                    continue;
                }

                if (RoundManager.Instance.insideAINodes.Length == 0)
                {
                    continue;
                }

                // Random pos
                nodePos = RoundManager.Instance.insideAINodes[shipTeleporterSeed.Next(0, RoundManager.Instance.insideAINodes.Length)].transform.position;

                int maxAttempts = 10;
                bool foundTeleportPosition = false;
                for (int i = 0; i < maxAttempts; i++)
                {
                    teleportPos = RoundManager.Instance.GetRandomNavMeshPositionInBoxPredictable(nodePos, 10f, default(NavMeshHit), shipTeleporterSeed, -1);

                    // Now we check if we would spawn inside of an object!
                    if (!Physics.CheckSphere(teleportPos + Vector3.up * 0.2f, radius: lethalBotAI.agent.radius, StartOfRound.Instance.allPlayersCollideWithMask))
                    {
                        foundTeleportPosition = true;
                        break;
                    }
                }

                // If we fail to find a vaild teleport position, just choose the node itself!
                // NEEDTOVALIDATE: We may no longer need this code as the bots can now stuck teleport!
                if (!foundTeleportPosition)
                {
                    Plugin.LogWarning($"BeamOutLethalBots failed to find random navmesh postion near target node that was not obstructed for Bot {lethalBotAI.NpcController.Npc.playerUsername} after {maxAttempts} attempts!");
                    Plugin.LogWarning("Falling back to the actual node position instead!");
                    teleportPos = RoundManager.Instance.GetNavMeshPosition(nodePos, default(NavMeshHit), 2.7f, -1);
                }

                // Teleport bot
                PlayerControllerB playerControllerB = lethalBotAI.NpcController.Npc;
                ShipTeleporterPatch.SetPlayerTeleporterId_ReversePatch(teleporter, playerControllerB, 2);
                ShipTeleporterPatch.SpikeTrapsReactToInverseTeleport_ReversePatch(teleporter);
                ShipTeleporterPatch.SetCaveReverb_ReversePatch(teleporter, playerControllerB);
                if (Plugin.Config.TeleportedBotDropItems)
                { 
                    playerControllerB.DropAllHeldItems(); 
                }
                if ((bool)audioReverbPresets)
                {
                    audioReverbPresets.audioPresets[2].ChangeAudioReverbForPlayer(playerControllerB);
                }
                playerControllerB.isInElevator = false;
                playerControllerB.isInHangarShipRoom = false;
                playerControllerB.isInsideFactory = true;
                playerControllerB.averageVelocity = 0f;
                playerControllerB.velocityLastFrame = Vector3.zero;
                lethalBotAI.InitStateToSearchingNoTarget(true);
                lethalBotAI.TeleportLethalBot(teleportPos, setOutside: false, targetEntrance: null);
                lethalBotAI.NpcController.Npc.beamOutParticle.Play();
                teleporter.shipTeleporterAudio.PlayOneShot(teleporter.teleporterBeamUpSFX);
                ShipTeleporterPatch.SetPlayerTeleporterId_ReversePatch(teleporter, playerControllerB, -1);
            }
            IsInverseTeleporterActive = false;
        }

        #endregion

        #region Trapped Players Checks

        private IEnumerator trappedPlayerCheck()
        {
            yield return null;
            StartOfRound instanceSOR = StartOfRound.Instance;
            while (instanceSOR != null 
                && !instanceSOR.inShipPhase 
                && instanceSOR.shipHasLanded)
            {
                // Check if there is a player trapped in the facility
                bool foundTrappedPlayer = false;
                for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
                {
                    PlayerControllerB player = instanceSOR.allPlayerScripts[i];
                    if (player.isPlayerControlled && !player.isPlayerDead && player.isInsideFactory)
                    {
                        // Check if the player is trapped
                        if (!CanPlayerPathToExit(player))
                        {
                            foundTrappedPlayer = true;
                            break;
                        }
                        yield return null; // Give the main thread a chance to do something else
                    }
                }

                IsThereATrappedPlayer = foundTrappedPlayer;

                yield return new WaitForSeconds(Const.TIMER_CHECK_FOR_TRAPPED_PLAYER);
            }

            trappedPlayerCheckCoroutine = null;
        }

        private void StartTrappedPlayerCoroutine()
        {
            if (trappedPlayerCheckCoroutine == null)
            {
                trappedPlayerCheckCoroutine = StartCoroutine(trappedPlayerCheck());
            }
        }

        private void StopTrappedPlayerCoroutine()
        {
            if (trappedPlayerCheckCoroutine != null)
            {
                StopCoroutine(trappedPlayerCheckCoroutine);
                trappedPlayerCheckCoroutine = null;
            }
        }

        /// <summary>
        /// A function that checks if the player can path to the exit!
        /// </summary>
        /// <remarks>
        /// This is basically and advanced call to <see cref="NavMesh.CalculatePath(Vector3, Vector3, int, NavMeshPath)"/> with mutliple checks
        /// to make sure the path is complete!
        /// </remarks>
        /// <param name="player">The player to test</param>
        /// <returns>true: if there is a valid path, false: if there is no valid path</returns>
        private bool CanPlayerPathToExit(PlayerControllerB player)
        {
            // Setup some variables
            Vector3 startPosition = RoundManager.Instance.GetNavMeshPosition(player.transform.position, RoundManager.Instance.navHit, 2.7f);
            LethalBotAI? isPlayerBot = GetLethalBotAI(player);
            bool isOutside = isPlayerBot != null ? isPlayerBot.isOutside : !player.isInsideFactory;
            int areaMask = isPlayerBot != null ? isPlayerBot.agent.areaMask : NavMesh.AllAreas;
            NavMeshPath path = new NavMeshPath();
            foreach (var entrance in LethalBotAI.EntrancesTeleportArray)
            {
                if (((isOutside && entrance.isEntranceToBuilding)
                        || (!isOutside && !entrance.isEntranceToBuilding))
                    && entrance.FindExitPoint())
                {
                    // Check if we can create a path there first!
                    Vector3 exitPosition = RoundManager.Instance.GetNavMeshPosition(entrance.entrancePoint.position, RoundManager.Instance.navHit, 2.7f);
                    if (IsValidPathToEntrance(startPosition, exitPosition, areaMask, ref path, entrance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Helper function that checks if we can path to the main entrance!
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="exitPosition"></param>
        /// <param name="areaMask"></param>
        /// <param name="path"></param>
        /// <param name="targetEntrance"></param>
        /// <returns></returns>
        private bool IsValidPathToEntrance(Vector3 startPosition, Vector3 exitPosition, int areaMask, ref NavMeshPath path, EntranceTeleport targetEntrance)
        {
            // Check if we can path to the entrance!
            if (!LethalBotAI.IsValidPathToTarget(startPosition, exitPosition, areaMask, ref path))
            {
                // Check if this is the front entrance if we need to use an elevator
                if (AIState.IsFrontEntrance(targetEntrance) && LethalBotAI.ElevatorScript != null)
                {
                    // Check if we can path to the bottom of the elevator
                    if (LethalBotAI.IsValidPathToTarget(startPosition, LethalBotAI.ElevatorScript.elevatorBottomPoint.position, areaMask, ref path))
                    {
                        return true;
                    }

                    // Check if they are inside the elevator!
                    return (startPosition - LethalBotAI.ElevatorScript.elevatorInsidePoint.position).sqrMagnitude < 2f * 2f;
                }
                return false;
            }
            return true;
        }

        #endregion

        /// <summary>
        /// Checks if the ship is compromised, bots use this when voting to leave early!
        /// </summary>
        /// <remarks>
        /// This uses <see cref="EnumFearQueryType.BotPanic"/> when checking if an enemy is dangerous
        /// </remarks>
        /// <param name="lethalBotAI">Optional <c>LethalBotAI</c> to check if the ship is compromised, if null it will create a fear query without it</param>
        /// <param name="bypassCooldown">If set to true, this forces an update! This is great for if you teleport the bot!</param>
        /// <returns>true: the ship is compromised, false: the ship is safe</returns>
        public static bool IsShipCompromised(LethalBotAI? lethalBotAI = null, bool bypassCooldown = false)
        {
            if (!bypassCooldown && (Time.timeSinceLevelLoad - nextCheckForShipSafety) < Const.TIMER_CHECK_EXPOSED)
            {
                return _isShipCompromised;
            }

            nextCheckForShipSafety = Time.timeSinceLevelLoad;

            RoundManager instanceRM = RoundManager.Instance;
            foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
            {
                // We check if the bots think this enemy is dangerous,
                // we don't use fear range for anything else
                float? fearRange = GetFearRangeForEnemy(new LethalBotFearQuery(lethalBotAI, spawnedEnemy, EnumFearQueryType.BotPanic));
                if (fearRange.HasValue)
                {
                    // NEEDTOVALIDATE: Should using the ship bounds instead?
                    int pathLayerMask = instanceRM.GetLayermaskForEnemySizeLimit(spawnedEnemy.enemyType);
                    if ((NavSizeLimit)pathLayerMask != NavSizeLimit.MediumSpaces 
                        && !spawnedEnemy.isEnemyDead 
                        && spawnedEnemy.isInsidePlayerShip)
                    {
                        _isShipCompromised = true;
                        return true;
                    }
                }
            }
            _isShipCompromised = false;
            return false;
        }

        /// <summary>
        /// Checks if all human players are dead!
        /// </summary>
        /// <param name="bypassCooldown">If set to true, this forces an update! This is great for if you teleport the bot!</param>
        /// <returns>true: all human players are dead, false: if there is a human player alive!</returns>
        public bool AreAllHumanPlayersDead(bool bypassCooldown = false)
        {
            if (!bypassCooldown && (Time.timeSinceLevelLoad - nextCheckForAliveHumanPlayers) < Const.TIMER_CHECK_EXPOSED)
            {
                return _areAllHumanPlayersDead;
            }

            nextCheckForAliveHumanPlayers = Time.timeSinceLevelLoad;

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isPlayerControlled && !player.isPlayerDead)
                {
                    if (!IsPlayerLethalBot(player) 
                        && (!Plugin.IsModLethalInternsLoaded || !IsPlayerIntern(player)))
                    {
                        _areAllHumanPlayersDead = false;
                        return false;
                    }
                }
            }
            _areAllHumanPlayersDead = true;
            return true;
        }

        /// <summary>
        /// Checks if all players are on the ship!
        /// </summary>
        /// <param name="bypassCooldown">If set to true, this forces an update! This is great for if you teleport the bot!</param>
        /// <returns>true: all players are on the ship, false: if there is a player not on the ship!</returns>
        public bool AreAllPlayersOnTheShip(bool bypassCooldown = false)
        {
            if (!bypassCooldown && (Time.timeSinceLevelLoad - nextCheckForAllPlayersOnShip) < Const.TIMER_CHECK_EXPOSED)
            {
                return _areAllPlayersOnTheShip;
            }

            nextCheckForAllPlayersOnShip = Time.timeSinceLevelLoad;

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isPlayerControlled && !player.isPlayerDead 
                    && (!Plugin.IsModLethalInternsLoaded || !IsPlayerIntern(player)))
                {
                    if (!player.isInElevator && !player.isInHangarShipRoom)
                    {
                        _areAllPlayersOnTheShip = false;
                        return false;
                    }
                }
            }
            _areAllPlayersOnTheShip = true;
            return true;
        }

        /// <summary>
        /// Get <c>LethalBotAI</c> from <c>PlayerControllerB</c> <c>playerClientId</c>
        /// </summary>
        /// <param name="playerClientId"><c>playerClientId</c> of <c>PlayerControllerB</c></param>
        /// <returns><c>LethalBotAI</c> if the <c>PlayerControllerB</c> has an <c>LethalBotAI</c> associated, else returns null</returns>
        public LethalBotAI? GetLethalBotAI(int playerClientId)
        {
            if (AllLethalBotAIs == null || AllLethalBotAIs.Length == 0)
            {
                return null;
            }
            return AllLethalBotAIs[playerClientId];
        }

        /// <summary>
        /// Get <c>LethalBotAI</c> from <c>PlayerControllerB</c>
        /// </summary>
        /// <param name="player"><c>PlayerControllerB</c> </param>
        /// <returns><c>LethalBotAI</c> if the <c>PlayerControllerB</c> has an <c>LethalBotAI</c> associated, else returns null</returns>
        public LethalBotAI? GetLethalBotAI(PlayerControllerB player)
        {
            if (AllLethalBotAIs == null 
                || AllLethalBotAIs.Length == 0 
                || player == null)
            {
                return null;
            }
            return AllLethalBotAIs[player.playerClientId];
        }

        /// <summary>
        /// Get <c>LethalBotAI</c> from <c>PlayerControllerB.playerClientId</c>, 
        /// only if the local client calling the method is the owner of <c>LethalBotAI</c>
        /// </summary>
        /// <param name="index"></param>
        /// <returns><c>LethalBotAI</c> if the <c>PlayerControllerB</c> has an <c>LethalBotAI</c> associated and the local client is the owner, 
        /// else returns null</returns>
        public LethalBotAI? GetLethalBotAIIfLocalIsOwner(int index)
        {
            LethalBotAI? lethalBotAI = GetLethalBotAI(index);
            if (lethalBotAI != null
                && lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId)
            {
                return lethalBotAI;
            }

            return null;
        }

        /// <summary>
        /// Get <c>LethalBotAI</c> from <c>PlayerControllerB.playerClientId</c>, 
        /// only if the local client calling the method is the owner of <c>LethalBotAI</c>
        /// </summary>
        /// <param name="player"><c>PlayerControllerB</c> </param>
        /// <returns><c>LethalBotAI</c> if the <c>PlayerControllerB</c> has an <c>LethalBotAI</c> associated and the local client is the owner, 
        /// else returns null</returns>
        public LethalBotAI? GetLethalBotAIIfLocalIsOwner(PlayerControllerB player)
        {
            LethalBotAI? lethalBotAI = GetLethalBotAI(player);
            if (lethalBotAI != null
                && lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId)
            {
                return lethalBotAI;
            }

            return null;
        }

        /// <summary>
        /// Get the <c>LethalBotAI</c> that holds the <c>GrabbableObject</c>
        /// </summary>
        /// <param name="grabbableObject">Object held by the <c>LethalBotAI</c> the method is looking for</param>
        /// <returns><c>LethalBotAI</c> if holding the <c>GrabbableObject</c>, else returns null</returns>
        public LethalBotAI? GetLethalBotAiOwnerOfObject(GrabbableObject grabbableObject)
        {
            foreach (var lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.IsSpawned
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || lethalBotAI.NpcController.Npc.isPlayerDead
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled)
                {
                    continue;
                }

                if (lethalBotAI.HasGrabbableObjectInInventory(grabbableObject, out _))
                {
                    return lethalBotAI;
                }
            }

            return null;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB</c> corresponding to an lethalBot in <c>allPlayerScripts</c>, 
        /// a <c>PlayerControllerB</c> that has <c>LethalBotAI</c>
        /// </summary>
        /// <returns><c>true</c> if <c>PlayerControllerB</c> has <c>LethalBotAI</c>, else <c>false</c></returns>
        public bool IsPlayerLethalBot(PlayerControllerB? player)
        {
            if (player == null) return false;
            LethalBotAI? lethalBotAI = GetLethalBotAI(player);
            return lethalBotAI != null;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB.playerClientId</c> corresponding to an index of a lethalBot in <c>allPlayerScripts</c>, 
        /// a <c>PlayerControllerB</c> that has <c>LethalBotAI</c>
        /// </summary>
        /// <param name="id"><c>PlayerControllerB.playerClientId</c></param>
        /// <returns><c>true</c> if <c>PlayerControllerB</c> has <c>LethalBotAI</c>, else <c>false</c></returns>
        public bool IsPlayerLethalBot(int id)
        {
            LethalBotAI? lethalBotAI = GetLethalBotAI(id);
            return lethalBotAI != null;
        }

        /// <summary>
        /// Copy of <see cref="LethalInternship.Managers.InternManager.IsPlayerIntern(PlayerControllerB)"/>!
        /// Only exists as to not make this addon break having both mods at the same time!
        /// </summary>
        /// <remarks>
        /// Made internal as other mods don't need access to this!
        /// </remarks>
        /// <param name="player"></param>
        /// <returns></returns>
        internal static bool IsPlayerIntern(PlayerControllerB player)
        {
            if (player == null) return false;
            return LethalInternship.Managers.InternManager.Instance.IsPlayerIntern(player);
        }

        /// <summary>
        /// Copy of <see cref="LethalInternship.Managers.InternManager.IsIdPlayerIntern(int)"/>!
        /// Only exists as to not make this addon having both mods at the same time!
        /// </summary>
        /// <remarks>
        /// Made internal as other mods don't need access to this!
        /// </remarks>
        /// <param name="id"></param>
        /// <returns></returns>
        internal bool IsPlayerIntern(int id)
        {
            LethalInternship.AI.InternAI? internAI = LethalInternship.Managers.InternManager.Instance.GetInternAI(id);
            return internAI != null;
        }

        /// <summary>
        /// Is the <c>EnemyAI</c> is an <c>LethalBotAI</c>
        /// </summary>
        /// <param name="ai"></param>
        /// <returns><c>true</c> if <c>EnemyAI</c> is <c>LethalBotAI</c>, else <c>false</c></returns>
        public bool IsAILethalBotAi(EnemyAI ai)
        {
            foreach (var lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == ai)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB</c> the local player or the body of an bot whose owner of <c>LethalBotAI</c> is the local player ?
        /// </summary>
        /// <remarks>
        /// Used by the patches for deciding if the behaviour of the code still applies if the original game code encounters a 
        /// <c>PlayerControllerB</c> that is an bot
        /// </remarks>
        /// <param name="player"></param>
        public bool IsPlayerLocalOrLethalBotOwnerLocal(PlayerControllerB player)
        {
            if (IsPlayerLocal(player))
            {
                return true;
            }

            LethalBotAI? lethalBotAI = GetLethalBotAI(player);
            if (lethalBotAI == null)
            {
                return false;
            }

            return lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB</c> the local player or the body of an bot <c>LethalBotAI</c>?
        /// </summary>
        /// <remarks>
        /// Used by the patches for deciding if the behaviour of the code still applies if the original game code encounters a 
        /// <c>PlayerControllerB</c> that is an bot
        /// </remarks>
        /// <param name="player"></param>
        public bool IsPlayerLocalOrLethalBot(PlayerControllerB player)
        {
            if (IsPlayerLocal(player))
            {
                return true;
            }

            LethalBotAI? lethalBotAI = GetLethalBotAI(player);
            return lethalBotAI != null;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB</c> the local player?
        /// </summary>
        /// <remarks>
        /// Used by the patches for deciding if the behaviour of the code still applies if the original game code encounters a 
        /// <c>PlayerControllerB</c> that is an bot
        /// </remarks>
        /// <param name="player"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPlayerLocal(PlayerControllerB player)
        {
            return player != null && player == GameNetworkManager.Instance.localPlayerController;
        }

        /// <summary>
        /// Is the collider a <c>PlayerControllerB</c> that is the local player, or an bot that is owned by the local player ?
        /// </summary>
        public bool IsColliderFromLocalOrLethalBotOwnerLocal(Collider collider)
        {
            PlayerControllerB player = collider.gameObject.GetComponent<PlayerControllerB>();
            return IsPlayerLocalOrLethalBotOwnerLocal(player);
        }

        /// <summary>
        /// Is the <c>PlayerControllerB</c> the body of an bot whose owner of <c>LethalBotAI</c> is the local player ?
        /// </summary>
        /// <remarks>
        /// Used by the patches for deciding if the behaviour of the code still applies if the original game code encounters a 
        /// <c>PlayerControllerB</c> who is an bot
        /// </remarks>
        /// <param name="player"></param>
        public bool IsPlayerLethalBotOwnerLocal(PlayerControllerB player)
        {
            if (player == null)
            {
                return false;
            }

            LethalBotAI? lethalBotAI = GetLethalBotAI(player);
            if (lethalBotAI == null)
            {
                return false;
            }

            return lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId;
        }

        /// <summary>
        /// Is the <c>PlayerControllerB.playerClientId</c> the body of a lethal bot whose owner of <c>LethalBotAI</c> is the local player ?
        /// </summary>
        /// <remarks>
        /// Used by the patches for deciding if the behaviour of the code still applies if the original game code encounters a 
        /// <c>PlayerControllerB</c> that is a lethal bot
        /// </remarks>
        /// <param name="idPlayer"><c>playerClientId</c> of <c>PlayerControllerB</c></param>
        public bool IsIdPlayerLethalBotOwnerLocal(int idPlayer)
        {
            LethalBotAI? lethalBotAI = GetLethalBotAI(idPlayer);
            if (lethalBotAI == null)
            {
                return false;
            }

            return lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId;
        }

        public bool IsPlayerLethalBotControlledAndOwner(PlayerControllerB player)
        {
            return IsPlayerLethalBotOwnerLocal(player) && player.isPlayerControlled;
        }

        public bool IsAnLethalBotAiOwnerOfObject(GrabbableObject grabbableObject)
        {
            LethalBotAI? lethalBotAI = GetLethalBotAiOwnerOfObject(grabbableObject);
            if (lethalBotAI == null)
            {
                return false;
            }

            return true;
        }

        public LethalBotAI[] GetLethalBotsAIOwnedByLocal()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            List<LethalBotAI> results = new List<LethalBotAI>();
            LethalBotAI? lethalBotAI;
            for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
            {
                lethalBotAI = GetLethalBotAIIfLocalIsOwner(instanceSOR.allPlayerScripts[i]);
                if (lethalBotAI != null)
                {
                    results.Add(lethalBotAI);
                }
            }
            return results.ToArray();
        }

        // NEEDTOVALIDATE: Should this be done in the NpcController's LateUpdate instead?
        public void SetLethalBotsInElevatorLateUpdate(float deltaTime)
        {
            timerSetLethalBotInElevator += deltaTime;
            if (timerSetLethalBotInElevator < 0.5)
            {
                return;
            }
            timerSetLethalBotInElevator = 0f;

            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null)
                {
                    continue;
                }

                lethalBotAI.SetLethalBotInElevator();
            }
        }

        public void UpdateOwnershipOfBotInventoryServer(float deltaTime)
        {
            timerUpdateOwnershipOfBotInventory += deltaTime;
            if (timerUpdateOwnershipOfBotInventory < 0.5)
            {
                return;
            }
            timerUpdateOwnershipOfBotInventory = 0f;

            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null)
                {
                    continue;
                }
                lethalBotAI.UpdateOwnershipOfBotInventoryServer();
            }
        }

        public bool IsLocalPlayerNextToChillLethalBots()
        {
            foreach (var lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.IsSpawned
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || lethalBotAI.NpcController.Npc.isPlayerDead
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled)
                {
                    continue;
                }

                if (lethalBotAI.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId
                    && lethalBotAI.State != null
                    && lethalBotAI.State.GetAIState() == EnumAIStates.ChillWithPlayer)
                {
                    return true;
                }
            }

            return false;
        }

        // HACKHACK: Yeah yeah, I know that they are cheating here!
        public int GetDamageFromSlimeIfLethalBot(PlayerControllerB player)
        {
            if (IsPlayerLethalBot(player))
            {
                return 5;
            }

            return 35;
        }

        public int MaxHealthPercent(int percentage, int maxHealth)
        {
            int healthPercent = (int)(((double)percentage / (double)100) * (double)maxHealth);
            return healthPercent < 1 ? 1 : healthPercent;
        }

        private void EndOfRoundForLethalBots(bool preRevive = false)
        {
            DictEnemyAINoiseListeners.Clear();
            ListEnemyAINonNoiseListeners.Clear();

            // Clear the mission controller bot!
            // No need for an RPC here since this is called for all players!
            if (IsServer || IsHost)
            {
                MissionControlPlayer = null;
            }
            SetLastReportedTimeOfDay(DayMode.Dawn);

            if (preRevive)
            {
                CountAliveAndDisableLethalBots(preRevive);
            }
            else
            {
                CountAliveAndDisableLethalBots();
            }
        }

        /// <summary>
        /// Disable the bots that were respawned
        /// </summary>
        /// <returns>void</returns>
        private void CountAliveAndDisableLethalBots()
        {
            // No bots on the company building moon unless there is a mod that adds navmesh there!
            StartOfRound instanceSOR = StartOfRound.Instance;
            if (AreWeAtTheCompanyBuilding() && !CanBotsSpawnAtCompanyBuilding())
            {
                return;
            }

            foreach (int lethalBot in AllBotPlayerIndexs)
            {
                // Don't use invaild indexes!
                if (lethalBot < 0
                    || lethalBot >= instanceSOR.allPlayerScripts.Length)
                {
                    continue;
                }

                PlayerControllerB lethalBotController = instanceSOR.allPlayerScripts[lethalBot];
                if (lethalBotController == null 
                    || !lethalBotController.isPlayerControlled)
                {
                    continue;
                }
                // NOTE: It should be impossible for the bot to be dead here since we are called
                // after the player revive function!
                /*if (lethalBotController.isPlayerDead)
                {
                    //instanceSOR.connectedPlayersAmount -= 1;
                    continue;
                }*/

                lethalBotController.isPlayerControlled = false;
                lethalBotController.TeleportPlayer(lethalBotController.playersManager.notSpawnedPosition.position);
                lethalBotController.localVisor.position = lethalBotController.playersManager.notSpawnedPosition.position;
                DisableLethalBotControllerModel(lethalBotController.gameObject, lethalBotController, enable: true, disableLocalArms: true);
                
                // Reset the animator state
                Animator lethalBotAnimator = lethalBotController.playerBodyAnimator;
                if (lethalBotAnimator != null)
                {
                    lethalBotAnimator.Rebind(true);
                    lethalBotAnimator.Update(0f);
                }

                lethalBotController.transform.position = lethalBotController.playersManager.notSpawnedPosition.position;
                lethalBotController.thisController.enabled = false;
                if (!NetworkManager.Singleton.ShutdownInProgress && base.IsServer)
                {
                    lethalBotController.gameObject.GetComponent<NetworkObject>().RemoveOwnership();
                }
                // This exists just in case the bot got added to it somehow!
                // TODO: Maybe I should allow adding the bot to it since I could allow the host to kick bots!
                // We would still have to remove them here since they "leave" the server after every round!
                QuickMenuManager quickMenuManager = UnityEngine.Object.FindObjectOfType<QuickMenuManager>();
                if (quickMenuManager != null)
                {
                    quickMenuManager.RemoveUserFromPlayerList(lethalBot);
                }

                instanceSOR.allPlayerObjects[lethalBotController.playerClientId].SetActive(false);
                //instanceSOR.connectedPlayersAmount -= 1;
                //instanceSOR.livingPlayers -= 1;
            }

            // Clear out the table in case this is called again somehow
            AllBotPlayerIndexs.Clear();

            // NEEDTOVALIDATE: Is this code better than the one above,
            // I know that its less prone to having a connected player miscount though
            int newPlayerCount = GameNetworkManager.Instance.connectedPlayers - 1; // StartOfRound doesn't count the host player while NetworkManager does!            instanceSOR.livingPlayers = GameNetworkManager.Instance.connectedPlayers;
            int newLivingPlayerCount = GameNetworkManager.Instance.connectedPlayers; 
            SendNewPlayerCountServerRpc(newPlayerCount, newLivingPlayerCount, GameNetworkManager.Instance.connectedPlayers);
        }

        /// <summary>
        /// Count and disable the bots still alive
        /// </summary>
        /// <returns>void</returns>
        private void CountAliveAndDisableLethalBots(bool _)
        {
            // No bots on the company building moon unless there is a mod that adds navmesh there!
            if (AreWeAtTheCompanyBuilding() && !CanBotsSpawnAtCompanyBuilding())
            {
                return;
            }

            // New tables!!!
            AllBotPlayerIndexs.Clear();
            AllBotEndOfGameStats.Clear();

            // FIXME: Is there a better way of doing this, but this works for now!
            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || lethalBotAI.NpcController == null)
                {
                    continue;
                }

                // Mod support!!!!
                if (Plugin.IsModModelReplacementAPILoaded)
                {
                    lethalBotAI.HideShowModelReplacement(show: false);
                }

                PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
                if (lethalBotController.isPlayerControlled && !lethalBotController.isPlayerDead)
                {
                    // Leave the terminal if we are using one!
                    if (lethalBotController.inTerminalMenu)
                    {
                        lethalBotAI.LeaveTerminal();
                    }

                    // Check if the bot should be abandoned or not
                    if (!lethalBotController.isInElevator && !lethalBotController.isInHangarShipRoom)
                    {
                        Plugin.LogDebug($"Killing player obj #{lethalBotController.playerClientId}, they were not in the ship when it left.");
                        lethalBotController.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Abandoned);

                        // NOTE: We use an IsOwner check so the client only calls this once!
                        // CountAliveAndDisableLethalBots is called for all players!
                        if (IsOwner)
                        {
                            HUDManager.Instance.AddTextToChatOnServer($"{lethalBotController.playerUsername} was left behind.");
                        }
                    }
                    else
                    {
                        if (!lethalBotController.isInHangarShipRoom)
                        {
                            lethalBotController.isInElevator = true;
                            lethalBotController.isInHangarShipRoom = true;
                            Vector3 shipPos = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(StartOfRound.Instance, (int)lethalBotController.playerClientId, false);
                            lethalBotController.thisController.enabled = false;
                            lethalBotController.TeleportPlayer(shipPos);
                            // HACKHACK: TeleportLethalBot acts weird at times, so we manually set the player position as well!
                            lethalBotAI.TeleportLethalBot(shipPos, setOutside: false, targetEntrance: null);
                            lethalBotAI.serverPosition = shipPos;
                            lethalBotAI.transform.position = shipPos;
                            lethalBotAI.transform.localPosition = shipPos;
                            lethalBotController.serverPlayerPosition = shipPos;
                            lethalBotController.transform.localPosition = shipPos;
                            lethalBotController.transform.position = shipPos;
                            lethalBotController.thisController.enabled = true;

                            // Make sure we move our inventory to the ship as well!
                            foreach (var item in lethalBotController.ItemSlots)
                            {
                                Transform? parentObject = item?.parentObject;
                                if (item != null && parentObject != null)
                                {
                                    item.transform.rotation = parentObject.rotation;
                                    item.transform.Rotate(item.itemProperties.rotationOffset);
                                    item.transform.position = parentObject.position;
                                    Vector3 positionOffset = item.itemProperties.positionOffset;
                                    positionOffset = parentObject.rotation * positionOffset;
                                    item.transform.position += positionOffset;
                                }
                            }
                        }
                    }

                    // Stop Emoting
                    lethalBotAI.NpcController.StopPreformingEmote(true);

                    // Drop our held items
                    lethalBotController.DropAllHeldItems();
                }

                // Mark the status as recently used so they are spawned in again!
                lethalBotAI.LethalBotIdentity.Status = EnumStatusIdentity.ToSpawn;
                lethalBotAI.LethalBotIdentity.DiedLastRound = lethalBotController.isPlayerDead;
                if (lethalBotAI.State != null
                    && lethalBotAI.State.GetAIState() != EnumAIStates.BrainDead)
                {
                    // If the bot was not in the BrainDead state, we set it to it so it doesn't do anything after this!
                    lethalBotAI.State = new BrainDeadState(lethalBotAI);
                }

                // Cache the lethal bot stats for the end of game stats.
                // This is done so the bot can gain XP and level up.
                // Only save stats for bots on the server!
                if (base.IsServer || base.IsHost)
                {
                    EndOfGameBotStats endOfGameBotStats = new EndOfGameBotStats(lethalBotAI.LethalBotIdentity, lethalBotController, !lethalBotController.isPlayerDead);
                    AllBotEndOfGameStats.Add(endOfGameBotStats);
                }

                // Mark the index as used so the post revive function understands what it needs to deactivate!
                // This is done on purpose so the bots count for the dead body penalties!
                AllBotPlayerIndexs.Add((int)lethalBotAI.NpcController.Npc.playerClientId);
            }
        }

        #region SyncEndOfRoundLethalBots

        /// <summary>
        /// Only for the owner of <c>LethalBotManager</c>, call server and clients to count bots left alive to re-drop on next round
        /// TODO: Change this since, bots will exist for as long as the server does!
        /// </summary>
        public void SyncEndOfRoundLethalBots(bool preRevive = false)
        {
            if (!base.IsOwner)
            {
                return;
            }

            if (base.IsServer)
            {
                /*foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
                {
                    if (lethalBotAI == null
                        || lethalBotAI.isEnemyDead
                        || lethalBotAI.NpcController.Npc.isPlayerDead
                        || !lethalBotAI.NpcController.Npc.isPlayerControlled
                        || !lethalBotAI.HasSomethingInInventory())
                    {
                        continue;
                    }

                    // NOTE: Drop all held items handles all of this now!
                    //lethalBotAI.DropItem();
                    lethalBotAI.NpcController.Npc.DropAllHeldItems();
                }*/

                SyncEndOfRoundLethalBotsFromServerToClientRpc(preRevive);
            }
            else
            {
                SyncEndOfRoundLethalBotsFromClientToServerRpc(preRevive);
            }
        }

        /// <summary>
        /// Server side, call clients to count bots left alive to re-drop on next round
        /// </summary>
        [ServerRpc]
        private void SyncEndOfRoundLethalBotsFromClientToServerRpc(bool preRevive = false)
        {
            SyncEndOfRoundLethalBotsFromServerToClientRpc(preRevive);
        }

        /// <summary>
        /// Client side, count bots left alive to re-drop on next round
        /// </summary>
        [ClientRpc]
        private void SyncEndOfRoundLethalBotsFromServerToClientRpc(bool preRevive = false)
        {
            EndOfRoundForLethalBots(preRevive);
        }

        #endregion

        #region Vehicle landing on map RPC

        public void VehicleHasLanded()
        {
            VehicleController = Object.FindObjectOfType<VehicleController>();
            Plugin.LogDebug($"Vehicle has landed : {VehicleController}");
        }

        #endregion

        #region Config RPC

        [ServerRpc(RequireOwnership = false)]
        public void SyncLoadedJsonIdentitiesServerRpc(ulong clientId)
        {
            Plugin.LogDebug($"Client {clientId} ask server/host {NetworkManager.LocalClientId} to SyncLoadedJsonIdentities");
            ClientRpcParams.Send = new ClientRpcSendParams()
            {
                TargetClientIds = new ulong[] { clientId }
            };

            SyncLoadedJsonIdentitiesClientRpc(
                new ConfigIdentitiesNetworkSerializable()
                {
                    ConfigIdentities = Plugin.Config.ConfigIdentities.configIdentities
                },
                ClientRpcParams);
        }

        [ClientRpc]
        private void SyncLoadedJsonIdentitiesClientRpc(ConfigIdentitiesNetworkSerializable configIdentityNetworkSerializable,
                                                       ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                return;
            }

            Plugin.LogInfo($"Client {NetworkManager.LocalClientId} : sync json bots identities");
            Plugin.LogDebug($"Loaded {configIdentityNetworkSerializable.ConfigIdentities.Length} identities from server");
            foreach (ConfigIdentity configIdentity in configIdentityNetworkSerializable.ConfigIdentities)
            {
                Plugin.LogDebug($"{configIdentity.ToString()}");
            }

            Plugin.LogDebug($"Recreate identities for {configIdentityNetworkSerializable.ConfigIdentities.Length} bots");
            IdentityManager.Instance.InitIdentities(configIdentityNetworkSerializable.ConfigIdentities);
        }

        #endregion

        #region Voices

        public void UpdateAllLethalBotsVoiceEffects()
        {
            foreach (LethalBotAI lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.IsSpawned
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || lethalBotAI.NpcController.Npc.isPlayerDead
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled
                    || lethalBotAI.creatureVoice == null)
                {
                    continue;
                }

                lethalBotAI.UpdateLethalBotVoiceEffects();
            }
        }

        public bool DidAnLethalBotJustTalkedClose(int idLethalBotTryingToTalk)
        {
            LethalBotAI lethalBotTryingToTalk = AllLethalBotAIs[idLethalBotTryingToTalk];

            foreach (var lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || !lethalBotAI.IsSpawned
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || lethalBotAI.NpcController.Npc.isPlayerDead
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled)
                {
                    continue;
                }

                if (lethalBotAI == lethalBotTryingToTalk)
                {
                    continue;
                }

                if (lethalBotAI.LethalBotIdentity.Voice.IsTalking()
                    && (lethalBotAI.NpcController.Npc.transform.position - lethalBotTryingToTalk.NpcController.Npc.transform.position).sqrMagnitude < VoicesConst.DISTANCE_HEAR_OTHER_BOTS * VoicesConst.DISTANCE_HEAR_OTHER_BOTS)
                {
                    return true;
                }
            }

            return false;
        }

        public void SyncPlayAudioLethalBot(int lethalBotID, string smallPathAudioClip)
        {
            AllLethalBotAIs[lethalBotID].PlayAudioServerRpc(smallPathAudioClip, Plugin.Config.Talkativeness.Value);
        }

        public void PlayAudibleNoiseForLethalBot(int lethalBotID,
                                              Vector3 noisePosition,
                                              float noiseRange = 10f,
                                              float noiseLoudness = 0.5f,
                                              int noiseID = 0)
        {
            LethalBotAI lethalBotAI = AllLethalBotAIs[lethalBotID];
            bool noiseIsInsideClosedShip = lethalBotAI.NpcController.Npc.isInHangarShipRoom && lethalBotAI.NpcController.Npc.playersManager.hangarDoorsClosed;
            lethalBotAI.NpcController.PlayAudibleNoiseLethalBot(noisePosition,
                                                          noiseRange,
                                                          noiseLoudness,
                                                          timesPlayedInSameSpot: 0,
                                                          noiseIsInsideClosedShip,
                                                          noiseID);
        }

        #endregion

        #region Animations culling

        private void UpdateAnimationsCulling()
        {
            if (StartOfRound.Instance == null
                || StartOfRound.Instance.localPlayerController == null)
            {
                return;
            }

            if (timerNoAnimationAfterLag > 0f)
            {
                timerNoAnimationAfterLag += Time.deltaTime;
            }

            timerAnimationCulling += Time.deltaTime;
            if (timerAnimationCulling < 0.05f)
            {
                return;
            }

            Array.Fill(lethalBotsInFOV, null);

            // If we are dead we need to update the animations based on the spectated player instead!
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            PlayerControllerB? spectatedPlayer = localPlayer.spectatedPlayerScript;
            PlayerControllerB? mapTarget = StartOfRound.Instance.mapScreen.targetedPlayer;
            Camera localPlayerCamera = localPlayer.gameplayCamera;
            Vector3 playerPos = localPlayer.transform.position;
            int baseLayer = localPlayer.gameObject.layer;
            localPlayer.gameObject.layer = 0;
            if (localPlayer.isPlayerDead)
            {
                playerPos = spectatedPlayer != null ? spectatedPlayer.transform.position : playerPos;
                localPlayerCamera = StartOfRound.Instance.spectateCamera;
            }

            int index = 0;
            Vector3 lethalBotBodyPos;
            Vector3 vectorPlayerToLethalBot;
            foreach (LethalBotAI? lethalBotAI in AllLethalBotAIs)
            {
                if (lethalBotAI == null
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled
                    || lethalBotAI.NpcController.Npc.isPlayerDead)
                {
                    continue;
                }

                // Cut animation before deciding which bot can animate
                lethalBotAI.NpcController.ShouldAnimate = false;

                // The bot we are spectating should ALWAYS have its animations updated!
                if (lethalBotAI.NpcController.Npc == spectatedPlayer 
                    || (mapTarget != null && lethalBotAI.NpcController.Npc == mapTarget))
                {
                    lethalBotsInFOV[index++] = lethalBotAI;
                    continue;
                }

                if (timerNoAnimationAfterLag > 3f)
                {
                    timerNoAnimationAfterLag = 0f;
                }
                if (timerNoAnimationAfterLag > 0f)
                {
                    continue;
                }
                // Stop animation if we are losing frames
                if (timerNoAnimationAfterLag <= 0f && Time.deltaTime > 0.125f)
                {
                    timerNoAnimationAfterLag += timerAnimationCulling;
                    continue;
                }

                // We only cull bot movement animations
                // So, if we can exit out early, we can save a lot of CPU time!
                if (!lethalBotAI.NpcController.IsMoving())
                {
                    continue;
                }

                lethalBotBodyPos = lethalBotAI.NpcController.Npc.transform.position + new Vector3(0, 1.7f, 0);
                vectorPlayerToLethalBot = lethalBotBodyPos - localPlayerCamera.transform.position;
                if (lethalBotAI.AngleFOVWithLocalPlayerTimedCheck.GetAngleFOVWithLocalPlayer(localPlayerCamera.transform, lethalBotBodyPos) < localPlayerCamera.fieldOfView * 0.81f)
                {
                    // Bot in FOV
                    lethalBotsInFOV[index++] = lethalBotAI;
                }
            }

            index = 0;
            var orderedLethalBotInFOV = lethalBotsInFOV.Where(x => x != null)
                                                 .OrderBy(x => (x.NpcController.Npc.transform.position - playerPos).sqrMagnitude);
            foreach (LethalBotAI? lethalBotAI in orderedLethalBotInFOV)
            {
                if (index >= Plugin.Config.MaxAnimatedBots.Value)
                {
                    break;
                }

                lethalBotAI.NpcController.Npc.gameObject.layer = 0;
                lethalBotBodyPos = lethalBotAI.NpcController.Npc.transform.position + new Vector3(0, 1.7f, 0);
                vectorPlayerToLethalBot = lethalBotBodyPos - localPlayerCamera.transform.position;

                bool collideWithAnotherPlayer = false;
                RaycastHit[] raycastHits = new RaycastHit[5];
                Ray ray = new Ray(localPlayerCamera.transform.position, vectorPlayerToLethalBot);
                int raycastResults = Physics.RaycastNonAlloc(ray, raycastHits, vectorPlayerToLethalBot.magnitude, StartOfRound.Instance.playersMask);
                for (int i = 0; i < raycastResults; i++)
                {
                    RaycastHit hit = raycastHits[i];
                    if (hit.collider != null
                        && hit.collider.tag == "Player")
                    {
                        collideWithAnotherPlayer = true;
                        break;
                    }
                }

                if (!collideWithAnotherPlayer)
                {
                    lethalBotAI.NpcController.ShouldAnimate = true;
                    index++;
                }

                lethalBotAI.NpcController.Npc.gameObject.layer = baseLayer;
            }

            localPlayer.gameObject.layer = baseLayer;
            timerAnimationCulling = 0f;
        }

        #endregion

        #region BunkbedMod RPC

        [ServerRpc(RequireOwnership = false)]
        public void UpdateReviveCountServerRpc(int id)
        {
            UpdateReviveCountClientRpc(id);
        }

        [ClientRpc]
        private void UpdateReviveCountClientRpc(int id)
        {
            BunkbedRevive.BunkbedController.UpdateReviveCount(id);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SyncGroupCreditsForNotOwnerTerminalServerRpc(int newGroupCredits, int numItemsInShip)
        {
            Terminal terminalScript = TerminalManager.Instance.GetTerminal();
            terminalScript.SyncGroupCreditsServerRpc(newGroupCredits, numItemsInShip);
        }

        #endregion

        #region ReviveCompany mod RPC

        [ServerRpc(RequireOwnership = false)]
        public void UpdateReviveCompanyRemainingRevivesServerRpc(string identityName)
        {
            UpdateReviveCompanyRemainingRevivesClientRpc(identityName);
        }

        [ClientRpc]
        private void UpdateReviveCompanyRemainingRevivesClientRpc(string identityName)
        {
            OPJosMod.ReviveCompany.GlobalVariables.RemainingRevives--;
            if (OPJosMod.ReviveCompany.GlobalVariables.RemainingRevives < 100)
            {
                HUDManager.Instance.DisplayTip(identityName + " was revived", string.Format("{0} revives remain!", OPJosMod.ReviveCompany.GlobalVariables.RemainingRevives), false, false, "LC_Tip1");
            }
        }

        /// <summary>
        /// Bots spawn in after the mod sets the starting revives.
        /// We call <c>setStartingRevives</c> to for it to consider the bots as well.
        /// </summary>
        private void SetStartingRevivesReviveCompany()
        {
            // This is not essental code, if it fails it fails!
            try
            {
                var type = AccessTools.TypeByName("OPJosMod.ReviveCompany.Patches.StartOfRoundPatch");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "setStartingRevives");
                    if (method != null)
                    {
                        method.Invoke(null, null);
                        Plugin.LogDebug("Successfully invoked setStartingRevives via AccessTools.");
                    }
                    else
                    {
                        Plugin.LogWarning("Could not find setStartingRevives method.");
                    }
                }
                else
                {
                    Plugin.LogWarning("Could not find StartOfRoundPatch type from ReviveCompany.");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error while invoking seting base available revives: {ex}");
            }
        }

        #endregion

        #region Mission Controller RPC

        /// <summary>
        /// Set the mission controller on all clients
        /// </summary>
        /// <param name="playerToSync"></param>
        public void SetMissionControllerAndSync(NetworkBehaviourReference playerToSync)
        {
            if (base.IsServer)
            {
                _missionControlPlayer.Value = playerToSync; // Sync to clients
            }
            else
            {
                SetMissionControllerServerRpc(playerToSync); // Request server to apply and sync
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetMissionControllerServerRpc(NetworkBehaviourReference playerToSync)
        {
            // Sync the mission control player across clients
            // NOTE: We should be the server here!
            SetMissionControllerAndSync(playerToSync);
        }

        #endregion

        #region Last Reported Time of day RPC

        /// <summary>
        /// Sets the last reported time of day from the mission controller
        /// </summary>
        /// <param name="timeOfDay">The new time of day</param>
        public void SetLastReportedTimeOfDayAndSync(DayMode timeOfDay)
        {
            if (base.IsServer)
            {
                SetLastReportedTimeOfDayClientRpc(timeOfDay);
            }
            else
            {
                SetLastReportedTimeOfDayServerRpc(timeOfDay);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetLastReportedTimeOfDayServerRpc(DayMode timeOfDay)
        {
            SetLastReportedTimeOfDayClientRpc(timeOfDay);
        }

        [ClientRpc]
        private void SetLastReportedTimeOfDayClientRpc(DayMode timeOfDay)
        {
            SetLastReportedTimeOfDay(timeOfDay);
        }

        /// <summary>
        /// Sets the <see cref="lastReportedTimeOfDay"/> for this client
        /// </summary>
        /// <param name="timeOfDay"></param>
        private void SetLastReportedTimeOfDay(DayMode timeOfDay)
        {
            lastReportedTimeOfDay = timeOfDay;
        }

        #endregion
    }

    public class TimedOrderedLethalBotDistanceListCheck
    {
        private List<LethalBotAI> orderedLethalBotDistanceList = null!;

        private long timer = 200 * TimeSpan.TicksPerMillisecond;
        private long lastTimeCalculate;

        public List<LethalBotAI> GetOrderedLethalBotDistanceList(LethalBotAI[] lethalBotAIs)
        {
            if (orderedLethalBotDistanceList == null)
            {
                orderedLethalBotDistanceList = new List<LethalBotAI>();
            }

            if (!NeedToRecalculate())
            {
                return orderedLethalBotDistanceList;
            }

            CalculateOrderedlethalBotDistanceList(lethalBotAIs);
            return orderedLethalBotDistanceList;
        }

        private bool NeedToRecalculate()
        {
            long elapsedTime = DateTime.Now.Ticks - lastTimeCalculate;
            if (elapsedTime > timer)
            {
                lastTimeCalculate = DateTime.Now.Ticks;
                return true;
            }
            else
            {
                return false;
            }
        }

        private void CalculateOrderedlethalBotDistanceList(LethalBotAI[] lethalBotAIs)
        {
            orderedLethalBotDistanceList.Clear();

            foreach (LethalBotAI? lethalBotAI in lethalBotAIs)
            {
                if (lethalBotAI == null
                    || lethalBotAI.isEnemyDead
                    || lethalBotAI.NpcController == null
                    || !lethalBotAI.NpcController.Npc.isPlayerControlled
                    || lethalBotAI.NpcController.Npc.isPlayerDead)
                {
                    continue;
                }

                orderedLethalBotDistanceList.Add(lethalBotAI);
            }

            orderedLethalBotDistanceList = orderedLethalBotDistanceList
                                            .OrderBy(x => x.NpcController.SqrDistanceWithLocalPlayerTimedCheck.GetSqrDistanceWithLocalPlayer(x.NpcController.Npc.transform.position))
                                            .ToList();
        }
    }
}
