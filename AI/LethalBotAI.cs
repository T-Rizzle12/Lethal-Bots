using Dissonance;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.NetworkSerializers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Patches.GameEnginePatches;
using LethalBots.Patches.MapHazardsPatches;
using LethalBots.Patches.MapPatches;
using LethalBots.Patches.ModPatches.LethalPhones;
using LethalBots.Patches.ModPatches.SelfSortingStorage;
using LethalBots.Patches.NpcPatches;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using LethalInternship.AI;
using ReservedItemSlotCore;
using ReservedItemSlotCore.Data;
using ReservedItemSlotCore.Patches;
using Scoops.gameobjects;
using Scoops.misc;
using Scoops.service;
using SelfSortingStorage.Cupboard;
using Steamworks.Ugc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.AI
{
    /// <summary>
    /// AI for the lethalBot.
    /// </summary>
    /// <remarks>
    /// The AI is a component attached to the <c>GameObject</c> parent of the <c>PlayerControllerB</c> for the lethalBot.<br/>
    /// For moving the AI has a agent that pathfind to the next node each game loop,
    /// the component moves by itself, detached from the body and the body (<c>PlayerControllerB</c>) moves toward it.<br/>
    /// For piloting the body, we use <see cref="NpcController"><c>NpcController</c></see> that has a reference to the body (<c>PlayerControllerB</c>).<br/>
    /// Then the AI class use its methods to pilot the body using <c>NpcController</c>.
    /// The <c>NpcController</c> is set outside in <see cref="LethalBotManager.InitLethalBotSpawning"><c>LethalBotManager.InitLethalBotSpawning</c></see>.
    /// </remarks>
    public class LethalBotAI : EnemyAI
    {
        /// <summary>
        /// Dictionary of the recently dropped object on the ground.
        /// The lethalBot will not try to grab them for a certain time (<see cref="Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS"><c>Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS</c></see>).
        /// </summary>
        public static Dictionary<GrabbableObject, float> DictJustDroppedItems = new Dictionary<GrabbableObject, float>();

        /// <summary>
        /// Dictionary of all masked players the bot is aware of.
        /// The lethalBot will avoid masked players at further distances once they become aware of them.
        /// </summary>
        public Dictionary<MaskedPlayerEnemy, bool> DictKnownMasked = new Dictionary<MaskedPlayerEnemy, bool>();

        private AIState _state = null!;
        /// <summary>
        /// Current state of the AI.
        /// </summary>
        /// <remarks>
        /// For the behaviour of the AI, we use a State pattern,
        /// with the class <see cref="AIState"><c>AIState</c></see> 
        /// that we instanciate with one of the behaviour corresponding to <see cref="EnumAIStates"><c>EnumAIStates</c></see>.
        /// </remarks>
        /// <exception cref="NullReferenceException">Called when a null state is given</exception>
        public AIState State
        {
            get => _state;
            set
            {
                AIState? oldState = _state;

                // If the new state is null, throw an exception
                if (value == null)
                {
                    throw new NullReferenceException($"LethalBot {NpcController.Npc.playerUsername} tried to set a null state!");
                }

                // If the old state is not null, stop its coroutines
                if (oldState != null)
                {
                    Plugin.LogDebug($"LethalBot {NpcController.Npc.playerUsername} change from {oldState.GetAIState()} to {value.GetAIState()}!");
                    oldState.OnExitState(value);
                    oldState.StopAllCoroutines();
                }

                // Update the state
                _state = value;

                value.OnEnterState(); // Call the OnEnterState method of the new state
            }
        }
        /// <summary>
        /// Pilot class of the body <c>PlayerControllerB</c> of the lethalBot.
        /// </summary>
        public NpcController NpcController = null!;
        public LethalBotIdentity LethalBotIdentity = null!;
        public AudioSource LethalBotVoice = null!;
        public DunGenTileTracker DunGenTileTracker
        {
            get
            {
                if (field == null)
                {
                    field = this.gameObject.GetComponent<DunGenTileTracker>() ?? this.gameObject.AddComponent<DunGenTileTracker>();
                    field.lethalBotAI = this;
                }
                return field;
            }
        }
        /// <summary>
        /// Currently held item by lethalBot
        /// </summary>
        public GrabbableObject? HeldItem
        {
            set
            {
                var player = NpcController?.Npc;
                if (player != null)
                {
                    player.currentlyHeldObjectServer = value;
                }
                else
                {
                    Plugin.LogWarning($"LethalBotAI.HeldItem failed to set currentlyHeldObjectServer to {value}. Bot PlayerControllerB was null!");
                }
            }
            get
            {
                var player = NpcController?.Npc;
                return player != null ? player.currentlyHeldObjectServer : null;
            }
        }
        private NetworkBehaviour? BotLethalPhone = null; // We store the NetworkBehaviour since Lethal Phones is a soft dependency, so we cant directly reference its classes.
        public Collider LethalBotBodyCollider = null!;

        public int BotId = -1;
        public int MaxHealth = 100;
        public float TimeSinceTeleporting = 0f;

        // Fired logic!
        internal bool choseRandomFlyDirForPlayer;
        internal Vector3 randomFlyDir;

        public TimedTouchingGroundCheck IsTouchingGroundTimedCheck = null!;

        private EnumStateControllerMovement StateControllerMovement;
        private static InteractTrigger[] laddersInteractTrigger = null!;
        public static EntranceTeleport[] EntrancesTeleportArray { private set; get; } = null!;
        public static QuicksandTrigger[] QuicksandArray { private set; get; } = null!;
        private static DoorLock[] doorLocksArray = null!;
        public static MineshaftElevatorController? ElevatorScript { private set; get; } = null;
        private float timerElevatorCooldown;
        private static float pressElevatorButtonCooldown;
        public bool IsInElevatorStartRoom { private set; get; }
        public bool IsInsideElevator
        {
            get
            {
                if (ElevatorScript != null)
                {
                    Collider? elevatorBounds = ElevatorScript.elevatorBounds;
                    return elevatorBounds != null && elevatorBounds.bounds.Contains(NpcController.Npc.transform.position);
                }
                return false;
            }
        }
        private Dictionary<Collider, BridgeTrigger> dictColliderToBridge = null!;
        
        public LethalBotSearchRoutine searchForScrap = null!;
        private Coroutine grabObjectCoroutine = null!;
        internal Coroutine? spawnAnimationCoroutine = null;
        public Coroutine? useLadderCoroutine = null;
        private Coroutine? offMeshLinkCoroutine = null;
        internal Coroutine? useInteractTriggerCoroutine = null;
        private Coroutine? lethalPhonesCoroutine = null;

        // Networked Variables
        /// <summary>
        /// The fear level of the lethalBot.
        /// Synced from the owning client (which varies depending on which player the bot is following),
        /// so all clients see consistent behavior.
        /// </summary>
        /// <remarks>
        /// Used primarily by <see cref="CaveDwellerPhysicsProp"/> to influence <see cref="CaveDwellerAI"/> rocking animation.
        /// Write permission is set to Owner because bot ownership is client-distributed.
        /// </remarks>
        public NetworkVariable<float> FearLevel = new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> FearLevelIncreasing = new NetworkVariable<bool>(writePerm: NetworkVariableWritePermission.Owner);

        /// <summary>
        /// The infection data used by <see cref="CadaverGrowthAI"/> to manage the bot's infection
        /// </summary>
        public NetworkVariable<LethalBotInfection> BotInfectionData = new NetworkVariable<LethalBotInfection>(writePerm: NetworkVariableWritePermission.Owner);
        
        /// <summary>
        /// Used by <see cref="HealPlayerState"/> to determine how infected a player must be by a <see cref="CadaverGrowthAI"/>, before the bot will cure them.
        /// </summary>
        /// <remarks>
        /// This is only synced to keep the level consistent between clients
        /// </remarks>
        public NetworkVariable<float> HealInfectionLevel = new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

        private string stateIndicatorServer = string.Empty;
        private Vector3 previousWantedDestination;
        private bool hasDestinationChanged = true;
        private float updateDestinationIntervalLethalBotAI;
        private CountdownTimer updateDestinationTimer = new CountdownTimer();
        private float healthRegenerateTimerMax;
        private CountdownTimer timerCheckDoor = new CountdownTimer();
        private CountdownTimer timerCheckLockedDoor = new CountdownTimer();
        private CachedValue<bool> areWeExposed = new CachedValue<bool>(value: false, updateInterval: Const.TIMER_CHECK_EXPOSED);
        private CachedValue<bool> isEyelessDogInPromimity = new CachedValue<bool>(value: false, updateInterval: Const.TIMER_CHECK_EXPOSED);

        public LineRendererUtil LineRendererUtil = null!;
        private float stuckTimer; // Used for stuck detection

        public override void Awake()
        {
            // Behaviour states
            enemyBehaviourStates = new EnemyBehaviourState[Enum.GetNames(typeof(EnumAIStates)).Length];
            int index = 0;
            foreach (var state in (EnumAIStates[])Enum.GetValues(typeof(EnumAIStates)))
            {
                enemyBehaviourStates[index++] = new EnemyBehaviourState() { name = state.ToString() };
            }
            currentBehaviourStateIndex = -1;
        }

        /// <summary>
        /// Start unity method.
        /// </summary>
        /// <remarks>
        /// The agent is initialized here
        /// </remarks>
        public override void Start()
        {
            // AIIntervalTime
            AIIntervalTime = 0.3f;

            try
            {
                agent = gameObject.GetComponentInChildren<NavMeshAgent>();
                agent.acceleration = float.MaxValue; // Is THIS a good idea?
                agent.autoTraverseOffMeshLink = false;
                agent.autoBraking = false; // This causes the bot's agent to slow around corners and stuff, we don't want that!
                //agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                Plugin.LogDebug($"LethalBot Agent Type ID {agent.agentTypeID}");
                Plugin.LogDebug($"LethalBot Area Mask {agent.areaMask}");
                SetAgent(enabled: false);
                enemyType.WaterType = EnemyWaterType.Amphibious;
                enemyType.pushPlayerDistance = 0f; // Don't run EnemyAI player push code!
                skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                meshRenderers = gameObject.GetComponentsInChildren<MeshRenderer>();
                if (creatureAnimator == null)
                {
                    creatureAnimator = gameObject.GetComponentInChildren<Animator>();
                }
                thisNetworkObject = gameObject.GetComponentInChildren<NetworkObject>();
                path1 = new NavMeshPath();
                openDoorSpeedMultiplier = enemyType.doorSpeedMultiplier;
            }
            catch (Exception arg)
            {
                Plugin.LogError(string.Format("Error when initializing lethalBot variables for {0} : {1}", gameObject.name, arg));
            }

            //Plugin.LogDebug("LethalBotAI started");
        }

        /// <summary>
        /// Initialization of the field.
        /// </summary>
        /// <remarks>
        /// This method is used as an initialization and re-initialization too.
        /// </remarks>
        public void Init(EnumSpawnAnimation enumSpawnAnimation, bool clientJoining = false)
        {
            // Entrances
            EntrancesTeleportArray = Object.FindObjectsByType<EntranceTeleport>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Ladders
            laddersInteractTrigger = RefreshLaddersList();

            // Doors
            doorLocksArray = Object.FindObjectsByType<DoorLock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Elevator
            ElevatorScript = Object.FindObjectOfType<MineshaftElevatorController>();

            // Find all patches of quicksand and water
            QuicksandArray = Object.FindObjectsByType<QuicksandTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Important colliders
            InitImportantColliders();

            // Grabbableobject
            LethalBotManager.Instance.RegisterItems();

            // Init controller
            if (enumSpawnAnimation != EnumSpawnAnimation.ReinitializePlayer || clientJoining)
                this.NpcController.Awake(clientJoining);

            // Init DunGenTileTracker
            DunGenTileTracker.TargetOverride = NpcController.Npc.transform;

            // Health
            MaxHealth = LethalBotIdentity.HpMax;
            NpcController.Npc.health = Mathf.Clamp(NpcController.Npc.health, 1, MaxHealth);
            healthRegenerateTimerMax = 100f / (float)MaxHealth;
            NpcController.Npc.healthRegenerateTimer = healthRegenerateTimerMax;

            // AI init
            this.ventAnimationFinished = true;
            this.isEnemyDead = false;
            this.enabled = true;
            addPlayerVelocityToDestination = 3f;
            StateControllerMovement = EnumStateControllerMovement.FollowAgent;

            // Reset Network Variables
            if (base.IsOwner)
            {
                FearLevel.Value = 0f;
                FearLevelIncreasing.Value = false;
                BotInfectionData.Value = new LethalBotInfection();
                HealInfectionLevel.Value = UnityEngine.Random.Range(0.3f, 0.7f); // For now, pick a number between 0.3 and 0.7!
            }

            // Search coroutines
            searchForScrap = new LethalBotSearchRoutine(this);

            // Body collider
            LethalBotBodyCollider = NpcController.Npc.GetComponentInChildren<Collider>();
            BoxCollider ourCollider = this.GetComponentInChildren<BoxCollider>();
            ourCollider?.size = LethalBotBodyCollider.bounds.extents; // Set the bounds of the collider to the body collider bounds

            // Bot voice
            InitLethalBotVoiceComponent();
            UpdateLethalBotVoiceEffects();
            StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();

            // Line renderer used for debugging stuff
            LineRendererUtil = new LineRendererUtil(6, this.transform);

            TeleportAgentAIAndBody(NpcController.Npc.transform.position, !StartOfRound.Instance.shipHasLanded);
            StateControllerMovement = EnumStateControllerMovement.FollowAgent;

            // Start timed calulation
            IsTouchingGroundTimedCheck = new TimedTouchingGroundCheck();

            // Spawn animation
            spawnAnimationCoroutine = BeginLethalBotSpawnAnimation(enumSpawnAnimation);

            // NOTE: This is used to debug bot pathfinding to doors!
            //CheckIfLockedDoorsCanBeReached();

        }

        private void InitLethalBotVoiceComponent()
        {
            if (this.creatureVoice == null)
            {
                foreach (var component in this.gameObject.GetComponentsInChildren<AudioSource>())
                {
                    if (component.name == "CreatureVoice")
                    {
                        this.creatureVoice = component;
                        break;
                    }
                }
            }
            if (this.creatureVoice == null)
            {
                Plugin.LogWarning($"Could not initialize lethalBot {this.BotId} {NpcController.Npc.playerUsername} voice !");
                return;
            }

            NpcController.Npc.currentVoiceChatAudioSource = this.creatureVoice;
            this.LethalBotVoice = NpcController.Npc.currentVoiceChatAudioSource;
            this.LethalBotVoice.enabled = true;
            LethalBotIdentity.Voice.BotID = this.BotId;
            LethalBotIdentity.Voice.CurrentAudioSource = this.LethalBotVoice;

            // OccludeAudio
            NpcController.OccludeAudioComponent = creatureVoice.GetComponent<OccludeAudio>();

            // AudioLowPassFilter
            AudioLowPassFilter? audioLowPassFilter = creatureVoice.GetComponent<AudioLowPassFilter>();
            if (audioLowPassFilter == null)
            {
                audioLowPassFilter = creatureVoice.gameObject.AddComponent<AudioLowPassFilter>();
            }
            NpcController.AudioLowPassFilterComponent = audioLowPassFilter;

            // AudioHighPassFilter
            AudioHighPassFilter? audioHighPassFilter = creatureVoice.GetComponent<AudioHighPassFilter>();
            if (audioHighPassFilter == null)
            {
                audioHighPassFilter = creatureVoice.gameObject.AddComponent<AudioHighPassFilter>();
            }
            NpcController.AudioHighPassFilterComponent = audioHighPassFilter;

            // AudioMixerGroup
            /*if ((int)NpcController.Npc.playerClientId >= SoundManager.Instance.playerVoiceMixers.Length)
            {
                // Because of morecompany, playerVoiceMixers gets somehow resized down
                LethalBotManager.Instance.ResizePlayerVoiceMixers(LethalBotManager.Instance.AllEntitiesCount);
            }*/
            this.LethalBotVoice.outputAudioMixerGroup = SoundManager.Instance.playerVoiceMixers[(int)NpcController.Npc.playerClientId];

            // Copy player voice prefab values
            if (SingletonManager.DissonanceComms.TryGet(out DissonanceComms? dissonanceComms))
            {
                // Find the prefab
                Plugin.LogDebug($"Lethal Bot {NpcController.Npc.playerUsername}: found DissonanceComms!");
                AudioSource? voicePrefab = dissonanceComms.PlaybackPrefab?.GetComponentInChildren<AudioSource>();
                if (voicePrefab != null)
                {
                    // Copy over the setting used in the prefab!
                    Plugin.LogDebug($"Lethal Bot {NpcController.Npc.playerUsername}: found voice prefab!");
                    var customCurve = voicePrefab.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    this.LethalBotVoice.rolloffMode = AudioRolloffMode.Custom;
                    this.LethalBotVoice.minDistance = voicePrefab.minDistance;
                    this.LethalBotVoice.maxDistance = voicePrefab.maxDistance;
                    this.LethalBotVoice.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customCurve);
                }
            }
        }

        private void FixedUpdate()
        {
            UpdateSurfaceRayCast();
        }

        private void UpdateSurfaceRayCast()
        {
            // AI may not have finished initializing yet
            if (NpcController == null)
            {
                return;
            }

            PlayerControllerB lethalBotController = NpcController.Npc;
            bool shouldUpdate = IsTouchingGroundTimedCheck.NeedToRecalculate();
            NpcController.IsTouchingGround = IsTouchingGroundTimedCheck.IsTouchingGround(lethalBotController.thisPlayerBody.position);

            // Update current material standing on
            if (NpcController.IsTouchingGround && shouldUpdate)
            {
                /*RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position);
                if (LethalBotManager.Instance.DictTagSurfaceIndex.ContainsKey(groundRaycastHit.collider.tag))
                {
                    NpcController.Npc.currentFootstepSurfaceIndex = LethalBotManager.Instance.DictTagSurfaceIndex[groundRaycastHit.collider.tag];
                }*/
                //lethalBotController.GetCurrentMaterialStandingOn();
                lethalBotController.CalculateGroundNormal();
            }
        }

        /// <summary>
        /// Update unity method.
        /// </summary>
        /// <remarks>
        /// The AI does not calculate each frame but use a timer <c>updateDestinationIntervalLethalBotAI</c>
        /// to update every some number of ms.
        /// </remarks>
        public override void Update()
        {
            // AI may not have finished initializing yet
            if (NpcController == null)
            {
                return;
            }

            // Update identity
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            LethalBotIdentity.Hp = lethalBotController.isPlayerDead ? 0 : lethalBotController.health;
            isEnemyDead = !LethalBotIdentity.Alive; // Let the identity manager handle this!

            // Not owner no AI
            if (!IsOwner)
            {
                if (IsUsingOffMeshLink())
                {
                    StopOffMeshLinkMovement();
                }

                SetAgent(enabled: false);

                if (State == null
                    || State.GetAIState() != EnumAIStates.BrainDead)
                {
                    State = new BrainDeadState(this);
                }

                // If the bot is using a terminal and is not in a state that needs it,
                // they should leave the terminal
                if (!State.CheckAllowsTerminalUse())
                {
                    if (lethalBotController.inTerminalMenu)
                    {
                        LeaveTerminal();
                    }
                }

                // No AI calculation if in special animation if climbing ladder or inSpecialInteractAnimation
                if (!lethalBotController.isClimbingLadder && !lethalBotController.inTerminalMenu
                    && (lethalBotController.inSpecialInteractAnimation || lethalBotController.enteringSpecialAnimation))
                {
                    // If we are using a trigger, set our position and rotation to it!
                    InteractTrigger ourTrigger = lethalBotController.currentTriggerInAnimationWith;
                    if (ourTrigger != null && !ourTrigger.isLadder)
                    {
                        lethalBotController.thisPlayerBody.localPosition = Vector3.Lerp(lethalBotController.thisPlayerBody.localPosition, lethalBotController.thisPlayerBody.parent.InverseTransformPoint(ourTrigger.playerPositionNode.position), Time.deltaTime * 20f);
                        lethalBotController.thisPlayerBody.rotation = Quaternion.Lerp(lethalBotController.thisPlayerBody.rotation, ourTrigger.playerPositionNode.rotation, Time.deltaTime * 20f);
                        NpcController.SetTurnBodyTowardsDirection(ourTrigger.playerPositionNode.rotation.eulerAngles); // NEEDTOVALIDATE: Is this correct?
                    }
                }

                // Move the AI to where the bot controller is
                this.transform.position = lethalBotController.transform.position;
                this.serverPosition = lethalBotController.transform.position;
                return;
            }

            if (!lethalBotController.gameObject.activeSelf
                || !lethalBotController.isPlayerControlled
                || isEnemyDead
                || lethalBotController.isPlayerDead)
            {
                // Lethal Bot dead or
                // Not controlled we do nothing
                SetAgent(enabled: false);

                // No logic if player controller is disabled!
                if (!lethalBotController.gameObject.activeSelf
                || (!lethalBotController.isPlayerControlled 
                    && !lethalBotController.isPlayerDead))
                {
                    return;
                }

                if (State != null && State.GetAIState() == EnumAIStates.BrainDead)
                {
                    // Do the AI calculation behaviour only if we are in the brain dead state
                    // Update interval timer for AI calculation
                    if (updateDestinationIntervalLethalBotAI >= 0f)
                    {
                        updateDestinationIntervalLethalBotAI -= Time.deltaTime;
                    }
                    else
                    {
                        // Do the AI calculation behaviour for the current state
                        State.DoAI();
                        State.TryPlayCurrentStateVoiceAudio();
                        updateDestinationIntervalLethalBotAI = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
                    }
                }
                else if (lethalBotController.isPlayerDead)
                {
                    State = new BrainDeadState(this);
                }
                return;
            }

            // No AI calculation if in special animation
            if (inSpecialAnimation)
            {
                SetAgent(enabled: false);
                return;
            }

            // No AI calculation if in special animation if climbing ladder or inSpecialInteractAnimation
            if (!lethalBotController.isClimbingLadder && !lethalBotController.inTerminalMenu
                && (lethalBotController.inSpecialInteractAnimation || lethalBotController.enteringSpecialAnimation))
            {
                // If we are using a trigger, set our position and rotation to it!
                InteractTrigger ourTrigger = lethalBotController.currentTriggerInAnimationWith;
                if (ourTrigger != null && !ourTrigger.isLadder)
                {
                    lethalBotController.thisPlayerBody.localPosition = Vector3.Lerp(lethalBotController.thisPlayerBody.localPosition, lethalBotController.thisPlayerBody.parent.InverseTransformPoint(ourTrigger.playerPositionNode.position), Time.deltaTime * 20f);
                    lethalBotController.thisPlayerBody.rotation = Quaternion.Lerp(lethalBotController.thisPlayerBody.rotation, ourTrigger.playerPositionNode.rotation, Time.deltaTime * 20f);
                    NpcController.SetTurnBodyTowardsDirection(ourTrigger.playerPositionNode.rotation.eulerAngles); // NEEDTOVALIDATE: Is this correct?
                }

                // Don't do this if the bot is using an off the mesh link
                if (!IsUsingOffMeshLink())
                {
                    SetAgent(enabled: false);
                    this.transform.position = lethalBotController.transform.position;
                    this.serverPosition = lethalBotController.transform.position;
                }
                return;
            }

            // Update if we are in the elevator start room or not!
            if (ElevatorScript != null)
            {
                // Give the bot a cooldown after the elevator finishing moving before we press the button
                // this gives players and other bots time to move into or out of the elevator
                if (ElevatorScript.elevatorFinishedMoving)
                {
                    timerElevatorCooldown += Time.deltaTime;
                }
                else
                {
                    timerElevatorCooldown = 0.0f;
                }

                // Update if we are in the elevator start room or not!
                if (IsInElevatorStartRoom || isOutside)
                {
                    if (isOutside || (lethalBotController.transform.position - ElevatorScript.elevatorBottomPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_BOTTOM * Const.DISTANCE_TO_ELEVATOR_BOTTOM)
                    {
                        IsInElevatorStartRoom = false;
                    }
                }
                else if ((lethalBotController.transform.position - ElevatorScript.elevatorTopPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_TOP * Const.DISTANCE_TO_ELEVATOR_TOP)
                {
                    IsInElevatorStartRoom = true;
                }
            }

            // Update movement
            float x;
            float z;
            if (NpcController.HasToMove)
            {
                Vector2 vector2 = (new Vector2(NpcController.MoveVector.x, NpcController.MoveVector.z));
                agent.speed = 1f * vector2.magnitude;
                //agent.angularSpeed = float.MaxValue; // Players can change direction instantly.....right?
                //agent.autoTraverseOffMeshLink = false;
                //agent.acceleration = 1f * vector2.magnitude; // Is this a good idea?

                // Look where we are going!
                if (!lethalBotController.isClimbingLadder
                    && (NpcController.LookAtTarget.IsLookingForward() 
                        || NpcController.LookAtTarget.CanSwapAimState(lookAtMustExpire: true)))
                {
                    NpcController.OrderToLookForward();
                    NpcController.SetTurnBodyTowardsDirectionWithPosition(this.transform.position);
                }

            }
            // Disable agent if we are not moving!
            else if (!IsInsideElevator && !IsUsingOffMeshLink() && (State == null || !State.IsSafePathRunning()))
            {
                SetAgent(enabled: false);
            }

            // Copied from PlayerControllerB!
            // This also fixes the issue where bots would move slower or even spin in place with low FPS.
            float num9 = 8f;
            if (lethalBotController.jetpackControls)
            {
                num9 = 15f;
            }
            float num10 = Mathf.Clamp(num9 * Vector3.Distance(lethalBotController.transform.position, this.transform.position), 0.9f, 300f);
            Vector3 tempVector = Vector3.MoveTowards(lethalBotController.transform.position, this.transform.position, num10 * Time.deltaTime);
            x = tempVector.x;
            z = tempVector.z;

            // Movement free (falling from bridge, jetpack, tulip snake taking off...)
            bool shouldFreeMovement = ShouldFreeMovement();
            bool shouldFixedMovement = ShouldFixedMovement();

            // Update position
            if (shouldFreeMovement
                || StateControllerMovement == EnumStateControllerMovement.Free)
            {
                StateControllerMovement = EnumStateControllerMovement.Free;
                //Plugin.LogDebug($"{lethalBotController.playerUsername} falling ! lethalBotController.transform.position {lethalBotController.transform.position} MoveVector {NpcController.MoveVector}");
                /*Vector3 endPos = lethalBotController.transform.position + NpcController.MoveVector * Time.deltaTime;
                if (IsTouchingGroundTimedCheck.IsTouchingGround(lethalBotController.transform.position) && NpcController.MoveVector.y < 0)
                {
                    RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(lethalBotController.thisPlayerBody.position);
                    endPos.y = groundRaycastHit.point.y;
                }
                lethalBotController.transform.position = endPos;*/
                // Just use the character controller as this fixes multiple issues the old addon had!
                lethalBotController.thisController.Move(NpcController.MoveVector * Time.deltaTime);
            }
            else if (shouldFixedMovement 
                || StateControllerMovement == EnumStateControllerMovement.Fixed)
            {
                // If we are using a trigger, set our position and rotation to it!
                InteractTrigger ourTrigger = lethalBotController.currentTriggerInAnimationWith;
                if (ourTrigger != null && !ourTrigger.isLadder)
                {
                    lethalBotController.thisPlayerBody.localPosition = Vector3.Lerp(lethalBotController.thisPlayerBody.localPosition, lethalBotController.thisPlayerBody.parent.InverseTransformPoint(ourTrigger.playerPositionNode.position), Time.deltaTime * 20f);
                    lethalBotController.thisPlayerBody.rotation = Quaternion.Lerp(lethalBotController.thisPlayerBody.rotation, ourTrigger.playerPositionNode.rotation, Time.deltaTime * 20f);
                    NpcController.SetTurnBodyTowardsDirection(ourTrigger.playerPositionNode.rotation.eulerAngles); // NEEDTOVALIDATE: Is this correct?
                }
                this.transform.position = lethalBotController.transform.position;
                this.serverPosition = lethalBotController.transform.position;
            }
            else if (StateControllerMovement == EnumStateControllerMovement.FollowAgent)
            {
                Vector3 aiPosition = this.transform.position;
                lethalBotController.thisController.Move(NpcController.MoveVector * Time.deltaTime); // Update player controller.
                //Plugin.LogDebug($"{lethalBotController.playerUsername} --> y {(NpcController.IsTouchingGround ? NpcController.GroundHit.point.y : aiPosition.y)} MoveVector {NpcController.MoveVector}");
                lethalBotController.transform.position = new Vector3(x,
                                                                   aiPosition.y,
                                                                   z); // Override the player controller's movement, since the NavMeshAgent will handle the actual movement.
                this.transform.position = aiPosition;
                lethalBotController.ResetFallGravity();
            }

            // Is still falling ?
            if (StateControllerMovement == EnumStateControllerMovement.Free
                && NpcController.IsTouchingGround
                && !shouldFreeMovement)
            {
                //Plugin.LogDebug($"{lethalBotController.playerUsername} ============= touch ground GroundHit.point {NpcController.GroundHit.point}");
                StateControllerMovement = EnumStateControllerMovement.FollowAgent;
                TeleportAgentAIAndBody(IsTouchingGroundTimedCheck.GetGroundHit(lethalBotController.thisPlayerBody.position).point, onlyAgent: true);
                //Plugin.LogDebug($"{lethalBotController.playerUsername} ============= lethalBotController.transform.position {lethalBotController.transform.position}");
            }

            // No AI when falling
            // NEEDTOVALIDATE: I wonder that since bots now properly set their moveInputVector, if this is no longer needed.
            // Lethal Internship used to set it to Vector2(1.0, 0.0) I believe. I changed it to use the direction of the path the bot was following,
            // which fixed the movement animations.
            //if (StateControllerMovement == EnumStateControllerMovement.Free)
            //{
            //    return;
            //}

            // Do stuck detection
            if (NpcController.HasToMove || (agent.isActiveAndEnabled && !agent.isOnNavMesh))
            {
                // If we are stuck, teleport to the closest node!
                StartOfRound instanceSOR = StartOfRound.Instance;
                if (agent.velocity.sqrMagnitude < 0.002f
                    && StateControllerMovement == EnumStateControllerMovement.FollowAgent
                    && !this.IsUsingOffMeshLink()
                    && !IsInsideElevator
                    && (LethalBotManager.AreWeInOrbit(instanceSOR) 
                        || LethalBotManager.IsTheShipLanded(instanceSOR))) // Mathf.Abs((lethalBotController.oldPlayerPosition - lethalBotController.transform.position).sqrMagnitude) < Const.EPSILON * Const.EPSILON
                {
                    if (stuckTimer > 4f)
                    {
                        Plugin.LogWarning($"Bot {lethalBotController.playerClientId} {lethalBotController.playerUsername} is stuck! Telporting to closest node!");
                        Plugin.LogWarning($"Agent velocity: {agent.velocity.sqrMagnitude} Previous distance from last position: {(lethalBotController.oldPlayerPosition - lethalBotController.transform.position).sqrMagnitude}");
                        State?.OnBotStuck(); // Call the OnBotStuck method of the current state if it exists
                        stuckTimer = 0f;
                    }
                    else
                    {
                        stuckTimer += Time.deltaTime;
                    }
                }
                // If we are no longer stuck, slowly decrement incase we only unstick ourselves a bit!
                else if (stuckTimer > 0f)
                {
                    stuckTimer = Mathf.Max(stuckTimer - Time.deltaTime, 0f);
                }
            }
            else
            {
                // Clear stuck status
                stuckTimer = 0f;
            }

            // Update bot interaction
            State?.LethalBotInteraction?.Update(this, Time.deltaTime);

            // Update interval timer for AI calculation
            if (updateDestinationIntervalLethalBotAI >= 0f)
            {
                updateDestinationIntervalLethalBotAI -= Time.deltaTime;
            }
            else
            {
                SetAgent(enabled: true);

                // Do the actual AI calculation
                DoAIInterval();
                updateDestinationIntervalLethalBotAI = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
            }
        }

        /// <summary>
        /// Where the AI begin its calculations.
        /// </summary>
        /// <remarks>
        /// For the behaviour of the AI, we use a State pattern,
        /// with the class <see cref="AIState"><c>AIState</c></see> 
        /// that we instanciate with one of the behaviour corresponding to <see cref="EnumAIStates"><c>EnumAIStates</c></see>.
        /// </remarks>
        public override void DoAIInterval()
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (isEnemyDead
                || lethalBotController.isPlayerDead
                || State == null)
            {
                return;
            }

            // Update area costs for bots
            State.SetAreaCostsForBot();

            // Do the AI calculation behaviour for the current state
            State.DoAI();

            // Doors
            // TODO: I should probably just override the door code,
            // which would be more optimized than this.
            OpenDoorIfNeeded();

            // Ladders
            UseLadderIfNeeded();

            // Copy movement
            FollowCrouchStateIfCan();

            // Voice
            if (CheckProximityForEyelessDogs())
            {
                LethalBotIdentity.Voice.TryStopAudioFadeOut();
            }
            else
            {
                State.TryPlayCurrentStateVoiceAudio();
            }

            // If the bot is using a terminal and is not in a state that needs it,
            // they should leave the terminal
            if (!State.CheckAllowsTerminalUse())
            {
                if (lethalBotController.inTerminalMenu)
                {
                    LeaveTerminal();
                }
            }

            // Use the currently held item
            State.UseHeldItem();
        }

        public void UpdateController()
        {
           if (NpcController.IsControllerInCruiser)
           {
                return;
           }

            NpcController.Update();
        }

        private void LateUpdate()
        {
            // AI may not have finished initializing yet
            if (NpcController == null)
            {
                return;
            }

            // Update voice position
            // NEEDTOVALIDATE: Would it just be better to parent it instead?
            LethalBotVoice.transform.position = NpcController.Npc.gameplayCamera.transform.position;

            // Update the bot's physic parents!
            SetLethalBotInElevator();

            // Update fear mechanic
            // Network variables can only be updated by the owner of the object!
            if (base.IsOwner)
            {
                if (FearLevelIncreasing.Value)
                {
                    FearLevelIncreasing.Value = false;
                }
                else if (NpcController.Npc.isPlayerDead)
                {
                    FearLevel.Value -= Time.deltaTime * 0.5f;
                }
                else
                {
                    FearLevel.Value -= Time.deltaTime * 0.055f;
                }

                // Update the infection data to other clients if its out of date.
                // NOTE: IsDirty will automatically tell Unity to update the var to other players!
                if (BotInfectionData.IsDirty())
                {
                    Plugin.LogDebug($"Infection data for bot {NpcController.Npc.playerUsername} was out of date. Sending update to all clients");
                }
            }
        }

        private bool ShouldFreeMovement()
        {
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if (NpcController.IsTouchingGround)
            {
                RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(lethalBotController.thisPlayerBody.position);
                //Plugin.LogDebug($"{NpcController.Npc.playerUsername} groundRaycastHit collider and transform {groundRaycastHit.collider}: {groundRaycastHit.collider?.name ?? "NULL"}, {groundRaycastHit.transform}: {groundRaycastHit.transform?.name ?? "NULL"}");
                Collider? collider = groundRaycastHit.collider;
                if (collider != null && dictColliderToBridge.TryGetValue(collider, out BridgeTrigger bridgeTrigger))
                {
                    if (bridgeTrigger != null
                        && bridgeTrigger.fallenBridgeColliders.Length > 0
                        && bridgeTrigger.fallenBridgeColliders[0].enabled)
                    {
                        Plugin.LogDebug($"{lethalBotController.playerUsername} on fallen bridge ! {collider.name}");
                        IsTouchingGroundTimedCheck.ForceRecalculationNextThink(true); // Make sure we actually fall and not teleport ourselves back up!
                        return true;
                    }
                }
            }

            if (StartOfRound.Instance.suckingPlayersOutOfShip)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} being sucked out of the ship!");
                return true;
            }

            Vector3 externalForces = lethalBotController.externalForces;
            Vector3 previousExternalForces = NpcController.PreviousExternalForces;
            if (externalForces.sqrMagnitude > 2f * 2f 
                || previousExternalForces.sqrMagnitude > 2f * 2f)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} externalForces {externalForces.sqrMagnitude} previousExternalForces {previousExternalForces}");
                return true;
            }

            if (lethalBotController.externalForceAutoFade.sqrMagnitude > 2f * 2f)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} externalForceAutoFade {lethalBotController.externalForceAutoFade.sqrMagnitude}");
                return true;
            }

            return false;
        }

        private bool ShouldFixedMovement()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if ((lethalBotController.isInElevator || lethalBotController.isInHangarShipRoom)
                && !LethalBotManager.AreWeInOrbit(instanceSOR)
                && (LethalBotManager.IsTheShipLeaving(instanceSOR)
                    || !LethalBotManager.IsTheShipLanded(instanceSOR)))
            {
                return true;
            }
            if (lethalBotController.currentTriggerInAnimationWith != null)
            {
                return true;
            }
            return false;
        }

        private void FollowCrouchStateIfCan()
        {
            // The state has TOTAL authority over crouching or not!
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if (State != null)
            {
                bool? shouldCrouch = State.ShouldBotCrouch();
                if (shouldCrouch.HasValue)
                {
                    // We can't crouch while sprinting!
                    if (shouldCrouch.Value == true && lethalBotController.isSprinting)
                    {
                        NpcController.OrderToStopSprint();
                    }
                    if (lethalBotController.isCrouching != shouldCrouch.Value)
                    {
                        Plugin.LogDebug($"[{State}] Decided to {(shouldCrouch.Value ? "crouch" : "stand")}.");
                        NpcController.OrderToToggleCrouch();
                    }
                    return;
                }
            }

            if (Plugin.Config.FollowCrouchWithPlayer
                && targetPlayer != null
                && IsFollowingTargetPlayer())
            {
                if (targetPlayer.isCrouching
                    && !lethalBotController.isCrouching)
                {
                    NpcController.OrderToToggleCrouch();
                }
                else if (!targetPlayer.isCrouching
                        && lethalBotController.isCrouching)
                {
                    NpcController.OrderToToggleCrouch();
                }
            }
        }

        public bool IsFollowingTargetPlayer()
        {
            switch (State.GetAIState())
            {
                case EnumAIStates.GetCloseToPlayer:
                case EnumAIStates.ChillWithPlayer:
                case EnumAIStates.JustLostPlayer:
                case EnumAIStates.PlayerInCruiser:
                    return true;
                case EnumAIStates.FetchingObject:
                    return targetPlayer != null 
                        && targetPlayer.isPlayerControlled
                        && !targetPlayer.isPlayerDead;
                default:
                    return false;
            }
        }

        public bool IsFollowingLocalPlayer()
        {
            // Must be a valid player
            if (targetPlayer == null
                || targetPlayer != GameNetworkManager.Instance.localPlayerController
                || !targetPlayer.isPlayerControlled
                || targetPlayer.isPlayerDead)
            {
                return false;
            }

            switch (State.GetAIState())
            {
                case EnumAIStates.GetCloseToPlayer:
                case EnumAIStates.ChillWithPlayer:
                case EnumAIStates.JustLostPlayer:
                case EnumAIStates.PlayerInCruiser:
                case EnumAIStates.FetchingObject:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Helper function that returns how the user wants the bots to follow them by default.
        /// </summary>
        /// <remarks>
        /// If a bot is following another bot, they will use standard by default.<br/>
        /// Bots also use standard following while in orbit
        /// </remarks>
        /// <returns></returns>
        public EnumFollowType GetFollowType()
        {
            // Standard following if we are not following the local player.
            EnumFollowType followType = Plugin.Config.DefaultFollowType.Value;
            PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
            if (localPlayer != targetPlayer)
            {
                return EnumFollowType.Standard;
            }
            // If the player is on the ship, we should be nearby them!
            else if (localPlayer.isInElevator)
            {
                return EnumFollowType.Standard;
            }
            // Don't wander around until we are in the facility!
            else if (!localPlayer.isInsideFactory 
                || IsInElevatorStartRoom
                || !NpcController.Npc.isInsideFactory)
            {
                return followType != EnumFollowType.Wander ? followType : EnumFollowType.Standard; 
            }
            return followType;
        }

        // FIXME: We should recreate the player push away code rather than this!
        public override void OnCollideWithPlayer(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB componentPlayer = other.GetComponent<PlayerControllerB>();
                if (componentPlayer != null
                    && !LethalBotManager.Instance.IsPlayerLethalBot(componentPlayer))
                {
                    NpcController.NearEntitiesPushVector += Vector3.Normalize((NpcController.Npc.transform.position - other.transform.position) * 100f) * 1.2f;
                }
            }
        }

        // FIXME: We should recreate the player push away code rather than this!
        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy)
        {
            if (!IsOwner)
            {
                return;
            }

            if (collidedEnemy == null
                || collidedEnemy is LethalBotAI
                || collidedEnemy is FlowerSnakeEnemy)
            {
                return;
            }

            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if ((lethalBotController.transform.position - other.transform.position).sqrMagnitude < collidedEnemy.enemyType.pushPlayerDistance * collidedEnemy.enemyType.pushPlayerDistance)
            {
                NpcController.NearEntitiesPushVector += Vector3.Normalize((lethalBotController.transform.position - other.transform.position) * 100f) * collidedEnemy.enemyType.pushPlayerForce;
            }

            // Enemy collide with the lethalBot collider
            // NOTE: We don't need to call this anymore as the CharacterController manages this now!
            //collidedEnemy.OnCollideWithPlayer(LethalBotBodyCollider);
        }

        // TODO: Change this so the bots get quiet when they hear noises made by enemies
        // NOTE: This could be replaced with another mod that allows me to grab voice chat itself!
        // FIXME: Adding a list of items is very time consuming,
        // Should I patch into the play sound event or something?
        // List Of Known Sound IDs
        // Default/General: 0 // Some sounds have their ID set to 0!
        // Player footsteps: 7 Other Players, 6 Local Player!
        // Player Voice Chat: 75 Shared
        // Company Curiser Horn: 106217 Shared
        // Company Curiser Engine: 2692 Shared
        // Radar Booster: 1015 Shared
        // Play Audio Animatied Event: 546 Shared?
        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            // Player voice = 75 ?
            if (noiseID != 75)
            {
                return;
            }

            if (NpcController == null
                || isEnemyDead
                || State == null)
            {
                return;
            }

            PlayerControllerB? lethalBotController = NpcController.Npc;
            if (lethalBotController == null
                || !lethalBotController.gameObject.activeSelf
                || !lethalBotController.isPlayerControlled
                || lethalBotController.isPlayerDead)
            {
                return;
            }

            // Make the lethalBot stop talking for some time
            LethalBotIdentity.Voice.TryStopAudioFadeOut();

            if (IsOwner)
            {
                // Reset the cooldown for both types/states
                LethalBotIdentity.Voice.SetNewRandomCooldownForBothAudio();
            }

            Plugin.LogDebug($"Lethal Bot {lethalBotController.playerUsername} detected noise noisePosition {noisePosition}, noiseLoudness {noiseLoudness}, timesPlayedInOneSpot {timesPlayedInOneSpot}, noiseID {noiseID}");
            // Player heard
            State.PlayerHeard(noisePosition);
        }

        /// <summary>
        /// Helper method that determines whether a complete and valid NavMesh path exists between two points.
        /// </summary>
        /// <remarks>
        /// This is an enhanced check that wraps <see cref="NavMesh.CalculatePath(Vector3, Vector3, int, NavMeshPath)"/> with additional validation:
        /// <list type="bullet">
        ///   <item>Ensures the path calculation succeeds</item>
        ///   <item>Confirms the path is not empty</item>
        ///   <item>Verifies that the last path corner is sufficiently close to the destination, ensuring the path is complete</item>
        /// </list>
        /// </remarks>
        /// <param name="startPosition">The starting position of the path</param>
        /// <param name="endPosition">The target position to reach</param>
        /// <param name="areaMask">The NavMesh area mask to use when calculating the path</param>
        /// <param name="path">A reference to the <see cref="NavMeshPath"/> that will contain the calculated path if valid</param>
        /// <param name="calculatePathDistance">This updates <paramref name="pathDistance"/> with the length of the path. <paramref name="pathDistance"/> is set to zero on failure</param>
        /// <param name="pathDistance"></param>
        /// <returns><see langword="true"/> if a valid and complete path exists; otherwise, <see langword="false"/></returns>

        public static bool IsValidPathToTarget(Vector3 startPosition, Vector3 endPosition, int areaMask, ref NavMeshPath path, bool calculatePathDistance, out float pathDistance)
        {
            // Check if we can create a path there first!
            pathDistance = 0f;
            if (!NavMesh.CalculatePath(startPosition, endPosition, areaMask, path))
            {
                return false;
            }

            // Check to make sure the path is valid!
            Vector3[]? corners = path?.corners;
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            // This may be a partial path, make sure the end of the path actually reaches our target destiniation!
            if ((corners[corners.Length - 1] - RoundManager.Instance.GetNavMeshPosition(endPosition, RoundManager.Instance.navHit, 2.7f)).sqrMagnitude > 1.5f * 1.5f)
            {
                return false;
            }

            if (calculatePathDistance)
            {
                for (int i = 1; i < corners.Length; i++)
                {
                    pathDistance += Vector3.Distance(corners[i - 1], corners[i]);
                }
            }

            return true;
        }

        /// <inheritdoc cref="IsValidPathToTarget(Vector3, ref NavMeshPath, bool, float, float)"/>
        /// <remarks>
        /// This calls <see cref="IsValidPathToTarget(Vector3, ref NavMeshPath, bool, float, float)"/> using the bot's EnemyAI's <see cref="EnemyAI.path1"/> internally!
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValidPathToTarget(Vector3 targetPos, bool calculatePathDistance = false, float nearestNavAreaRange = 2.7f, float maxRangeToEnd = 1.5f)
        {
            return IsValidPathToTarget(targetPos, ref this.path1, calculatePathDistance, nearestNavAreaRange, maxRangeToEnd);
        }

        /// <summary>
        /// Checks if a valid path can be made to the target position.
        /// This is alot like <seealso cref="PathIsIntersectedByLineOfSight(Vector3, out bool, bool, bool, EnemyAI?, bool)"/>
        /// </summary>
        /// <param name="targetPos">The target position the bot want to create a path to</param>
        /// <param name="path">The <see cref="NavMeshPath"/> to calculate the vaild path to</param>
        /// <param name="calculatePathDistance">This updates <see cref="EnemyAI.pathDistance"/> with the length of the path. <see cref="EnemyAI.pathDistance"/> is set to zero on failure</param>
        /// <param name="nearestNavAreaRange">The range the game will search for a nearby NavArea for targetPos</param>
        /// <param name="maxRangeToEnd">The maximum range the nearest NavArea will the path be considered vaild</param>
        /// <returns>true: if a valid path was found. false: if no vaild path exists</returns>
        public bool IsValidPathToTarget(Vector3 targetPos, ref NavMeshPath path, bool calculatePathDistance = false, float nearestNavAreaRange = 2.7f, float maxRangeToEnd = 1.5f)
        {
            pathDistance = 0f;
            //Plugin.LogDebug($"BEFORE: Is agent on NavMesh {agent.isOnNavMesh}?");
            //Plugin.LogDebug($"BEFORE: Is agent enabled {agent.enabled}?");
            // Make sure the agent is enabled BEFORE we call the pathfind function!
            bool wasEnabled = agent.enabled;
            SetAgent(enabled: true);
            //Plugin.LogDebug($"AFTER: Is agent on NavMesh {agent.isOnNavMesh}?");
            //Plugin.LogDebug($"AFTER: Is agent enabled {agent.enabled}?");
            if (agent.isOnNavMesh && !agent.CalculatePath(targetPos, path))
            {
                Plugin.LogDebug("IsValidPathToTarget: Path could not be calculated");
                return false;
            }

            Vector3[]? corners = path?.corners;
            if (corners == null || corners.Length == 0)
            {
                Plugin.LogDebug("IsValidPathToTarget: Path is invalid");
                return false;
            }

            if ((corners[corners.Length - 1] - RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, nearestNavAreaRange)).sqrMagnitude > maxRangeToEnd * maxRangeToEnd)
            {
                Plugin.LogDebug($"IsValidPathToTarget: Path is not complete; final waypoint of path was too far from target position: {targetPos}");
                return false;
            }

            if (calculatePathDistance)
            {
                for (int i = 1; i < corners.Length; i++)
                {
                    pathDistance += Vector3.Distance(corners[i - 1], corners[i]);
                }
            }

            // No need for the agent to be active on non-owners!
            if (!base.IsOwner)
            {
                SetAgent(enabled: false);
            }
            else
            {
                SetAgent(enabled: wasEnabled);
            }

            return true;
        }

        /// <summary>
        /// Checks if the entered pos is out of sight from hostile enemies.
        /// Has checks similar to <see cref="EnemyAI.PathIsIntersectedByLineOfSight(Vector3, bool, bool, bool)"/>.
        /// </summary>
        /// <remarks>
        /// For the path check, calls <see cref="IsValidPathToTarget(Vector3, bool, float, float)"/>.
        /// </remarks>
        /// <param name="targetPos">The position to check a safe path to</param>
        /// <param name="calculatePathDistance">Should we update <see cref="EnemyAI.pathDistance"/> with the length of the path</param>
        /// <param name="useEyePosition">Should we use the eye position of an enemy when checking if the path is dangerous</param>
        /// <param name="checkForEnemies">Should we do the enemy checks</param>
        /// <returns>true: this path is dangerous, false: this path is safe</returns>
        public bool IsPathDangerous(Vector3 targetPos, bool calculatePathDistance = false, bool useEyePosition = true, bool checkForEnemies = true)
        {
            // Check if we can path there!
            if (!IsValidPathToTarget(targetPos))
            {
                return true;
            }

            // Lets us know when the bot is checking if a path is dangerous
            Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} is checking if a path to {targetPos} is dangerous!");

            // The code above does the pathfinding for us, we just have to do the rest here!
            Vector3 actualHeadPos = NpcController.Npc.gameplayCamera.transform.position;
            //Collider[] hitColliders = new Collider[10];
            bool skipLOSCheckThisSegment = false;
            float headOffset = actualHeadPos.y - NpcController.Npc.transform.position.y;
            float predictedDrownTimer = NpcController.DrowningTimer; // Travel based on how much air we have left. This makes us wait outside of water before we head back in to it!
            float moveSpeed = NpcController.Npc.movementSpeed > 0f ? NpcController.Npc.movementSpeed : 4.5f;
            moveSpeed /= NpcController.Npc.carryWeight;
            if (calculatePathDistance)
            {
                for (int j = 1; j < path1.corners.Length; j++)
                {
                    Vector3 previousNode = path1.corners[j - 1];
                    Vector3 nodePos = path1.corners[j];
                    float tempDistance = Vector3.Distance(previousNode, nodePos);
                    pathDistance += tempDistance;

                    // If we reach corner 15, stop doing checks now
                    // As we should wait until we get closer to do them!
                    if (j > 15)
                    {
                        continue;
                    }

                    if (checkForEnemies)
                    {
                        if (!skipLOSCheckThisSegment && j > 8 && tempDistance < 2f)
                        {
                            if (DebugEnemy)
                            {
                                Plugin.LogDebug($"Distance between corners {j} and {j - 1} under 3 meters; skipping LOS check");
                                Debug.DrawRay(previousNode + Vector3.up * 0.2f, nodePos + Vector3.up * 0.2f, Color.magenta, 0.2f);
                            }

                            skipLOSCheckThisSegment = true;
                            continue;
                        }
                        skipLOSCheckThisSegment = false;

                        RoundManager instanceRM = RoundManager.Instance;
                        foreach (EnemyAI checkLOSToTarget in instanceRM.SpawnedEnemies)
                        {
                            if (checkLOSToTarget == null 
                                || checkLOSToTarget.isEnemyDead 
                                || !checkLOSToTarget.isOutside)
                            {
                                continue;
                            }

                            // Check if the target is a threat!
                            float? dangerRange = GetFearRangeForEnemies(checkLOSToTarget, EnumFearQueryType.PathfindingAvoid);
                            if (!dangerRange.HasValue)
                            {
                                continue;
                            }

                            // Fog reduce the visibility
                            if (isOutside && !checkLOSToTarget.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
                            {
                                dangerRange = Mathf.Clamp(dangerRange.Value, 0, 30);
                            }

                            // Do the actual check!
                            if ((checkLOSToTarget.transform.position - previousNode).sqrMagnitude > dangerRange * dangerRange)
                            {
                                continue;
                            }

                            Vector3 enemyViewVector = useEyePosition && checkLOSToTarget.eye != null ? checkLOSToTarget.eye.position : checkLOSToTarget.transform.position;
                            if (!Physics.Linecast(previousNode + Vector3.up * headOffset, enemyViewVector + Vector3.up * 0.2f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                            {
                                return true;
                            }

                            if (Physics.Linecast(previousNode, nodePos, 262144))
                            {
                                if (DebugEnemy)
                                {
                                    Plugin.LogDebug($"{enemyType.enemyName}: The path is blocked by line of sight at corner {j}");
                                }

                                return true;
                            }
                        }
                    }

                    // Check if the path intersects with any quicksand or water.
                    Plugin.LogDebug($"Testing quicksand safety between current node {nodePos} and previous node {previousNode}");
                    foreach (var quicksand in QuicksandArray)
                    {
                        if (quicksand == null || !quicksand.isActiveAndEnabled)
                            continue;

                        Bounds quicksandBounds = default;
                        bool foundCollider = false;
                        Collider[] colliders = quicksand.gameObject.GetComponents<Collider>();
                        foreach (Collider collider in colliders)
                        {
                            if (collider != null)
                            {
                                if (!foundCollider)
                                {
                                    quicksandBounds = collider.bounds;
                                    foundCollider = true;
                                }
                                else
                                {
                                    quicksandBounds.Encapsulate(collider.bounds);
                                }
                            }
                        }

                        if (!foundCollider)
                        {
                            continue;
                        }

                        Vector3 a = previousNode;
                        Vector3 b = nodePos;
                        Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(a, b, quicksandBounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);

                        if (!quicksand.isWater)
                        {
                            Plugin.LogDebug("This is quicksand!");

                            // Check if the closest point is within or on the collider
                            /*int arraySize = Physics.OverlapSphereNonAlloc(closestPoint, quicksandBuffer, hitColliders);
                            if (arraySize >= hitColliders.Length)
                            {
                                Array.Resize(ref hitColliders, arraySize);
                                arraySize = Physics.OverlapSphereNonAlloc(closestPoint, quicksandBuffer, hitColliders);
                            }
                            //Collider[] hitColliders = Physics.OverlapSphere(closestPoint, quicksandBuffer);
                            for(int i = 0; i < arraySize; i++)
                            {
                                var hitCollider = hitColliders[i];
                                if (hitCollider == collider)
                                {
                                    Plugin.LogDebug("Segment intersects solid quicksand!");
                                    return true;
                                }
                            }*/

                            // Check if the closest point is within or on the collider
                            if (quicksandBounds.Contains(closestPoint))
                            {
                                Plugin.LogDebug("Segment intersects solid quicksand!");
                                return true;
                            }
                        }
                        else
                        {
                            Plugin.LogDebug("This is water!");

                            // For some reason this works really well like this unlike the code above
                            Vector3 simulatedHead = closestPoint + Vector3.up * headOffset;
                            //if ((testPoint - simulatedHead).sqrMagnitude < quicksandBuffer * quicksandBuffer)
                            if (quicksandBounds.Contains(simulatedHead))
                            {
                                // Test the amount of time we would spend underwater to get here
                                Plugin.LogDebug("Simulated head intersects water!");
                                float travelTime = tempDistance / moveSpeed;

                                float downingDelta = travelTime / Const.LETHAL_BOT_DROWN_TIME; // Match game logic
                                predictedDrownTimer -= downingDelta;
                                Plugin.LogDebug($"Time left in water: {predictedDrownTimer:F2}");

                                if (predictedDrownTimer <= 0f)
                                {
                                    Plugin.LogDebug("Path would drown the bot! Marking path as dangerous!");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                for (int k = 1; k < path1.corners.Length; k++)
                {
                    Vector3 previousNode = path1.corners[k - 1];
                    Vector3 nodePos = path1.corners[k];
                    if (DebugEnemy)
                    {
                        Debug.DrawLine(previousNode, nodePos, Color.green);
                    }

                    float tempDistance = Vector3.Distance(previousNode, nodePos);
                    if (checkForEnemies)
                    {
                        if (!skipLOSCheckThisSegment && k > 8 && tempDistance < 2f)
                        {
                            if (DebugEnemy)
                            {
                                Plugin.LogDebug($"Distance between corners {k} and {k - 1} under 3 meters; skipping LOS check");
                                Debug.DrawRay(previousNode + Vector3.up * 0.2f, nodePos + Vector3.up * 0.2f, Color.magenta, 0.2f);
                            }

                            skipLOSCheckThisSegment = true;
                            continue;
                        }
                        skipLOSCheckThisSegment = false;

                        RoundManager instanceRM = RoundManager.Instance;
                        foreach (EnemyAI checkLOSToTarget in instanceRM.SpawnedEnemies)
                        {
                            if (checkLOSToTarget == null 
                                || checkLOSToTarget.isEnemyDead
                                || !checkLOSToTarget.isOutside)
                            {
                                continue;
                            }

                            // Check if the target is a threat!
                            float? dangerRange = GetFearRangeForEnemies(checkLOSToTarget, EnumFearQueryType.PathfindingAvoid);
                            if (!dangerRange.HasValue)
                            {
                                continue;
                            }

                            // Fog reduce the visibility
                            if (isOutside && !checkLOSToTarget.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
                            {
                                dangerRange = Mathf.Clamp(dangerRange.Value, 0, 30);
                            }

                            // Do the actual check!
                            Vector3 travelMidPoint = Vector3.Lerp(previousNode, nodePos, 0.5f);
                            if ((checkLOSToTarget.transform.position - travelMidPoint).sqrMagnitude > dangerRange * dangerRange)
                            {
                                continue;
                            }

                            Vector3 enemyViewVector = useEyePosition && checkLOSToTarget.eye != null ? checkLOSToTarget.eye.position : checkLOSToTarget.transform.position;
                            if (!Physics.Linecast(travelMidPoint + Vector3.up * headOffset, enemyViewVector + Vector3.up * 0.2f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                            {
                                return true;
                            }

                            if (Physics.Linecast(previousNode, nodePos, 262144))
                            {
                                if (DebugEnemy)
                                {
                                    Plugin.LogDebug($"{enemyType.enemyName}: The path is blocked by line of sight at corner {k}");
                                }

                                return true;
                            }
                        }
                    }

                    // Check if the path intersects with any quicksand or water.
                    Plugin.LogDebug($"Testing quicksand safety between current node {nodePos} and previous node {previousNode}");
                    foreach (var quicksand in QuicksandArray)
                    {
                        if (quicksand == null || !quicksand.isActiveAndEnabled)
                            continue;

                        Bounds quicksandBounds = default;
                        bool foundCollider = false;
                        Collider[] colliders = quicksand.gameObject.GetComponents<Collider>();
                        foreach (Collider colldier in colliders)
                        {
                            if (colldier != null)
                            {
                                if (!foundCollider)
                                {
                                    quicksandBounds = colldier.bounds;
                                    foundCollider = true;
                                }
                                else
                                {
                                    quicksandBounds.Encapsulate(colldier.bounds);
                                }
                            }
                        }

                        if (!foundCollider)
                        {
                            continue;
                        }

                        Vector3 a = previousNode;
                        Vector3 b = nodePos;
                        Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(a, b, quicksandBounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);

                        if (!quicksand.isWater)
                        {
                            Plugin.LogDebug("This is quicksand!");

                            // Check if the closest point is within or on the collider
                            /*int arraySize = Physics.OverlapSphereNonAlloc(closestPoint, quicksandBuffer, hitColliders);
                            if (arraySize >= hitColliders.Length)
                            {
                                Array.Resize(ref hitColliders, arraySize);
                                arraySize = Physics.OverlapSphereNonAlloc(closestPoint, quicksandBuffer, hitColliders);
                            }
                            //Collider[] hitColliders = Physics.OverlapSphere(closestPoint, quicksandBuffer);
                            for(int i = 0; i < arraySize; i++)
                            {
                                var hitCollider = hitColliders[i];
                                if (hitCollider == collider)
                                {
                                    Plugin.LogDebug("Segment intersects solid quicksand!");
                                    return true;
                                }
                            }*/

                            // Check if the closest point is within or on the collider
                            if (quicksandBounds.Contains(closestPoint))
                            {
                                Plugin.LogDebug("Segment intersects solid quicksand!");
                                return true;
                            }
                        }
                        else
                        {
                            Plugin.LogDebug("This is water!");

                            // For some reason this works really well like this unlike the code above
                            Vector3 simulatedHead = closestPoint + Vector3.up * headOffset;
                            //if ((testPoint - simulatedHead).sqrMagnitude < quicksandBuffer * quicksandBuffer)
                            if (quicksandBounds.Contains(simulatedHead))
                            {
                                // Test the amount of time we would spend underwater to get here
                                Plugin.LogDebug("Simulated head intersects water!");
                                float travelTime = tempDistance / moveSpeed;

                                float downingDelta = travelTime / Const.LETHAL_BOT_DROWN_TIME; // Match game logic
                                predictedDrownTimer -= downingDelta;
                                Plugin.LogDebug($"Time left in water: {predictedDrownTimer:F2}");

                                if (predictedDrownTimer <= 0f)
                                {
                                    Plugin.LogDebug("Path would drown the bot! Marking path as dangerous!");
                                    return true;
                                }
                            }
                        }
                    }

                    if (k > 15)
                    {
                        if (DebugEnemy)
                        {
                            Plugin.LogDebug(enemyType.enemyName + ": Reached corner 15, stopping checks now");
                        }

                        return false;
                    }
                }
            }

            return false;
        }

        /// <returns>
        /// <see cref="Task"/> which can be used to check if the path is safe or not.
        /// Please note that you <c>MUST</c> wait until <see cref="Task.IsCompleted"/> is true before you can get the result!
        /// </returns>
        /// <inheritdoc cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, bool, CancellationToken)"/>
        public Task<(bool isDangerous, bool isPathValid, float pathDistance)> TryStartPathDangerousAsync(Vector3 targetPos, bool calculatePathDistance = false, bool useEyePosition = true, bool checkForEnemies = true, bool checkForQuicksand = true, CancellationToken token = default)
        {
            // Check if we were canceled before pathfinding!  
            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<(bool isDangerous, bool isPathValid, float pathDistance)>(token);
            }

            // Lets us know when the bot is checking if a path is dangerous
            Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} is checking if a path to {targetPos} is dangerous! This will be called asynchronously!");

            // Check if we can path there!  
            // We MUST have a local version of the path since this is running over multiple frames  
            NavMeshPath ourPath = new NavMeshPath();

            // Check if there is a valid path  
            if (!IsValidPathToTarget(targetPos, ref ourPath))
            {
                Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} failed to find a path to {targetPos}!");
                return Task.FromResult((true, false, 0f));
            }

            Plugin.LogDebug($"Path found to target {targetPos}. Checking for danger...");
            return IsPathDangerousAsync(ourPath, calculatePathDistance, useEyePosition, checkForEnemies, checkForQuicksand, token);
        }

        /// <summary>
        /// This is an asynchronous version of <see cref="IsPathDangerous(Vector3, bool, bool, bool)"/>.<br/>
        /// This was made for the purpose of being used inside of <see cref="Coroutine"/>s.<br/>
        /// Since the logic may be every heavy to call multiple times per frame
        /// </summary>
        /// <remarks>
        /// This was made because the normal IsPathDangerous can be <c>VERY</c> laggy with multiple enemies.<br/>
        /// Please note that unlike <see cref="IsPathDangerous(Vector3, bool, bool, bool)"/> this runs over mutiple frames!<br/>
        /// You may be better of using <see cref="IsPathDangerous(Vector3, bool, bool, bool)"/> if you need the result immediately!
        /// </remarks>
        /// <param name="ourPath">The path to check the safety of</param>
        /// <param name="calculatePathDistance">Should this update the <see cref="EnemyAI.pathDistance"/> once we finish?</param>
        /// <param name="useEyePosition">Should we use the eye position of an enemy rather than their position when checking if the path is dangerous</param>
        /// <param name="checkForEnemies">Should we check the path can be seen by enemies</param>
        /// <param name="checkForQuicksand">Should we check for quicksand and water on the path</param>
        /// <param name="token">The cancelation token, this allows you to stop the function early!</param>
        /// <returns>Task indicating if the path is safe or not</returns>
        private async Task<(bool isDangerous, bool isPathValid, float pathDistance)> IsPathDangerousAsync(NavMeshPath ourPath, bool calculatePathDistance = false, bool useEyePosition = true, bool checkForEnemies = true, bool checkForQuicksand = true, CancellationToken token = default)
        {
            // Check if we were canceled before pathfinding!
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            // We need to make sure we have a valid path
            Vector3[]? corners = ourPath?.corners;
            if (corners == null || corners.Length == 0)
            {
                return (true, false, 0f);
            }

            // Cache stuff we use a lot, since this could get very expensive fast!
            bool isPathDangerous = false; // Grandpa, why does this bool exist? Well you see Timmy, we want to be able to return the full path distance, even if we fail in the end. So, this holds the true final result!
            bool skipLOSCheckThisSegment = false;
            float pathDistance = 0f; // We must cache this and set it when we finish, since we are running asynchronously
            float headOffset = NpcController.Npc.gameplayCamera.transform.position.y - NpcController.Npc.transform.position.y;
            float predictedDrownTimer = NpcController.DrowningTimer; // Travel based on how much air we have left. This makes us wait outside of water before we head back in to it!
            float moveSpeed = NpcController.Npc.movementSpeed > 0f ? NpcController.Npc.movementSpeed : 4.5f;
            moveSpeed /= NpcController.Npc.carryWeight;
            // FIXME: Rethink this, each water body has its own speed multiplier, so we should not use the NpcController's hindered multiplier here!
            //moveSpeed /= 2f * NpcController.Npc.hinderedMultiplier; // We need to account for the hindered multiplier, since moving in water is slower!
            for (int j = 1; j < corners.Length; j++)
            {
                // Check if we were canceled before running danger checks!
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                // We cache the corners we are using for quicker lookups
                // also we always use the default distance function as we may be calculating path distance!
                Vector3 previousNode = corners[j - 1];
                Vector3 nodePos = corners[j];
                float tempDistance = Vector3.Distance(previousNode, nodePos);

                // Log current path segment check
                Plugin.LogDebug($"Checking path segment from {previousNode} to {nodePos}. Distance: {tempDistance}.");

                // Calculate the path distance as requested
                if (calculatePathDistance)
                {
                    pathDistance += tempDistance;
                }

                // If we reach corner 15, stop doing checks now
                // As we should wait until we get closer to do them!
                if (j > 15)
                {
                    // We should still calculate the full distance as needed!
                    if (!calculatePathDistance)
                    {
                        Plugin.LogDebug($"{NpcController.Npc.playerUsername}: Reached corner 15, stopping checks now");
                        return (false, true, pathDistance);
                    }
                    continue;
                }

                // Give the main thread a chance to think
                if (j % 10 == 0)
                {
                    await Task.Yield();
                }

                // We already know that the current path is dangerous, no
                // need to do the checks again!
                if (isPathDangerous)
                {
                    if (calculatePathDistance)
                        continue; // Keep looping to get the full path distance
                    else
                        break; // End it here, no point on continuing! BTW, THIS SHOULD NEVER HAPPEN!!!!!
                }

                // Check if the path may be exposed to enemies!
                if (checkForEnemies)
                {
                    // After 8 interations, skip redudant LOS checks
                    if (!skipLOSCheckThisSegment && j > 8 && tempDistance < 2f)
                    {
                        skipLOSCheckThisSegment = true;
                        Plugin.LogDebug($"Skipping redundant LOS checks at segment {j} due to proximity and small distance.");
                    }
                    else
                    {
                        skipLOSCheckThisSegment = false;
                    }

                    // Call our asynchronous enemy check!
                    if (!skipLOSCheckThisSegment && await IsEnemyDangerousAtSegment(previousNode, nodePos, headOffset, useEyePosition, token))
                    {
                        Plugin.LogDebug($"Danger detected at segment {j} from {previousNode} to {nodePos}. Path is dangerous!");
                        if (!calculatePathDistance)
                            return (true, true, pathDistance);
                        else
                            isPathDangerous = true;
                    }
                }

                // Check for if we walk into quicksand or water
                if (checkForQuicksand)
                {
                    var (isDangerous, updatedDrownTimer) = await CheckQuicksandDanger(previousNode, nodePos, headOffset, tempDistance, moveSpeed, predictedDrownTimer, token);
                    predictedDrownTimer = updatedDrownTimer; // Update the global drown timer!
                    if (isDangerous)
                    {
                        Plugin.LogDebug($"Danger detected due to quicksand or water at segment {j}. Path is dangerous!");
                        if (!calculatePathDistance)
                            return (true, true, pathDistance);
                        else
                            isPathDangerous = true;
                    }
                }
            }

            // Set the path distance on the bot itself!
            this.pathDistance = pathDistance; // Is there even a point at doing this?

            if (!isPathDangerous)
                Plugin.LogDebug("Path is safe. No danger detected.");

            return (isPathDangerous, true, pathDistance); // NOTE: Return the path distance here since it may be modifed by other pathfind calls!
        }

        /// <summary>
        /// Checks if the line segment is exposed to enemies.
        /// Was specificaly made for use in <see cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, bool, CancellationToken)"/>
        /// </summary>
        /// <param name="from">The previous point on the path</param>
        /// <param name="to">The point on the path we are moving to</param>
        /// <param name="headOffset">The distance the head of the player is off the ground</param>
        /// <param name="useEyePosition">Should we use the <see cref="EnemyAI"/>'s eye position or the current position for danger testing.</param>
        /// <param name="token">The cancelation token, this allows you to stop the function early!</param>
        /// <returns>true: if the segment is exposed, false: if the segment is safe!</returns>
        private async Task<bool> IsEnemyDangerousAtSegment(Vector3 from, Vector3 to, float headOffset, bool useEyePosition, CancellationToken token = default)
        {
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} is checking path segment from {from} to {to} for enemy exposure...");

            // T-Rizzle: I don't know what 262144 stands for,
            // all I know is that is what the default EnemyAI uses
            // NEEDTOVALIDATE: Why does this exist!? It seems to trigger randomly and cause issues with the bot!
            // Making normally safe paths dangerous!
            // Commenting this out for now, but I should look into this later!
            /*if (Physics.Linecast(from, to, 262144))
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername}: The path is blocked by line of sight.");
                return true;
            }*/

            // This CANNOT be a foreach loop since we are running over time!
            RoundManager instanceRM = RoundManager.Instance;
            Vector3 travelMidPoint = instanceRM.GetNavMeshPosition(Vector3.Lerp(from, to, 0.5f), instanceRM.navHit, 2.7f); // Make sure this is on the NavMesh!
            bool ourWeOutside = isOutside;
            string skipText = ourWeOutside ? "not outside" : "not inside";
            for (int i = 0; i < instanceRM.SpawnedEnemies.Count; i++)
            {
                // Check if we were canceled before checking the next enemy!
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                // Give the main thread a chance to think
                EnemyAI? enemy = instanceRM.SpawnedEnemies[i];
                if (i % 10 == 0)
                {
                    await Task.Yield();
                }

                if (enemy == null)
                {
                    Plugin.LogDebug($"Enemy At Index {i}: Skipped (null)");
                    continue;
                }

                string enemyName = enemy.enemyType != null ? enemy.enemyType.enemyName : "Unknown Enemy";
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} is checking {enemyName} for exposure...");
                if (enemy.isEnemyDead || ourWeOutside != enemy.isOutside) 
                {
                    Plugin.LogDebug($"{enemyName}: Skipped (dead or {skipText})");
                    continue; 
                }

                // Check if the target is a threat!
                float? dangerRange = GetFearRangeForEnemies(enemy, EnumFearQueryType.PathfindingAvoid);
                if (!dangerRange.HasValue)
                {
                    Plugin.LogDebug($"{enemyName}: Skipped (not a threat)");
                    continue;
                }

                // Fog reduce the visibility
                if (ourWeOutside && (enemy.enemyType == null || !enemy.enemyType.canSeeThroughFog) && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
                {
                    dangerRange = Mathf.Clamp(dangerRange.Value, 0, 30);
                }

                // Do the actual check!
                Vector3 enemyPos = enemy.transform.position;
                Vector3 closestPoint = instanceRM.GetNavMeshPosition(GetClosestPointOnLineSegment(from, to, enemyPos), instanceRM.navHit, 2.7f);
                if ((closestPoint - enemyPos).sqrMagnitude > dangerRange * dangerRange)
                {
                    Plugin.LogDebug($"{enemyName}: Skipped (outside danger range)");
                    continue;
                }

                // Check the closest point
                Vector3 viewPos = useEyePosition && enemy.eye != null ? enemy.eye.position : enemyPos;
                viewPos += Vector3.up * 0.3f;
                if (!Physics.Linecast(closestPoint + Vector3.up * headOffset, viewPos,
                    StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Plugin.LogDebug($"{enemyName}: Segment is exposed from closest {closestPoint} to view position!");
                    return true;
                }

                // We check the midpoint as well since this path may be out in the open,
                // and the midpoint may just be out of range!
                if (!Physics.Linecast(travelMidPoint + Vector3.up * headOffset, viewPos,
                    StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Plugin.LogDebug($"{enemyName}: Segment is exposed from midpoint {travelMidPoint} to view position!");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the line segment is dangerous.
        /// Was specificaly made for use in <see cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, bool, CancellationToken)"/>
        /// </summary>
        /// <param name="from">The previous point on the path</param>
        /// <param name="to">The point on the path we are moving to</param>
        /// <param name="headOffset">The distance the head of the player is off the ground</param>
        /// <param name="tempDistance">The distance between <paramref name="from"/> and <paramref name="to"/></param>
        /// <param name="moveSpeed">The momement speed of the player</param>
        /// <param name="predictedDrownTimer">The amount of time the player has left in the water</param>
        /// <param name="token">The cancelation token, this allows you to stop the function early!</param>
        /// <returns>Returns two objects. A bool that returns if we would drown or sink in quicksand and a float that is the remaining O2 the player has left from the original value given in <paramref name="predictedDrownTimer"/></returns>
        private async Task<(bool isDangerous, float updatedDownTimer)> CheckQuicksandDanger(Vector3 from, Vector3 to, float headOffset, float tempDistance, float moveSpeed, float predictedDrownTimer, CancellationToken token = default)
        {
            // Check to make sure that the quicksand array is not null or empty
            if (QuicksandArray == null || QuicksandArray.Length == 0)
            {
                if (predictedDrownTimer <= 0f)
                {
                    Plugin.LogDebug("Path would drown the bot! Marking path as dangerous!");
                    return (true, predictedDrownTimer);
                }

                return (false, predictedDrownTimer);
            }

            // Keep track if we went through water. If this part of the path is on land,
            // we need to simulate the air gain from being out of the water or the bot may mark a safe path as dangerous!
            bool pathGoesThroughWater = false;
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} is testing quicksand safety between previous node {from} and current node {to}");
            for (int i = 0; i < QuicksandArray.Length; i++)
            {
                // Check if we were canceled before pathfinding!
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                // Give the main thread a chance to think
                QuicksandTrigger? quicksand = QuicksandArray[i];
                if (i % 5 == 0)
                {
                    await Task.Yield();
                }

                if (quicksand == null || !quicksand.isActiveAndEnabled)
                    continue;

                Bounds quicksandBounds = default;
                bool foundCollider = false;
                Collider[] colliders = quicksand.gameObject.GetComponents<Collider>();
                for (int j = 0; j < colliders.Length; j++)
                {
                    Collider collider = colliders[j];
                    if (collider != null)
                    {
                        if (!foundCollider)
                        {
                            quicksandBounds = collider.bounds;
                            foundCollider = true;
                        }
                        else
                        {
                            quicksandBounds.Encapsulate(collider.bounds);
                        }
                    }
                }

                if (!foundCollider)
                {
                    continue;
                }

                float modifiedMoveSpeed = moveSpeed / (2f * (1f * quicksand.movementHinderance));
                Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(from, to, quicksandBounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);
                if (!quicksand.isWater)
                {
                    Plugin.LogDebug("This is quicksand!");
                    if (quicksandBounds.Contains(closestPoint))
                    {
                        Plugin.LogDebug("Segment intersects solid quicksand!");
                        return (true, predictedDrownTimer);
                    }
                }
                else
                {
                    Plugin.LogDebug("This is water!");

                    // For some reason this works really well like this unlike the code above
                    Vector3 simulatedHead = closestPoint + Vector3.up * headOffset;
                    if (quicksandBounds.Contains(simulatedHead))
                    {
                        // Test the amount of time we would spend underwater to get here
                        Plugin.LogDebug("Simulated head intersects water!");
                        float travelTime = tempDistance / modifiedMoveSpeed;

                        float downingDelta = travelTime / Const.LETHAL_BOT_DROWN_TIME; // Match game logic
                        predictedDrownTimer -= downingDelta;
                        Plugin.LogDebug($"Time left in water: {predictedDrownTimer:F2}");

                        pathGoesThroughWater = true;
                        if (predictedDrownTimer <= 0f)
                        {
                            Plugin.LogDebug("Path would drown the bot! Marking path as dangerous!");
                            return (true, predictedDrownTimer);
                        }
                    }
                }
            }

            // If the path doesn't go through water, we need to simulate the amount of
            // air we would gain back from being outside of the water.
            if (!pathGoesThroughWater)
            {
                // Match game logic
                float travelTime = tempDistance / moveSpeed;
                predictedDrownTimer = Mathf.Clamp(predictedDrownTimer + (travelTime / Const.LETHAL_BOT_DROWN_TIME), 0f, 1f);
            }

            return (false, predictedDrownTimer);
        }

        /// <summary>
        /// Calculates the closest point on a line segment defined by two points to a given target point.
        /// </summary>
        /// <param name="vLineA">The start point of the line segment.</param>
        /// <param name="vLineB">The end point of the line segment.</param>
        /// <param name="point">The point to find the closest point on the line segment to.</param>
        /// <returns>
        /// The point on the line segment between <paramref name="vLineA"/> and <paramref name="vLineB"/> 
        /// that is closest to <paramref name="point"/>.
        /// </returns>
        internal static Vector3 GetClosestPointOnLineSegment(Vector3 vLineA, Vector3 vLineB, Vector3 point)
        {
            // Check if we are at the same point
            if (vLineA == vLineB)
            {
                return vLineA; // or b, they are the same
            }
            
            float t = GetClosestPointOnLineSegmentT(vLineA, vLineB, point);
            return Vector3.Lerp(vLineA, vLineB, t);
        }

        /// <summary>
        /// Calculates the direction vector of a line segment and the normalized scalar <c>t</c> 
        /// representing how far along the segment the closest point to <paramref name="point"/> lies.
        /// </summary>
        /// <param name="vLineA">The start point of the line segment.</param>
        /// <param name="vLineB">The end point of the line segment.</param>
        /// <param name="point">The point to find the closest position to.</param>
        /// <returns>Output scalar in the range [0, 1] indicating the relative position of the closest point 
        /// along the line segment (0 = at <paramref name="vLineA"/>, 1 = at <paramref name="vLineB"/>).</returns>
        private static float GetClosestPointOnLineSegmentT(Vector3 vLineA, Vector3 vLineB, Vector3 point)
        {
            Vector3 vDir = vLineB - vLineA;
            float div = Vector3.Dot(vDir, vDir);
            if (div < 0.00001f)
            {
                return 0f; // they are the same
            }
            return Mathf.Clamp01(Vector3.Dot(point - vLineA, vDir) / div); // Old Code: (Vector3.Dot(vDir, point) - Vector3.Dot(vDir, vLineA));
        }

        /// <summary>
        /// Checks if the path is intersected by line of sight.
        /// </summary>
        /// <remarks>
        /// This function is originally from the <see cref="EnemyAI"/> class.<br/>
        /// The orignial function was modified to allow for the use of <see cref="EnemyAI"/> as a target rather than a <see cref="EnemyAI.targetPlayer"/>.<br/>
        /// Check the original function <see cref="EnemyAI.PathIsIntersectedByLineOfSight(Vector3, bool, bool, bool)"/> for more details on how this works.<br/>
        /// </remarks>
        /// <param name="targetPos">The position we are trying to path to</param>
        /// <param name="isPathVaild">A bool that represents if the current path is vaild. NOTE: This will be always be true if there is a vaild path, even if the path is visible to an enemy!</param>
        /// <param name="calculatePathDistance">If true, set <see cref="EnemyAI.pathDistance"/> to the length of the path. Is set to 0f on failure!</param>
        /// <param name="avoidLineOfSight">If true, the bot does LOS checks to see if <paramref name="checkLOSToTarget"/> can see any point on the path</param>
        /// <param name="checkLOSToTarget">The <see cref="EnemyAI"/> that we want to test path visibility for</param>
        /// <param name="useEnemyEyePos"></param>
        /// <returns>true: if there is no path or <paramref name="checkLOSToTarget"/> can see a point on the path. false: if there is a vaild path and <paramref name="checkLOSToTarget"/> can't see any point on the path</returns>
        public bool PathIsIntersectedByLineOfSight(Vector3 targetPos, out bool isPathVaild, bool calculatePathDistance = false, bool avoidLineOfSight = true, EnemyAI? checkLOSToTarget = null, bool useEnemyEyePos = true)
        {
            isPathVaild = true;
            pathDistance = 0f;
            if (agent.isOnNavMesh && !agent.CalculatePath(targetPos, path1))
            {
                if (DebugEnemy)
                {
                    Debug.Log("Path could not be calculated");
                }

                isPathVaild = false;
                return true;
            }

            if (DebugEnemy)
            {
                for (int i = 1; i < path1.corners.Length; i++)
                {
                    Debug.DrawLine(path1.corners[i - 1], path1.corners[i], Color.red);
                }
            }

            Vector3[]? corners = path1?.corners;
            if (corners == null || corners.Length == 0)
            {
                isPathVaild = false;
                return true;
            }

            if ((corners[corners.Length - 1] - RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)).sqrMagnitude > 1.5f * 1.5f)
            {
                if (DebugEnemy)
                {
                    Debug.Log($"Path is not complete; final waypoint of path was too far from target position: {targetPos}");
                }

                isPathVaild = false;
                return true;
            }

            if (calculatePathDistance || avoidLineOfSight)
            {
                bool flag = false;
                float headOffset = NpcController.Npc.gameplayCamera.transform.position.y - NpcController.Npc.transform.position.y;
                RoundManager instanceRM = RoundManager.Instance;
                Vector3 enemyPos = checkLOSToTarget != null ? checkLOSToTarget.transform.position : Vector3.zero;
                Vector3 viewPos = useEnemyEyePos && checkLOSToTarget != null && checkLOSToTarget.eye != null ? checkLOSToTarget.eye.position : enemyPos;
                viewPos += Vector3.up * 0.3f;
                for (int j = 1; j < corners.Length; j++)
                {
                    // We cache the corners we are using for quicker lookups
                    // also we always use the default distance function as we may be calculating path distance!
                    Vector3 previousNode = corners[j - 1];
                    Vector3 currentNode = corners[j];
                    float tempDistance = Vector3.Distance(previousNode, currentNode);

                    // Calculate the path distance as requested
                    if (calculatePathDistance)
                    {
                        pathDistance += tempDistance;
                    }

                    // If we reach corner 15, stop doing checks now
                    // As we should wait until we get closer to do them!
                    if (j > 15)
                    {
                        // We should still calculate the full distance as needed!
                        if (!calculatePathDistance)
                        {
                            Plugin.LogDebug($"{NpcController.Npc.playerUsername}: Reached corner 15, stopping checks now");
                            return false;
                        }
                        continue;
                    }

                    if (!flag && j > 8 && tempDistance < 2f)
                    {
                        if (DebugEnemy)
                        {
                            Debug.Log($"Distance between corners {j} and {j - 1} under 3 meters; skipping LOS check");
                            Debug.DrawRay(previousNode + Vector3.up * 0.2f, currentNode + Vector3.up * 0.2f, Color.magenta, 0.2f);
                        }

                        flag = true;
                        continue;
                    }

                    // Test the path's visibility to the target!
                    flag = false;
                    if (checkLOSToTarget != null)
                    {
                        // First, start with the closest point on the path
                        Vector3 closestPoint = instanceRM.GetNavMeshPosition(GetClosestPointOnLineSegment(previousNode, currentNode, enemyPos), instanceRM.navHit, 2.7f);
                        if (!Physics.Linecast(closestPoint + Vector3.up * headOffset, viewPos,
                            StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }

                        // Second, check the middle part of the path
                        Vector3 travelMidPoint = instanceRM.GetNavMeshPosition(Vector3.Lerp(previousNode, currentNode, 0.5f), instanceRM.navHit, 2.7f); // Make sure this is on the NavMesh!
                        if (!Physics.Linecast(travelMidPoint + Vector3.up * headOffset, viewPos,
                            StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }
                    }

                    /*if (avoidLineOfSight && Physics.Linecast(previousNode, currentNode, 262144))
                    {
                        if (DebugEnemy)
                        {
                            Debug.Log($"{enemyType.enemyName}: The path is blocked by line of sight at corner {j}");
                        }

                        return true;
                    }*/
                }
            }

            return false;
        }

        /// <summary>
        /// Enables or disables the bot's <see cref="EnemyAI.agent"/>
        /// </summary>
        /// <remarks>
        /// This calls <see cref="AIState.SetAreaCostsForBot"/> internally
        /// </remarks>
        /// <param name="enabled"></param>
        public void SetAgent(bool enabled)
        {
            if (agent != null && agent.enabled != enabled)
            {
                agent.enabled = enabled;
                if (enabled)
                {
                    State?.SetAreaCostsForBot();
                }
            }
        }

        /// <summary>
        /// This helper applies the preset area costs for the bots
        /// </summary>
        [Obsolete("This has been moved to AIState. Call this using the bot's current AIState instead.")]
        public void SetAreaCostsForBot()
        {
            State?.SetAreaCostsForBot();
        }

        /// <summary>
        /// Set the destination in <c>EnemyAI</c>, not on the agent
        /// </summary>
        /// <param name="position">the destination</param>
        public void SetDestinationToPositionLethalBotAI(Vector3 position)
        {
            moveTowardsDestination = true;
            movingTowardsTargetPlayer = false;

            if (previousWantedDestination != position)
            {
                previousWantedDestination = position;
                hasDestinationChanged = true;
                destination = position;
            }
        }

        /// <summary>
        /// Try to set the destination on the agent, if destination not reachable, try the closest possible position of the destination
        /// </summary>
        public void OrderMoveToDestination()
        {
            NpcController.OrderToMove();

            if (!hasDestinationChanged 
                && updateDestinationTimer.HasStarted()
                && !updateDestinationTimer.Elapsed())
            {
                return;
            }

            if (agent.isActiveAndEnabled
                && agent.isOnNavMesh
                && !this.IsUsingOffMeshLink()
                && !isEnemyDead
                && !NpcController.Npc.isPlayerDead)
            {
                // Check if we can path to the new destination!
                if (!this.IsValidPathToTarget(destination))
                {
                    try
                    {
                        // If we failed to find a path, pick the closest NavArea to our destination instead.
                        //destination = this.ChooseClosestNodeToPosition(destination, avoidLineOfSight).position;
                        destination = RoundManager.Instance.GetNavMeshPosition(destination, default, 2.7f);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogDebug($"{NpcController.Npc.playerUsername} GetNavMeshPosition error : {e.Message} , InnerException : {e.InnerException}");
                    }
                }
                this.SetDestinationToPosition(destination);
                agent.SetDestination(destination);
                updateDestinationTimer.Start(1f); // One second cooldown!
                hasDestinationChanged = false;
            }
        }

        public void StopMoving()
        {
            if (NpcController.HasToMove)
            {
                // HACKHACK: Done on purpose to fix a potential issue where the bot walks in place!
                // This makes the bot repath next call to OrderMoveToDestination!
                hasDestinationChanged = true;
                NpcController.OrderToStopMoving();
            }
        }

        /// <summary>
        /// Is the current client running the code is the owner of the <c>LethalBotAI</c> ?
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsClientOwnerOfLethalBot()
        {
            return this.OwnerClientId == GameNetworkManager.Instance.localPlayerController.actualClientId;
        }

        public void InitStateToSearchingNoTarget(bool isInverseTeleport = false)
        {
            // Don't change states while dead!
            if (isEnemyDead
                || NpcController.Npc.isPlayerDead)
            {
                return;
            }

            // We were teleported by the inverse teleporter,
            // we should be looking for scrap now!
            if (isInverseTeleport)
            {
                State = new SearchingForScrapState(this);
            }
            else
            {
                // We got teleported back to the ship,
                // we chill at the ship for a bit.
                State = new ChillAtShipState(this); // NEEDTOVALIDATE: Should this be a return to ship instead?
            }
            this.targetPlayer = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int MaxHealthPercent(int percentage)
        {
            return LethalBotManager.MaxHealthPercent(percentage, MaxHealth);
        }

        public void CheckAndBringCloserTeleportLethalBot(float percentageOfDestination)
        {
            bool isAPlayerSeeingLethalBot = false;
            StartOfRound instanceSOR = StartOfRound.Instance;
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;
            PlayerControllerB player;
            Vector3 vectorPlayerToLethalBot;
            Vector3 lethalBotDestination = NpcController.Npc.thisPlayerBody.transform.position + ((this.destination - NpcController.Npc.transform.position) * percentageOfDestination);
            Vector3 lethalBodyBodyDestination = lethalBotDestination + new Vector3(0, 1f, 0);
            for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
            {
                player = instanceSOR.allPlayerScripts[i];
                if (player.isPlayerDead
                    || !player.isPlayerControlled
                    || LethalBotManager.Instance.IsPlayerLethalBot(player))
                {
                    continue;
                }

                // No obsruction
                if (!Physics.Linecast(player.gameplayCamera.transform.position, thisLethalBotCamera.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    vectorPlayerToLethalBot = thisLethalBotCamera.position - player.gameplayCamera.transform.position;
                    if (Vector3.Angle(player.gameplayCamera.transform.forward, vectorPlayerToLethalBot) < player.gameplayCamera.fieldOfView)
                    {
                        isAPlayerSeeingLethalBot = true;
                        break;
                    }
                }

                if (!Physics.Linecast(player.gameplayCamera.transform.position, lethalBodyBodyDestination, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    vectorPlayerToLethalBot = lethalBodyBodyDestination - player.gameplayCamera.transform.position;
                    if (Vector3.Angle(player.gameplayCamera.transform.forward, vectorPlayerToLethalBot) < player.gameplayCamera.fieldOfView)
                    {
                        isAPlayerSeeingLethalBot = true;
                        break;
                    }
                }
            }

            if (!isAPlayerSeeingLethalBot)
            {
                TeleportLethalBot(lethalBotDestination);
            }
        }

        /// <summary>
        /// Check the line of sight if the lethalBot can see the target player
        /// </summary>
        /// <param name="width">FOV of the lethalBot</param>
        /// <param name="range">Distance max for seeing something</param>
        /// <param name="proximityAwareness">Distance where the lethal bots "sense" the player, in line of sight or not. -1 for no proximity awareness</param>
        /// <returns>Target player <c>PlayerControllerB</c> or null</returns>
        public PlayerControllerB? CheckLOSForTarget(float width = 45f, int range = 60, int proximityAwareness = -1)
        {
            if (targetPlayer == null)
            {
                return null;
            }

            if (!PlayerIsTargetable(targetPlayer))
            {
                return null;
            }

            // Fog reduce the visibility
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }

            // Check for target player
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;
            Vector3 posTargetCamera = targetPlayer.gameplayCamera.transform.position;
            if (Vector3.Distance(posTargetCamera, thisLethalBotCamera.position) < (float)range
                && !Physics.Linecast(thisLethalBotCamera.position, posTargetCamera, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                // Target close enough and nothing in between to break line of sight 
                Vector3 to = posTargetCamera - thisLethalBotCamera.position;
                if (Vector3.Angle(thisLethalBotCamera.forward, to) < width
                    || (proximityAwareness != -1 && (thisLethalBotCamera.position - posTargetCamera).sqrMagnitude < (float)proximityAwareness * (float)proximityAwareness))
                {
                    // Target in FOV or proximity awareness range
                    return targetPlayer;
                }
            }

            return null;
        }

        /// <summary>
        /// Check the line of sight if the lethalBot see another lethalBot who see the same target player.
        /// </summary>
        /// <param name="width">FOV of the lethalBot</param>
        /// <param name="range">Distance max for seeing something</param>
        /// <param name="proximityAwareness">Distance where the lethal bots "sense" the player, in line of sight or not. -1 for no proximity awareness</param>
        /// <returns>Target player <c>PlayerControllerB</c> or null</returns>
        public PlayerControllerB? CheckLOSForLethalBotHavingTargetInLOS(float width = 45f, int range = 60, int proximityAwareness = -1)
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;

            // Check for any lethal bots that has target still in LOS
            foreach (PlayerControllerB lethalBot in instanceSOR.allPlayerScripts)
            {
                if (lethalBot.playerClientId == this.NpcController.Npc.playerClientId
                    || lethalBot.isPlayerDead
                    || !lethalBot.isPlayerControlled)
                {
                    continue;
                }

                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(lethalBot);
                if (lethalBotAI == null
                    || lethalBotAI.targetPlayer == null
                    || lethalBotAI.State.GetAIState() == EnumAIStates.JustLostPlayer)
                {
                    continue;
                }

                // Check for target player
                Vector3 posLethalBotCamera = lethalBot.gameplayCamera.transform.position;
                if (Vector3.Distance(posLethalBotCamera, thisLethalBotCamera.position) < (float)range
                    && !Physics.Linecast(thisLethalBotCamera.position, posLethalBotCamera, instanceSOR.collidersAndRoomMaskAndDefault))
                {
                    // Target close enough and nothing in between to break line of sight 
                    Vector3 to = posLethalBotCamera - thisLethalBotCamera.position;
                    if (Vector3.Angle(thisLethalBotCamera.forward, to) < width
                        || (proximityAwareness != -1 && (thisLethalBotCamera.position - posLethalBotCamera).sqrMagnitude < (float)proximityAwareness * (float)proximityAwareness))
                    {
                        // Target in FOV or proximity awareness range
                        if (lethalBotAI.targetPlayer == targetPlayer)
                        {
                            Plugin.LogDebug($"{this.NpcController.Npc.playerClientId} Found lethalBot {lethalBot.playerUsername} who knows target {targetPlayer.playerUsername}");
                            return targetPlayer;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Check the line of sight if the lethalBot can see any player and take the closest.
        /// </summary>
        /// <param name="width">FOV of the lethalBot</param>
        /// <param name="range">Distance max for seeing something</param>
        /// <param name="proximityAwareness">Distance where the lethal bots "sense" the player, in line of sight or not. -1 for no proximity awareness</param>
        /// <param name="bufferDistance"></param>
        /// <returns>Target player <c>PlayerControllerB</c> or null</returns>
        public PlayerControllerB? CheckLOSForClosestPlayer(float width = 45f, int range = 60, int proximityAwareness = -1, float bufferDistance = 0f)
        {
            // Fog reduce the visibility
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }

            List<PlayerControllerB> groupMembers = GroupManager.Instance.GetOtherGroupMembers(NpcController.Npc);
            StartOfRound instanceSOR = StartOfRound.Instance;
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;
            float currentClosestDistance = 1000f;
            int indexPlayer = -1;
            for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = instanceSOR.allPlayerScripts[i];

                if (!player.isPlayerControlled 
                    || player.isPlayerDead 
                    || (!groupMembers.Contains(player) 
                        && LethalBotManager.Instance.IsPlayerLethalBot(player)))
                {
                    continue;
                }

                // Target close enough ?
                Vector3 cameraPlayerPosition = player.gameplayCamera.transform.position;
                if ((cameraPlayerPosition - this.transform.position).sqrMagnitude > range * range)
                {
                    continue;
                }

                if (!PlayerIsTargetable(player))
                {
                    continue;
                }

                // Nothing in between to break line of sight ?
                if (Physics.Linecast(thisLethalBotCamera.position, cameraPlayerPosition, instanceSOR.collidersAndRoomMaskAndDefault))
                {
                    continue;
                }

                Vector3 vectorLethalBotToPlayer = cameraPlayerPosition - thisLethalBotCamera.position;
                float distanceLethalBotToPlayer = vectorLethalBotToPlayer.magnitude;
                if ((Vector3.Angle(thisLethalBotCamera.forward, vectorLethalBotToPlayer) < width || (proximityAwareness != -1 && distanceLethalBotToPlayer < (float)proximityAwareness))
                    && distanceLethalBotToPlayer < currentClosestDistance)
                {
                    // Target in FOV or proximity awareness range
                    currentClosestDistance = distanceLethalBotToPlayer;
                    indexPlayer = i;
                }
            }

            if (targetPlayer != null
                && indexPlayer != -1
                && targetPlayer != instanceSOR.allPlayerScripts[indexPlayer]
                && bufferDistance > 0f
                && Mathf.Abs(currentClosestDistance - Vector3.Distance(base.transform.position, targetPlayer.transform.position)) < bufferDistance)
            {
                return null;
            }

            if (indexPlayer < 0)
            {
                return null;
            }

            mostOptimalDistance = currentClosestDistance;
            return instanceSOR.allPlayerScripts[indexPlayer];
        }

        /// <summary>
        /// Check if enemy in line of sight.
        /// </summary>
        /// <param name="width">FOV of the lethalBot</param>
        /// <param name="range">Distance max for seeing something</param>
        /// <param name="proximityAwareness">Distance where the lethal bots "sense" the player, in line of sight or not. -1 for no proximity awareness</param>
        /// <returns>Enemy <c>EnemyAI</c> or null</returns>
        public EnemyAI? CheckLOSForEnemy(float width = 45f, int range = 20, int proximityAwareness = -1)
        {
            // Fog reduce the visibility
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }

            StartOfRound instanceSOR = StartOfRound.Instance;
            RoundManager instanceRM = RoundManager.Instance;
            Bounds insideShipBounds = instanceSOR.shipInnerRoomBounds.bounds;
            PlayerControllerB lethalBotController = NpcController.Npc;
            Transform thisLethalBotCamera = lethalBotController.gameplayCamera.transform;
            EnemyAI? closestEnemy = null;
            float closestEnemyDistSqr = float.MaxValue;
            List<EnemyAI> spawnedEnemies = instanceRM.SpawnedEnemies;
            for(int i = 0; i < spawnedEnemies.Count; i++)
            {
                EnemyAI spawnedEnemy = spawnedEnemies[i];
                if (spawnedEnemy == null || spawnedEnemy.isEnemyDead)
                {
                    continue;
                }

                // Enemy close enough ?
                Vector3 positionEnemy = spawnedEnemy.transform.position;
                Vector3 directionEnemyFromCamera = positionEnemy - thisLethalBotCamera.position;
                float sqrDistanceToEnemy = directionEnemyFromCamera.sqrMagnitude;
                if (sqrDistanceToEnemy > range * range)
                {
                    continue;
                }

                // Can they reach us?
                if (!spawnedEnemy.isInsidePlayerShip
                    && lethalBotController.isInHangarShipRoom
                    && !insideShipBounds.Contains(positionEnemy))
                {
                    continue;
                }

                // Fear range
                float? fearRange = GetFearRangeForEnemies(spawnedEnemy);
                if (!fearRange.HasValue
                    || sqrDistanceToEnemy > fearRange * fearRange)
                {
                    continue;
                }

                // Obstructed
                Vector3 viewPos = spawnedEnemy.eye != null ? spawnedEnemy.eye.position : positionEnemy;
                if (Physics.Linecast(thisLethalBotCamera.position, viewPos, instanceSOR.collidersAndRoomMaskAndDefault))
                {
                    continue;
                }
                // Enemy in distance of fear range

                // Proximity awareness, danger
                if (proximityAwareness > -1
                    && sqrDistanceToEnemy < (float)proximityAwareness * (float)proximityAwareness)
                {
                    Plugin.LogDebug($"{lethalBotController.playerUsername} DANGER CLOSE \"{spawnedEnemy.enemyType.enemyName}\" {spawnedEnemy.enemyType.name}");
                    if (spawnedEnemy is MaskedPlayerEnemy masked)
                    {
                        DictKnownMasked[masked] = true;
                    }

                    // Only update closest enemy, if they are actually closer
                    if (sqrDistanceToEnemy < closestEnemyDistSqr)
                    {
                        closestEnemy = spawnedEnemy;
                        closestEnemyDistSqr = sqrDistanceToEnemy;
                    }
                }
                // Line of Sight, danger
                else if (Vector3.Angle(thisLethalBotCamera.forward, directionEnemyFromCamera) < width)
                {
                    Plugin.LogDebug($"{lethalBotController.playerUsername} DANGER LOS \"{spawnedEnemy.enemyType.enemyName}\" {spawnedEnemy.enemyType.name}");
                    if (spawnedEnemy is MaskedPlayerEnemy masked)
                    {
                        DictKnownMasked[masked] = true;
                    }

                    // Only update closest enemy, if they are actually closer
                    if (sqrDistanceToEnemy < closestEnemyDistSqr)
                    {
                        closestEnemy = spawnedEnemy;
                        closestEnemyDistSqr = sqrDistanceToEnemy;
                    }
                }
            }

            return closestEnemy;
        }

        /// <summary>
        /// Attempts to find a visible bent line-of-sight path around an obstacle toward the target.
        /// </summary>
        /// <param name="origin">The eye or start position.</param>
        /// <param name="target">The final position the bot wants to see or reach.</param>
        /// <param name="angleLimit">Maximum angle to bend in degrees (default 135).</param>
        /// <param name="bendStepSize">Distance in units to step during each check.</param>
        /// <param name="hitMask">LayerMask to test visibility against.</param>
        /// <param name="bentPoint">The point from which the bot has visibility to the target.</param>
        /// <returns>True if a bent line of sight was found, false otherwise.</returns>
        /*public static bool TryBendLineOfSight(Vector3 origin, Vector3 target, out Vector3 bentPoint, float angleLimit = 135f, float bendStepSize = 0.5f, LayerMask? hitMask = null)
        {
            bentPoint = Vector3.zero;
            LayerMask mask = hitMask ?? StartOfRound.Instance.collidersAndRoomMaskAndDefault;

            // Direct line of sight
            if (!Physics.Linecast(origin, target, mask))
            {
                bentPoint = target;
                return true;
            }

            Vector3 toTarget = target - origin;
            float startAngle = VecToYaw(toTarget);  // FIXED
            float distance = new Vector2(toTarget.x, toTarget.z).magnitude;
            toTarget.Normalize();

            float[] priorVisibleLength = { 0f, 0f };

            float angleStep = 5f;
            for (float angle = angleStep; angle <= angleLimit; angle += angleStep)
            {
                for (int side = 0; side < 2; side++)
                {
                    float actualAngle = side == 1 ? startAngle + angle : startAngle - angle;

                    float dx = Mathf.Cos(actualAngle * Mathf.Deg2Rad);
                    float dz = Mathf.Sin(actualAngle * Mathf.Deg2Rad);

                    Vector3 rotPoint = new Vector3(origin.x + distance * dx, origin.y, origin.z + distance * dz);

                    Vector3 ray = rotPoint - origin;
                    float rayLength = ray.magnitude;
                    ray.Normalize();
                    float visibleLength;
                    if (Physics.Linecast(origin, rotPoint, out RaycastHit bendHit, mask))
                    {
                        if (bendHit.collider != null && bendHit.distance > 0f)
                        {
                            visibleLength = bendHit.distance;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        visibleLength = rayLength;
                    }

                    for (float bendLength = priorVisibleLength[side]; bendLength < visibleLength; bendLength += bendStepSize)
                    {
                        Vector3 midPoint = origin + ray * bendLength;

                        // Final visibility test to target
                        if (!Physics.Linecast(midPoint, target, mask))
                        {
                            bentPoint = midPoint;
                            return true;
                        }
                    }

                    priorVisibleLength[side] = visibleLength;
                }
            }

            return false;
        }


        private static float VecToYaw(Vector3 vec)
        {
            if (vec.x == 0f && vec.z == 0f)
                return 0f;

            float yaw = Mathf.Atan2(vec.x, vec.z) * Mathf.Rad2Deg;

            if (yaw < 0f)
                yaw += 360f;

            return yaw;
        }*/


        /// <summary>
        /// Checks if we can be seen by an enemy
        /// </summary>
        /// <remarks>
        /// This only updates every <see cref="Const.TIMER_CHECK_EXPOSED"/> when called, this is done as an optimization.
        /// This will return a cached value inbetween updates
        /// </remarks>
        /// <param name="bypassCooldown">If set to true, this forces an update! This is great for if you teleport the bot!</param>
        /// <returns>true: if an enemy can see us, false: if no enemy can see us</returns>
        public bool AreWeExposed(bool bypassCooldown = false)
        {
            if (!bypassCooldown && !areWeExposed.CanUpdate())
            {
                return areWeExposed;
            }

            Vector3 ourPos = NpcController.Npc.transform.position;
            float headOffset = NpcController.Npc.gameplayCamera.transform.position.y - ourPos.y;
            Vector3 headPos = ourPos + Vector3.up * headOffset;
            RoundManager instanceRM = RoundManager.Instance;
            List<EnemyAI> spawnedEnemies = instanceRM.SpawnedEnemies;
            for (int i = 0; i < spawnedEnemies.Count; i++)
            {
                EnemyAI checkLOSToTarget = spawnedEnemies[i];
                if (checkLOSToTarget == null 
                    || checkLOSToTarget.isEnemyDead 
                    || this.isOutside != checkLOSToTarget.isOutside)
                {
                    continue;
                }

                // Check if the target is a threat!
                float? dangerRange = GetFearRangeForEnemies(checkLOSToTarget, EnumFearQueryType.PathfindingAvoid);
                if (dangerRange.HasValue)
                {
                    // Fog reduce the visibility
                    if (isOutside && !checkLOSToTarget.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
                    {
                        dangerRange = Mathf.Clamp(dangerRange.Value, 0, 30);
                    }

                    Vector3 enemyPos = checkLOSToTarget.transform.position;
                    if ((enemyPos - headPos).sqrMagnitude <= dangerRange * dangerRange)
                    {
                        // Do the actual traceline check
                        Vector3 viewPos = checkLOSToTarget.eye != null ? checkLOSToTarget.eye.position : enemyPos;
                        if (!Physics.Linecast(viewPos + Vector3.up * 0.25f, headPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            areWeExposed.Value = true;
                            return true;
                        }
                    }
                }
            }
            areWeExposed.Value = false;
            return false;
        }

        /// <summary>
        /// Check for, an enemy, the minimal distance from enemy to lethalBot before they will panik.
        /// </summary>
        /// <param name="enemy">Enemy to check</param>
        /// <param name="queryType"></param>
        /// <returns>The minimal distance from enemy to lethalBot before panicking, null if nothing to worry about</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float? GetFearRangeForEnemies(EnemyAI enemy, EnumFearQueryType queryType = EnumFearQueryType.BotPanic)
        {
            //Plugin.LogDebug($"enemy \"{enemy.enemyType.enemyName}\" {enemy.enemyType.name}");
            return LethalBotManager.GetFearRangeForEnemy(new LethalBotFearQuery(this, enemy, queryType));
        }

        /// <summary>
        /// Check for, an enemy, the minimal distance from enemy to the target player 
        /// before the bot will consider teleporting them.
        /// </summary>
        /// <param name="enemy">Enemy to check</param>
        /// <param name="playerToCheck">Player to check</param>
        /// <returns>The minimal distance from enemy to player before teleporting them, null if nothing to worry about</returns>
        public float? GetFearRangeForEnemies(EnemyAI enemy, PlayerControllerB? playerToCheck)
        {
            //Plugin.LogDebug($"enemy \"{enemy.enemyType.enemyName}\" {enemy.enemyType.name}");
            if (playerToCheck == null)
            {
                return null;
            }
            return LethalBotManager.GetFearRangeForEnemy(new LethalBotFearQuery(this, enemy, playerToCheck, EnumFearQueryType.PlayerTeleport));
        }

        /// <summary>
        /// Check for, an enemy, the minimal distance from enemy to lethalBot before based on the given fear query.
        /// </summary>
        /// <param name="fearQuery">Fear query to check</param>
        /// <returns>The minimal distance from enemy based on the given fear query, null if nothing to worry about</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Obsolete("This function is just a glorified call to LethalBotManager.GetFearRangeForEnemy. Use that instead!")]
        public float? GetFearRangeForEnemies(in LethalBotFearQuery fearQuery)
        {
            //Plugin.LogDebug($"enemy \"{enemy.enemyType.enemyName}\" {enemy.enemyType.name}");
            return LethalBotManager.GetFearRangeForEnemy(fearQuery);
        }

        /// <summary>
        /// Returns true if the given EnemyAI can be killed!
        /// </summary>
        /// <inheritdoc cref="LethalBotAI.CanEnemyBeKilled(EnemyAI, bool, bool, bool)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Obsolete("This has been moved into ShouldAttackEnemy. Use that instead!")]
        public bool CanEnemyBeKilled(EnemyAI enemy, bool isMissionController = false)
        {
            return ShouldAttackEnemy(enemy, isMissionController);
        }

        /// <summary>
        /// Returns true if the given EnemyAI can be killed!
        /// </summary>
        /// <remarks>
        /// <para>NOTE: This is a switch statement and doesn't work with custom enemies!</para>
        /// TODO: Move this into a Query system like the fear ranges!
        /// </remarks>
        /// <param name="enemy"></param>
        /// <param name="hasRangedWeapon"></param>
        /// <param name="isHumanPlayer"></param>
        /// <param name="isMissionController"></param>
        /// <returns>Can the enemy be killed?</returns>
        [Obsolete("This has been replaced by ShouldAttackEnemy. Use that instead!", true)]
        public static bool CanEnemyBeKilled(EnemyAI enemy, bool hasRangedWeapon = false, bool isHumanPlayer = false, bool isMissionController = false)
        {
            // If you turn this on.....just know what you are getting yourself into......
            // After all, the bots can't tell if you are outmatched here...........
            if (Plugin.Config.ShouldKillEverything)
            {
                return true;
            }

            // FIXME: Only a few enemies can be targeted since
            // I need to check when its a good idea to fight!
            bool isEnemyStunned = enemy.stunnedIndefinitely > 0f || enemy.stunNormalizedTimer > 0f;
            if (enemy is CentipedeAI 
                || enemy is MaskedPlayerEnemy 
                || enemy is CrawlerAI
                || enemy is HoarderBugAI
                || enemy is BaboonBirdAI)
            {
                return true;
            }
            else if (enemy is NutcrackerEnemyAI nutcracker 
                && (hasRangedWeapon || isHumanPlayer || isEnemyStunned)
                        && (enemy.currentBehaviourStateIndex == 2
                            || nutcracker.isInspecting))
            {
                return true;
            }
            else if (enemy is FlowermanAI 
                || enemy is SandSpiderAI)
            {
                return hasRangedWeapon || isHumanPlayer || isEnemyStunned;
            }
            else if (enemy is ButlerEnemyAI 
                || enemy is MouthDogAI
                || enemy is CaveDwellerAI)
            {
                return isHumanPlayer;
            }
            else if (enemy is BushWolfEnemy bushWolf)
            {
                if (bushWolf.draggingPlayer != null)
                {
                    return true; // We need to save a player, ATTACK!
                }
                return hasRangedWeapon || isHumanPlayer || isEnemyStunned || enemy.isInsidePlayerShip;
            }
            else if (enemy is PumaAI || enemy is CadaverBloomAI)
            {
                // The mission controller bot should only protect the ship, not give chase to the fieopar or cadaver!
                return !isMissionController || enemy.isInsidePlayerShip;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the given <paramref name="enemy"/> can be killed!
        /// </summary>
        /// <remarks>
        /// This prefills some information about the bot.<br/>
        /// If you want control over what is in the <see cref="LethalBotAttackQuery"/>,
        /// you should use <see cref="LethalBotManager.ShouldAttackEnemy(in LethalBotAttackQuery)"/> instead.
        /// </remarks>
        /// <returns>Can the enemy be killed?</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldAttackEnemy(Object enemy, bool isMissionController = false)
        {
            return LethalBotManager.ShouldAttackEnemy(new LethalBotAttackQuery(this, enemy, hasRangedWeapon: HasRangedWeapon(), isHumanPlayer: false, isMissionController: isMissionController));
        }

        /// <summary>
        /// Checks if there is an eyeless dog nearby, 
        /// the bot will use this to determine if the should crouch or not
        /// </summary>
        /// <remarks>
        /// This only updates every <see cref="Const.TIMER_CHECK_EXPOSED"/> when called, this is done as an optimization.
        /// This will return a cached value inbetween updates
        /// </remarks>
        /// <param name="bypassCooldown">If set to true, this forces an update! This is great for if you teleport the bot!</param>
        /// <returns>true: there is an eyeless dog nearby, false: no eyeless dog nearby</returns>
        public bool CheckProximityForEyelessDogs(bool bypassCooldown = false)
        {
            if (!bypassCooldown && !isEyelessDogInPromimity.CanUpdate())
            {
                return isEyelessDogInPromimity;
            }

            RoundManager instanceRM = RoundManager.Instance;
            Vector3 ourPos = NpcController.Npc.transform.position;
            List<EnemyAI> spawnedEnemies = instanceRM.SpawnedEnemies;
            for (int i = 0; i < spawnedEnemies.Count; i++)
            {
                EnemyAI spawnedEnemy = spawnedEnemies[i];
                if (spawnedEnemy != null && !spawnedEnemy.isEnemyDead && spawnedEnemy is MouthDogAI)
                {
                    // NOTE: We don't use GetFearRangeForEnemies since
                    // we don't want to trigger the dog in the first place
                    const float fearRange = 30f; // NOTE: 22f is the footstep range when running!
                    if ((spawnedEnemy.transform.position - ourPos).sqrMagnitude < fearRange * fearRange)
                    {
                        isEyelessDogInPromimity.Value = true;
                        return true;
                    }
                }
            }
            isEyelessDogInPromimity.Value = false;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReParentLethalBot(Transform newParent)
        {
            NpcController.ReParentNotSpawnedTransform(newParent);
        }

        /// <summary>
        /// Is the target player in the vehicle cruiser
        /// </summary>
        /// <returns></returns>
        public VehicleController? GetVehicleCruiserTargetPlayerIsIn()
        {
            if (targetPlayer == null
                || targetPlayer.isPlayerDead)
            {
                return null;
            }

            VehicleController? vehicleController = LethalBotManager.Instance.VehicleController;
            if (vehicleController == null)
            {
                return null;
            }

            if (this.targetPlayer.inVehicleAnimation)
            {
                return vehicleController;
            }

            return null;
        }

        //TODO: Check if we still want this!
        public string GetSizedBillboardStateIndicator()
        {
            string indicator;
            int sizePercentage = Math.Clamp((int)(100f + 2.5f * (StartOfRound.Instance.localPlayerController.transform.position - NpcController.Npc.transform.position).sqrMagnitude),
                                 100, 800);

            if (IsOwner)
            {
                indicator = State == null ? string.Empty : State.GetBillboardStateIndicator();
            }
            else
            {
                indicator = stateIndicatorServer;
            }

            return $"<size={sizePercentage}%>{indicator}</size>";
        }

        internal static ShipTeleporter? FindTeleporter(bool inverseTeleporter = false)
        {
            ShipTeleporter[] shipTeleporters = Object.FindObjectsByType<ShipTeleporter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < shipTeleporters.Length; i++)
            {
                var teleporter = shipTeleporters[i];
                if (teleporter == null)
                {
                    continue;
                }

                if (teleporter.isInverseTeleporter == inverseTeleporter)
                {
                    return teleporter;
                }
            }
            return null;
        }

        /// <summary>
        /// Helper function to find the target item, using the given function, on the ship!
        /// </summary>
        /// <param name="filter">The filter to use</param>
        /// <returns>The found item that is on the ship and fufills the given <paramref name="filter"/></returns>
        public GrabbableObject? FindItemOnShip(Func<GrabbableObject, bool> filter)
        {
            // Lets check the ship for our target item!
            Vector3 ourPos = NpcController.Npc.transform.position;
            GrabbableObject? closestFoundItem = null;
            float closestFoundItemSqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GrabbableObject? foundItem = LethalBotManager.grabbableObjectsInMap[i];
                if (foundItem != null
                    && foundItem.isInShipRoom
                    && filter(foundItem))
                {
                    float foundItemSqr = (foundItem.transform.position - ourPos).sqrMagnitude;
                    if (foundItemSqr < closestFoundItemSqr
                        && IsGrabbableObjectGrabbable(foundItem)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestFoundItemSqr = foundItemSqr;
                        closestFoundItem = foundItem;
                    }
                }
            }
            return closestFoundItem;
        }

        /// <summary>
        /// Search for all the loaded ladders on the map.
        /// </summary>
        /// <returns>Array of <c>InteractTrigger</c> (ladders)</returns>
        private InteractTrigger[] RefreshLaddersList()
        {
            List<InteractTrigger> ladders = new List<InteractTrigger>();
            InteractTrigger[] interactsTrigger = Resources.FindObjectsOfTypeAll<InteractTrigger>();
            foreach (var ladder in interactsTrigger)
            {
                if (ladder == null)
                {
                    continue;
                }

                if (ladder.isLadder && ladder.ladderHorizontalPosition != null)
                {
                    ladders.Add(ladder);
                }
            }
            return ladders.ToArray();
        }

        /// <summary>
        /// Check every ladder to see if the body of lethalBot is close to either the bottom of the ladder (wants to go up) or the top of the ladder (wants to go down).
        /// Orders the controller to set field <c>hasToGoDown</c>.
        /// </summary>
        /// <remarks>
        /// FIXME: This should use the bot's current path to determine when to climb or not!
        /// </remarks>
        /// <returns>The ladder to use, null if nothing close</returns>
        public InteractTrigger? GetLadderIfWantsToUseLadder()
        {
            OffMeshLinkData offMeshLinkData = agent.currentOffMeshLinkData;
            if (!offMeshLinkData.valid 
                || this.offMeshLinkCoroutine != null)
            {
                return null;
            }

            Vector3 ourPos = NpcController.Npc.transform.position;
            Vector3 linkStartPos = offMeshLinkData.startPos;
            Vector3 linkEndPos = offMeshLinkData.endPos;
            Vector3 closestLinkPos;
            if ((linkStartPos - ourPos).sqrMagnitude < (linkEndPos - ourPos).sqrMagnitude)
            {
                closestLinkPos = linkStartPos;
            }
            else
            {
                closestLinkPos = linkEndPos;
            }

            InteractTrigger? closestLadder = null;
            float closestLadderDistSqr = Const.DISTANCE_NPCBODY_FROM_LADDER * Const.DISTANCE_NPCBODY_FROM_LADDER;
            for (int i = 0; i < laddersInteractTrigger.Length; i++)
            {
                InteractTrigger ladder = laddersInteractTrigger[i];
                if (ladder == null || !ladder.interactable) continue;

                // Setup important local variables
                Vector3 ladderBottomPos = ladder.bottomOfLadderPosition.position;
                Vector3 ladderTopPos = ladder.topOfLadderPosition.position;
                float ladderDistSqrToBottom = (ladderBottomPos - closestLinkPos).sqrMagnitude;
                float ladderDistSqrToTop = (ladderTopPos - closestLinkPos).sqrMagnitude;

                // Find the closest part of the ladder to us!
                float bestLadderDistSqr;
                bool climbUp;
                if (ladderDistSqrToBottom < ladderDistSqrToTop)
                {
                    bestLadderDistSqr = ladderDistSqrToBottom;
                    climbUp = true;
                }
                else
                {
                    bestLadderDistSqr = ladderDistSqrToTop;
                    climbUp = false;
                }

                // Check if this is the closest ladder
                if (bestLadderDistSqr < closestLadderDistSqr)
                {
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} Path wants to climb {(climbUp ? "UP" : "DOWN")} ladder");
                    NpcController.OrderToGoUpDownLadder(hasToGoDown: !climbUp);
                    closestLadderDistSqr = bestLadderDistSqr;
                    closestLadder = ladder;
                }
            }
            return closestLadder;
        }

        /// <summary>
        /// Is the entrance (main or fire exit) is close for the two entity position in parameters ?
        /// </summary>
        /// <remarks>
        /// Use to know if the player just used the entrance and teleported away,
        /// the lethalBot gets close to last seen position in front of the door, we check if lethalBot is close
        /// to the door and the last seen position too.
        /// </remarks>
        /// <param name="entityPos1">Position of entity 1</param>
        /// <param name="entityPos2">Position of entity 1</param>
        /// <returns>The entrance close for both, else null</returns>
        public EntranceTeleport? IsEntranceCloseForBoth(Vector3 entityPos1, Vector3 entityPos2)
        {
            for (int i = 0; i < EntrancesTeleportArray.Length; i++)
            {
                var entrance = EntrancesTeleportArray[i];
                if (entrance == null) continue;

                Vector3 entrancePos = entrance.entrancePoint.position;
                if ((entityPos1 - entrancePos).sqrMagnitude < Const.DISTANCE_TO_ENTRANCE * Const.DISTANCE_TO_ENTRANCE
                    && (entityPos2 - entrancePos).sqrMagnitude < Const.DISTANCE_TO_ENTRANCE * Const.DISTANCE_TO_ENTRANCE)
                {
                    return entrance;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the position of teleport of entrance, to teleport lethalBot to it, if he needs to go in/out of the facility to follow player.
        /// </summary>
        /// <param name="entranceToUse"></param>
        /// <returns></returns>
        public Vector3? GetTeleportPosOfEntrance(EntranceTeleport? entranceToUse)
        {
            if (entranceToUse == null || !entranceToUse.FindExitPoint())
            {
                return null;
            }
            return entranceToUse.exitScript.entrancePoint.position;
        }

        /// <summary>
        /// Check all doors to know if the lethalBot is close enough to it to open it if necessary.
        /// </summary>
        /// <returns></returns>
        public DoorLock? GetDoorIfWantsToOpen()
        {
            Vector3 npcBodyPos = NpcController.Npc.thisController.transform.position;
            for (int i = 0; i < doorLocksArray.Length; i++)
            {
                var door = doorLocksArray[i];
                if (door != null && !door.isLocked && (door.transform.position - npcBodyPos).sqrMagnitude < Const.DISTANCE_NPCBODY_FROM_DOOR * Const.DISTANCE_NPCBODY_FROM_DOOR)
                {
                    return door;
                }
            }
            return null;
        }

        /// <summary>
        /// Check the doors after some interval of ms to see if lethalBot can open one to unstuck himself.
        /// </summary>
        /// <returns>true: a door has been opened by lethalBot. Else false</returns>
        private bool OpenDoorIfNeeded()
        {
            if (!timerCheckDoor.HasStarted() || timerCheckDoor.Elapsed())
            {
                timerCheckDoor.Start(Const.TIMER_CHECK_DOOR);

                DoorLock? door = GetDoorIfWantsToOpen();
                if (door != null && !door.isDoorOpened)
                {
                    // Open door
                    door.OpenOrCloseDoor(NpcController.Npc);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check the doors after some interval of ms to see if lethalBot can open one to unstuck himself.
        /// </summary>
        /// <returns>the locked door has been found by lethalBot. Else null</returns>
        public DoorLock? UnlockDoorIfNeeded(float lockedDoorRange = Const.DISTANCE_NPCBODY_FROM_DOOR, bool checkLineOfSight = false, float proximityRange = -1f, bool bypassCooldown = false)
        {
            if (bypassCooldown 
                || !timerCheckLockedDoor.HasStarted() 
                || timerCheckLockedDoor.Elapsed())
            {
                timerCheckLockedDoor.Start(Const.TIMER_CHECK_DOOR);

                if (!HasKeyInInventory())
                {
                    return null;
                }

                Vector3 npcBodyPos = NpcController.Npc.thisController.transform.position;
                for (int i = 0; i < doorLocksArray.Length; i++)
                {
                    var lockedDoor = doorLocksArray[i];
                    if (lockedDoor != null && lockedDoor.isLocked && !lockedDoor.isPickingLock)
                    {
                        float distSqrFromDoor = (lockedDoor.transform.position - npcBodyPos).sqrMagnitude;
                        if (distSqrFromDoor < lockedDoorRange * lockedDoorRange)
                        {
                            // If we are nearby the door, we don't need to be able to see it!
                            if (proximityRange < 0 || distSqrFromDoor > proximityRange * proximityRange)
                            {
                                if (checkLineOfSight
                                && Physics.Linecast(eye.position, lockedDoor.transform.position + Vector3.up * 0.2f, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                                {
                                    // If the hit object is not the door, it's blocked
                                    if (hitInfo.transform.gameObject.GetComponentInParent<DoorLock>() != lockedDoor 
                                        && hitInfo.transform.gameObject.GetComponentInParent<TriggerPointToDoor>()?.pointToDoor != lockedDoor)
                                        continue;
                                }
                            }

                            // Get potential door positions
                            Vector3 doorPos1 = GetOffsetLockPickerPosition(lockedDoor);
                            Vector3 doorPos2 = GetOffsetLockPickerPosition(lockedDoor, true);

                            // Check path validity and distance for both positions
                            float? doorDistance1 = null;
                            float? doorDistance2 = null;

                            Plugin.LogDebug("[UnlockDoorIfNeeded] Checking path to front of door!");
                            if (IsValidPathToTarget(doorPos1, true))
                            {
                                Plugin.LogDebug("[UnlockDoorIfNeeded] Successfuly found path to front of door!");
                                doorDistance1 = pathDistance;
                            }

                            Plugin.LogDebug("[UnlockDoorIfNeeded] Checking path to back of door!");
                            if (IsValidPathToTarget(doorPos2, true))
                            {
                                Plugin.LogDebug("[UnlockDoorIfNeeded] Successfuly found path to back of door!");
                                doorDistance2 = pathDistance;
                            }

                            // Select the closest valid door position
                            if (doorDistance1.HasValue && doorDistance2.HasValue)
                            {
                                // Both positions are valid, check to see if its worth unlocking in the first place
                                float distanceBetweenSidesOfDoor;
                                if (doorDistance1 < doorDistance2)
                                {
                                    distanceBetweenSidesOfDoor = doorDistance2.Value - doorDistance1.Value;
                                }
                                else
                                {
                                    distanceBetweenSidesOfDoor = doorDistance1.Value - doorDistance2.Value;
                                }

                                // Debug call to check the distance between both sides of the door!
                                Plugin.LogDebug($"[UnlockDoorIfNeeded] Both sides to door {lockedDoor} are pathable, distance between both sides {distanceBetweenSidesOfDoor} meters!");

                                // Now check if its worthwhile to unlock the door
                                if (distanceBetweenSidesOfDoor > 10f)
                                {
                                    Plugin.LogDebug($"[UnlockDoorIfNeeded] Door {lockedDoor} passed all checks and should be unlocked.");
                                    return lockedDoor;
                                }
                            }
                            else if (doorDistance1.HasValue || doorDistance2.HasValue)
                            {
                                // Only one side is valid
                                Plugin.LogDebug($"[UnlockDoorIfNeeded] Door {lockedDoor} passed all checks and should be unlocked.");
                                return lockedDoor;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A debug function I use to check if a door could be unlocked by the bot!
        /// </summary>
        /// <returns>the locked door has been found by lethalBot. Else null</returns>
        private void CheckIfLockedDoorsCanBeReached()
        {
            NavMeshPath tempPath = new NavMeshPath();
            List<Vector3> startPoses = new List<Vector3>();
            foreach (EntranceTeleport entrance in EntrancesTeleportArray)
            {
                if (!entrance.isEntranceToBuilding && entrance.FindExitPoint())
                {
                    startPoses.Add(entrance.entrancePoint.position);
                }
            }
            foreach (var lockedDoor in doorLocksArray)
            {
                Vector3? posIsReachable = null;
                Vector3? pos2IsReachable = null;
                if (lockedDoor.isLocked)
                {
                    foreach (Vector3 pos in startPoses)
                    {
                        // Check if we can path to it!
                        NavMeshObstacle doorBlocker = lockedDoor.GetComponent<NavMeshObstacle>();
                        Plugin.LogDebug($"Door blocker radius {doorBlocker.radius}");
                        Plugin.LogDebug($"Door blocker size {doorBlocker.size.magnitude}");
                        Vector3 doorPos = GetOffsetLockPickerPosition(lockedDoor);
                        Vector3 doorPos2 = GetOffsetLockPickerPosition(lockedDoor, true);
                        if (!posIsReachable.HasValue && NavMesh.CalculatePath(pos, doorPos, NavMesh.AllAreas, tempPath))
                        {
                            if (tempPath != null && tempPath.corners.Length > 0)
                            {
                                if (Vector3.Distance(tempPath.corners[tempPath.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(doorPos, RoundManager.Instance.navHit, 2.7f)) <= 1.5f)
                                {
                                    Plugin.LogDebug($"Door {lockedDoor} can be reached! Door Pos: {lockedDoor.transform.position} Door Lock Pick Pos: {doorPos}");
                                    posIsReachable = doorPos;
                                }
                            }
                        }
                        if (!pos2IsReachable.HasValue && NavMesh.CalculatePath(pos, doorPos2, NavMesh.AllAreas, tempPath))
                        {
                            if (tempPath != null && tempPath.corners.Length > 0)
                            {
                                if (Vector3.Distance(tempPath.corners[tempPath.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(doorPos2, RoundManager.Instance.navHit, 2.7f)) <= 1.5f)
                                {
                                    Plugin.LogDebug($"Door {lockedDoor} can be reached! Door Pos: {lockedDoor.transform.position} Door Lock Pick Pos2: {doorPos2}");
                                    pos2IsReachable = doorPos2;
                                }
                            }
                        }
                        if (posIsReachable.HasValue && pos2IsReachable.HasValue)
                        {
                            break;
                        }
                    }
                    if (posIsReachable.HasValue || pos2IsReachable.HasValue)
                    {
                        if (posIsReachable.HasValue)
                        {
                            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            marker.transform.position = posIsReachable.Value + Vector3.up * 0.2f;
                            //marker.transform.localScale = Vector3.one * 0.3f;
                            marker.GetComponent<Renderer>().material.color = Color.green;
                            GameNetworkManager.Instance.localPlayerController.TeleportPlayer(posIsReachable.Value);
                            Plugin.LogDebug($"Door {lockedDoor} can be reached! Door Pos: {lockedDoor.transform.position} Door Lock Pick Pos: {posIsReachable}");
                        }
                        if (pos2IsReachable.HasValue)
                        {
                            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            marker.transform.position = pos2IsReachable.Value + Vector3.up * 0.2f;
                            //marker.transform.localScale = Vector3.one * 0.3f;
                            marker.GetComponent<Renderer>().material.color = Color.cyan;
                            GameNetworkManager.Instance.localPlayerController.TeleportPlayer(pos2IsReachable.Value);
                            Plugin.LogDebug($"Door {lockedDoor} can be reached! Door Pos: {lockedDoor.transform.position} Door Lock Pick Pos2: {pos2IsReachable}");
                        }
                    }
                    else
                    {
                        Plugin.LogDebug("[WARNING] Bot was unable to reach door!");
                    }
                }
            }
        }

        /// <summary>
        /// Helper function for moving the lockpicker postion away from the door so we can create a path to it
        /// </summary>
        /// <param name="doorScript">The door to test</param>
        /// <param name="checkBack">If false this function returns the front with the outward <paramref name="offsetDistance"/>. If true returns back with the outward <paramref name="offsetDistance"/> </param>
        /// <param name="offsetDistance">The distance to move the position from the door</param>
        /// <param name="areaMask">Allows you to change the area mask for the nearest nav area check</param>
        /// <returns>The modified distance from the door that is adjusted to the nearest nav area!</returns>
        public static Vector3 GetOffsetLockPickerPosition(DoorLock doorScript, bool checkBack = false, float offsetDistance = 1.5f, int areaMask = NavMesh.AllAreas)
        {
            // Compute the push direction from the door center to the lock picker in world space
            Vector3 doorForward = doorScript.transform.position + doorScript.transform.right;
            Vector3 doorBackward = doorScript.transform.position - doorScript.transform.right;
            Vector3 pushDirection = checkBack ? (doorForward - doorBackward).normalized : (doorBackward - doorForward).normalized;

            // Convert to world direction (respect door's rotation)
            Vector3 offsetPos = doorScript.transform.position + pushDirection * offsetDistance;

            // Offset outward in that direction
            return RoundManager.Instance.GetNavMeshPosition(offsetPos, RoundManager.Instance.navHit, 3f, areaMask);
        }

        /// <summary>
        /// Uses the target elevator if lethalBot needs to use one to follow the player or leave and enter the facility.
        /// </summary>
        /// <remarks>
        /// Ok, so this was ripped from the Masked AI <see cref="MaskedPlayerEnemy.UseElevator"/>, there may be bugs that need to be fixed
        /// </remarks>
        /// <returns><see langword="true"/>: the lethalBot is using or is waiting to use the elevator, else <see langword="false"/></returns>
        public bool UseElevator(bool goUp)
        {
            if (ElevatorScript == null || this.isOutside)
            {
                return false;
            }
            Vector3 vector = ((!goUp) ? ElevatorScript.elevatorTopPoint.position : ElevatorScript.elevatorBottomPoint.position);
            float distanceFromInsidePosition = Vector3.Distance(NpcController.Npc.transform.position, ElevatorScript.elevatorInsidePoint.position);
            if (ElevatorScript.elevatorFinishedMoving 
                && (distanceFromInsidePosition <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION || IsValidPathToTarget(ElevatorScript.elevatorInsidePoint.position, false)))
            {
                if (distanceFromInsidePosition <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION
                    && ElevatorScript.elevatorMovingDown == goUp 
                    && timerElevatorCooldown > Const.TIMER_USE_ELEVATOR
                    && (Time.timeSinceLevelLoad - pressElevatorButtonCooldown) > (AIIntervalTime + 0.16f))
                {
                    //ElevatorScript.PressElevatorButtonOnServer(true);
                    pressElevatorButtonCooldown = Time.timeSinceLevelLoad;
                    ElevatorScript.PressElevatorButton(); // This is networked, unlike the function above!
                }
                //SetDestinationToPositionLethalBotAI(ElevatorScript.elevatorInsidePoint.position);
                //OrderMoveToDestination();
                if (distanceFromInsidePosition > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    SetDestinationToPositionLethalBotAI(ElevatorScript.elevatorInsidePoint.position);
                    OrderMoveToDestination();
                }
                else
                {
                    StopMoving();
                }
                return true;
            }
            if (distanceFromInsidePosition > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION && IsValidPathToTarget(vector, false))
            {
                float distanceFromVector = Vector3.Distance(NpcController.Npc.transform.position, vector);
                if (distanceFromVector <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION
                    && ElevatorScript.elevatorMovingDown != goUp 
                    && !ElevatorScript.elevatorCalled 
                    && timerElevatorCooldown > Const.TIMER_USE_ELEVATOR
                    && (Time.timeSinceLevelLoad - pressElevatorButtonCooldown) > (AIIntervalTime + 0.16f))
                {
                    //ElevatorScript.CallElevatorOnServer(goUp);
                    pressElevatorButtonCooldown = Time.timeSinceLevelLoad;
                    ElevatorScript.CallElevator(goUp); // This is networked, unlike the function above!
                }

                // Move closer to the elevator!
                if (distanceFromVector > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    SetDestinationToPositionLethalBotAI(vector);
                    OrderMoveToDestination();
                }
                else
                {
                    StopMoving();
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the entered player is near the elevator or not!.
        /// </summary>
        /// <returns>true: the player is nearby the elevator, else false</returns>
        public bool IsPlayerNearElevatorEntrance(PlayerControllerB player)
        {
            if (ElevatorScript == null)
            {
                return false;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(player);
            if (lethalBotAI != null)
            {
                return lethalBotAI.IsInElevatorStartRoom;
            }

            // Elevators are only inside the building!
            if (!player.isInsideFactory)
            {
                return false;
            }

            Vector3 playerPos = player.transform.position;
            if ((playerPos - ElevatorScript.elevatorBottomPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_BOTTOM * Const.DISTANCE_TO_ELEVATOR_BOTTOM)
            {
                return false;
            }
            else if ((playerPos - ElevatorScript.elevatorTopPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_TOP * Const.DISTANCE_TO_ELEVATOR_TOP)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the entered position is near the elevator or not!.
        /// </summary>
        /// <returns>true: the position is nearby the elevator, else false</returns>
        public bool IsPositionNearElevatorEntrance(Vector3 position)
        {
            // NEEDTOVALIDATE: Does this work as expected?
            if (ElevatorScript != null)
            {
                if ((position - ElevatorScript.elevatorBottomPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_BOTTOM * Const.DISTANCE_TO_ELEVATOR_BOTTOM)
                {
                    return false;
                }
                else if ((position - ElevatorScript.elevatorTopPoint.position).sqrMagnitude < Const.DISTANCE_TO_ELEVATOR_TOP * Const.DISTANCE_TO_ELEVATOR_TOP)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check ladders if lethalBot needs to use one to follow player.
        /// </summary>
        /// <returns>true: the lethalBot is using or is waiting to use the ladder, else false</returns>
        private bool UseLadderIfNeeded()
        {
            if (NpcController.Npc.isClimbingLadder 
                || useLadderCoroutine != null)
            {
                return true;
            }

            InteractTrigger? ladder = GetLadderIfWantsToUseLadder();
            if (ladder == null)
            {
                // If this is a gap, do the default logic instead!
                if (agent.isOnOffMeshLink)
                {
                    OffMeshLinkData offMeshLinkData = agent.currentOffMeshLinkData;
                    if (offMeshLinkData.valid && offMeshLinkData.activated)
                    {
                        if (offMeshLinkCoroutine == null)
                        {
                            offMeshLinkCoroutine = StartCoroutine(offMeshLinkParabola(agent, 0.6f)); // NpcController.Npc.jumpForce
                        }
                    }
                    else
                    {
                        StopOffMeshLinkMovement();
                    }
                }
                else if (offMeshLinkCoroutine != null)
                {
                    StopOffMeshLinkMovement();
                }
                return false;
            }

            // Lethal Bot wants to use ladder
            if (Plugin.Config.TeleportWhenUsingLadders.Value)
            {
                if (agent.isOnOffMeshLink)
                {
                    TeleportLethalBot(agent.currentOffMeshLinkData.endPos, isOutside);
                    agent.CompleteOffMeshLink();
                }
                return true;
            }

            // Try to use ladder
            if (NpcController.CanUseLadder(ladder))
            {
                //InteractTriggerPatch.Interact_ReversePatch(ladder, NpcController.Npc.thisPlayerBody);
                ladder.Interact(NpcController.Npc.thisPlayerBody);

                // Set rotation of lethalBot to face ladder
                NpcController.Npc.transform.rotation = ladder.ladderPlayerPositionNode.transform.rotation;
                NpcController.SetTurnBodyTowardsDirection(NpcController.Npc.transform.forward);
            }
            else
            {
                // Wait to use ladder
                this.StopMoving();
            }

            return true;
        }

        /// <summary>
        /// Helper function to check if the bot is currently using an off mesh link.
        /// </summary>
        /// <returns>true if the bot is using an off mesh link, otherwise false</returns>
        public bool IsUsingOffMeshLink()
        {
            return offMeshLinkCoroutine != null || useLadderCoroutine != null || agent.isOnOffMeshLink;
        }

        /// <summary>
        /// Moves the bot across an off the mesh link using a parabola
        /// </summary>
        /// <remarks>
        /// This is almost a perfect recreation from the Unity Documentation!
        /// </remarks>
        /// <param name="agent"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private IEnumerator offMeshLinkParabola(NavMeshAgent agent, float height)
        {
            OffMeshLinkData data = agent.currentOffMeshLinkData;
            Vector3 startPos = this.transform.position;
            Vector3 endPos = data.endPos + Vector3.up * agent.baseOffset;
            float normalizedTime = 0f;

            // Calculate duration from speed
            Vector3 flatStart = Vector3.ProjectOnPlane(startPos, Vector3.up);
            Vector3 flatEnd = Vector3.ProjectOnPlane(endPos, Vector3.up);
            float horizontalDistance = Vector3.Distance(flatStart, flatEnd);

            Plugin.LogDebug($"Beginning off mesh link movement. {data.valid}; {data.activated}; {base.IsOwner}");
            while (normalizedTime < 1f && base.IsOwner)
            {
                data = agent.currentOffMeshLinkData; // Update our data!
                if (!data.valid || !data.activated)
                {
                    break;
                }
                endPos = data.endPos + Vector3.up * agent.baseOffset; // Link end position could be moving (like a moving platform), so we need to update it every frame!
                float duration = horizontalDistance / agent.speed;
                float num = height * 4f * (normalizedTime - normalizedTime * normalizedTime);
                Plugin.LogDebug($"Moving on off mesh link; time: {normalizedTime}; y: {num}");
                Vector3 newPos = Vector3.Lerp(startPos, endPos, normalizedTime) + num * Vector3.up;
                this.transform.position = newPos;
                agent.transform.position = newPos;
                NpcController.Npc.transform.position = newPos;
                normalizedTime += Time.deltaTime / duration;
                yield return null;
            }
            agent.CompleteOffMeshLink();
            TeleportAgentAIAndBody(endPos, skipNavMeshCheck: true); // We should already be on the NavMesh......
            Plugin.LogDebug($"Completed off mesh link without interruption, position: {base.transform.position}");
            offMeshLinkCoroutine = null;
        }

        public void StopOffMeshLinkMovement(bool warpToEnd = true)
        {
            // Stop using the off mesh link
            if (offMeshLinkCoroutine != null)
            {
                StopCoroutine(offMeshLinkCoroutine);
                offMeshLinkCoroutine = null;
                OffMeshLinkData currentOffMeshLinkData = agent.currentOffMeshLinkData;
                agent.CompleteOffMeshLink();
                if (currentOffMeshLinkData.valid)
                {
                    Plugin.LogDebug($"Completed off mesh EARLY link due to an interruption; position: {base.transform.position}");
                    if ((base.transform.position - currentOffMeshLinkData.startPos).sqrMagnitude < (base.transform.position - currentOffMeshLinkData.endPos).sqrMagnitude)
                    {
                        Plugin.LogDebug($"Warping agent to start position at {currentOffMeshLinkData.startPos}");
                        if (warpToEnd)
                            TeleportAgentAIAndBody(currentOffMeshLinkData.startPos, skipNavMeshCheck: true);
                    }
                    else
                    {
                        Plugin.LogDebug($"Warping agent to end position at {currentOffMeshLinkData.endPos}");
                        if (warpToEnd)
                            TeleportAgentAIAndBody(currentOffMeshLinkData.endPos, skipNavMeshCheck: true);
                    }
                }
                else
                {
                    Plugin.LogDebug("Off mesh link data invalid; agent completing off mesh link anyway");
                }
            }

            // Stop using ladder
            if (useLadderCoroutine != null)
            {
                NpcController.Npc.CancelSpecialTriggerAnimations();
            }
        }

        #region Inventory and Weapon Helpers

        /// <summary>
        /// Is the lethalBot holding an item ?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(false, nameof(HeldItem))]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AreHandsFree()
        {
            return HeldItem == null;
        }

        /// <summary>
        /// Is the lethalBot holding a weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(true, nameof(HeldItem))]
        public bool IsHoldingCombatWeapon()
        {
            // Need ammo in order to use this weapon!
            GrabbableObject? heldItem = this.HeldItem;
            if (!HasAmmoForWeapon(heldItem))
            {
                return false;
            }
            return ItemsManager.Instance.IsItemWeapon(heldItem);
        }

        /// <summary>
        /// Is the lethalBot holding a ranged weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(true, nameof(HeldItem))]
        public bool IsHoldingRangedWeapon()
        {
            // Need ammo in order to use this weapon!
            GrabbableObject? heldItem = this.HeldItem;
            if (!HasAmmoForWeapon(heldItem))
            {
                return false;
            }
            return ItemsManager.Instance.IsItemRangedWeapon(heldItem);
        }

        /// <summary>
        /// Is the lethalBot holding a key ?
        /// </summary>
        /// <param name="keyOnly">Should we only consider "actual" keys</param>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(true, nameof(HeldItem))]
        public bool IsHoldingKey(bool keyOnly = false)
        {
            return ItemsManager.IsItemKey(this.HeldItem, keyOnly);
        }

        /// <summary>
        /// Does the lethalBot have a weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasCombatWeapon()
        {
            if (IsHoldingCombatWeapon())
            { 
                return true;
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (HasAmmoForWeapon(itemOnlySlot))
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                // Do we need ammo in order to use this weapon?
                // NOTE: HasAmmoForWeapon, checks if the item is a weapon internally!
                GrabbableObject weapon = itemSlots[i];
                if (!HasAmmoForWeapon(weapon))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have a ranged weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasRangedWeapon()
        {
            if (IsHoldingRangedWeapon())
            {
                return true;
            }
            ItemsManager instanceIM = ItemsManager.Instance;
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (HasAmmoForWeapon(itemOnlySlot) 
                && instanceIM.IsItemRangedWeapon(itemOnlySlot))
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                GrabbableObject weapon = itemSlots[i];
                if (HasAmmoForWeapon(weapon) 
                    && instanceIM.IsItemRangedWeapon(weapon))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have a key ?
        /// </summary>
        /// <param name="keyOnly">Should we only consider "actual" keys</param>
        /// <returns>I mean come on</returns>
        public bool HasKeyInInventory(bool keyOnly = false)
        {
            if (IsHoldingKey(keyOnly))
            {
                return true;
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (ItemsManager.IsItemKey(itemOnlySlot, keyOnly))
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (ItemsManager.IsItemKey(itemSlots[i], keyOnly))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Is the given item a ranged weapon ?
        /// </summary>
        /// <remarks>
        /// I will note that it only works on items derived off of the ShotgunItem or PatcherTool class!<br/>
        /// Modders can once again override this as desired!
        /// </remarks>
        /// <returns>I mean come on</returns>
        [Obsolete("This has been moved to ItemsManager. Use that one instead!")]
        public static bool IsItemRangedWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            return ItemsManager.Instance.IsItemRangedWeapon(weapon);
        }

        /// <summary>
        /// Is the given item a weapon?
        /// </summary>
        /// <remarks>
        /// Modders can override this to add their own custom weapons for the bots to use!
        /// </remarks>
        /// <param name="weapon">The item to check</param>
        /// <returns>I mean come on</returns>
        [Obsolete("This has been moved to ItemsManager. Use that one instead!")]
        public static bool IsItemWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            return ItemsManager.Instance.IsItemWeapon(weapon);
        }

        /// <summary>
        /// Is the given item scrap?
        /// </summary>
        /// <remarks>
        /// Modders can override this to add their own custom scrap items!
        /// </remarks>
        /// <param name="item">The item to check</param>
        /// <returns>I mean come on</returns>
        [Obsolete("This has been moved to ItemsManager. Use that one instead!")]
        public static bool IsItemScrap([NotNullWhen(true)] GrabbableObject? item)
        {
            return ItemsManager.IsItemScrap(item);
        }

        /// <summary>
        /// Is the given item a key or lockpicker ?
        /// </summary>
        /// <param name="item"></param>
        /// <param name="keyOnly">Should we only consider "actual" keys</param>
        /// <returns>I mean come on</returns>
        [Obsolete("This has been moved to ItemsManager. Use that one instead!")]
        public static bool IsItemKey([NotNullWhen(true)] GrabbableObject? item, bool keyOnly = false)
        {
            return ItemsManager.IsItemKey(item, keyOnly);
        }

        /// <summary>
        /// Helper function to check if an item has a charge or not, 
        /// this is used for the bots to know if they can use an item or not!
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <returns>true: the item has a charge or doesn't use batteries; otherwise false</returns>
        [Obsolete("This has been moved to ItemsManager. Use that one instead!")]
        public static bool IsItemPowered([NotNullWhen(true)] GrabbableObject? item)
        {
            return ItemsManager.HasRequiredCharge(item);
        }

        /// <summary>
        /// Checks if the current weapon has ammo.
        /// </summary>
        /// <remarks>
        /// This also checks if the item is a weapon internally!
        /// </remarks>
        /// <param name="weapon">The weapon we are checking</param>
        /// <param name="spareOnly">Should we only consider the spare ammunation for this weapon?</param>
        /// <returns></returns>
        public bool HasAmmoForWeapon([NotNullWhen(true)] GrabbableObject? weapon, bool spareOnly = false)
        {
            return ItemsManager.Instance.TryGetWeaponInfo(weapon, out WeaponInfo? weaponInfo) && weaponInfo.HasAmmo(NpcController.Npc, weapon, spareOnly);
        }

        /// <summary>
        /// Check if the lethalBot has the given object in its inventory.
        /// </summary>
        /// <param name="grabbableObject">The object to check if the bot has in its inventory</param>
        /// <param name="objectSlot">The slot of where the object was found at! Is set to <see cref="Const.INVALID_ITEM_SLOT"/> if item was not found!</param>
        /// <returns>true: the bot has the object in its inventory, false: the bot doesn't have the given object in its inventory</returns>
        public bool HasGrabbableObjectInInventory([NotNullWhen(true)] GrabbableObject? grabbableObject, out int objectSlot)
        {
            objectSlot = Const.INVALID_ITEM_SLOT;
            if (grabbableObject == null)
            {
                return false;
            }

            // Check if the lethalBot is holding the object
            if (this.HeldItem == grabbableObject)
            {
                objectSlot = NpcController.Npc.currentItemSlot;
                return true;
            }

            // Check if the lethalBot has the object in its item only slot
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot == grabbableObject)
            {
                objectSlot = Const.RESERVED_EQUIPMENT_SLOT;
                return true;
            }

            // Check if the lethalBot has the object in its inventory
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int index = 0; index < itemSlots.Length; index++)
            {
                var item = itemSlots[index];
                if (item == grabbableObject)
                {
                    objectSlot = index;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if the lethalBot has an object that fulfills the given function in its inventory.
        /// </summary>
        /// <remarks>
        /// NOTE: If you are comparing object references, you are better off using <see cref="HasGrabbableObjectInInventory(GrabbableObject?, out int)"/>
        /// </remarks>
        /// <param name="objectPredicate">The function to inspect the object in the inventory!</param>
        /// <param name="objectSlot">The slot of where the object was found at! Is set to <see cref="Const.INVALID_ITEM_SLOT"/> if item was not found!</param>
        /// <returns><see langword="true"/>: the bot has the object in its inventory, <see langword="false"/>: the bot doesn't have the given object in its inventory</returns>
        public bool HasGrabbableObjectInInventory(Func<GrabbableObject, bool> objectPredicate, out int objectSlot)
        {
            // Check if the lethalBot is holding the object
            objectSlot = Const.INVALID_ITEM_SLOT;
            GrabbableObject? heldItem = this.HeldItem;
            if (heldItem != null && objectPredicate(heldItem))
            {
                objectSlot = NpcController.Npc.currentItemSlot;
                return true;
            }

            // Check if the lethalBot has the object in its item only slot
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null && itemOnlySlot != heldItem && objectPredicate(itemOnlySlot))
            {
                objectSlot = Const.RESERVED_EQUIPMENT_SLOT;
                return true;
            }

            // Check if the lethalBot has the object in its inventory
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int index = 0; index < itemSlots.Length; index++)
            {
                var item = itemSlots[index];
                if (item != null && heldItem != item && objectPredicate(item))
                {
                    objectSlot = index;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if the lethalBot has an object that fulfills the given filter in its inventory, and find the best one according to isBetter.
        /// </summary>
        /// <remarks>
        /// NOTE: If you are comparing object references, you are better off using <see cref="HasGrabbableObjectInInventory(GrabbableObject?, out int)"/><br/>
        /// NOTE: If you just want to know if the bot has an item that fulfills the filter, use <see cref="HasGrabbableObjectInInventory(Func{GrabbableObject?, bool}, out int)"/>
        /// </remarks>
        /// <param name="filter">The function to inspect the object in the inventory!</param>
        /// <param name="isBetter">The function to determine if the found object is better than the current best one! First parameter is the current best item, second parameter is the new candidate item!</param>
        /// <param name="objectSlot">The slot of where the object was found at! Is set to <see cref="Const.INVALID_ITEM_SLOT"/> if item was not found!</param>
        /// <returns></returns>
        public bool TryFindItemInInventory(Func<GrabbableObject, bool> filter, Func<GrabbableObject, GrabbableObject, bool> isBetter, out int objectSlot)
        {
            // Unlike HasGrabbableObjectInInventory, we can't early out if the bot's held item matches the filter
            objectSlot = Const.INVALID_ITEM_SLOT;
            GrabbableObject? bestItem = null;

            // Assess all items in inventory
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null && filter(itemOnlySlot))
            {
                bestItem = itemOnlySlot;
                objectSlot = Const.RESERVED_EQUIPMENT_SLOT;
            }

            // Onto the regular item slots, using for since we need manual index tracking
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int index = 0; index < itemSlots.Length; index++)
            {
                var canidate = itemSlots[index];
                if (canidate != null && filter(canidate))
                {
                    if (bestItem == null || isBetter(bestItem, canidate))
                    {
                        bestItem = canidate;
                        objectSlot = index;
                    }
                }
            }
            return bestItem != null;
        }

        /// <summary>
        /// Returns the item stored in the given inventory slot
        /// </summary>
        /// <remarks>
        /// This exists since the new reserved equipment slot uses a field rather than a slot in the inventory
        /// </remarks>
        /// <param name="slot"></param>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/>. Only exists as an optimization!</param>
        /// <param name="itemSlots">The bot's inventory. Only exists as an optimization!</param>
        /// <returns></returns>
        public GrabbableObject? GetItemAtSlot(int slot, PlayerControllerB? lethalBotController = null, GrabbableObject[]? itemSlots = null)
        {
            lethalBotController ??= NpcController.Npc;
            if (slot == Const.RESERVED_EQUIPMENT_SLOT)
            {
                return lethalBotController.ItemOnlySlot;
            }

            itemSlots ??= lethalBotController.ItemSlots;
            if (slot < 0 || slot >= itemSlots.Length)
            {
                Plugin.LogWarning($"LethalBotAI.GetItemAtSlot was given a slot out of range {slot}. Returning null!");
                return null;
            }

            return itemSlots[slot];
        }

        /// <summary>
        /// Does the lethalBot have room for another item?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasSpaceInInventory()
        {
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            int inventorySize = GetInventorySize(NpcController.Npc, itemSlots);
            for (int i = 0; i < inventorySize; i++)
            {
                GrabbableObject? item = itemSlots[i];
                if (item == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Does the lethalBot have room for another item?
        /// </summary>
        /// <remarks>
        /// This considers <see cref="Plugin.IsModReservedItemSlotCoreLoaded"/>.<br/>
        /// This checks if the bot has space for the given item in their inventory.<br/>
        /// Which is slightly different from <see cref="HasSpaceInInventory()"/>
        /// </remarks>
        /// <param name="grabbableObject">The object to assess, can be null!</param>
        /// <returns>I mean come on</returns>
        public bool HasSpaceInInventory(GrabbableObject? grabbableObject)
        {
            return FirstEmptyItemSlot(grabbableObject) != Const.INVALID_ITEM_SLOT;
        }

        /// <summary>
        /// Does the lethalBot have something in its inventory?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasSomethingInInventory()
        {
            if (!AreHandsFree())
            {
                return true;
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null)
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have scrap in its inventory?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasScrapInInventory()
        {
            GrabbableObject? heldItem = this.HeldItem;
            if (heldItem != null
                && ItemsManager.IsItemScrap(heldItem) 
                && !IsGrabbableObjectInLoadout(heldItem))
            {
                return true;
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null 
                && heldItem != itemOnlySlot
                && ItemsManager.IsItemScrap(itemOnlySlot)
                && !IsGrabbableObjectInLoadout(itemOnlySlot))
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                var scrap = itemSlots[i];
                if (scrap != heldItem 
                    && ItemsManager.IsItemScrap(scrap) 
                    && !IsGrabbableObjectInLoadout(scrap))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have an item to sell in its inventory?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasSellableItemInInventory()
        {
            GrabbableObject? heldItem = this.HeldItem;
            if (heldItem != null && IsGrabbableObjectSellable(heldItem, true, true))
            {
                return true;
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null && heldItem != itemOnlySlot && IsGrabbableObjectSellable(itemOnlySlot, true, true))
            {
                return true;
            }
            GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                var item = itemSlots[i];
                if (item != heldItem && IsGrabbableObjectSellable(item, true, true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if the lethalBot has duplicate loadout items in its inventory.
        /// </summary>
        /// <param name="grabbableObject">The object to check if the bot has duplicates of in its inventory</param>
        /// <param name="objectSlot">The slot of where the duplicate object was found at! Is set to <see cref="Const.INVALID_ITEM_SLOT"/> if item was not found!</param>
        /// <returns>true: the bot has a duplicate loadout object in its inventory, false: the bot doesn't have a duplicate loadout object in its inventory</returns>
        public bool HasDuplicateLoadoutItems(GrabbableObject grabbableObject, out int objectSlot)
        {
            // Make sure this item is in our loadout!
            objectSlot = Const.INVALID_ITEM_SLOT;
            if (!IsGrabbableObjectInLoadout(grabbableObject))
            {
                return false;
            }

            // Make sure this is in our inventory!
            if (HasGrabbableObjectInInventory(grabbableObject, out int itemSlot))
            {
                string itemName = grabbableObject.itemProperties.itemName;
                GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
                if (itemOnlySlot != null
                    && itemSlot != Const.RESERVED_EQUIPMENT_SLOT
                    && itemOnlySlot.itemProperties.itemName == itemName)
                {
                    objectSlot = Const.RESERVED_EQUIPMENT_SLOT;
                    return true;
                }
                GrabbableObject[] itemSlots = NpcController.Npc.ItemSlots;
                for (int i = 0; i < itemSlots.Length; i++)
                {
                    // Skip this item!
                    if (i == itemSlot)
                        continue;

                    // Lets see if they are the same!
                    GrabbableObject item = itemSlots[i];
                    if (item != null && item.itemProperties.itemName == itemName)
                    {
                        objectSlot = i;
                        return true;
                    }
                }
            }
            else
            {
                Plugin.LogWarning($"HasDuplicateLoadoutItems was called with a grabbable object \"{grabbableObject}\" not in the bot's inventory!");
            }

            return false;
        }

        /// <summary>
        /// Is the given grabbable object a part of our loadout?
        /// </summary>
        /// <param name="grabbableObject">The object to check</param>
        /// <returns>true: this object in in our loadout; otherwise false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGrabbableObjectInLoadout([NotNullWhen(true)] GrabbableObject grabbableObject)
        {
            return LethalBotIdentity.Loadout.IsGrabbableObjectInLoadout(grabbableObject);
        }

        /// <summary>
        /// Basically a carbon copy of <see cref="PlayerControllerB.CanUseItem"/>, but made for bots
        /// </summary>
        /// <returns></returns>
        public bool CanUseHeldItem()
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (!base.IsOwner || !lethalBotController.isPlayerControlled)
            {
                return false;
            }
            GrabbableObject? heldItem = lethalBotController.currentlyHeldObjectServer;
            if (heldItem == null)
            {
                return false;
            }
            if (lethalBotController.isPlayerDead)
            {
                return false;
            }
            if (!heldItem.itemProperties.usableInSpecialAnimations 
                && (lethalBotController.isGrabbingObjectAnimation 
                    || lethalBotController.inTerminalMenu 
                    || lethalBotController.isTypingChat 
                    || (lethalBotController.inSpecialInteractAnimation 
                        && !lethalBotController.inShockingMinigame)))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Helper function to turn off the item held by the bot!
        /// </summary>
        /// <returns>false: we didn't press a button to turn off the held item. true: we pressed a button to turn off the held item</returns>
        public bool TurnOffHeldItem()
        {
            GrabbableObject? grabbableObject = this.HeldItem;
            if (grabbableObject == null)
            {
                return false;
            }

            if (grabbableObject is FlashlightItem flashlight && flashlight.isBeingUsed)
            {
                flashlight.UseItemOnClient(true);
                if (flashlight.itemProperties.holdButtonUse)
                {
                    flashlight.UseItemOnClient(false); // HACKHACK: Fake release the button!
                }
                return true;
            }
            else if (grabbableObject is WalkieTalkie walkieTalkie && walkieTalkie.isBeingUsed)
            {
                // Wait until we are not holding the button anymore
                // we may be talking to someone
                // UseHeldItem is called in the base class, and will handle the button release
                if (!walkieTalkie.isHoldingButton)
                {
                    walkieTalkie.ItemInteractLeftRightOnClient(false);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Change the ownership of the lethalBot inventory to the given player.
        /// </summary>
        /// <remarks>
        /// This is called when the bot switches ownership to another player.
        /// </remarks>
        /// NEEDTOVALIDATE: Should this be internal? Rather than public?
        /// <param name="newOwnerClientId"></param>
        [ServerRpc(RequireOwnership = false)]
        public void ChangeOwnershipOfBotInventoryServerRpc(ulong newOwnerClientId)
        {
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                NetworkObject? networkObject = item?.NetworkObject;
                if (networkObject != null && networkObject.OwnerClientId != newOwnerClientId)
                {
                    // Change ownership of the item to the player that owns the bot
                    networkObject.ChangeOwnership(newOwnerClientId);
                }
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null)
            {
                NetworkObject? networkObject = itemOnlySlot.NetworkObject;
                if (networkObject != null && networkObject.OwnerClientId != newOwnerClientId)
                {
                    // Change ownership of the item to the player that owns the bot
                    networkObject.ChangeOwnership(newOwnerClientId);
                }
            }
        }

        #endregion

        #region PlayerControllerB Ownership RPCs

        /// <summary>
        /// Change the ownership of the lethalBot inventory to the player that owns the bot.
        /// </summary>
        /// <remarks>
        /// This is called on an interval inside of <see cref="StartOfRound.LateUpdate"/> to ensure items are owned by the correct player.
        /// </remarks>
        public void UpdateOwnershipOfBotServer()
        {
            // NetworkObject ownership is only updated on the server
            if (!IsServer && !IsHost)
            {
                ChangeOwnershipOfBotInventoryServerRpc(this.OwnerClientId);
                ChangeNpcOwnershipOfBotServerRpc(this.OwnerClientId);
                if (Plugin.IsModLethalPhonesLoaded)
                {
                    ChangeOwnershipOfLethalPhoneServerRpc(this.OwnerClientId);
                }
                return;
            }
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                NetworkObject? networkObject = item?.NetworkObject;
                if (networkObject != null && networkObject.OwnerClientId != this.OwnerClientId)
                {
                    // Change ownership of the item to the player that owns the bot
                    networkObject.ChangeOwnership(this.OwnerClientId);
                }
            }
            GrabbableObject? itemOnlySlot = NpcController.Npc.ItemOnlySlot;
            if (itemOnlySlot != null)
            {
                NetworkObject? networkObject = itemOnlySlot.NetworkObject;
                if (networkObject != null && networkObject.OwnerClientId != this.OwnerClientId)
                {
                    // Change ownership of the item to the player that owns the bot
                    networkObject.ChangeOwnership(this.OwnerClientId);
                }
            }

            // Make sure Npc ownership is up to date!
            NetworkObject? playerControllerObject = NpcController.Npc.gameObject.GetComponent<NetworkObject>();
            if (playerControllerObject != null && playerControllerObject.OwnerClientId != this.OwnerClientId)
            {
                playerControllerObject.ChangeOwnership(this.OwnerClientId);
            }

            // Make sure lethal phone ownership is up to date!
            if (Plugin.IsModLethalPhonesLoaded)
            {
                UpdateLethalPhoneOwnership();
            }
        }

        /// <summary>
        /// Change the ownership of the lethalBot's <see cref="PlayerControllerB"/> player.
        /// </summary>
        /// <remarks>
        /// This is called when the bot switches ownership to another player.
        /// </remarks>
        /// NEEDTOVALIDATE: Should this be internal? Rather than public?
        /// <param name="newOwnerClientId"></param>
        [ServerRpc(RequireOwnership = false)]
        public void ChangeNpcOwnershipOfBotServerRpc(ulong newOwnerClientId)
        {
            NetworkObject? playerControllerObject = NpcController.Npc.gameObject.GetComponent<NetworkObject>();
            if (playerControllerObject != null && playerControllerObject.OwnerClientId != newOwnerClientId)
            {
                playerControllerObject.ChangeOwnership(newOwnerClientId);
            }
        }

        /// <summary>
        /// Change the ownership of the lethalBot's <see cref="PlayerPhone"/>.
        /// </summary>
        /// <remarks>
        /// This is called when the bot switches ownership to another player.
        /// </remarks>
        /// NEEDTOVALIDATE: Should this be internal? Rather than public?
        /// <param name="newOwnerClientId"></param>
        [ServerRpc(RequireOwnership = false)]
        public void ChangeOwnershipOfLethalPhoneServerRpc(ulong newOwnerClientId)
        {
            PlayerPhone? phone = GetOurPlayerPhone();
            if (phone != null)
            {
                NetworkObject? lethalPhoneNetworkObject = phone.GetComponent<NetworkObject>();
                if (lethalPhoneNetworkObject != null && lethalPhoneNetworkObject.OwnerClientId != newOwnerClientId)
                {
                    lethalPhoneNetworkObject.ChangeOwnership(newOwnerClientId);
                    StopLethalPhonesCoroutine(); // We need to stop the coroutine since ownership is changing.
                }
            }
        }

        /// <summary>
        /// Small helper function that only exists since Lethal Phones is a soft dependency,
        /// and some users may not have the mod installed.
        /// </summary>
        private void UpdateLethalPhoneOwnership()
        {
            PlayerPhone? phone = GetOurPlayerPhone();
            if (phone != null)
            {
                NetworkObject? lethalPhoneNetworkObject = phone.GetComponent<NetworkObject>();
                if (lethalPhoneNetworkObject != null && lethalPhoneNetworkObject.OwnerClientId != this.OwnerClientId)
                {
                    lethalPhoneNetworkObject.ChangeOwnership(this.OwnerClientId);
                    StopLethalPhonesCoroutine(); // We need to stop the coroutine since ownership is changing.
                }
            }
        }

        #endregion

        /// <summary>
        /// Check all object array <c>LethalBotManager.grabbableObjectsInMap</c>, 
        /// if lethalBot is close and can see an item to grab.
        /// </summary>
        /// <returns><c>GrabbableObject</c> if lethalBot sees an item he can grab, else null.</returns>
        public GrabbableObject? LookingForObjectToGrab()
        {
            Vector3 ourEyePos = this.eye.position;
            Vector3 ourEyeForward = this.eye.forward;
            GrabbableObject? closestObject = null;
            float closestObjectDistSqr = Const.LETHAL_BOT_OBJECT_RANGE * Const.LETHAL_BOT_OBJECT_RANGE;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                // Get grabbable object infos
                GrabbableObject? grabbableObject = LethalBotManager.grabbableObjectsInMap[i];
                if (grabbableObject == null)
                {
                    continue;
                }

                // Object not outside when ai inside and vice versa
                // NEEDTOVALIDATE: Should I use grabbableObject.isInFactory to check this?
                Vector3 grabbableObjectPosition = grabbableObject.transform.position;
                if (isOutside && grabbableObjectPosition.y < -100f)
                {
                    continue;
                }
                else if (!isOutside && grabbableObjectPosition.y > -80f)
                {
                    continue;
                }

                // Object in range ?
                // Check if object is further away from the closest object
                // FIXME: This should be PATH distance not elucian!
                float sqrDistanceEyeGrabbableObject = (grabbableObjectPosition - ourEyePos).sqrMagnitude;
                if (sqrDistanceEyeGrabbableObject > closestObjectDistSqr)
                {
                    continue;
                }

                // Object on ship
                // NEEDTOVALIDATE: Should I only check if the item is in the ship room?
                if (grabbableObject.isInElevator
                    || grabbableObject.isInShipRoom)
                {
                    continue;
                }

                // Black listed ? 
                if (IsGrabbableObjectBlackListed(grabbableObject))
                {
                    continue;
                }

                // Object in cruiser vehicle
                // TODO: Let the bot move items between the ship and the cruiser
                Transform? companyCruiser = grabbableObject.transform.parent;
                if (companyCruiser != null
                    && companyCruiser.name.StartsWith("CompanyCruiser"))
                {
                    continue;
                }

                // Object in a container mod of some sort ?
                if (Plugin.IsModCustomItemBehaviourLibraryLoaded)
                {
                    if (IsGrabbableObjectInContainerMod(grabbableObject))
                    {
                        continue;
                    }
                }

                // Object in the self sorting storage?
                if (IsGrabbaleObjectInSelfSortingStorage(grabbableObject))
                {
                    continue;
                }

                // Is a pickmin (LethalMin mod) holding the object ?
                if (Plugin.IsModLethalMinLoaded)
                {
                    if (IsGrabbableObjectHeldByPikminMod(grabbableObject))
                    {
                        continue;
                    }
                }

                // Grabbable object ?
                if (!IsGrabbableObjectGrabbable(grabbableObject))
                {
                    continue;
                }

                // Object close to awareness distance ?
                if (sqrDistanceEyeGrabbableObject < Const.LETHAL_BOT_OBJECT_AWARNESS * Const.LETHAL_BOT_OBJECT_AWARNESS)
                {
                    Plugin.LogDebug($"awareness {grabbableObject.name}");
                }
                // Object visible ?
                else if (!Physics.Linecast(ourEyePos, grabbableObjectPosition + Vector3.up * 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) // Was + Vector3.up * grabbableObject.itemProperties.verticalOffset, testing a small value to see if it has any kind of effect!
                {
                    Vector3 to = grabbableObjectPosition - ourEyePos;
                    if (Vector3.Angle(ourEyeForward, to) < Const.LETHAL_BOT_FOV)
                    {
                        // Object in FOV
                        Plugin.LogDebug($"LOS {grabbableObject.name}");
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    // Object not in line of sight
                    continue;
                }

                closestObject = grabbableObject;
                closestObjectDistSqr = sqrDistanceEyeGrabbableObject;
            }

            return closestObject;
        }

        /// <summary>
        /// Check all object array <c>LethalBotManager.grabbableObjectsInMap</c>, 
        /// lethalBot has omnipotent knowlege of all items to sell.
        /// </summary>
        /// <param name="ignoreHeldFlag">Should we consider objects held by other players?</param>
        /// <returns><c>GrabbableObject</c> if lethalBot sees an item he can sell, else null.</returns>
        public GrabbableObject? LookingForObjectsToSell(bool ignoreHeldFlag = false)
        {
            // We don't want to grab items if we already fulfilled the profit quota!
            if (LethalBotManager.HaveWeFulfilledTheProfitQuota())
            {
                return null;
            }
            Vector3 ourPos = NpcController.Npc.transform.position;
            GrabbableObject? closestObject = null;
            float closestObjectDistSqr = float.MaxValue;
            int closestObjectValue = int.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                // Get grabbable object infos
                GrabbableObject? grabbableObject = LethalBotManager.grabbableObjectsInMap[i];
                if (grabbableObject == null)
                {
                    continue;
                }

                // Object not outside when ai inside and vice versa
                Vector3 grabbableObjectPosition = grabbableObject.transform.position;
                if (isOutside && grabbableObjectPosition.y < -100f)
                {
                    continue;
                }
                else if (!isOutside && grabbableObjectPosition.y > -80f)
                {
                    continue;
                }

                // Black listed ? 
                if (IsGrabbableObjectBlackListed(grabbableObject, EnumGrabbableObjectCall.Selling))
                {
                    continue;
                }

                // Only do the value check if we are not selling all scrap on ship
                if (!Plugin.Config.SellAllScrapOnShip.Value)
                {
                    // We want to grab the cheapest item to sell,
                    // so if the item is more expensive than the closest object, skip it
                    if (closestObjectValue < grabbableObject.scrapValue)
                    {
                        continue;
                    }
                    // If the item is not the same value as the closest object,
                    // then reset the distance to the closest object
                    else if (closestObjectValue != grabbableObject.scrapValue)
                    {
                        closestObjectDistSqr = float.MaxValue;
                    }
                }

                // Object is further away from the closest object
                // FIXME: This should be PATH distance not elucian!
                float gameObjectDistSqr = (grabbableObjectPosition - ourPos).sqrMagnitude;
                if (gameObjectDistSqr > closestObjectDistSqr)
                {
                    continue;
                }

                // Object in a container mod of some sort ?
                if (Plugin.IsModCustomItemBehaviourLibraryLoaded)
                {
                    if (IsGrabbableObjectInContainerMod(grabbableObject))
                    {
                        continue;
                    }
                }

                // Object in the self sorting storage?
                if (IsGrabbaleObjectInSelfSortingStorage(grabbableObject))
                {
                    continue;
                }

                // Is a pickmin (LethalMin mod) holding the object ?
                if (Plugin.IsModLethalMinLoaded)
                {
                    if (IsGrabbableObjectHeldByPikminMod(grabbableObject))
                    {
                        continue;
                    }
                }

                // Grabbable object ?
                if (!IsGrabbableObjectSellable(grabbableObject, ignoreHeldFlag))
                {
                    continue;
                }

                closestObject = grabbableObject;
                closestObjectDistSqr = gameObjectDistSqr;
                closestObjectValue = grabbableObject.scrapValue;
            }

            return closestObject;
        }

        /// <summary>
        /// Check all player scripts for a player to revive, 
        /// if lethalBot is close and can see said dead player.
        /// </summary>
        /// <param name="ignoreLOS">Should we ignore line of sight when checking for a player to revive?</param>
        /// <param name="shipOnly">Should we only consider dead players on the ship?</param>
        /// <returns><c>PlayerControllerB</c> if lethalBot sees an player they can revive, else null.</returns>
        public PlayerControllerB? LookingForPlayerToRevive(bool ignoreLOS = false, bool shipOnly = false)
        {
            // If no revive mods are installed, do nothing!
            if (!RescueAndReviveState.IsAnyReviveModInstalled())
            {
                return null;
            }

            Vector3 ourEyePos = this.eye.position;
            Vector3 ourEyeForward = this.eye.forward;
            PlayerControllerB? closestDeadPlayer = null;
            float closestDeadPlayerDistSqr = Const.LETHAL_BOT_RESCUE_RANGE * Const.LETHAL_BOT_RESCUE_RANGE;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                // Get grabbable object infos
                // NOTE: This also functions as a null check!
                GrabbableObject? grabbableObject = LethalBotManager.grabbableObjectsInMap[i];
                if (grabbableObject == null || grabbableObject is not RagdollGrabbableObject deadBody)
                {
                    //Plugin.LogInfo("Body is null!");
                    continue;
                }

                // Object not outside when ai inside and vice versa
                bool isHeld = grabbableObject.isHeld || grabbableObject.isPocketed;
                Vector3 deadBodyPosition = grabbableObject.transform.position;
                if (!isHeld)
                {
                    if (isOutside && deadBodyPosition.y < -100f)
                    {
                        //Plugin.LogInfo("Body is not in range");
                        continue;
                    }
                    else if (!isOutside && deadBodyPosition.y > -80f)
                    {
                        //Plugin.LogInfo("Body is not in range");
                        continue;
                    }
                }

                // Object in range ?
                // Check if object is further away from the closest object
                // FIXME: This should be PATH distance not elucian!
                float sqrDistanceEyeDeadPlayer = (deadBodyPosition - ourEyePos).sqrMagnitude;
                if (sqrDistanceEyeDeadPlayer > closestDeadPlayerDistSqr)
                {
                    continue;
                }

                // Object on ship
                if (shipOnly
                    && !isHeld
                    && !grabbableObject.isInElevator
                    && !grabbableObject.isInShipRoom)
                {
                    //Plugin.LogInfo("Body is not on the ship");
                    continue;
                }

                PlayerControllerB playerController = StartOfRound.Instance.allPlayerScripts[deadBody.bodyID];
                if (playerController == null || !RescueAndReviveState.CanRevivePlayer(this, playerController))
                {
                    //Plugin.LogInfo($"Can Revive Player? {playerController != null && RescueAndReviveState.CanRevivePlayer(this, playerController)}");
                    continue;
                }

                // Black listed ? 
                if (IsGrabbableObjectBlackListed(grabbableObject, EnumGrabbableObjectCall.Reviving))
                {
                    //Plugin.LogInfo("Body is blacklisted?");
                    continue;
                }

                // Object in a container mod of some sort ?
                if (Plugin.IsModCustomItemBehaviourLibraryLoaded)
                {
                    if (IsGrabbableObjectInContainerMod(grabbableObject))
                    {
                        //Plugin.LogInfo("Body is in custom container");
                        continue;
                    }
                }

                // Is a pickmin (LethalMin mod) holding the object ?
                if (Plugin.IsModLethalMinLoaded)
                {
                    if (IsGrabbableObjectHeldByPikminMod(grabbableObject))
                    {
                        //Plugin.LogInfo("Body is held by pikmin!");
                        continue;
                    }
                }

                // Grabbable object ?
                if (!IsGrabbableObjectGrabbable(grabbableObject, EnumGrabbableObjectCall.Reviving))
                {
                    //Plugin.LogInfo("Body is not grabbable");
                    continue;
                }

                // Check if we should do the Line of Sight checks!
                if (!ignoreLOS)
                {
                    // Object close to awareness distance ?
                    if (isHeld || sqrDistanceEyeDeadPlayer < Const.LETHAL_BOT_OBJECT_AWARNESS * Const.LETHAL_BOT_OBJECT_AWARNESS)
                    {
                        Plugin.LogDebug($"awareness {grabbableObject.name}");
                    }
                    // Object visible ?
                    else if (!Physics.Linecast(ourEyePos, deadBodyPosition + Vector3.up * 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) // Was + Vector3.up * grabbableObject.itemProperties.verticalOffset, testing a small value to see if it has any kind of effect!
                    {
                        Vector3 to = deadBodyPosition - ourEyePos;
                        if (Vector3.Angle(ourEyeForward, to) < Const.LETHAL_BOT_FOV)
                        {
                            // Object in FOV
                            Plugin.LogDebug($"LOS {grabbableObject.name}");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Object not in line of sight
                        continue;
                    }
                }
                else
                {
                    Plugin.LogDebug($"Skipped LOS {grabbableObject.name}");
                }

                closestDeadPlayer = playerController;
                closestDeadPlayerDistSqr = sqrDistanceEyeDeadPlayer;
            }

            return closestDeadPlayer;
        }

        /// <summary>
        /// Check all player scripts for a player to heal and
        /// if lethalBot is close and can see said player.
        /// </summary>
        /// <param name="ignoreLOS">Should we ignore line of sight when checking for a player to heal?</param>
        /// <param name="shipOnly">Should we only consider players on the ship?</param>
        /// <returns><c>PlayerControllerB</c> if lethalBot sees an player they can heal, else null.</returns>
        public PlayerControllerB? LookingForPlayerToHeal(bool ignoreLOS = false, bool shipOnly = false)
        {
            // First things first, check if we need to be healed!
            PlayerControllerB lethalBotController = NpcController.Npc;
            float requiredInfectionLevel = HealInfectionLevel.Value;
            if (HealPlayerState.CanHealPlayer(this, lethalBotController, requiredInfectionLevel))
            {
                return lethalBotController;
            }

            // Fog reduce the visibility
            float range = Const.LETHAL_BOT_HEAL_RANGE;
            float proximityAwareness = Const.DISTANCE_CLOSE_ENOUGH_HOR;
            float width = Const.LETHAL_BOT_FOV;
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }

            StartOfRound instanceSOR = StartOfRound.Instance;
            Vector3 ourPos = this.transform.position;
            Vector3 ourEyePos = lethalBotController.gameplayCamera.transform.position;
            Vector3 ourEyeForward = lethalBotController.gameplayCamera.transform.forward;
            float currentClosestDistance = float.MaxValue;
            PlayerControllerB? closestPlayer = null;
            for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
            {
                // Check if they are a valid heal target
                PlayerControllerB player = instanceSOR.allPlayerScripts[i];
                if (player == lethalBotController 
                    || !HealPlayerState.CanHealPlayer(this, player, requiredInfectionLevel))
                {
                    continue;
                }

                // Only consider players on the ship
                if (shipOnly 
                    && !player.isInElevator 
                    && !player.isInHangarShipRoom)
                {
                    continue;
                }

                // Target close enough ?
                Vector3 cameraPlayerPosition = player.gameplayCamera.transform.position;
                if ((cameraPlayerPosition - ourPos).sqrMagnitude > range * range)
                {
                    continue;
                }

                // Nothing in between to break line of sight ?
                if (!ignoreLOS && Physics.Linecast(ourEyePos, cameraPlayerPosition, instanceSOR.collidersAndRoomMaskAndDefault))
                {
                    continue;
                }

                Vector3 vectorLethalBotToPlayer = cameraPlayerPosition - ourEyePos;
                float distanceLethalBotToPlayer = vectorLethalBotToPlayer.magnitude;
                if ((ignoreLOS || Vector3.Angle(ourEyeForward, vectorLethalBotToPlayer) < width || (proximityAwareness != -1 && distanceLethalBotToPlayer < proximityAwareness))
                    && distanceLethalBotToPlayer < currentClosestDistance)
                {
                    // Target in FOV or proximity awareness range
                    // Make sure we can actually reach them
                    if (IsValidPathToTarget(player.transform.position))
                    { 
                        currentClosestDistance = distanceLethalBotToPlayer;
                        closestPlayer = player;
                    }
                }
            }

            return closestPlayer;
        }

        /// <summary>
        /// Check all conditions for deciding if an item is grabbable or not.
        /// </summary>
        /// <param name="grabbableObject">Item to check</param>
        /// <param name="enumGrabbable">Type of blacklist checks that should be done or skipped</param>
        /// <returns></returns>
        public bool IsGrabbableObjectGrabbable(GrabbableObject? grabbableObject, EnumGrabbableObjectCall enumGrabbable = EnumGrabbableObjectCall.Default)
        {
            if (enumGrabbable == EnumGrabbableObjectCall.Selling)
            {
                return IsGrabbableObjectSellable(grabbableObject);
            }

            if (grabbableObject == null
                || !grabbableObject.gameObject.activeSelf)
            {
                return false;
            }

            NetworkObject? networkObject = grabbableObject.NetworkObject;
            if (networkObject == null
                || !networkObject.IsSpawned)
            {
                return false;
            }

            // If its held, check if we are reviving the player or not
            bool skipPathCheck = false;
            bool isHeld = grabbableObject.isHeld || grabbableObject.isPocketed;
            if (isHeld)
            {
                // Alright, if we are picking up a player to revive them, check if we are already holding their body!
                skipPathCheck = true;
                if (enumGrabbable != EnumGrabbableObjectCall.Reviving || !this.HasGrabbableObjectInInventory(grabbableObject, out _))
                {
                    return false;
                }
            }

            if (!grabbableObject.grabbable
                || grabbableObject.deactivated)
            {
                return false;
            }

            if (!GameNetworkManager.Instance.gameHasStarted 
                && !grabbableObject.itemProperties.canBeGrabbedBeforeGameStart 
                && StartOfRound.Instance.testRoom == null)
            {
                return false;
            }

            // Don't steal from loot bugs, unless the object was already stolen
            // FIXME: If the loot bugs are dead, the bots will still ignore their loot!
            if (grabbableObject.isInFactory)
            {
                if (LethalBotManager.DictHoardingBugItems.TryGetValue(grabbableObject, out HoarderBugItem? item)
                    && item.itemGrabbableObject == grabbableObject // Just to be safe, should always be true if the dictionary is managed correctly
                    && item.status != HoarderBugItemStatus.Any
                    && item.status != HoarderBugItemStatus.Stolen)
                {
                    // Lets not anger them now.....
                    return false;
                }
            }
            
            // Disabled as this never worked and is just a waste of CPU time.
            //RagdollGrabbableObject? ragdollGrabbableObject = grabbableObject as RagdollGrabbableObject;
            //if (ragdollGrabbableObject != null)
            //{
            //    if (ragdollGrabbableObject?.ragdoll?.attachedTo != null)
            //    {
            //        SpikeRoofTrap? attachedToTrap = ragdollGrabbableObject.ragdoll.attachedTo?.parent?.gameObject?.GetComponent<SpikeRoofTrap>();
            //        if (attachedToTrap != null)
            //        {
            //            // Don't try to grab bodies stuck in traps
            //            return false;
            //        }
            //    }
            //}

            // Item just dropped, should wait a bit before grab it again
            CaveDwellerPhysicsProp? caveDwellerGrabbableObject = grabbableObject as CaveDwellerPhysicsProp;
            if (!isHeld
                && (caveDwellerGrabbableObject == null
                || !caveDwellerGrabbableObject.caveDwellerScript.babyCrying)
                && DictJustDroppedItems.TryGetValue(grabbableObject, out float justDroppedItemTime))
            {
                if (Time.realtimeSinceStartup - justDroppedItemTime < Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS)
                {
                    return false;
                }

                // Remove redundant dictionary entry to save memory.
                DictJustDroppedItems.Remove(grabbableObject);
            }

            // Are we holding a two handed item and is the item we are grabbing two handed
            PlayerControllerB lethalBotController = NpcController.Npc;
            GrabbableObject? heldItem = lethalBotController.currentlyHeldObjectServer;
            if (heldItem != null && enumGrabbable != EnumGrabbableObjectCall.Reviving && heldItem.itemProperties.twoHanded)
            {
                // If the item requires one hand then we can set down our large item and pick up the small one!
                if (grabbableObject.itemProperties.twoHanded 
                    && (caveDwellerGrabbableObject == null 
                        || !caveDwellerGrabbableObject.caveDwellerScript.babyCrying))
                {
                    return false;
                }
            }

            // Is item too close to entrance (with config option enabled)
            Vector3 itemPos = grabbableObject.transform.position;
            bool shouldReturnToShip = this.State?.ShouldReturnToShip() ?? false;
            bool botIsTransferringItems = LethalBotManager.Instance.LootTransferPlayers.Contains(lethalBotController);
            if (((!Plugin.Config.GrabItemsNearEntrances.Value 
                    && !LethalBotManager.Instance.AreAllHumanPlayersDead())
                    || (LethalBotManager.Instance.LootTransferPlayers.Count > 0 && !shouldReturnToShip))
                && !botIsTransferringItems 
                && enumGrabbable != EnumGrabbableObjectCall.Reviving)
            {
                for (int i = 0; i < EntrancesTeleportArray.Length; i++)
                {
                    EntranceTeleport entrance = EntrancesTeleportArray[i];
                    if (entrance == null) continue;

                    if (entrance.isEntranceToBuilding 
                        && (itemPos - entrance.entrancePoint.position).sqrMagnitude < Const.DISTANCE_ITEMS_TO_ENTRANCE * Const.DISTANCE_ITEMS_TO_ENTRANCE)
                    {
                        return false;
                    }
                }
            }

            // Is the item reachable with the agent pathfind ? (only owner knows and calculate) real position of ai lethalBot)
            Vector3 objectPos = RoundManager.Instance.GetNavMeshPosition(itemPos, default, lethalBotController.grabDistance, NavMesh.AllAreas);
            if (IsOwner
                && !skipPathCheck
                && !this.IsValidPathToTarget(objectPos, false, maxRangeToEnd: lethalBotController.grabDistance))
            {
                //Plugin.LogDebug($"object {grabbableObject.name} pathfind is not reachable");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check all conditions for deciding if an item is sellable or not.
        /// </summary>
        /// <param name="grabbableObject">Item to check</param>
        /// <param name="ignoreHeldFlag"></param>
        /// <param name="skipPathCheck"></param>
        /// <returns></returns>
        public bool IsGrabbableObjectSellable(GrabbableObject? grabbableObject, bool ignoreHeldFlag = false, bool skipPathCheck = false)
        {
            if (grabbableObject == null
                || !grabbableObject.gameObject.activeSelf)
            {
                return false;
            }

            NetworkObject? networkObject = grabbableObject.NetworkObject;
            if (networkObject == null
                || !networkObject.IsSpawned)
            {
                return false;
            }

            if ((!ignoreHeldFlag && (grabbableObject.isHeld || grabbableObject.isPocketed))
                || !grabbableObject.grabbable
                || grabbableObject.deactivated 
                || !ItemsManager.IsItemScrap(grabbableObject))
            {
                return false;
            }

            // Don't sell gift boxes!!!!
            // Don't sell shotguns!!!!
            // Don't sell shovels!!!!
            // Don't sell gun ammo!!!!
            // And don't sell knives!!!!
            if (grabbableObject is GiftBoxItem 
                || grabbableObject is ShotgunItem 
                || grabbableObject is Shovel
                || grabbableObject is GunAmmo
                || grabbableObject is KnifeItem)
            {
                return false;
            }

            // Bots will only sell bodies if we don't have enough items to reach the profit quota
            RagdollGrabbableObject? ragdollGrabbableObject = grabbableObject as RagdollGrabbableObject;
            if (ragdollGrabbableObject != null 
                && ragdollGrabbableObject.ragdoll != null)
            {
                // Welp, are we desperate for cash?
                if (LethalBotManager.GetValueOfAllScrapOnShip(this) > 0 
                    || LethalBotManager.HaveWeFulfilledTheProfitQuota())
                {
                    return false; // We have enough scrap on the ship, don't sell bodies!
                }
            }

            // Is the object blacklisted from being sold
            if (LethalBotManager.Instance.blacklistedItems.Contains(grabbableObject))
            {
                return false;
            }

            // Are we holding a two handed item and is the item we are grabbing two handed
            if (!ignoreHeldFlag)
            {
                GrabbableObject? heldItem = this.HeldItem;
                if (heldItem != null && heldItem.itemProperties.twoHanded)
                {
                    // If the item requires one hand then we can set down our large item and pick up the small one!
                    if (grabbableObject.itemProperties.twoHanded)
                    {
                        return false;
                    }
                }
            }

            // Is the item reachable with the agent pathfind ? (only owner knows and calculate) real position of ai lethalBot)
            Vector3 objectPos = RoundManager.Instance.GetNavMeshPosition(grabbableObject.transform.position, default, NpcController.Npc.grabDistance, NavMesh.AllAreas);
            if (IsOwner 
                && !skipPathCheck
                && !this.IsValidPathToTarget(objectPos, false, maxRangeToEnd: NpcController.Npc.grabDistance))
            {
                //Plugin.LogDebug($"object {grabbableObject.name} pathfind is not reachable");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Trim dictionnary if too large, trim only the dropped item since a long time
        /// </summary>
        internal static void TrimDictJustDroppedItems()
        {
            if (DictJustDroppedItems != null && DictJustDroppedItems.Count > 20)
            {
                Plugin.LogDebug($"TrimDictJustDroppedItems Count{DictJustDroppedItems.Count}");
                var itemsToClean = DictJustDroppedItems
                    .Where(x => Time.realtimeSinceStartup - x.Value > Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS)
                    .Select(x => x.Key)
                    .ToList();
                foreach (var item in itemsToClean)
                {
                    DictJustDroppedItems.Remove(item);
                }
            }
        }

        public void SetLethalBotInElevator()
        {
            if (this.NpcController == null)
            {
                return;
            }

            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if (base.IsOwner && lethalBotController.isPlayerControlled)
            {
                // Do the same ship checks as the base game.
                StartOfRound instanceSOR = StartOfRound.Instance;
                Vector3 playerPos = lethalBotController.transform.position;
                Bounds shipBounds = instanceSOR.shipBounds.bounds;
                Bounds shipInnerRoomBounds = instanceSOR.shipInnerRoomBounds.bounds;
                if (!instanceSOR.inShipPhase && instanceSOR.shipDoorsEnabled && !instanceSOR.suckingPlayersOutOfShip)
                {
                    const float OUT_OF_BOUNDS_Y_RANGE = -600f;
                    if (lethalBotController.transform.position.y < OUT_OF_BOUNDS_Y_RANGE)
                    {
                        lethalBotController.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Gravity);
                    }
                    else if (NpcController.IsTouchingGround)
                    {
                        bool isInElevator = shipBounds.Contains(playerPos + Vector3.up * 0.25f);
                        if (lethalBotController.isInElevator != isInElevator)
                        {
                            lethalBotController.isInElevator = isInElevator;
                            if (!isInElevator)
                            {
                                lethalBotController.isInHangarShipRoom = false;
                                this.isInsidePlayerShip = false;
                            }
                            lethalBotController.SetAllItemsInElevator(inShipRoom: lethalBotController.isInHangarShipRoom, inElevator: isInElevator);
                        }
                        else
                        {
                            bool isInHangarShipRoom = shipInnerRoomBounds.Contains(playerPos + Vector3.up * 0.25f);
                            if (isInElevator && lethalBotController.isInHangarShipRoom != isInHangarShipRoom)
                            {
                                lethalBotController.isInHangarShipRoom = isInHangarShipRoom;
                                this.isInsidePlayerShip = isInHangarShipRoom;
                                lethalBotController.SetAllItemsInElevator(inShipRoom: lethalBotController.isInHangarShipRoom, inElevator: isInElevator);
                            }
                        }
                    }
                }
                else if (!instanceSOR.suckingPlayersOutOfShip)
                {
                    // Cleanup, in case we were previously fired!
                    if (choseRandomFlyDirForPlayer)
                    {
                        choseRandomFlyDirForPlayer = false;
                        lethalBotController.TeleportPlayer(instanceSOR.GetPlayerSpawnPosition((int)lethalBotController.playerClientId, false));
                    }

                    // Just in case.....
                    lethalBotController.isInElevator = true;
                    lethalBotController.isInHangarShipRoom = true;
                    this.isInsidePlayerShip = true;
                    if (!this.isOutside)
                    {
                        this.SetEnemyOutside(true);
                    }

                    if (instanceSOR.testRoom == null && !shipInnerRoomBounds.Contains(playerPos + Vector3.up * 0.25f))
                    {
                        lethalBotController.TeleportPlayer(instanceSOR.GetPlayerSpawnPosition((int)lethalBotController.playerClientId, true));
                    }
                }

                // Physics regions
                int priority = 0;
                Transform? transform = null;
                NetworkObject networkObject = null!;
                List<PlayerPhysicsRegion> currentLethalBotPhysicsRegions = NpcController.CurrentLethalBotPhysicsRegions;
                for (int i = 0; i < currentLethalBotPhysicsRegions.Count; i++)
                {
                    PlayerPhysicsRegion playerPhysicsRegion = currentLethalBotPhysicsRegions[i];
                    if (playerPhysicsRegion.priority > priority)
                    {
                        priority = playerPhysicsRegion.priority;
                        transform = playerPhysicsRegion.physicsTransform;
                        networkObject = playerPhysicsRegion.parentNetworkObject;
                    }
                }
                if (lethalBotController.isInElevator && priority <= 0)
                {
                    transform = null;
                }
                lethalBotController.physicsParent = transform;

                if (lethalBotController.overridePhysicsParent != null)
                {
                    if (lethalBotController.overridePhysicsParent != lethalBotController.lastSyncedPhysicsParent)
                    {
                        lethalBotController.parentedToElevatorLastFrame = false;
                        lethalBotController.lastSyncedPhysicsParent = lethalBotController.overridePhysicsParent;
                        this.ReParentLethalBot(lethalBotController.overridePhysicsParent);
                        lethalBotController.UpdatePlayerPhysicsParentServerRpc(lethalBotController.thisPlayerBody.localPosition, lethalBotController.overridePhysicsParent.GetComponent<NetworkObject>(), isOverride: true, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
                    }
                }
                else if (lethalBotController.physicsParent != null)
                {
                    if (lethalBotController.physicsParent != lethalBotController.lastSyncedPhysicsParent)
                    {
                        lethalBotController.parentedToElevatorLastFrame = false;
                        lethalBotController.lastSyncedPhysicsParent = lethalBotController.physicsParent;
                        this.ReParentLethalBot(lethalBotController.physicsParent);
                        lethalBotController.UpdatePlayerPhysicsParentServerRpc(lethalBotController.thisPlayerBody.localPosition, networkObject.GetComponent<NetworkObject>(), isOverride: false, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
                    }
                }
                else
                {
                    if (lethalBotController.lastSyncedPhysicsParent != null)
                    {
                        lethalBotController.lastSyncedPhysicsParent = null;
                        this.ReParentLethalBot(lethalBotController.playersManager.playersContainer);
                        lethalBotController.RemovePlayerPhysicsParentServerRpc(lethalBotController.thisPlayerBody.localPosition, removeOverride: false, removeBoth: true, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
                    }
                    if (lethalBotController.isInElevator)
                    {
                        if (!lethalBotController.parentedToElevatorLastFrame)
                        {
                            lethalBotController.parentedToElevatorLastFrame = true;
                            this.ReParentLethalBot(lethalBotController.playersManager.elevatorTransform);
                        }
                    }
                    else if (lethalBotController.parentedToElevatorLastFrame)
                    {
                        lethalBotController.parentedToElevatorLastFrame = false;
                        this.ReParentLethalBot(lethalBotController.playersManager.playersContainer);
                    }
                }
            }
            lethalBotController.previousElevatorPosition = lethalBotController.playersManager.elevatorTransform.position;
        }

        /// <summary>
        /// Checks if the given object is blacklisted
        /// </summary>
        /// <param name="grabbableObjectToEvaluate">The object to check</param>
        /// <param name="enumGrabbable">Type of blacklist checks that should be done or skipped</param>
        /// <returns>true: this object is blacklisted. false: we are allowed to pick up this object</returns>
        public bool IsGrabbableObjectBlackListed(GrabbableObject grabbableObjectToEvaluate, EnumGrabbableObjectCall enumGrabbable = EnumGrabbableObjectCall.Default)
        {
            // Are we returning to the ship?
            bool shouldReturnToShip = this.State?.ShouldReturnToShip() ?? false;

            // Bee nest
            if (!Plugin.Config.GrabBeesNest.Value 
                && enumGrabbable != EnumGrabbableObjectCall.Selling
                && grabbableObjectToEvaluate.name.Contains("RedLocustHive"))
            {
                return true;
            }

            // Dead bodies
            if (!Plugin.Config.GrabDeadBodies.Value
                && enumGrabbable != EnumGrabbableObjectCall.Selling
                && enumGrabbable != EnumGrabbableObjectCall.Reviving
                && grabbableObjectToEvaluate is RagdollGrabbableObject deadBody
                && deadBody.ragdoll != null)
            {
                return true;
            }

            // Apparatus
            if (!Plugin.Config.GrabDockedApparatus.Value
                && enumGrabbable != EnumGrabbableObjectCall.Selling 
                && grabbableObjectToEvaluate is LungProp apparatus 
                && apparatus.isLungDocked)
            {
                return true;
            }

            // Maneater
            if (grabbableObjectToEvaluate is CaveDwellerPhysicsProp caveDwellerGrabbableObject) // Was gameObject.name.Contains("CaveDwellerEnemy"), but CaveDwellerPhysicsProp is better and more reliable
            {
                // The host has config options to allow or disallow the bot to grab the maneater baby
                if (!Plugin.Config.TakeCareOfManeaterBaby.Value)
                {
                    Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will not pickup the maneater, pickup is disabled!");
                    return true;
                }

                // Make sure the maneater baby is vaild
                CaveDwellerAI? caveDwellerAI = caveDwellerGrabbableObject.caveDwellerScript;
                if (caveDwellerAI == null)
                {
                    Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will not pickup the maneater, ai has not spawned!");
                    return true;
                }

                // The host only wants the bot to grab the maneater baby if it is crying
                if (!Plugin.Config.AdvancedManeaterBabyAI.Value)
                {
                    Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will use basic AI for maneater!");
                    if (!caveDwellerAI.babyCrying)
                    {
                        Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will not pickup the maneater, maneater is not crying!");
                        return true;
                    }
                    return false;
                }

                // If the bot is not liked by the maneater baby, then only grab it if the maneater baby is crying
                BabyPlayerMemory? playerMemory = caveDwellerAI.GetBabyMemoryOfPlayer(NpcController.Npc);
                if ((playerMemory == null 
                    || playerMemory.likeMeter < 0.1f) 
                    && !caveDwellerAI.babyCrying)
                {
                    Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will not pickup the maneater, maneater is not crying and doesn't like us!");
                    return true;
                }
            }

            // Giant Kiwi/Sapsucker eggs
            if (enumGrabbable != EnumGrabbableObjectCall.Selling && grabbableObjectToEvaluate is KiwiBabyItem egg)
            {
                GiantKiwiAI? giantKiwiAI = egg.mamaAI;
                if (giantKiwiAI == null || giantKiwiAI.isEnemyDead)
                {
                    return false; // Parent is dead, allow pickup
                }
                return true; // Don't allow pickup if parent is alive
            }

            // Wheelbarrow
            if ((!Plugin.Config.GrabWheelbarrow.Value || enumGrabbable == EnumGrabbableObjectCall.Selling)
                && grabbableObjectToEvaluate.name.Contains("Wheelbarrow"))
            {
                return true;
            }

            // ShoppingCart
            if ((!Plugin.Config.GrabShoppingCart.Value || enumGrabbable == EnumGrabbableObjectCall.Selling)
                && grabbableObjectToEvaluate.name.Contains("ShoppingCart"))
            {
                return true;
            }

            // ZedDogs!
            if (enumGrabbable == EnumGrabbableObjectCall.Selling && grabbableObjectToEvaluate.name.Contains("ZeddogPlushie"))
            {
                return true;
            }

            // Don't pickup extended extention ladders
            if (grabbableObjectToEvaluate is ExtensionLadderItem extensionLadder && (!shouldReturnToShip || extensionLadder.ladderActivated))
            {
                return true;
            }

            // Don't pickup radar boosters unless we are returning to the ship
            if (grabbableObjectToEvaluate is RadarBoosterItem && !shouldReturnToShip)
            {
                return true;
            }

            // Lockpickers in use!
            if (grabbableObjectToEvaluate is LockPicker lockPicker && lockPicker.isPickingLock)
            {
                return true;
            }

            // Don't pickup used flashbangs!
            if (grabbableObjectToEvaluate is StunGrenadeItem flashbang && ((flashbang.pinPulled && !flashbang.explodeOnCollision) || flashbang.hasExploded))
            {
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsGrabbableObjectInContainerMod(GrabbableObject grabbableObject)
        {
            return CustomItemBehaviourLibrary.AbstractItems.ContainerBehaviour.CheckIfItemInContainer(grabbableObject);
        }

        internal static bool IsGrabbaleObjectInSelfSortingStorage(GrabbableObject grabbableObject)
        {
            return Plugin.IsModSelfSortingStorageLoaded && LethalBotManager.ItemsInSelfSortingStorage.Contains(grabbableObject);
        }

        internal static bool IsGrabbableObjectHeldByPikminMod(GrabbableObject grabbableObject)
        {
            HashSet<LethalMin.PikminItem>? listPickMinItems = LethalMin.PikminManager.instance?.PikminItems;
            if (listPickMinItems == null
                || listPickMinItems.Count == 0)
            {
                return false;
            }

            foreach (var item in listPickMinItems)
            {
                if(item != null
                    && item.ItemScript == grabbableObject
                    && item.PikminOnItem.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void InitImportantColliders()
        {
            if (dictColliderToBridge == null)
            {
                dictColliderToBridge = new Dictionary<Collider, BridgeTrigger>();
            }
            else
            {
                dictColliderToBridge.Clear();
            }

            // Find and cache the colliders associated with bridges!
            BridgeTrigger[] bridgeTriggers = Object.FindObjectsByType<BridgeTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var bridge in bridgeTriggers)
            {
                // It was the animator that held the bridge colliders, not the physics parts container
                HashSet<Collider> fallenBridgeColliders = bridge.fallenBridgeColliders.ToHashSet();
                Collider[] bridgePhysicsPartsContainerComponents = bridge.bridgeAnimator.gameObject.GetComponentsInChildren<Collider>();
                foreach (var collider in bridgePhysicsPartsContainerComponents)
                {
                    //if (collider.name == "Mesh")
                    //{
                    //    continue;
                    //}

                    if (!dictColliderToBridge.ContainsKey(collider) 
                        && !fallenBridgeColliders.Contains(collider))
                    {
                        dictColliderToBridge.Add(collider, bridge);
                    }
                }
            }

            // This function was a Godsend when debugging issues with fallen bridges and colliders
            //foreach (var a in dictColliderToBridge)
            //{
            //    Plugin.LogDebug($"dictComponentByCollider {a.Key} {a.Value}");
            //    //ComponentUtil.ListAllComponents(a.Value.gameObject);
            //    //ComponentUtil.ListAllComponents(a.Value.bridgePhysicsPartsContainer.gameObject);
            //    //ComponentUtil.ListAllColliders(a.Value.gameObject);
            //    //ComponentUtil.ListAllColliders(a.Key.gameObject);
            //    ComponentUtil.ListAllColliders(a.Value.bridgeAnimator.gameObject);
            //    ComponentUtil.ListAllColliders(a.Value.bridgePhysicsPartsContainer.gameObject);

            //    Plugin.LogDebug($"Fallen bridge colliders for bridge {a.Value}:");
            //    foreach (var b in a.Value.fallenBridgeColliders)
            //    {
            //        Plugin.LogDebug($"fallenBridgeColliders {b}");
            //    }
            //}
        }

        public void HideShowModelReplacement(bool show)
        {
            NpcController.Npc.gameObject
                .GetComponent<ModelReplacement.BodyReplacementBase>()?
                .SetAvatarRenderers(show);
        }

        public void HideShowReplacementModelOnlyBody(bool show)
        {
            NpcController.Npc.thisPlayerModel.enabled = show;
            NpcController.Npc.thisPlayerModelLOD1.enabled = show;
            NpcController.Npc.thisPlayerModelLOD2.enabled = show;

            int layer = show ? 0 : 31;
            NpcController.Npc.thisPlayerModel.gameObject.layer = layer;
            NpcController.Npc.thisPlayerModelLOD1.gameObject.layer = layer;
            NpcController.Npc.thisPlayerModelLOD2.gameObject.layer = layer;
            NpcController.Npc.thisPlayerModelArms.gameObject.layer = layer;

            ModelReplacement.BodyReplacementBase? bodyReplacement = NpcController.Npc.gameObject.GetComponent<ModelReplacement.BodyReplacementBase>();
            if (bodyReplacement == null)
            {
                HideShowLevelStickerBetaBadge(show);
                return;
            }

            GameObject? model = bodyReplacement.replacementModel;
            if (model == null)
            {
                return;
            }

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = show;
            }
        }

        public void HideShowLevelStickerBetaBadge(bool show)
        {
            /*MeshRenderer[] componentsInChildren = NpcController.Npc.gameObject.GetComponentsInChildren<MeshRenderer>();
            (from x in componentsInChildren
             where x.gameObject.name == "LevelSticker"
             select x).First<MeshRenderer>().enabled = show;
            (from x in componentsInChildren
             where x.gameObject.name == "BetaBadge"
             select x).First<MeshRenderer>().enabled = show;*/
            NpcController.Npc.playerBetaBadgeMesh.enabled = show;
            NpcController.Npc.playerBadgeMesh.gameObject.GetComponent<MeshRenderer>().enabled = show;
        }

        #region Voices

        public void UpdateLethalBotVoiceEffects()
        {
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            int lethalBotPlayerClientID = (int)lethalBotController.playerClientId;
            PlayerControllerB spectatedPlayerScript;
            if (GameNetworkManager.Instance.localPlayerController.isPlayerDead && GameNetworkManager.Instance.localPlayerController.spectatedPlayerScript != null)
            {
                spectatedPlayerScript = GameNetworkManager.Instance.localPlayerController.spectatedPlayerScript;
            }
            else
            {
                spectatedPlayerScript = GameNetworkManager.Instance.localPlayerController;
            }

            bool walkieTalkie = lethalBotController.speakingToWalkieTalkie
                                && spectatedPlayerScript.holdingWalkieTalkie
                                && lethalBotController != spectatedPlayerScript;
            if (lethalBotController.isPlayerDead)
            {
                this.NpcController.AudioLowPassFilterComponent.enabled = false;
                this.NpcController.AudioHighPassFilterComponent.enabled = false;
                this.creatureVoice.panStereo = 0f;
                SoundManager.Instance.playerVoicePitchTargets[lethalBotPlayerClientID] = this.LethalBotIdentity.Voice.VoicePitch;
                SoundManager.Instance.SetPlayerPitch(this.LethalBotIdentity.Voice.VoicePitch, lethalBotPlayerClientID);
                if (GameNetworkManager.Instance.localPlayerController.isPlayerDead)
                {
                    this.creatureVoice.spatialBlend = 0f;
                    this.creatureVoice.volume = this.LethalBotIdentity.Voice.Volume;
                }
                else
                {
                    this.creatureVoice.spatialBlend = 1f;
                    this.creatureVoice.volume = 0f;
                }
            }
            else
            {
                AudioLowPassFilter audioLowPassFilter = this.NpcController.AudioLowPassFilterComponent;
                OccludeAudio occludeAudio = this.NpcController.OccludeAudioComponent;
                audioLowPassFilter.enabled = true;
                occludeAudio.overridingLowPass = (walkieTalkie || lethalBotController.voiceMuffledByEnemy);
                this.NpcController.AudioHighPassFilterComponent.enabled = walkieTalkie;
                if (!walkieTalkie)
                {
                    this.creatureVoice.spatialBlend = 1f;
                    this.creatureVoice.bypassListenerEffects = false;
                    this.creatureVoice.bypassEffects = false;
                    this.creatureVoice.outputAudioMixerGroup = SoundManager.Instance.playerVoiceMixers[lethalBotPlayerClientID];
                    audioLowPassFilter.lowpassResonanceQ = 1f;
                }
                else
                {
                    this.creatureVoice.spatialBlend = 0f;
                    if (GameNetworkManager.Instance.localPlayerController.isPlayerDead)
                    {
                        this.creatureVoice.panStereo = 0f;
                        this.creatureVoice.outputAudioMixerGroup = SoundManager.Instance.playerVoiceMixers[lethalBotPlayerClientID];
                        this.creatureVoice.bypassListenerEffects = false;
                        this.creatureVoice.bypassEffects = false;
                    }
                    else
                    {
                        this.creatureVoice.panStereo = 0.4f;
                        this.creatureVoice.bypassListenerEffects = false;
                        this.creatureVoice.bypassEffects = false;
                        this.creatureVoice.outputAudioMixerGroup = SoundManager.Instance.playerVoiceMixers[lethalBotPlayerClientID];
                    }
                    occludeAudio.lowPassOverride = 4000f;
                    audioLowPassFilter.lowpassResonanceQ = 3f;
                }
                if (GameNetworkManager.Instance.localPlayerController.isPlayerDead)
                {
                    this.creatureVoice.volume = this.LethalBotIdentity.Voice.Volume * 0.8f;
                }
                else
                {
                    this.creatureVoice.volume = this.LethalBotIdentity.Voice.Volume;
                }
            }
        }


        [ServerRpc(RequireOwnership = false)]
        public void PlayAudioServerRpc(string smallPathAudioClip, EnumTalkativeness enumTalkativeness, EnumResponsiveness enumResponsiveness)
        {
            PlayAudioClientRpc(smallPathAudioClip, enumTalkativeness, enumResponsiveness);
        }

        [ClientRpc]
        private void PlayAudioClientRpc(string smallPathAudioClip, EnumTalkativeness enumTalkativeness, EnumResponsiveness enumResponsiveness)
        {
            if (enumTalkativeness == Plugin.Config.Talkativeness.Value || enumResponsiveness == Plugin.Config.Responsiveness.Value || LethalBotIdentity.Voice.CanPlayAudioAfterCooldown(LethalBotIdentity.Voice.LastVoiceState))
            {
                Managers.AudioManager.Instance.PlayAudio(smallPathAudioClip, LethalBotIdentity.Voice);
            }
        }

        #endregion

        #region TeleportLethalBot RPC

        /// <summary>
        /// Teleport lethalBot and send to server to call client to sync
        /// </summary>
        /// <param name="pos">Position destination</param>
        /// <param name="setOutside">Is the teleport destination outside of the facility</param>
        /// <param name="targetEntrance">Is the lethalBot actually using entrance to teleport ?</param>
        /// <param name="withRotation"></param>
        /// <param name="rot"></param>
        /// <param name="allowInteractTrigger"></param>
        /// <param name="skipNavMeshCheck"></param>
        public void SyncTeleportLethalBot(Vector3 pos, bool? setOutside = null, EntranceTeleport? targetEntrance = null, bool withRotation = false, float rot = 0f, bool allowInteractTrigger = false, bool skipNavMeshCheck = false)
        {
            if (!IsOwner)
            {
                return;
            }
            TeleportLethalBot(pos, setOutside, targetEntrance, withRotation, rot, allowInteractTrigger, skipNavMeshCheck);
            if (targetEntrance != null)
            {
                if (targetEntrance.NetworkObject == null || !targetEntrance.NetworkObject.IsSpawned)
                {
                    Plugin.LogWarning($"{NpcController.Npc.playerUsername}: Tried to teleport using an unspawned object! Networking to other clients, but without entrance sounds!");
                    TeleportLethalBotServerRpc(new TeleportLethalBotNetworkSerializable()
                    {
                        Pos = pos,
                        SetOutside = setOutside,
                        WithRotation = withRotation,
                        Rot = rot,
                        AllowInteractTrigger = allowInteractTrigger,
                        SkipNavMeshCheck = skipNavMeshCheck
                    });
                    return;
                }

                TeleportLethalBotServerRpc(new TeleportLethalBotNetworkSerializable()
                {
                    Pos = pos,
                    SetOutside = setOutside,
                    WithRotation = withRotation,
                    Rot = rot,
                    TargetEntrance = targetEntrance.NetworkObject,
                    AllowInteractTrigger = allowInteractTrigger,
                    SkipNavMeshCheck = skipNavMeshCheck
                });
            }
            else
            {
                TeleportLethalBotServerRpc(new TeleportLethalBotNetworkSerializable()
                {
                    Pos = pos,
                    SetOutside = setOutside,
                    WithRotation = withRotation,
                    Rot = rot,
                    AllowInteractTrigger = allowInteractTrigger,
                    SkipNavMeshCheck = skipNavMeshCheck
                });
            }
        }
        /// <summary>
        /// Server side, call clients to sync teleport lethalBot
        /// </summary>
        /// <param name="teleportData"></param>
        [ServerRpc]
        private void TeleportLethalBotServerRpc(TeleportLethalBotNetworkSerializable teleportData)
        {
            TeleportLethalBotClientRpc(teleportData);
        }
        /// <summary>
        /// Client side, teleport lethalBot on client, only for not the owner
        /// </summary>
        /// <param name="teleportLethalBotNetworkSerializable"></param>
        [ClientRpc]
        private void TeleportLethalBotClientRpc(TeleportLethalBotNetworkSerializable teleportLethalBotNetworkSerializable)
        {
            if (IsOwner)
            {
                return;
            }

            // Its ok if we fail to get the entrance as we only use it for sound effects!
            NetworkObjectReference? targetEntraceNetworkObject = teleportLethalBotNetworkSerializable.TargetEntrance;
            EntranceTeleport? targetEntrance = null;
            if (targetEntraceNetworkObject.HasValue)
            {
                if (targetEntraceNetworkObject.Value.TryGet(out NetworkObject entranceNetworked, null))
                {
                    targetEntrance = entranceNetworked.GetComponent<EntranceTeleport>();
                    if (targetEntrance == null)
                    {
                        Plugin.LogWarning("Failed to retrieve EntranceTeleport for teleportation, sound effects will not play.");
                    }
                }
                else
                {
                    Plugin.LogWarning("Failed to retrieve EntranceTeleport for teleportation, sound effects will not play.");
                }
            }

            TeleportLethalBot(teleportLethalBotNetworkSerializable.Pos, 
                teleportLethalBotNetworkSerializable.SetOutside, 
                targetEntrance, 
                teleportLethalBotNetworkSerializable.WithRotation, 
                teleportLethalBotNetworkSerializable.Rot, 
                teleportLethalBotNetworkSerializable.AllowInteractTrigger, 
                teleportLethalBotNetworkSerializable.SkipNavMeshCheck);
        }

        /// <summary>
        /// Teleport the lethalBot.
        /// </summary>
        /// <remarks>
        /// TODO: Bots should really use the entrance teleport code when using the entrance 
        /// rather than this hack!
        /// </remarks>
        /// <param name="pos">Position destination</param>
        /// <param name="setOutside">Is the teleport destination outside of the facility</param>
        /// <param name="targetEntrance">Is the lethalBot actually using entrance to teleport ?</param>
        /// <param name="withRotation"></param>
        /// <param name="rot"></param>
        /// <param name="allowInteractTrigger"></param>
        /// <param name="skipNavMeshCheck"></param>
        public void TeleportLethalBot(Vector3 pos, bool? setOutside = null, EntranceTeleport? targetEntrance = null, bool withRotation = false, float rot = 0f, bool allowInteractTrigger = false, bool skipNavMeshCheck = false)
        {
            // teleport body
            TeleportAgentAIAndBody(pos, skipNavMeshCheck);

            // Removed since bots are considered "other clients"!
            /*if (base.IsOwner && !allowInteractTrigger)
            {
                NpcController.Npc.CancelSpecialTriggerAnimations();
            }*/
            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if (!allowInteractTrigger && lethalBotController.currentTriggerInAnimationWith != null)
            {
                lethalBotController.CancelSpecialTriggerAnimations();
                this.State?.LethalBotInteraction?.StopHoldInteractionOnTrigger();
                if (useInteractTriggerCoroutine != null)
                {
                    StopCoroutine(useInteractTriggerCoroutine);
                    useInteractTriggerCoroutine = null;
                }
            }

            if ((bool)lethalBotController.inAnimationWithEnemy)
            {
                lethalBotController.inAnimationWithEnemy.CancelSpecialAnimationWithPlayer();
            }

            if (withRotation)
            {
                lethalBotController.transform.localEulerAngles = new Vector3(0f, rot, 0f);
            }

            // Set AI outside or inside dungeon
            if (!setOutside.HasValue)
            {
                setOutside = !targetEntrance?.isEntranceToBuilding ?? pos.y >= -80f;
            }

            lethalBotController.isInsideFactory = !setOutside.Value;
            if (this.isOutside != setOutside.Value)
            {
                this.SetEnemyOutside(setOutside.Value);
            }

            // Debug logs for the purpose of checking if we are properly setting the outside/inside
            // attribute of lethalBot
            Plugin.LogDebug($"Teleport lethalBot {lethalBotController.playerUsername} to {pos} outside {setOutside.Value}");
            if (targetEntrance != null)
                Plugin.LogDebug($"Entrance type: {targetEntrance.isEntranceToBuilding}");
            Plugin.LogDebug($"Is lethalBot in the facility: {lethalBotController.isInsideFactory}");

            // Using main entrance or fire exits ?
            if (targetEntrance != null)
            {
                Transform thisPlayerBody = lethalBotController.thisPlayerBody;
                thisPlayerBody.eulerAngles = new Vector3(thisPlayerBody.eulerAngles.x, targetEntrance.exitScript.entrancePoint.eulerAngles.y, thisPlayerBody.eulerAngles.z);
                TimeSinceTeleporting = Time.timeSinceLevelLoad;
                targetEntrance.timeAtLastUse = Time.realtimeSinceStartup;
                targetEntrance.FinishOpeningEntrance(playShutAudio: false);
                //EntranceTeleport entranceTeleport = RoundManager.FindMainEntranceScript(setOutside.Value);
                //audioReverbPresets.audioPresets[targetEntrance.audioReverbPreset].ChangeAudioReverbForPlayer(NpcController.Npc);
                if (SingletonManager.AudioReverbPresets.TryGet(out AudioReverbPresets? audioReverbPresets) && targetEntrance.audioReverbPreset != -1)
                {
                    audioReverbPresets.audioPresets[targetEntrance.audioReverbPreset].ChangeAudioReverbForPlayer(lethalBotController);
                    if (targetEntrance.entrancePointAudio != null)
                    {
                        targetEntrance.PlayAudioAtTeleportPositions();
                    }
                }

                if (AIState.IsFrontEntrance(targetEntrance) && targetEntrance.isEntranceToBuilding)
                {
                    DunGenTileTracker tileTracker = this.DunGenTileTracker;
                    if (tileTracker != null)
                    {
                        tileTracker.SetToStartTile();
                    }
                }
            }

            if (!lethalBotController.isUnderwater && !lethalBotController.isSinking)
            {
                return;
            }
            Plugin.LogInfo($"Bot {lethalBotController.playerUsername} is sinking; disable all quicksand locally");
            for (int i = 0; i < QuicksandArray.Length; i++)
            {
                QuicksandTrigger quicksand = QuicksandArray[i];
                QuicksandTriggerPatch.QuicksandTriggerMonitor quicksandTriggerMonitor = QuicksandTriggerPatch.GetOrCreateMonitor(quicksand);
                if (quicksandTriggerMonitor.IsSinkingLethalBot(this))
                {
                    quicksand.OnExit(lethalBotController.gameObject.GetComponent<Collider>());
                    break;
                }
            }
        }

        /// <summary>
        /// Teleport the brain and body of lethalBot
        /// </summary>
        /// <param name="pos">The position to teleport the bot to</param>
        /// <param name="skipNavMeshCheck">Should the navmesh check be skipped</param>
        /// <param name="onlyAgent">Should on the <see cref="NavMeshAgent"/> and <see cref="LethalBotAI"/> be teleported</param>
        private void TeleportAgentAIAndBody(Vector3 pos, bool skipNavMeshCheck = false, bool onlyAgent = false)
        {
            Vector3 navMeshPosition = skipNavMeshCheck ? pos : RoundManager.Instance.GetNavMeshPosition(pos, default, 2.7f);
            serverPosition = navMeshPosition;

            if (!onlyAgent)
            {
                NpcController.Npc.transform.position = navMeshPosition;
            }

            this.transform.position = navMeshPosition;
            agent?.Warp(navMeshPosition);

            // For CullFactory mod
            GrabbableObject? heldItem = this.HeldItem;
            if (heldItem != null)
            {
                heldItem.EnableItemMeshes(true);
            }
        }

        public void SyncTeleportLethalBotVehicle(Vector3 pos, bool enteringVehicle, NetworkBehaviourReference networkBehaviourReferenceVehicle)
        {
            if (!IsOwner)
            {
                return;
            }
            TeleportLethalBotVehicle(pos, enteringVehicle, networkBehaviourReferenceVehicle);
            TeleportLethalBotVehicleServerRpc(pos, enteringVehicle, networkBehaviourReferenceVehicle);
        }

        [ServerRpc]
        private void TeleportLethalBotVehicleServerRpc(Vector3 pos, bool enteringVehicle, NetworkBehaviourReference networkBehaviourReferenceVehicle)
        {
            TeleportLethalBotVehicleClientRpc(pos, enteringVehicle, networkBehaviourReferenceVehicle);
        }
        [ClientRpc]
        private void TeleportLethalBotVehicleClientRpc(Vector3 pos, bool enteringVehicle, NetworkBehaviourReference networkBehaviourReferenceVehicle)
        {
            if (IsOwner)
            {
                return;
            }
            TeleportLethalBotVehicle(pos, enteringVehicle, networkBehaviourReferenceVehicle);
        }

        private void TeleportLethalBotVehicle(Vector3 pos, bool enteringVehicle, NetworkBehaviourReference networkBehaviourReferenceVehicle)
        {
            if (enteringVehicle)
            {
                if (agent != null)
                {
                    SetAgent(enabled: false);
                }
                NpcController.Npc.transform.position = pos;
                StateControllerMovement = EnumStateControllerMovement.Fixed;
            }
            else
            {
                TeleportLethalBot(pos);
                StateControllerMovement = EnumStateControllerMovement.FollowAgent;
            }

            NpcController.IsControllerInCruiser = enteringVehicle;

            if (NpcController.IsControllerInCruiser)
            {
                if (networkBehaviourReferenceVehicle.TryGet(out VehicleController vehicleController))
                {
                    // Attach lethalBot to vehicle
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} enters vehicle");
                    NpcController.Npc.physicsParent = vehicleController.transform;
                    this.ReParentLethalBot(vehicleController.transform);
                }
            }
            else
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} exits vehicle");
                NpcController.Npc.physicsParent = null;
                this.ReParentLethalBot(NpcController.Npc.playersManager.playersContainer);
            }
        }

        /// <summary>
        /// Sets if the enemy is outside, used to change <see cref="EnemyAI.allAINodes"/> to inside
        /// or outside nodes for <see cref="LethalBotSearchRoutine"/>s and for safe pathfinding checks!
        /// </summary>
        /// <remarks>
        /// This version has an edit to include the nodes inside the ship!
        /// </remarks>
        /// <param name="outside"></param>
        public override void SetEnemyOutside(bool outside = false)
        {
            base.SetEnemyOutside(outside);

            if (!outside) return;

            Transform[] shipNodes = StartOfRound.Instance.insideShipPositions;
            if (shipNodes == null || shipNodes.Length == 0)
            {
                Plugin.LogWarning("No insideShipPositions found!");
                return;
            }

            List<GameObject> nodeList = this.allAINodes.ToList();
            for (int i = 0; i < shipNodes.Length; i++)
            {
                var node = shipNodes[i];
                if (node != null)
                { 
                    nodeList.Add(node.gameObject); 
                }
                else
                {
                    Plugin.LogWarning("Encountered null insideShipPosition node!");
                }
            }

            this.allAINodes = nodeList.ToArray();
            if (searchForScrap != null && (searchForScrap.searchInProgress || searchForScrap.visitInProgress))
            {
                searchForScrap.StopSearch();
            }

            #if DEBUG
            for (int i = 0; i < this.allAINodes.Length; i++)
            {
                var node = this.allAINodes[i];
                if (node == null)
                {
                    Plugin.LogWarning($"[NULL] Node at index {i} is null!");
                }
                else
                {
                    Plugin.LogDebug($"Node {node.name} at index {i}!");
                }
            }
            #endif
        }

        #endregion

        #region AssignTargetAndSetMovingTo RPC

        /// <summary>
        /// Change the ownership of the lethalBot to the new player target,
        /// and set the destination to him.
        /// </summary>
        /// <param name="newTarget">New <c>PlayerControllerB to set the owner of lethalBot to.</c></param>
        public void SyncAssignTargetAndSetMovingTo(PlayerControllerB newTarget)
        {
            // If we are set to follow a bot, make sure our AI is on the correct client.
            // NEEDTOVALIDATE: Should I even do this logic for bots? Would it be better to skip the ownership check for them?
            LethalBotAI? isPlayerBot = LethalBotManager.Instance.GetLethalBotAI(newTarget);
            ulong targetClientId = isPlayerBot != null ? isPlayerBot.OwnerClientId : newTarget.actualClientId;
            if (this.OwnerClientId != targetClientId)
            {
                // Changes the ownership of the lethalBot, on server and client directly
                ChangeOwnershipOfEnemy(targetClientId);

                if (this.IsServer)
                {
                    SyncFromAssignTargetAndSetMovingToClientRpc(newTarget.playerClientId);
                }
                else
                {
                    SyncAssignTargetAndSetMovingToServerRpc(newTarget.playerClientId);
                }
            }
            else
            {
                AssignTargetAndSetMovingTo(newTarget.playerClientId);
            }
        }

        /// <summary>
        /// Server side, call clients to sync the set destination to new target player.
        /// </summary>
        /// <param name="playerid">Id of the new target player</param>
        [ServerRpc(RequireOwnership = false)]
        private void SyncAssignTargetAndSetMovingToServerRpc(ulong playerid)
        {
            SyncFromAssignTargetAndSetMovingToClientRpc(playerid);
        }

        /// <summary>
        /// Client side, set destination to the new target player
        /// </summary>
        /// <remarks>
        /// Change the state to <c>GetCloseToPlayerState</c>
        /// </remarks>
        /// <param name="playerid">Id of the new target player</param>
        [ClientRpc]
        private void SyncFromAssignTargetAndSetMovingToClientRpc(ulong playerid)
        {
            if (!IsOwner)
            {
                return;
            }

            AssignTargetAndSetMovingTo(playerid);
        }

        private void AssignTargetAndSetMovingTo(ulong playerid)
        {
            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[playerid];
            SetMovingTowardsTargetPlayer(targetPlayer);

            if (NpcController.IsControllerInCruiser)
            {
                this.State = new PlayerInCruiserState(this, this.GetVehicleCruiserTargetPlayerIsIn());
            }
            else if (this.State == null
                || this.State.GetAIState() != EnumAIStates.GetCloseToPlayer
                || this.targetPlayer != targetPlayer)
            {
                this.State = new GetCloseToPlayerState(this, targetPlayer);
            }
        }

        #endregion

        #region UpdatePlayerPosition RPC

        /// <summary>
        /// Sync the lethalBot position between server and clients.
        /// </summary>
        /// <param name="newPos">New position of the lethalBot controller</param>
        /// <param name="inElevator">Is the lethalBot on the ship ?</param>
        /// <param name="inShipRoom">Is the lethalBot in the ship room ?</param>
        /// <param name="exhausted">Is the lethalBot exhausted ?</param>
        /// <param name="isPlayerGrounded">Is the lethalBot player body touching the ground ?</param>
        public void SyncUpdateLethalBotPosition(Vector3 newPos, bool inElevator, bool inShipRoom, bool exhausted, bool isPlayerGrounded)
        {
            if (IsServer)
            {
                UpdateLethalBotPositionClientRpc(newPos, inElevator, inShipRoom, exhausted, isPlayerGrounded);
            }
            else
            {
                UpdateLethalBotPositionServerRpc(newPos, inElevator, inShipRoom, exhausted, isPlayerGrounded);
            }
        }

        /// <summary>
        /// Server side, call clients to sync the new position of the lethalBot
        /// </summary>
        /// <param name="newPos">New position of the lethalBot controller</param>
        /// <param name="inElevator">Is the lethalBot on the ship ?</param>
        /// <param name="inShipRoom">Is the lethalBot in the ship room ?</param>
        /// <param name="exhausted">Is the lethalBot exhausted ?</param>
        /// <param name="isPlayerGrounded">Is the lethalBot player body touching the ground ?</param>
        [ServerRpc(RequireOwnership = false)]
        private void UpdateLethalBotPositionServerRpc(Vector3 newPos, bool inElevator, bool inShipRoom, bool exhausted, bool isPlayerGrounded)
        {
            UpdateLethalBotPositionClientRpc(newPos, inElevator, inShipRoom, exhausted, isPlayerGrounded);
        }

        /// <summary>
        /// Update the lethalBot position if not owner of lethalBot, the owner move on his side the lethalBot.
        /// </summary>
        /// <param name="newPos">New position of the lethalBot controller</param>
        /// <param name="inElevator">Is the lethalBot on the ship ?</param>
        /// <param name="isInShip">Is the lethalBot in the ship room ?</param>
        /// <param name="exhausted">Is the lethalBot exhausted ?</param>
        /// <param name="isPlayerGrounded">Is the lethalBot player body touching the ground ?</param>
        [ClientRpc]
        private void UpdateLethalBotPositionClientRpc(Vector3 newPos, bool inElevator, bool isInShip, bool exhausted, bool isPlayerGrounded)
        {
            if (NpcController == null)
            {
                return;
            }

            PlayerControllerB lethalBotController = this.NpcController.Npc;
            lethalBotController.playersManager.gameStats.allPlayerStats[lethalBotController.playerClientId].stepsTaken++;
            lethalBotController.playersManager.gameStats.allStepsTaken++;
            bool flag = lethalBotController.currentFootstepSurfaceIndex == 8 && ((base.IsOwner && this.NpcController.IsTouchingGround) || isPlayerGrounded);
            if (lethalBotController.bleedingHeavily || flag)
            {
                lethalBotController.DropBlood(Vector3.down, lethalBotController.bleedingHeavily, flag);
            }
            lethalBotController.timeSincePlayerMoving = 0f;

            if (base.IsOwner)
            {
                // Only update if not owner
                // We already do this logic in the LateUpdate hook!
                return;
            }
            if (!inElevator)
            {
                lethalBotController.isInHangarShipRoom = false;
            }

            lethalBotController.isExhausted = exhausted;
            lethalBotController.isInElevator = inElevator;
            lethalBotController.isInHangarShipRoom = isInShip;
            this.isInsidePlayerShip = isInShip;

            // NEEDTOVAIDATE: Does this create issues?
            GrabbableObject[] itemSlots = lethalBotController.ItemSlots;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                var item = itemSlots[i];
                if (item != null && item.isHeld)
                {
                    if (item.isInShipRoom != isInShip)
                    {
                        lethalBotController.SetItemInElevator(droppedInShipRoom: isInShip, droppedInElevator: inElevator, item);
                    }
                    item.isInElevator = inElevator;
                }
            }
            GrabbableObject? itemOnlySlot = lethalBotController.ItemOnlySlot;
            if (itemOnlySlot != null)
            {
                lethalBotController.SetItemInElevator(droppedInShipRoom: isInShip, droppedInElevator: inElevator, itemOnlySlot);
            }

            // NEEDTOVAILDATE: Make sure the player movement code works as expected
            // The following code should be found under UpdatePlayerPositionClientRpc!
            lethalBotController.oldPlayerPosition = lethalBotController.serverPlayerPosition;
            if (!lethalBotController.disableSyncInAnimation && !lethalBotController.inVehicleAnimation)
            {
                lethalBotController.serverPlayerPosition = newPos;
                this.serverPosition = newPos;
                this.transform.position = newPos;
            }
            if (lethalBotController.overridePhysicsParent != null)
            {
                if (lethalBotController.overridePhysicsParent != lethalBotController.lastSyncedPhysicsParent)
                {
                    lethalBotController.lastSyncedPhysicsParent = lethalBotController.overridePhysicsParent;
                    this.ReParentLethalBot(lethalBotController.overridePhysicsParent);
                }
            }
            else if (lethalBotController.physicsParent != null)
            {
                if (lethalBotController.physicsParent != lethalBotController.lastSyncedPhysicsParent)
                {
                    lethalBotController.lastSyncedPhysicsParent = lethalBotController.physicsParent;
                    this.ReParentLethalBot(lethalBotController.physicsParent);
                }
            }
            else if (lethalBotController.lastSyncedPhysicsParent != null)
            {
                lethalBotController.lastSyncedPhysicsParent = null;
            }
            else if (lethalBotController.isInElevator)
            {
                if (!lethalBotController.parentedToElevatorLastFrame)
                {
                    lethalBotController.parentedToElevatorLastFrame = true;
                    this.ReParentLethalBot(lethalBotController.playersManager.elevatorTransform);
                }
            }
            else if (lethalBotController.parentedToElevatorLastFrame)
            {
                lethalBotController.parentedToElevatorLastFrame = false;
                this.ReParentLethalBot(lethalBotController.playersManager.playersContainer);
                lethalBotController.transform.eulerAngles = new Vector3(0f, lethalBotController.transform.eulerAngles.y, 0f);
            }
        }

        #endregion

        #region UpdatePlayerRotation and look RPC

        /// <summary>
        /// Sync the lethalBot body rotation and rotation of head (where he looks) between server and clients.
        /// </summary>
        /// <param name="stateIndicator"></param>
        /// <param name="lookAtTarget"></param>
        public void SyncUpdateLethalBotRotationAndLook(string stateIndicator, LookAtTarget lookAtTarget)
        {
            if (IsServer)
            {
                UpdateLethalBotRotationAndLookClientRpc(stateIndicator, lookAtTarget);
            }
            else
            {
                UpdatelLethalBotRotationAndLookServerRpc(stateIndicator, lookAtTarget);
            }
        }

        /// <summary>
        /// Server side, call clients to update lethalBot body rotation and rotation of head (where he looks)
        /// </summary>
        /// <param name="stateIndicator"></param>
        /// <param name="lookAtTarget"></param>
        [ServerRpc(RequireOwnership = false)]
        private void UpdatelLethalBotRotationAndLookServerRpc(string stateIndicator, LookAtTarget lookAtTarget)
        {
            UpdateLethalBotRotationAndLookClientRpc(stateIndicator, lookAtTarget);
        }

        /// <summary>
        /// Client side, update the lethalBot body rotation and rotation of head (where he looks).
        /// </summary>
        /// <param name="stateIndicator"></param>
        /// <param name="lookAtTarget"></param>
        [ClientRpc]
        private void UpdateLethalBotRotationAndLookClientRpc(string stateIndicator, LookAtTarget lookAtTarget)
        {
            if (NpcController == null)
            {
                return;
            }

            PlayerControllerB lethalBotController = this.NpcController.Npc;
            lethalBotController.playersManager.gameStats.allPlayerStats[lethalBotController.playerClientId].turnAmount++;
            if (IsClientOwnerOfLethalBot())
            {
                // Only update if not owner
                return;
            }

            // Update state indicator
            this.stateIndicatorServer = stateIndicator;

            // Update direction
            NpcController.SetCurrentLookAt(lookAtTarget);
        }

        #endregion

        #region UpdatePlayer animations RPC

        /// <summary>
        /// Server side, call client to sync changes in animation of the lethalBot
        /// </summary>
        /// <param name="animationState">Current animation state</param>
        /// <param name="animationSpeed">Current animation speed</param>
        [ServerRpc(RequireOwnership = false)]
        public void UpdateLethalBotAnimationServerRpc(int animationState, float animationSpeed)
        {
            UpdateLethalBotAnimationClientRpc(animationState, animationSpeed);
        }

        /// <summary>
        /// Client, update changes in animation of the lethalBot
        /// </summary>
        /// <param name="animationState">Current animation state</param>
        /// <param name="animationSpeed">Current animation speed</param>
        [ClientRpc]
        private void UpdateLethalBotAnimationClientRpc(int animationState, float animationSpeed)
        {
            if (NpcController == null)
            {
                return;
            }

            if (IsClientOwnerOfLethalBot())
            {
                // Only update if not owner
                return;
            }

            NpcController.ApplyUpdateLethalBotAnimationsNotOwner(animationState, animationSpeed);
        }

        #endregion

        #region UpdateSpecialAnimation RPC

        /// <summary>
        /// Sync the changes in special animation of the lethalBot body, between server and clients
        /// </summary>
        /// <param name="specialAnimation">Is in special animation ?</param>
        /// <param name="timed">Wait time of the special animation to end</param>
        /// <param name="climbingLadder">Is climbing ladder ?</param>
        public void UpdateLethalBotSpecialAnimationValue(bool specialAnimation, float timed, bool climbingLadder)
        {
            if (!IsClientOwnerOfLethalBot())
            {
                return;
            }

            UpdateLethalBotSpecialAnimationServerRpc(specialAnimation, timed, climbingLadder);
        }

        /// <summary>
        /// Server side, call clients to update the lethalBot special animation
        /// </summary>
        /// <param name="specialAnimation">Is in special animation ?</param>
        /// <param name="timed">Wait time of the special animation to end</param>
        /// <param name="climbingLadder">Is climbing ladder ?</param>
        [ServerRpc(RequireOwnership = false)]
        private void UpdateLethalBotSpecialAnimationServerRpc(bool specialAnimation, float timed, bool climbingLadder)
        {
            UpdateLethalBotSpecialAnimationClientRpc(specialAnimation, timed, climbingLadder);
        }

        /// <summary>
        /// Client side, update the lethalBot special animation
        /// </summary>
        /// <param name="specialAnimation">Is in special animation ?</param>
        /// <param name="timed">Wait time of the special animation to end</param>
        /// <param name="climbingLadder">Is climbing ladder ?</param>
        [ClientRpc]
        private void UpdateLethalBotSpecialAnimationClientRpc(bool specialAnimation, float timed, bool climbingLadder)
        {
            UpdateLethalBotSpecialAnimation(specialAnimation, timed, climbingLadder);
        }

        /// <summary>
        /// Update the lethalBot special animation
        /// </summary>
        /// <param name="specialAnimation">Is in special animation ?</param>
        /// <param name="timed">Wait time of the special animation to end</param>
        /// <param name="climbingLadder">Is climbing ladder ?</param>
        private void UpdateLethalBotSpecialAnimation(bool specialAnimation, float timed, bool climbingLadder)
        {
            if (NpcController == null)
            {
                return;
            }

            NpcController.Npc.IsInSpecialAnimationClientRpc(specialAnimation, timed, climbingLadder);
            NpcController.Npc.ResetZAndXRotation();
        }

        #endregion

        #region Grab item RPC

        /// <summary>
        /// Carbon Copy of <see cref="PlayerControllerB.BeginGrabObject"/>, but made for bots
        /// </summary>
        /// <param name="grabbableObject"></param>
        public void GrabObject(GrabbableObject grabbableObject)
        {
            // Only the owner can call this
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (!base.IsOwner)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not owner.");
                return;
            }

            // NOTE: IsGrabbableObjectGrabbable doesn't work here since we are just checking the same conditions as the human player
            if (!CanGrabObject(grabbableObject))
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not grabbable. A");
                return;
            }

            lethalBotController.currentlyGrabbingObject = grabbableObject;
            if (!GameNetworkManager.Instance.gameHasStarted && !lethalBotController.currentlyGrabbingObject.itemProperties.canBeGrabbedBeforeGameStart && StartOfRound.Instance.testRoom == null)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not grabbable. B");
                return;
            }
            lethalBotController.grabInvalidated = false;
            if (lethalBotController.currentlyGrabbingObject == null 
                || lethalBotController.inSpecialInteractAnimation 
                || lethalBotController.currentlyGrabbingObject.isHeld 
                || lethalBotController.currentlyGrabbingObject.isPocketed)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not grabbable. C");
                return;
            }

            NetworkObject networkObject = lethalBotController.currentlyGrabbingObject.NetworkObject;
            if (networkObject == null || !networkObject.IsSpawned)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not grabbable. D");
                return;
            }

            lethalBotController.currentlyGrabbingObject.InteractItem();
            if (lethalBotController.currentlyGrabbingObject.grabbable && FirstEmptyItemSlot(lethalBotController.currentlyGrabbingObject) != Const.INVALID_ITEM_SLOT)
            {
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABINVALIDATED, value: false);
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, value: false);
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, value: false);
                lethalBotController.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);
                SetSpecialGrabAnimationBool(setBool: true, grabbableObject);
                lethalBotController.isGrabbingObjectAnimation = true;
                lethalBotController.twoHanded = lethalBotController.currentlyGrabbingObject.itemProperties.twoHanded;
                lethalBotController.carryWeight = Mathf.Clamp(lethalBotController.carryWeight + (lethalBotController.currentlyGrabbingObject.itemProperties.weight - 1f), 1f, 10f);
                if (lethalBotController.currentlyGrabbingObject.itemProperties.grabAnimationTime > 0f)
                {
                    lethalBotController.grabObjectAnimationTime = lethalBotController.currentlyGrabbingObject.itemProperties.grabAnimationTime;
                }
                else
                {
                    lethalBotController.grabObjectAnimationTime = 0.4f;
                }
                lethalBotController.GrabObjectServerRpc(networkObject);
                if (grabObjectCoroutine != null)
                {
                    StopCoroutine(grabObjectCoroutine);
                }
                grabObjectCoroutine = StartCoroutine(this.GrabAnimationCoroutine());
            }
            else
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} grabbableObject {grabbableObject} not grabbable. E");
            }
        }

        /// <summary>
        /// Helper function for <see cref="GrabObject(GrabbableObject)"/>
        /// </summary>
        /// <remarks>
        /// This is basically a copy of the checks called by <see cref="PlayerControllerB.Interact_performed"/><br/>
        /// While this is similar to <see cref="IsGrabbableObjectGrabbable(GrabbableObject, EnumGrabbableObjectCall)"/>, 
        /// its only the same code human players go through before an item can be picked up
        /// </remarks>
        /// <param name="grabbableObject"></param>
        /// <returns></returns>
        public bool CanGrabObject(GrabbableObject? grabbableObject)
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (lethalBotController.twoHanded
                || lethalBotController.sinkingValue > 0.73f
                || lethalBotController.inSpecialMenu 
                || lethalBotController.timeSinceSwitchingSlots < 0.2f)
            {
                return false;
            }
            if (!lethalBotController.isGrabbingObjectAnimation 
                && !lethalBotController.isTypingChat 
                && !lethalBotController.inTerminalMenu 
                && !lethalBotController.throwingObject 
                && !lethalBotController.IsInspectingItem 
                && lethalBotController.inAnimationWithEnemy == null 
                && !lethalBotController.jetpackControls 
                && !lethalBotController.disablingJetpackControls 
                && !StartOfRound.Instance.suckingPlayersOutOfShip)
            {
                if (!lethalBotController.activatingItem && !lethalBotController.waitingToDropItem)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the first empty slot the bot has
        /// Returns <see cref="Const.INVALID_ITEM_SLOT"/> if no slot is avilable!
        /// </summary>
        /// <param name="grabbableObject">The object the bot is grabbing!</param>
        /// <returns>Returns the open slot <c>int</c> or <see cref="Const.INVALID_ITEM_SLOT"/> </returns>
        public int FirstEmptyItemSlot(GrabbableObject? grabbableObject = null)
        {
            PlayerControllerB thisBot = NpcController.Npc;
            GrabbableObject[] itemSlots = thisBot.ItemSlots;
            int result = Const.INVALID_ITEM_SLOT;
            if (thisBot.ItemOnlySlot == null
                && grabbableObject != null
                && !grabbableObject.itemProperties.isScrap
                && !grabbableObject.itemProperties.twoHanded
                && !grabbableObject.itemProperties.disallowUtilitySlot)
            {
                result = Const.RESERVED_EQUIPMENT_SLOT;
            }
            else if (thisBot.currentItemSlot != Const.RESERVED_EQUIPMENT_SLOT && itemSlots[thisBot.currentItemSlot] == null)
            {
                result = thisBot.currentItemSlot;
            }
            else
            {
                for (int i = 0; i < itemSlots.Length; i++)
                {
                    if (itemSlots[i] == null)
                    {
                        result = i;
                        break;
                    }
                }
            }

            // Support for reserved item slots!
            if (Plugin.IsModReservedItemSlotCoreLoaded)
            {
                return GetFirstEmptyReservedItemSlot(result, grabbableObject);
            }

            return result;
        }

        /// <summary>
        /// Helper function that checks if the bot has an open reserved item slot for this item!
        /// </summary>
        /// <param name="foundIndex"></param>
        /// <param name="grabbableObject">The object the bot is grabbing!</param>
        /// <returns>Returns the open slot <c>int</c> or <see cref="Const.INVALID_ITEM_SLOT"/></returns>
        private int GetFirstEmptyReservedItemSlot(int foundIndex, GrabbableObject? grabbableObject = null)
        {
            if (PlayerPatcher.reservedHotbarSize <= 0 || !HUDPatcher.hasReservedItemSlotsAndEnabled)
            {
                return foundIndex;
            }

            PlayerControllerB lethalBotController = NpcController.Npc;
            if (grabbableObject == null || !ReservedPlayerData.allPlayerData.TryGetValue(lethalBotController, out var playerData))
            { 
                return foundIndex; 
            }

            // Alright, we fallback onto the item name but the session manager lets us get the actual 
            // name they use if it exists.
            string itemName = grabbableObject.itemProperties.itemName;
            if (SessionManager.TryGetUnlockedItemData(grabbableObject, out var itemData))
            {
                itemName = itemData.itemName;
            }

            var reservedItemSlot = playerData.GetFirstEmptySlotForReservedItem(itemName);
            if (reservedItemSlot != null)
            {
                return reservedItemSlot.GetIndexInInventory(lethalBotController);
            }

            if (playerData.IsReservedItemSlot(foundIndex))
            {
                foundIndex = Const.INVALID_ITEM_SLOT;
                GrabbableObject[] itemSlots = lethalBotController.ItemSlots;
                for (int i = 0; i < itemSlots.Length; i++)
                {
                    if (!playerData.IsReservedItemSlot(i) && itemSlots[i] == null)
                    {
                        foundIndex = i;
                        break;
                    }
                }
            }

            return foundIndex;
        }

        /// <summary>
        /// Helper function that is used to get the size of the bot's inventory if we want to consider reserved item slots
        /// </summary>
        /// <remarks>
        /// Only use this function if you don't want to check the bot's reserved item slots!
        /// </remarks>
        /// <param name="playerController">The player controller to check.</param>
        /// <param name="cachedInventory">The player's inventory. Only exists as an optimization!</param>
        /// <returns></returns>
        public static int GetInventorySize(PlayerControllerB playerController, GrabbableObject[] cachedInventory = null!)
        {
            // Minor optimization, lets me skip an index call!
            cachedInventory ??= playerController.ItemSlots;

            // Support for reserved item slots!
            int inventorySize = cachedInventory.Length;
            if (Plugin.IsModReservedItemSlotCoreLoaded)
            {
                return GetReservedInventorySize(playerController, inventorySize);
            }

            return inventorySize;
        }

        /// <summary>
        /// Helper function that only exists to stop my mod from attempting to load ReservedItemSlotCore if its not installed!
        /// </summary>
        /// <param name="playerController"></param>
        /// <param name="inventorySize"></param>
        /// <returns></returns>
        private static int GetReservedInventorySize(PlayerControllerB playerController, int inventorySize)
        {
            if (ReservedPlayerData.allPlayerData.TryGetValue(playerController, out var playerData))
            {
                return Mathf.Min(playerData.reservedHotbarStartIndex, inventorySize); // Sanity check, use the smaller value!
            }

            return inventorySize;
        }

        /// <summary>
        /// Makes the lethalBot swap its currently held item to the slot indicated
        /// </summary>
        /// <remarks>
        /// Calls server or client based on the current realm its called in
        /// </remarks>
        /// <param name="slotNum"></param>
        public void SwitchItemSlotsAndSync(int slotNum)
        {
            if (base.IsServer)
            {
                SwitchItemSlotsClientRpc(slotNum);
            }
            else
            {
                SwitchItemSlotsServerRpc(slotNum);
            }
        }

        /// <summary>
        /// Server side, call clients to make the lethalBot swap item on their side to sync everyone
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void SwitchItemSlotsServerRpc(int slotNum)
        {
            SwitchItemSlotsClientRpc(slotNum);
        }

        /// <summary>
        /// Client side, make the lethalBot swap to an item
        /// </summary>
        [ClientRpc]
        private void SwitchItemSlotsClientRpc(int slotNum)
        {
            SwitchToItemSlot(slotNum);
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, checks if the bot can swap to another slot
        /// </summary>
        public bool CanSwitchItemSlot()
        {
            PlayerControllerB thisBot = NpcController.Npc;
            if (thisBot.isGrabbingObjectAnimation 
                || thisBot.inSpecialInteractAnimation 
                || thisBot.isTypingChat 
                || thisBot.twoHanded 
                || thisBot.activatingItem 
                || thisBot.jetpackControls 
                || thisBot.disablingJetpackControls
                || thisBot.throwingObject
                || thisBot.timeSinceSwitchingSlots < 0.3f)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, tells the bot to swap to the specified slot 
        /// </summary>
        internal void SwitchToItemSlot(int slot, GrabbableObject? fillSlotWithItem = null)
        {
            if (!CanSwitchItemSlot() && fillSlotWithItem == null)
            {
                return;
            }

            // Cached Values
            PlayerControllerB thisBot = NpcController.Npc;
            GrabbableObject[] itemSlots = thisBot.ItemSlots;

            // Rest of copied code!
            thisBot.currentItemSlot = slot;
            if (fillSlotWithItem != null)
            {
                if (slot == Const.RESERVED_EQUIPMENT_SLOT)
                {
                    thisBot.ItemOnlySlot = fillSlotWithItem;
                }
                else
                {
                    itemSlots[slot] = fillSlotWithItem;
                }
            }
            if (thisBot.currentlyHeldObjectServer != null)
            {
                thisBot.currentlyHeldObjectServer.playerHeldBy = thisBot;
                this.SetSpecialGrabAnimationBool(false, thisBot.currentlyHeldObjectServer);
                thisBot.currentlyHeldObjectServer.PocketItem();
                if (slot == Const.RESERVED_EQUIPMENT_SLOT)
                {
                    if (thisBot.ItemOnlySlot != null && !string.IsNullOrEmpty(thisBot.ItemOnlySlot.itemProperties.pocketAnim))
                    {
                        thisBot.playerBodyAnimator.SetTrigger(thisBot.ItemOnlySlot.itemProperties.pocketAnim);
                    }
                }
                else if (itemSlots[slot] != null && !string.IsNullOrEmpty(itemSlots[slot].itemProperties.pocketAnim))
                {
                    thisBot.playerBodyAnimator.SetTrigger(itemSlots[slot].itemProperties.pocketAnim);
                }
            }
            GrabbableObject? grabbableObject = slot == Const.RESERVED_EQUIPMENT_SLOT ? thisBot.ItemOnlySlot : itemSlots[slot];
            if (grabbableObject != null)
            {
                grabbableObject.playerHeldBy = thisBot;
                grabbableObject.EquipItem();
                this.SetSpecialGrabAnimationBool(true, grabbableObject);
                if (thisBot.currentlyHeldObjectServer != null)
                {
                    if (grabbableObject.itemProperties.twoHandedAnimation || thisBot.currentlyHeldObjectServer.itemProperties.twoHandedAnimation)
                    {
                        thisBot.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATIONTWOHANDED);
                        thisBot.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATIONTWOHANDED);
                    }
                    thisBot.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATION);
                    thisBot.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATION);
                }
                thisBot.twoHandedAnimation = grabbableObject.itemProperties.twoHandedAnimation;
                thisBot.twoHanded = grabbableObject.itemProperties.twoHanded;
                thisBot.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, true);
                thisBot.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, false);
                thisBot.isHoldingObject = true;
                thisBot.currentlyHeldObjectServer = grabbableObject;
                if (fillSlotWithItem == null)
                {
                    thisBot.currentlyHeldObjectServer.gameObject.GetComponent<AudioSource>().PlayOneShot(thisBot.currentlyHeldObjectServer.itemProperties.grabSFX, 0.6f);
                }
            }
            else
            {
                thisBot.currentlyHeldObject = null;
                thisBot.currentlyHeldObjectServer = null;
                thisBot.isHoldingObject = false;
                thisBot.twoHanded = false;
                thisBot.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, true);
            }
            thisBot.timeSinceSwitchingSlots = 0f;
        }

        /// <summary>
        /// Coroutine for the grab animation
        /// </summary>
        /// <returns></returns>
        private IEnumerator GrabAnimationCoroutine()
        {
            // Grab our player controller
            PlayerControllerB lethalBotController = NpcController.Npc;

            // Clear the validated grab flag
            lethalBotController.grabbedObjectValidated = false;

            // Yield just like the base game
            yield return new WaitForSeconds(0.1f);

            // Play pickup audio
            lethalBotController.itemAudio.PlayOneShot(StartOfRound.Instance.playerGrabSFX[UnityEngine.Random.Range(0, StartOfRound.Instance.playerGrabSFX.Length)]);

            // Play item pickup audio
            lethalBotController.currentlyGrabbingObject.parentObject = lethalBotController.serverItemHolder;
            if (lethalBotController.currentlyGrabbingObject.itemProperties.grabSFX != null)
            {
                lethalBotController.itemAudio.PlayOneShot(lethalBotController.currentlyGrabbingObject.itemProperties.grabSFX, 1f);
            }

            // For the the grab to be validated by the server
            while ((lethalBotController.currentlyGrabbingObject != lethalBotController.currentlyHeldObjectServer || !lethalBotController.currentlyHeldObjectServer.wasOwnerLastFrame) && !lethalBotController.grabInvalidated)
            {
                yield return null;
            }

            // Update local variable with our new field
            if (lethalBotController.grabInvalidated)
            {
                // If the grab was marked as invalid by the server, let the user know!
                lethalBotController.grabInvalidated = false;
                Plugin.LogInfo("Grab was invalidated on object: " + lethalBotController.currentlyGrabbingObject.name);
                if (lethalBotController.currentlyGrabbingObject.playerHeldBy != null)
                {
                    Plugin.LogInfo($"playerHeldBy on currentlyGrabbingObject 2: {lethalBotController.currentlyGrabbingObject.playerHeldBy}");
                }
                if (lethalBotController.currentlyGrabbingObject.parentObject == lethalBotController.serverItemHolder)
                {
                    if (lethalBotController.currentlyGrabbingObject.playerHeldBy != null)
                    {
                        Plugin.LogInfo($"Grab invalidated; giving grabbed object to the client who got it first; {lethalBotController.currentlyGrabbingObject.playerHeldBy}");
                        lethalBotController.currentlyGrabbingObject.parentObject = lethalBotController.currentlyGrabbingObject.playerHeldBy.serverItemHolder;
                    }
                    else
                    {
                        Plugin.LogInfo("Grab invalidated; no other client has possession of it, so set its parent object to null.");
                        lethalBotController.currentlyGrabbingObject.parentObject = null;
                    }
                }
                lethalBotController.twoHanded = false;
                SetSpecialGrabAnimationBool(setBool: false, lethalBotController.currentlyGrabbingObject);
                if (lethalBotController.currentlyHeldObjectServer != null)
                {
                    lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRAB, value: true);
                }
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABINVALIDATED, value: true);
                lethalBotController.carryWeight = Mathf.Clamp(lethalBotController.carryWeight - (lethalBotController.currentlyGrabbingObject.itemProperties.weight - 1f), 1f, 10f);
                lethalBotController.isGrabbingObjectAnimation = false;
                lethalBotController.currentlyGrabbingObject = null;
                lethalBotController.queueDiscardObject = false;
            }
            else
            {
                lethalBotController.grabbedObjectValidated = true;
                lethalBotController.currentlyHeldObjectServer.GrabItemOnClient();
                lethalBotController.isHoldingObject = true;
                yield return new WaitForSeconds(lethalBotController.grabObjectAnimationTime - 0.2f);
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, value: true);
                lethalBotController.isGrabbingObjectAnimation = false;
                if (lethalBotController.queueDiscardObject)
                {
                    Plugin.LogInfo($"Activating queued DiscardHeldObject to force player #{lethalBotController.playerClientId} '{lethalBotController.playerUsername}' to drop their item! item: '{lethalBotController.currentlyHeldObjectServer.itemProperties.itemName}'");
                    lethalBotController.DiscardHeldObject();
                    lethalBotController.queueDiscardObject = false;
                }
            }
            yield break;
        }

        /// <summary>
        /// Set the animation of body to something special if the item has a special grab animation.
        /// </summary>
        /// <param name="setBool">Activate or deactivate special animation</param>
        /// <param name="item">Item that has the special grab animation</param>
        internal void SetSpecialGrabAnimationBool(bool setBool, GrabbableObject? item)
        {
            NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRAB, setBool);
            if (item != null
                && !string.IsNullOrEmpty(item.itemProperties.grabAnim))
            {
                try
                {
                    NpcController.SetAnimationBoolForItem(item.itemProperties.grabAnim, setBool);
                    NpcController.Npc.playerBodyAnimator.SetBool(item.itemProperties.grabAnim, setBool);
                }
                catch (Exception)
                {
                    Plugin.LogError("An item tried to set an animator bool which does not exist: " + item.itemProperties.grabAnim);
                }
            }
        }

        #endregion

        #region Drop item RPC

        [ServerRpc(RequireOwnership = false)]
        public void SyncBatteryLethalBotServerRpc(NetworkObjectReference networkObjectReferenceGrabbableObject, int charge)
        {
            SyncBatteryLethalBotClientRpc(networkObjectReferenceGrabbableObject, charge);
        }

        [ClientRpc]
        private void SyncBatteryLethalBotClientRpc(NetworkObjectReference networkObjectReferenceGrabbableObject, int charge)
        {
            if (!networkObjectReferenceGrabbableObject.TryGet(out NetworkObject networkObject))
            {
                Plugin.LogError($"SyncBatteryLethalBotClientRpc : Unknown to get network object from network object reference (Grab item RPC)");
                return;
            }

            GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
            if (grabbableObject == null)
            {
                Plugin.LogError($"SyncBatteryLethalBotClientRpc : Unknown to get GrabbableObject component from network object (Grab item RPC)");
                return;
            }

            SyncBatteryLethalBot(grabbableObject, charge);
        }

        private void SyncBatteryLethalBot(GrabbableObject grabbableObject, int charge)
        {
            float num = (float)charge / 100f;
            grabbableObject.insertedBattery = new Battery(num <= 0f, num);
            grabbableObject.ChargeBatteries();
        }

        #endregion

        #region Bot Chat

        /// <summary>
        /// Helper function for sending chat messages to all players!
        /// </summary>
        /// <remarks>
        /// Currently, bots send messages instantly, we will work on adding a "typing" delay later on.
        /// </remarks>
        /// <param name="message">The message the bot wants to send</param>
        /// <param name="bypassConfig">Should we bypass the config check for allowing bots to chat?</param>
        public void SendChatMessage(string message, bool bypassConfig = false)
        {
            // First, check if bots are allowed to chat!
            if (!bypassConfig && !Plugin.Config.AllowBotsToChat.Value)
            {
                return;
            }

            // OK, there is a 50 character limit for chat messages, so we need to split them up!
            // FIXME: Allow users to change the char limit
            const int charLimit = 49;
            List<string> splitMessages = new List<string>();

            // Split the message into words
            string[] words = message.Split(' ');
            StringBuilder currentLine = new StringBuilder();

            foreach (string word in words)
            {
                // If the word itself is longer than the limit, force-split it
                if (word.Length > charLimit)
                {
                    // Flush current line first
                    if (currentLine.Length > 0)
                    {
                        splitMessages.Add(currentLine.ToString());
                        currentLine.Clear();
                    }

                    // Split the long word
                    for (int i = 0; i < word.Length; i += charLimit)
                    {
                        splitMessages.Add(
                            word.Substring(i, Math.Min(charLimit, word.Length - i))
                        );
                    }

                    continue;
                }

                // Check if adding this word would exceed the limit
                int extraLength = currentLine.Length == 0 ? word.Length : word.Length + 1;

                if (currentLine.Length + extraLength <= charLimit)
                {
                    if (currentLine.Length > 0)
                        currentLine.Append(' ');

                    currentLine.Append(word);
                }
                else
                {
                    // Commit the current line and start a new one
                    splitMessages.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                }
            }

            // Add the last line if it exists
            if (currentLine.Length > 0)
            {
                splitMessages.Add(currentLine.ToString());
            }

            // Send each message separately
            int playerClientId = (int)NpcController.Npc.playerClientId;
            foreach (string msg in splitMessages)
            {
                HUDManager.Instance.AddPlayerChatMessageServerRpc(msg, playerClientId);
            }
        }


        #endregion

        #region Lethal Phones AI

        /// <summary>
        /// Has the bot call the given <paramref name="player"/> using Lethal Phones!
        /// </summary>
        /// <param name="player"></param>
        public void CallPlayer(PlayerControllerB player)
        {
            // Only the owner can call players using the phone!
            if (!base.IsOwner)
            {
                return;
            }

            // Grab our phone and the player's phone
            SwitchboardPhone? switchboardPhone = PhoneNetworkHandler.Instance?.switchboard;
            PlayerControllerB? switchboardOperator = switchboardPhone?.switchboardOperator;
            PhoneBehavior? ourPhone = switchboardOperator != null && switchboardOperator == NpcController.Npc ? switchboardPhone : GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to call a player, but was unable to find their phone!");
                return;
            }

            // Make sure the player we are calling has a phone!
            PhoneBehavior? playerPhone = switchboardOperator != null && switchboardOperator == player ? switchboardPhone : player.transform?.Find("PhonePrefab(Clone)")?.GetComponent<PlayerPhone>();
            if (playerPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to call player {player.playerUsername}, but was unable to find their phone!");
                return;
            }

            if (lethalPhonesCoroutine != null)
            {
                StopCoroutine(lethalPhonesCoroutine);
            }
            lethalPhonesCoroutine = StartCoroutine(callPlayerCoroutine(ourPhone, playerPhone));
        }

        /// <summary>
        /// Has the bot call a random alive player using Lethal Phones!
        /// </summary>
        public void CallRandomPlayer()
        {
            // Grab all possible players to call
            List<PlayerControllerB> playerControllers = StartOfRound.Instance.allPlayerScripts.ToList();
            PlayerControllerB? playerToCall = null;
            while (playerControllers.Count > 0)
            {
                // Make sure this player is valid, not us, and alive!
                int index = UnityEngine.Random.Range(0, playerControllers.Count);
                playerToCall = playerControllers[index];
                if (playerToCall == null
                    || playerToCall == NpcController.Npc
                    || !playerToCall.isPlayerControlled
                    || playerToCall.isPlayerDead)
                {
                    playerControllers.RemoveAt(index);
                    playerToCall = null;
                }
                else
                {
                    break;
                }
            }

            // Lets give them a call!
            if (playerToCall != null)
            {
                CallPlayer(playerToCall);
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="PlayerPhone.HangupButtonPressed()"/>, but adjusted to work for bots.
        /// </summary>
        public void HangupPhone(bool putAwayPhone = true)
        {
            // Only the owner can hang up the phone!
            if (!base.IsOwner)
            {
                return;
            }

            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to hangup phone, but was unable to find their phone!");
                return;
            }

            NetworkVariable<short> incomingCall = (NetworkVariable<short>)PhoneBehaviorPatch.incomingCall.GetValue(ourPhone);
            NetworkVariable<short> activeCall = (NetworkVariable<short>)PhoneBehaviorPatch.activeCall.GetValue(ourPhone);
            NetworkVariable<short> outgoingCall = (NetworkVariable<short>)PhoneBehaviorPatch.outgoingCall.GetValue(ourPhone);
            if (incomingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
            {
                PhoneNetworkHandler.Instance.HangUpCallServerRpc(incomingCall.Value, ourPhone.NetworkObjectId);
                ourPhone.StopRingingServerRpc();
                ourPhone.PlayHangupSoundServerRpc();
                incomingCall.Value = Const.LETHAL_PHONES_NO_CALLER_ID;
            }
            else if (activeCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
            {
                PhoneNetworkHandler.Instance.HangUpCallServerRpc(activeCall.Value, ourPhone.NetworkObjectId);
                ourPhone.PlayHangupSoundServerRpc();
                activeCall.Value = Const.LETHAL_PHONES_NO_CALLER_ID;
            }
            else if (outgoingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
            {
                PhoneNetworkHandler.Instance.HangUpCallServerRpc(outgoingCall.Value, ourPhone.NetworkObjectId);
                ourPhone.PlayHangupSoundServerRpc();
                outgoingCall.Value = Const.LETHAL_PHONES_NO_CALLER_ID;
            }

            // Make sure to put the phone away after hanging up!
            if (putAwayPhone)
            {
                ourPhone.ToggleServerPhoneModelServerRpc(false);
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="PlayerPhone.CallButtonPressed"/>, but adjusted to work for bots.
        /// </summary>
        public void AcceptIncomingCall()
        {
            // Only the owner can accept calls using the phone!
            if (!base.IsOwner)
            {
                return;
            }

            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to accept an incoming call, but was unable to find their phone!");
                return;
            }

            if (lethalPhonesCoroutine != null)
            {
                StopCoroutine(lethalPhonesCoroutine);
            }
            lethalPhonesCoroutine = StartCoroutine(pickupCallCoroutine(ourPhone));
        }

        /// <summary>
        /// Checks if someone is calling the bot
        /// </summary>
        /// <returns>true: we have an incoming call; otherwise false</returns>
        public bool HasIncomingCall()
        {
            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to check for incoming call, but was unable to find their phone!");
                return false;
            }
            NetworkVariable<short> incomingCall = (NetworkVariable<short>)PhoneBehaviorPatch.incomingCall.GetValue(ourPhone);
            return incomingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID;
        }

        /// <summary>
        /// Checks if we are calling someone
        /// </summary>
        /// <returns>true: we are calling someone; otherwise false</returns>
        public bool IsCallingPlayer()
        {
            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to check if calling player, but was unable to find their phone!");
                return false;
            }
            NetworkVariable<short> outgoingCall = (NetworkVariable<short>)PhoneBehaviorPatch.outgoingCall.GetValue(ourPhone);
            return outgoingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID;
        }

        /// <summary>
        /// Checks if we are currently in a call
        /// </summary>
        /// <returns></returns>
        public bool AreWeInCall()
        {
            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to check if in call, but was unable to find their phone!");
                return false;
            }
            return ourPhone.IsBusy();
        }

        private IEnumerator callPlayerCoroutine(NetworkBehaviour ourPhoneNetworkBehavior, NetworkBehaviour targetPhoneNetworkBehavior)
        {
            // Sigh, to keep Lethal Phones as a soft dependency.
            // The coroutine parameters must not use any of the classes from Lethal Phones.
            yield return null;
            PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhoneComponent =>
            {
                PlayerPhone ourPhone = (PlayerPhone)ourPhoneComponent;
                if (PhoneBehaviorPatch.GetServerLeftArmRig(ourPhone).weight < Const.LETHAL_PHONES_OPEN_PHONE)
                {
                    ourPhone.ToggleServerPhoneModelServerRpc(true);
                }
            });
            HangupPhone(putAwayPhone: false);

            // Alright, we need to fake dialing in the number
            short phoneNumber = 0;
            PhoneBehaviorPatch.TryWithPhone(targetPhoneNetworkBehavior, targetPhone =>
            {
                phoneNumber = ((PlayerPhone)targetPhone).phoneNumber;
            });

            // Wait a second, just to make sure the phone is pulled out.
            yield return null;
            yield return new WaitUntil(() =>
            {
                float armWeight = 0f;
                PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhone => armWeight = PhoneBehaviorPatch.GetServerLeftArmRig(ourPhone).weight);
                return armWeight >= Const.LETHAL_PHONES_OPEN_PHONE;
            });

            // So we have the entire number, 1234 for example, but we to dial it one by one.
            string phoneNumberString = phoneNumber.ToString("D4");
            foreach (char digit in phoneNumberString)
            {
                PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhone => ((PlayerPhone)ourPhone).DialNumber((short)char.GetNumericValue(digit)));
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.1f, 0.3f)); // Wait a bit between dialing each digit, again this is a band-aid solution
            }

            // Finally, we "press" the call button!
            PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhone => ((PlayerPhone)ourPhone).CallDialedNumber());
            StopLethalPhonesCoroutine();
        }

        private IEnumerator pickupCallCoroutine(NetworkBehaviour ourPhoneNetworkBehavior)
        {
            // Sigh, to keep Lethal Phones as a soft dependency.
            // The coroutine parameters must not use any of the classes from Lethal Phones.
            yield return null;
            PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhoneComponent =>
            {
                PlayerPhone ourPhone = (PlayerPhone)ourPhoneComponent;
                if (PhoneBehaviorPatch.GetServerLeftArmRig(ourPhone).weight < Const.LETHAL_PHONES_OPEN_PHONE)
                {
                    ourPhone.ToggleServerPhoneModelServerRpc(true);
                }
            });

            // Wait a second, just to make sure the phone is pulled out.
            yield return null;
            yield return new WaitUntil(() =>
            {
                float armWeight = 0f;
                PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhone => armWeight = PhoneBehaviorPatch.GetServerLeftArmRig((PlayerPhone)ourPhone).weight);
                return armWeight >= Const.LETHAL_PHONES_OPEN_PHONE;
            });

            // Ok, now give the bot a fake reaction time for reading the caller ID and deciding to pick up,
            // we don't want the bot to be too fast at picking up calls!
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 1.5f));

            // Code copied from PlayerPhone.CallButtonPressed, but adjusted to work for bots!
            NetworkVariable<short> incomingCall = (NetworkVariable<short>)PhoneBehaviorPatch.incomingCall.GetValue(ourPhoneNetworkBehavior);
            NetworkVariable<ulong> incomingCaller = (NetworkVariable<ulong>)PhoneBehaviorPatch.incomingCaller.GetValue(ourPhoneNetworkBehavior);
            if (incomingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
            {
                NetworkVariable<short> activeCall = (NetworkVariable<short>)PhoneBehaviorPatch.activeCall.GetValue(ourPhoneNetworkBehavior);
                NetworkVariable<ulong> activeCaller = (NetworkVariable<ulong>)PhoneBehaviorPatch.activeCaller.GetValue(ourPhoneNetworkBehavior);
                if (activeCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
                {
                    PhoneNetworkHandler.Instance.HangUpCallServerRpc(activeCall.Value, ourPhoneNetworkBehavior.NetworkObjectId);
                }

                activeCall.Value = incomingCall.Value;
                activeCaller.Value = incomingCaller.Value;
                incomingCall.Value = Const.LETHAL_PHONES_NO_CALLER_ID;
                PhoneBehaviorPatch.TryWithPhone(ourPhoneNetworkBehavior, ourPhoneComponent =>
                {
                    PlayerPhone ourPhone = (PlayerPhone)ourPhoneComponent;
                    PhoneNetworkHandler.Instance.AcceptIncomingCallServerRpc(activeCall.Value, ourPhone.NetworkObjectId);
                    ourPhone.StopRingingServerRpc();
                    ourPhone.PlayPickupSoundServerRpc();
                });
            }
            StopLethalPhonesCoroutine();
        }

        /// <summary>
        /// Helper that checks if the bot is currently in the middle of a lethal phones coroutine.
        /// This exists to stop redundant calls to <see cref="CallPlayer(PlayerControllerB)"/> and <see cref="AcceptIncomingCall"/>
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsLethalPhonesCoroutineRunning()
        {
            return lethalPhonesCoroutine != null;
        }

        public void StopLethalPhonesCoroutine()
        {
            if (lethalPhonesCoroutine != null)
            {
                StopCoroutine(lethalPhonesCoroutine);
                lethalPhonesCoroutine = null;
            }
        }

        /// <summary>
        /// Checks if the bot currently has their phone equipped, 
        /// this is done by checking the phone equip animation progress, 
        /// since there is no "isPhoneEquipped" boolean or anything like that.
        /// </summary>
        /// <returns>true: the bot has their phone out; otherwise false</returns>
        public bool IsPhoneEquipped()
        {
            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to check if phone equipped, but was unable to find their phone!");
                return false;
            }
            return PhoneBehaviorPatch.GetServerLeftArmRig(ourPhone).weight >= Const.LETHAL_PHONES_OPEN_PHONE;
        }

        /// <summary>
        /// Helper function to get the bot's phone, if it has one! 
        /// </summary>
        /// <returns>The bot's <see cref="PlayerPhone"/>.</returns>
        public PlayerPhone? GetOurPlayerPhone()
        {
            if (BotLethalPhone == null)
            {
                BotLethalPhone = NpcController.Npc.transform?.Find("PhonePrefab(Clone)")?.GetComponent<PlayerPhone>();
            }
            return BotLethalPhone as PlayerPhone;
        }

        /// <summary>
        /// For Lethal Phones <see cref="SwitchboardPhone"/>
        /// </summary>
        public void BecomeSwitchboardOperator()
        {
            // Only the owner can do this!
            if (!base.IsOwner)
            {
                return;
            }

            SwitchboardPhone? switchboardPhone = PhoneNetworkHandler.Instance?.switchboard;
            if (switchboardPhone == null)
            {
                // Just do nothing, we may not have the switchboard upgrade!
                return;
            }

            // If someone is already the switchboardOperator, there is nothing we can do here!
            PlayerControllerB switchboardOperator = switchboardPhone.switchboardOperator;
            if (switchboardOperator != null)
            {
                return;
            }

            // Alright, make us the operator!
            switchboardPhone.OperatorSwitch(NpcController.Npc);
        }

        /// <summary>
        /// For Lethal Phones <see cref="SwitchboardPhone"/>
        /// </summary>
        public void StopBeingSwitchboardOperator()
        {
            // Only the owner can do this!
            if (!base.IsOwner)
            {
                return;
            }

            SwitchboardPhone? switchboardPhone = PhoneNetworkHandler.Instance?.switchboard;
            if (switchboardPhone == null)
            {
                // Just do nothing, we may not have the switchboard upgrade!
                return;
            }

            // If someone is already the switchboardOperator, there is nothing we can do here!
            PlayerControllerB switchboardOperator = switchboardPhone.switchboardOperator;
            if (switchboardOperator == null 
                || switchboardOperator != NpcController.Npc)
            {
                return;
            }

            // Alright, we don't want to be the operator anymore!
            switchboardPhone.OperatorSwitch(NpcController.Npc);
        }

        #endregion

        // NOTE: We HAVE to fake the use terminal call, it would make some incompatability with some mods,
        // but they can be fixed with custom patches.
        #region Bot Terminal

        /// <summary>
        /// Makes the bot enter the terminal, this has proper support for animations!
        /// </summary>
        /// <remarks>
        /// FIXME: At the current moment, this doesn't seem like a "good" way of doing this.
        /// There is probably a better way of doing this so for now i'm only leaving this here
        /// so I can use it for reference!
        /// NOTE: Bots can NOT use the terminal's interact trigger to enter the terminal,
        /// this is because of how its programmed, there is not much else I can do about it!
        /// </remarks>
        public void EnterTerminal()
        {
            // Terminal is invalid for some reason, report the error!
            Terminal ourTerminal = Managers.TerminalManager.Instance.GetTerminal();
            if (ourTerminal == null)
            {
                Plugin.LogError($"[ERROR] Bot {NpcController.Npc.playerUsername} was unable to find the terminal!");
                return;
            }
            InteractTrigger terminalTrigger = ourTerminal.gameObject.GetComponent<InteractTrigger>();
            ourTerminal.StartCoroutine(waitUntilFrameEndToSetActive(true, ourTerminal));
            //ourTerminal.terminalUIScreen.gameObject.SetActive(true);
            PlayerControllerB localPlayerController = NpcController.Npc;
            terminalTrigger.UpdateUsedByPlayerServerRpc((int)localPlayerController.playerClientId);
            localPlayerController.inSpecialInteractAnimation = true;
            localPlayerController.currentTriggerInAnimationWith = terminalTrigger;
            localPlayerController.inTerminalMenu = true;
            localPlayerController.playerBodyAnimator.ResetTrigger(terminalTrigger.animationString);
            localPlayerController.playerBodyAnimator.SetTrigger(terminalTrigger.animationString);
            localPlayerController.Crouch(crouch: false);
            localPlayerController.UpdateSpecialAnimationValue(specialAnimation: true, (short)terminalTrigger.playerPositionNode.eulerAngles.y);
            if ((bool)terminalTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = terminalTrigger.overridePlayerParent;
            }
            ourTerminal.LoadNewNode(ourTerminal.terminalNodes.specialNodes[TerminalConst.INDEX_DEFAULT_TERMINALNODE]);
            if (!ourTerminal.usedTerminalThisSession)
            {
                ourTerminal.usedTerminalThisSession = true;
                if (!ourTerminal.syncedTerminalValues)
                {
                    ourTerminal.SyncTerminalValuesServerRpc();
                }
            }
            ourTerminal.SetTerminalInUseLocalClient(true);
            terminalTrigger.interactable = false; // Don't let other player use the terminal!
            ourTerminal.terminalAudio.PlayOneShot(ourTerminal.enterTerminalSFX);
        }

        /// <summary>
        /// Makes the bot leave the terminal, this has proper support for animations!
        /// </summary>
        /// <param name="syncTerminalInUse">Should the terminal update its status on all clients?</param>
        /// <param name="forceEndUse">Should the code run even if <see cref="IsUsingTerminal"/> returns false?</param>
        public void LeaveTerminal(bool syncTerminalInUse = true, bool forceEndUse = false)
        {
            // Terminal is invalid for some reason, report the error!
            PlayerControllerB localPlayerController = NpcController.Npc;
            Terminal ourTerminal = Managers.TerminalManager.Instance.GetTerminal();
            if (ourTerminal == null)
            {
                localPlayerController.inTerminalMenu = false;
                localPlayerController.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANINATION_TRIGGER_TERMINAL);
                Plugin.LogError($"[ERROR] Bot {localPlayerController.playerUsername} was unable to properly leave the terminal. Issues may occur!");
                return;
            }

            if (!forceEndUse && !IsUsingTerminal())
            {
                Plugin.LogWarning($"Bot {localPlayerController.playerUsername} was told to leave a terminal when they were not using it!");
                return;
            }

            InteractTrigger terminalTrigger = ourTerminal.gameObject.GetComponent<InteractTrigger>();
            terminalTrigger.StopUsingServerRpc((int)localPlayerController.playerClientId);
            //ourTerminal.terminalInUse = false;
            ourTerminal.StartCoroutine(waitUntilFrameEndToSetActive(active: false, ourTerminal));
            localPlayerController.inSpecialInteractAnimation = false;
            localPlayerController.currentTriggerInAnimationWith = null;
            localPlayerController.inTerminalMenu = false;
            localPlayerController.playerBodyAnimator.ResetTrigger(terminalTrigger.animationString);
            if (terminalTrigger.stopAnimationManually)
            {
                localPlayerController.playerBodyAnimator.SetTrigger(terminalTrigger.stopAnimationString);
            }
            localPlayerController.UpdateSpecialAnimationValue(specialAnimation: false, 0);
            if ((bool)terminalTrigger.overridePlayerParent && localPlayerController.overridePhysicsParent == terminalTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = null;
            }
            ourTerminal.timeSinceTerminalInUse = 0f;
            terminalTrigger.interactable = true; // Let other player use the terminal!
            Plugin.LogDebug($"Quit terminal; inTerminalMenu true?: {localPlayerController.inTerminalMenu}");

            if (syncTerminalInUse)
            {
                ourTerminal.SetTerminalInUseLocalClient(inUse: false);
            }

            ourTerminal.terminalAudio.PlayOneShot(ourTerminal.leaveTerminalSFX);
        }

        /// <summary>
        /// Helper rpc that allows non owners to make the bot leave the terminal
        /// </summary>
        /// <param name="syncTerminalInUse">Should the terminal update its status on all clients?</param>
        /// <param name="forceEndUse">Should the code run even if <see cref="IsUsingTerminal"/> returns false?</param>
        [Rpc(SendTo.Owner, RequireOwnership = false)]
        public void LeaveTerminalRpc(bool syncTerminalInUse = true, bool forceEndUse = false)
        {
            LeaveTerminal(syncTerminalInUse, forceEndUse);
        }

        private IEnumerator waitUntilFrameEndToSetActive(bool active, Terminal? ourTerminal)
        {
            yield return new WaitForEndOfFrame();
            ourTerminal?.terminalUIScreen.gameObject.SetActive(active);
        }

        #endregion

        #region Light Level Helpers

        /// <summary>
        /// Gets the current light level around the bot!
        /// </summary>
        /// <remarks>
        /// NOTE: This is not perfect and may have some misleading values.....
        /// </remarks>
        /// <returns>The light level around the bot</returns>
        public float GetLightLevelAroundBot()
        {
            Vector3 ourPos = NpcController.Npc.gameplayCamera.transform.position;
            float lightLevel = 0f;
            int numLightsConsidered = 0;
            IReadOnlyList<Light> lightsOnMap = LethalBotManager.LightsOnMap;
            for (int i = 0; i < lightsOnMap.Count; i++)
            {
                // Make sure we want to consider this light source
                var light = lightsOnMap[i];
                if (ShouldIgnoreLightSource(light))
                    continue;

                // Have to be in range of the light to consider it
                LightType lightType = light.type;
                Vector3 toBot = ourPos - light.transform.position;
                float dist = toBot.sqrMagnitude; // NOTE: We don't use sqr distance since we need to use the exact range later.
                float lightRange = light.range;
                if (lightType != LightType.Directional 
                    && dist > lightRange * lightRange)
                    continue;

                // Convert distance to squared distance for attenuation calculation
                dist = lightType != LightType.Directional ? Mathf.Sqrt(dist) : 0f;

                // Check the light direction (if applicable)
                float coneFactor = 1f;
                if (lightType == LightType.Spot)
                {
                    float angle = Vector3.Angle(light.transform.forward, toBot.normalized);
                    float halfAngle = light.spotAngle * 0.5f;
                    if (angle > halfAngle)
                    {
                        continue;
                    }
                    coneFactor = Mathf.Clamp01(coneFactor - (angle / halfAngle));
                }

                // Adjust the light strength based on its distance from the bot and its intensity
                float atten = lightType != LightType.Directional ? 1f - Mathf.Clamp01(dist / lightRange) : 1f;
                atten *= atten;
                float occlusion = GetLightOcclusionFactor(light.transform.position, ourPos);
                lightLevel += light.intensity * atten * coneFactor * occlusion;
                numLightsConsidered++;
            }
            return lightLevel / (numLightsConsidered > 0 ? numLightsConsidered : 1); // Average light level, avoid divide by 0
        }

        /// <summary>
        /// Used in <see cref="GetLightLevelAroundBot"/> to check if the given <paramref name="light"/> should be considered.
        /// </summary>
        /// <param name="light">The light to check</param>
        /// <returns>true: don't consider this light source; otherwise false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ShouldIgnoreLightSource([NotNullWhen(false)] Light? light)
        {
            // Make sure its a active light!
            if (light == null || !light.enabled || light.intensity <= 0f) 
            { 
                return true; 
            }

            return false;
        }

        /// <summary>
        /// Helper function that checks if we have LOS to the light
        /// </summary>
        /// <remarks>
        /// This isn't perfect whatsoever, but its better than nothing!
        /// </remarks>
        /// <param name="lightPos"></param>
        /// <param name="targetPos"></param>
        /// <returns></returns>
        internal static float GetLightOcclusionFactor(Vector3 lightPos, Vector3 targetPos)
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            int blocked = 0;
            const int samples = 3;

            Vector3 dir = (targetPos - lightPos).normalized;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(dir, Vector3.forward);

            right = right.normalized * 0.2f;

            int layerMask = instanceSOR.collidersAndRoomMaskAndDefault;
            if (Physics.Linecast(lightPos, targetPos, layerMask)) blocked++;
            if (Physics.Linecast(lightPos + right, targetPos, layerMask)) blocked++;
            if (Physics.Linecast(lightPos - right, targetPos, layerMask)) blocked++;

            float occlusion = 1f - ((float)blocked / (float)samples);
            return Mathf.Max(occlusion, 0.15f); // light wrap
        }

        #endregion

        #region Damage bot RPC

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false, bool tamperWithMeshes = false)
        {
            // Bots use PlayerControllerB objects, so there is no mesh to enable or disable!
            return;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null!, bool playHitSFX = false, int hitID = -1)
        {
            // The HitEnemy function works with player controller instead
            return;
        }

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null!)
        {
            // TODO: Assess if this function is actually called. May be worth using!
            return;
        }

        /// <summary>
        /// Apply the damage to the lethalBot, kill him if needed, or make critically injured
        /// </summary>
        /// <param name="damageNumber"></param>
        /// <param name="hasDamageSFX"></param>
        /// <param name="callRPC"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        /// <param name="fallDamage">Coming from a long fall ?</param>
        /// <param name="force">Force applied to the lethalBot when taking the hit</param>
        public void DamageLethalBot(int damageNumber,
                                  bool hasDamageSFX = true,
                                  bool callRPC = true,
                                  CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                  int deathAnimation = 0,
                                  bool fallDamage = false,
                                  Vector3 force = default(Vector3))
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            Plugin.LogDebug(@$"DamageLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {lethalBotController.playerUsername},
                            damageNumber {damageNumber}, causeOfDeath {causeOfDeath}, deathAnimation {deathAnimation}, fallDamage {fallDamage}, force {force}");
            if (!base.IsOwner
                || lethalBotController.isPlayerDead
                || !lethalBotController.AllowPlayerDeath())
            {
                return;
            }

            // Apply damage, if not killed, set the minimum health to 5
            if (lethalBotController.health - damageNumber <= 0
                && !lethalBotController.criticallyInjured
                && damageNumber < MaxHealthPercent(50)
                && MaxHealthPercent(10) != MaxHealthPercent(20))
            {
                lethalBotController.health = MaxHealthPercent(5);
            }
            else
            {
                lethalBotController.health = Mathf.Clamp(lethalBotController.health - damageNumber, 0, MaxHealth);
            }

            // Kill lethalBot if necessary
            if (lethalBotController.health <= 0)
            {
                // Kill and network death to other players!
                bool spawnBody = deathAnimation != -1;
                lethalBotController.KillPlayer(force, spawnBody: spawnBody, causeOfDeath, deathAnimation, positionOffset: default);
            }
            else
            {
                // Critically injured
                if ((lethalBotController.health < MaxHealthPercent(10) || lethalBotController.health == MaxHealthPercent(5))
                    && !lethalBotController.criticallyInjured)
                {
                    // Client side only, since we are already in an rpc send to all clients
                    lethalBotController.MakeCriticallyInjured(enable: true);
                }
                else
                {
                    // Limit sprinting when close to death
                    if (damageNumber >= MaxHealthPercent(10))
                    {
                        lethalBotController.sprintMeter = Mathf.Clamp(lethalBotController.sprintMeter + (float)damageNumber / 125f, 0f, 1f);
                    }
                    if (callRPC)
                    {
                        if (base.IsServer)
                        {
                            lethalBotController.DamagePlayerClientRpc(damageNumber, lethalBotController.health);
                        }
                        else
                        {
                            lethalBotController.DamagePlayerServerRpc(damageNumber, lethalBotController.health);
                        }
                    }
                }
                if (fallDamage)
                {
                    lethalBotController.movementAudio.PlayOneShot(StartOfRound.Instance.fallDamageSFX, 1f);
                    WalkieTalkie.TransmitOneShotAudio(lethalBotController.movementAudio, StartOfRound.Instance.fallDamageSFX);
                    lethalBotController.BreakLegsSFXClientRpc();
                }
                else if (hasDamageSFX)
                {
                    lethalBotController.movementAudio.PlayOneShot(StartOfRound.Instance.damageSFX, 1f);
                }

                // Audio, we sync since we are not in an RPC
                this.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                {
                    VoiceState = EnumVoicesState.Hit,
                    CanTalkIfOtherLethalBotTalk = true,
                    WaitForCooldown = false,
                    CutCurrentVoiceStateToTalk = true,
                    CanRepeatVoiceState = true,

                    ShouldSync = true,
                    IsLethalBotInside = lethalBotController.isInsideFactory,
                    AllowSwearing = Plugin.Config.AllowSwearing.Value
                });
            }

            lethalBotController.takingFallDamage = false;
            if (!lethalBotController.inSpecialInteractAnimation && !lethalBotController.twoHandedAnimation)
            {
                lethalBotController.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_DAMAGE);
            }
            lethalBotController.specialAnimationWeight = 1f;
            lethalBotController.PlayQuickSpecialAnimation(0.7f);
        }

        public void HealthRegen()
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (lethalBotController.limpMultiplier > 0f)
            {
                lethalBotController.limpMultiplier -= Time.deltaTime / 1.8f;
            }

            if (lethalBotController.health < MaxHealthPercent(20)
                || lethalBotController.health == MaxHealthPercent(5))
            {
                if (lethalBotController.healthRegenerateTimer <= 0f)
                {
                    lethalBotController.healthRegenerateTimer = healthRegenerateTimerMax;
                    lethalBotController.health = lethalBotController.health + 1 > MaxHealth ? MaxHealth : lethalBotController.health + 1;
                    if (IsOwner && lethalBotController.criticallyInjured &&
                        (lethalBotController.health >= MaxHealthPercent(20) || MaxHealth == 1))
                    {
                        lethalBotController.MakeCriticallyInjured(false);
                    }
                }
                else
                {
                    lethalBotController.healthRegenerateTimer -= Time.deltaTime;
                }
            }
        }

        #endregion

        #region Kill bot RPC

        public override void KillEnemy(bool destroy = false)
        {
            // The kill function works with player controller instead
            return;
        }

        /// <summary>
        /// The action to kill lethalBot just like a human player
        /// </summary>
        /// <remarks>
        /// Better to call <see cref="PlayerControllerB.KillPlayer"><c>PlayerControllerB.KillPlayer</c></see> so prefixes from other mods can activate. (ex : peepers)
        /// The base game function will be ignored because this addon marks the player as dead!
        /// </remarks>
        /// <param name="bodyVelocity"></param>
        /// <param name="spawnBody">Should a body be spawned ?</param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        /// <param name="positionOffset"></param>
        /// <param name="setOverrideDropItems"></param>
        internal void KillLethalBot(Vector3 bodyVelocity,
                                bool spawnBody,
                                CauseOfDeath causeOfDeath,
                                int deathAnimation,
                                Vector3 positionOffset,
                                bool setOverrideDropItems)
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            if (!lethalBotController.IsOwner 
                || lethalBotController.isPlayerDead
                || !lethalBotController.AllowPlayerDeath())
            {
                return;
            }

            Plugin.LogInfo(@$"KillLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {lethalBotController.playerUsername}
                            bodyVelocity {bodyVelocity}, spawnBody {spawnBody}, causeOfDeath {causeOfDeath}, deathAnimation {deathAnimation}, positionOffset {positionOffset}");

            // Reset body
            lethalBotController.overrideDontSpawnBody = false;
            lethalBotController.overrideDropItems = setOverrideDropItems;
            lethalBotController.isPlayerDead = true;
            lethalBotController.isSprinting = false;
            if (!spawnBody)
            {
                deathAnimation = -1;
            }

            // WHY ZEEKERSS, now I have to recreate a bunch of logic for the bots. :(
            // NOTE: Ok looking at the code, only one enemy in the game hooks into this, it uses the provided controller.
            // I had to make a small transpiler patch to make sure it only uses the provided controller so it works with both the local player and bots.
            // I hope other modders that use this even in the future only use the provided controller, otherwise things may break in very weird ways!
            StartOfRound.Instance.LocalPlayerDieEvent.Invoke(lethalBotController, deathAnimation);
            lethalBotController.isPlayerControlled = false;
            lethalBotController.thisPlayerModelArms.enabled = false;
            lethalBotController.localVisor.position = lethalBotController.playersManager.notSpawnedPosition.position;
            lethalBotController.DisablePlayerModel(lethalBotController.gameObject);
            lethalBotController.isInsideFactory = false;
            lethalBotController.IsInspectingItem = false;
            if (IsUsingTerminal())
            {
                // If we were using the terminal, we should "leave" it so other players can use it!
                LeaveTerminal(syncTerminalInUse: true, forceEndUse: true);
            }
            lethalBotController.inTerminalMenu = false;
            lethalBotController.twoHanded = false;
            lethalBotController.carryWeight = 1f;
            lethalBotController.fallValue = 0f;
            lethalBotController.fallValueUncapped = 0f;
            lethalBotController.takingFallDamage = false;
            StopSinkingState();
            NpcController.DrowningTimer = 1f;
            lethalBotController.sinkingValue = 0f;
            lethalBotController.hinderedMultiplier = 1f;
            lethalBotController.isMovementHindered = 0;
            lethalBotController.inAnimationWithEnemy = null;
            lethalBotController.positionOfDeath = lethalBotController.transform.position;
            lethalBotController.bleedingHeavily = false;
            lethalBotController.setPositionOfDeadPlayer = true;
            lethalBotController.snapToServerPosition = false;
            lethalBotController.causeOfDeath = causeOfDeath;
            lethalBotController.drunkness = 0f;
            lethalBotController.drunknessInertia = 0f;
            lethalBotController.poison = 0f;
            lethalBotController.poisonInertia = 0f;
            lethalBotController.slipperyFloor = 0f;
            lethalBotController.slimeSlipAudio.Stop();
            lethalBotController.slimeSlipAudioVolumeSync = 0f;
            lethalBotController.slimeOnFaceDecals[0].gameObject.SetActive(value: false);
            lethalBotController.slimeOnFaceDecals[1].gameObject.SetActive(value: false);
            if (spawnBody && !lethalBotController.overrideDontSpawnBody)
            {
                //lethalBotController.thisPlayerBody.position = lethalBotController.transform.position;
                //lethalBotController.thisPlayerBody.rotation = lethalBotController.transform.rotation;
                lethalBotController.SpawnDeadBody((int)lethalBotController.playerClientId, bodyVelocity, (int)causeOfDeath, lethalBotController, deathAnimation, null, null, positionOffset: positionOffset);

                // Sigh, if the death animation is set to 9 the body has a chance to be null!
                if (lethalBotController.deadBody != null)
                {
                    // HACKHACK: Slightly change body position or else the body gets teleported out of bounds with shotgun or knife (don't know why)
                    //lethalBotController.deadBody.transform.position = lethalBotController.thisPlayerBody.position + Vector3.up * num + positionOffset;
                    lethalBotController.deadBody.transform.position += Vector3.up * 0.001f;
                    this.LethalBotIdentity.DeadBody = lethalBotController.deadBody;

                    // Lets make sure the bots don't attempt to grab dead bodies as soon as a player is killed!
                    GrabbableObject? deadBody = lethalBotController.deadBody.grabBodyObject;
                    if (deadBody != null)
                    {
                        DictJustDroppedItems[deadBody] = Time.realtimeSinceStartup;
                    }
                }
                else
                {
                    Plugin.LogWarning($"Bot {lethalBotController.playerUsername} dead body was not spawned. This is probably a bug with another mod or the base game itself!");
                }
            }
            lethalBotController.physicsParent = null;
            lethalBotController.overridePhysicsParent = null;
            lethalBotController.lastSyncedPhysicsParent = null;
            NpcController.CurrentLethalBotPhysicsRegions.Clear();
            this.ReParentLethalBot(lethalBotController.playersManager.playersContainer);
            lethalBotController.CancelSpecialTriggerAnimations();
            SoundManager.Instance.playerVoicePitchTargets[lethalBotController.playerClientId] = 1f;
            SoundManager.Instance.playerVoicePitchLerpSpeed[lethalBotController.playerClientId] = 3f;
            this.State?.LethalBotInteraction?.StopHoldInteractionOnTrigger();
            lethalBotController.KillPlayerServerRpc((int)lethalBotController.playerClientId, spawnBody, bodyVelocity, (int)causeOfDeath, deathAnimation, positionOffset, setOverrideDropItems);
            Plugin.LogInfo($"Override drop items : {lethalBotController.overrideDropItems}; overridedontspawnbody: {lethalBotController.overrideDontSpawnBody}");
            if (lethalBotController.overrideDropItems)
            {
                // This kill call is networked to all other players, DropAllHeldItemsAndSync is a network call,
                // so we only run this on the owning client.
                lethalBotController.DropAllHeldItemsAndSync(lethalBotController.transform.position, lethalBotController.serverItemHolder.position, lethalBotController.serverItemHolder.eulerAngles, lethalBotController.playerEye.position, lethalBotController.playerEye.eulerAngles);
            }
            else
            {
                lethalBotController.DropAllHeldItems(spawnBody);
            }
            if (this.State?.TargetItem != null)
            {
                // If the bot died trying to pickup an item, we need to make sure no other bot tries to pick it up!
                // As it may be too dangerous around the item
                DictJustDroppedItems[this.State.TargetItem] = Time.realtimeSinceStartup;
            }
            lethalBotController.DisableJetpackControlsLocally();
            NpcController.IsControllerInCruiser = false;
            if (GameNetworkManager.Instance.localPlayerController.isPlayerDead)
            {
                HUDManager.Instance.UpdateBoxesSpectateUI();
            }
            this.isEnemyDead = true;
            this.LethalBotIdentity.Hp = 0;
            StopOffMeshLinkMovement(warpToEnd: false);
            SetAgent(enabled: false);
            //this.LethalBotIdentity.Voice.StopAudioFadeOut();
            this.State = new BrainDeadState(this);
            Plugin.LogDebug($"Ran kill lethalBot function for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {lethalBotController.playerUsername}");

            // Remove bot from their group
            GroupManager.Instance.RemoveFromCurrentGroupAndSync(lethalBotController);

            // Compat with revive company mod
            if (Plugin.IsModReviveCompanyLoaded)
            {
                ReviveCompanySetPlayerDiedAt((int)lethalBotController.playerClientId);
            }

            // Compat with Lethal Phones
            if (Plugin.IsModLethalPhonesLoaded)
            {
                CallLethalPhonesDeath((int)causeOfDeath);
            }
        }

        /// <summary>
        /// Method separate to not load type of plugin of revive company if mod is not loaded in modpack
        /// </summary>
        /// <param name="playerClientId"></param>
        private void ReviveCompanySetPlayerDiedAt(int playerClientId)
        {
            if (OPJosMod.ReviveCompany.GlobalVariables.ModActivated)
            {
                OPJosMod.ReviveCompany.GeneralUtil.SetPlayerDiedAt(playerClientId);
            }
        }

        /// <summary>
        /// Seperate method to prevent loading of Lethal Phones in case the mod isn't installed
        /// </summary>
        private void CallLethalPhonesDeath(int causeOfDeath)
        {
            PlayerPhone? ourPhone = GetOurPlayerPhone();
            if (ourPhone == null)
            {
                Plugin.LogError($"Lethal Bot {NpcController.Npc.playerUsername} tried to call phone death hook, but was unable to find their phone!");
                return;
            }
            ourPhone.Death(causeOfDeath);
        }

        #endregion

        /// <summary>
        /// Scale ragdoll (without stretching the body parts)
        /// </summary>
        /// <param name="transform"></param>
        private void ResizeRagdoll(Transform transform)
        {
            // https://discussions.unity.com/t/joint-system-scale-problems/182154/4
            // https://stackoverflow.com/questions/68663372/how-to-enlarge-a-ragdoll-in-game-unity
            // Grab references to joints anchors, to update them during the game.
            Joint[] joints;
            List<Vector3> connectedAnchors = new List<Vector3>();
            List<Vector3> anchors = new List<Vector3>();
            joints = transform.GetComponentsInChildren<Joint>();

            Joint curJoint;
            for (int i = 0; i < joints.Length; i++)
            {
                curJoint = joints[i];
                connectedAnchors.Add(curJoint.connectedAnchor);
                anchors.Add(curJoint.anchor);
            }

            transform.localScale = Vector3.one;

            // Update joints by resetting them to their original values
            Joint joint;
            for (int i = 0; i < joints.Length; i++)
            {
                joint = joints[i];
                joint.connectedAnchor = connectedAnchors[i];
                joint.anchor = anchors[i];
            }
        }

        #region Spawn animation

        /// <summary>
        /// Checks if the bot is currently in the spawn animation
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSpawningAnimationRunning()
        {
            return spawnAnimationCoroutine != null;
        }

        /// <summary>
        /// Checks if the bot is currently in a special animation
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInSpecialAnimation()
        {
            return NpcController.Npc.inSpecialInteractAnimation || NpcController.Npc.enteringSpecialAnimation;
        }

        /// <summary>
        /// Checks if the bot is using the terminal
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsUsingTerminal()
        {
            if (base.IsOwner)
            {
                return NpcController.Npc.inTerminalMenu;
            }
            Terminal terminal = TerminalManager.Instance.GetTerminal();
            return NpcController.Npc.currentTriggerInAnimationWith == terminal.terminalTrigger;
        }

        public Coroutine BeginLethalBotSpawnAnimation(EnumSpawnAnimation enumSpawnAnimation)
        {
            // Check for the OnlyPlayerSpawnAnimationIfDead flag and apply it as needed!
            if (enumSpawnAnimation == EnumSpawnAnimation.OnlyPlayerSpawnAnimationIfDead)
            {
                enumSpawnAnimation = this.LethalBotIdentity.DiedLastRound ? EnumSpawnAnimation.OnlyPlayerSpawnAnimation : EnumSpawnAnimation.None;
            }
            switch (enumSpawnAnimation)
            {
                case EnumSpawnAnimation.None:
                case EnumSpawnAnimation.ReinitializePlayer:
                    return StartCoroutine(CoroutineNoSpawnAnimation());

                case EnumSpawnAnimation.OnlyPlayerSpawnAnimation:
                    return StartCoroutine(CoroutineOnlyPlayerSpawnAnimation());

                default:
                    return StartCoroutine(CoroutineNoSpawnAnimation());
            }
        }

        private IEnumerator CoroutineNoSpawnAnimation()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return null;

            // Refresh billboard position
            StartCoroutine(Wait2EndOfFrameToRefreshBillBoard());

            if (!IsOwner)
            {
                spawnAnimationCoroutine = null;
                yield break;
            }

            // Change ai state
            this.State = GetDesiredAIState();

            spawnAnimationCoroutine = null;
            yield break;
        }

        private IEnumerator CoroutineOnlyPlayerSpawnAnimation()
        {
            // HACKHACK: Wait a few frames before we start the animation or this wont work!
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return null;

            if (!IsOwner)
            {
                // Wait for spawn player animation
                yield return new WaitForSeconds(3f);

                // Refresh billboard position
                StartCoroutine(Wait2EndOfFrameToRefreshBillBoard());

                NpcController.Npc.inSpecialInteractAnimation = false;
                spawnAnimationCoroutine = null;
                yield break;
            }

            UpdateLethalBotSpecialAnimationValue(specialAnimation: true, timed: 0f, climbingLadder: false);
            NpcController.Npc.inSpecialInteractAnimation = true;
            NpcController.Npc.playerBodyAnimator.ResetTrigger("SpawnPlayer");
            NpcController.Npc.playerBodyAnimator.SetTrigger("SpawnPlayer");

            yield return new WaitForSeconds(3f);

            // Refresh billboard position
            StartCoroutine(Wait2EndOfFrameToRefreshBillBoard());

            NpcController.Npc.inSpecialInteractAnimation = false;
            UpdateLethalBotSpecialAnimationValue(specialAnimation: false, timed: 0f, climbingLadder: false);

            // Change ai state
            this.State = GetDesiredAIState();

            spawnAnimationCoroutine = null;
            yield break;
        }

        /// <summary>
        /// Helper function for deterimining which state run upon spawning!
        /// </summary>
        /// <returns>The <see cref="AIState"/> to run after spawning!</returns>
        internal AIState GetDesiredAIState()
        {
            // Check if we have a set default AI state
            PlayerControllerB lethalBotController = NpcController.Npc;
            EnumDefaultAIState defaultAIState = this.LethalBotIdentity.DefaultAIState;
            if (defaultAIState != EnumDefaultAIState.Dynamic)
            {
                switch (defaultAIState)
                {
                    case EnumDefaultAIState.FollowPlayer:
                    {
                        return new GetCloseToPlayerState(this, GetClosestIrlPlayer());
                    }
                    case EnumDefaultAIState.SearchForScrap:
                    {
                        return new SearchingForScrapState(this);
                    }
                    case EnumDefaultAIState.ShipDuty:
                    {
                        if (LethalBotManager.Instance.MissionControlPlayer != lethalBotController)
                        {
                            LethalBotManager.Instance.MissionControlPlayer = lethalBotController;
                        }
                        return new MissionControlState(this);
                    }
                    case EnumDefaultAIState.TransferLoot:
                    {
                        if (!LethalBotManager.Instance.LootTransferPlayers.Contains(lethalBotController))
                        {
                            LethalBotManager.Instance.AddPlayerToLootTransferListAndSync(lethalBotController);
                        }
                        return new TransferLootState(this);
                    }
                    default:
                    {
                        Plugin.LogWarning($"Bot {lethalBotController.playerUsername} has an invaild default AI state. Falling back to dynamic!");
                        break;
                    }
                }
            }

            // If we spawned on the ship as it was landing or taking off, follow closest player!
            PlayerControllerB closestHumanPlayer = GetClosestIrlPlayer();
            if ((lethalBotController.isInElevator || lethalBotController.isInHangarShipRoom) 
                && (StartOfRound.Instance.shipIsLeaving
                    || !StartOfRound.Instance.shipHasLanded))
            {
                return new GetCloseToPlayerState(this, closestHumanPlayer);
            }

            // We are the current mission controller, better get back to it!
            if (LethalBotManager.Instance.MissionControlPlayer == lethalBotController)
            {
                return new MissionControlState(this);
            }

            // We are within awareness range, they probably revived us! Better get back to following them now.
            if (closestHumanPlayer != null && closestHumanPlayer.isPlayerControlled && !closestHumanPlayer.isPlayerDead)
            {
                float sqrHorizontalDistanceWithTarget = Vector3.Scale((closestHumanPlayer.transform.position - lethalBotController.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
                float sqrVerticalDistanceWithTarget = Vector3.Scale((closestHumanPlayer.transform.position - lethalBotController.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
                if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR
                        && sqrVerticalDistanceWithTarget < Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER)
                {
                    return new GetCloseToPlayerState(this, closestHumanPlayer);
                }
            }

            // No human player nearby? Welp, back to the mines....I mean facility.
            return new SearchingForScrapState(this);
        }

        internal PlayerControllerB GetClosestIrlPlayer()
        {
            PlayerControllerB closest = null!;
            float closestDistSqr = float.MaxValue;
            Vector3 ourPos = this.NpcController.Npc.transform.position;
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!player.isPlayerControlled
                    || player.isPlayerDead
                    || LethalBotManager.Instance.IsPlayerLethalBot(player) 
                    || (Plugin.IsModLethalInternsLoaded && LethalBotManager.IsPlayerIntern(player)))
                {
                    continue;
                }

                float playerDistSqr = (player.transform.position - ourPos).sqrMagnitude;
                if (closest == null || playerDistSqr < closestDistSqr)
                {
                    closest = player;
                    closestDistSqr = playerDistSqr;
                }
            }

            return closest;
        }

        // T-Rizzle: What was the purpose of this function?
        private Vector3 GetRandomPushForce(Vector3 origin, Vector3 point, float forceMean)
        {
            point.y += UnityEngine.Random.Range(2f, 4f);

            //DrawUtil.DrawWhiteLine(LineRendererUtil.GetLineRenderer(), new Ray(origin, point - origin), Vector3.Distance(point, origin));
            float force = UnityEngine.Random.Range(forceMean * 0.5f, forceMean * 1.5f);
            return Vector3.Normalize(point - origin) * force / Vector3.Distance(point, origin);
        }

        #endregion

        #region Pull Ship Lever

        /// <summary>
        /// Makes the bot pull the ship lever as if they had interacted with it.
        /// The bot will play the associated animations!
        /// </summary>
        /// <param name="shipLever"></param>
        /// <returns></returns>
        public bool PullShipLever(StartMatchLever? shipLever)
        {
            // No Ship Lever?
            if (shipLever == null)
            {
                // Fallback and find it ourselves
                if (!SingletonManager.StartMatchLevel.TryGet(out shipLever))
                {
                    return false;
                }
            }

            // We have to wait until the InteractTrigger is available!
            InteractTrigger? shipLeverTrigger = shipLever.triggerScript;
            if (shipLeverTrigger == null 
                || !shipLeverTrigger.interactable 
                || shipLeverTrigger.isPlayingSpecialAnimation)
            {
                return false;
            }

            PlayerControllerB localPlayerController = NpcController.Npc;
            if (localPlayerController.inSpecialInteractAnimation)
            {
                return false;
            }

            if (useInteractTriggerCoroutine != null)
            {
                StopCoroutine(useInteractTriggerCoroutine);
            }
            useInteractTriggerCoroutine = StartCoroutine(pullLeverCoroutine(shipLever, shipLeverTrigger));
            return true;
        }

        /// <summary>
        /// The coroutine used to fake the pulling of the lever
        /// </summary>
        /// <param name="startMatchLever"></param>
        /// <param name="leverTrigger"></param>
        /// <returns></returns>
        private IEnumerator pullLeverCoroutine(StartMatchLever startMatchLever, InteractTrigger leverTrigger)
        {
            PlayerControllerB localPlayerController = NpcController.Npc;
            leverTrigger.UpdateUsedByPlayerServerRpc((int)localPlayerController.playerClientId);
            // Grandpa? Why don't we just call startMatchLever.LeverAnimation() and startMatchLever.PullLever()
            // Well you see Timmy, they only work for the local player and since the bots are not the local player
            // we have to recreate them here
            //startMatchLever.LeverAnimation();
            DoLeverAnimation(startMatchLever);
            leverTrigger.isPlayingSpecialAnimation = true;
            localPlayerController.inSpecialInteractAnimation = true;
            localPlayerController.currentTriggerInAnimationWith = leverTrigger;
            localPlayerController.playerBodyAnimator.ResetTrigger(leverTrigger.animationString);
            localPlayerController.playerBodyAnimator.SetTrigger(leverTrigger.animationString);
            localPlayerController.Crouch(crouch: false);
            localPlayerController.UpdateSpecialAnimationValue(specialAnimation: true, (short)leverTrigger.playerPositionNode.eulerAngles.y);
            if ((bool)leverTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = leverTrigger.overridePlayerParent;
            }
            //leverTrigger.interactable = false; // Don't let other player use the lever!
            yield return new WaitForSeconds(leverTrigger.animationWaitTime);
            leverTrigger.StopSpecialAnimation();
            leverTrigger.isPlayingSpecialAnimation = false;
            localPlayerController.inSpecialInteractAnimation = false;
            if ((bool)leverTrigger.overridePlayerParent && localPlayerController.overridePhysicsParent == leverTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = null;
            }
            localPlayerController.currentTriggerInAnimationWith = null;
            leverTrigger.currentCooldownValue = leverTrigger.cooldownTime;
            leverTrigger.StopUsingServerRpc((int)localPlayerController.playerClientId); // Just in case.
            //startMatchLever.PullLever();
            DoPullLever(startMatchLever);
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.PullLever"/>, but with support for bots
        /// </summary>
        /// <param name="startMatchLever"></param>
        public void DoPullLever(StartMatchLever startMatchLever)
        {
            if (startMatchLever.leverHasBeenPulled)
            {
                // Start game is public :)
                // and also doesn't need any changes!
                startMatchLever.StartGame();
            }
            else
            {
                // End game is public, but only works with the local player!
                EndGame(startMatchLever);
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.EndGame"/>, but with support for bots
        /// </summary>
        private void EndGame(StartMatchLever startMatchLever)
        {
            // Kinda hard to use the ship lever when dead
            if (!NpcController.Npc.isPlayerControlled
                || isEnemyDead
                || NpcController.Npc.isPlayerDead)
            {
                return;
            }
            Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} has successfuly pulled the ship lever to end the round!");

            StartOfRound playersManager = startMatchLever.playersManager;
            if (playersManager.shipHasLanded && !playersManager.shipIsLeaving && !playersManager.shipLeftAutomatically)
            {
                // This is my attempt to call the method from Facility Meltdown
                // Uses HarmonyX's AccessTools to get the type and method
                if (Plugin.IsModFacilityMeltdownLoaded)
                {
                    // This is not essental code, if it fails it fails!
                    try
                    {
                        var type = AccessTools.TypeByName("FacilityMeltdown.Patches.StartMatchLeverPatch");
                        if (type != null)
                        {
                            var method = AccessTools.Method(type, "ShortenMeltdownTimer");
                            if (method != null)
                            {
                                method.Invoke(null, null);
                                Plugin.LogDebug("Successfully invoked ShortenMeltdownTimer via AccessTools.");
                            }
                            else
                            {
                                Plugin.LogWarning("Could not find ShortenMeltdownTimer method.");
                            }
                        }
                        else
                        {
                            Plugin.LogWarning("Could not find StartMatchLeverPatch type from FacilityMeltdown.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogError($"Error while invoking meltdown timer shortening: {ex}");
                    }
                }
                startMatchLever.triggerScript.interactable = false;
                //playersManager.shipIsLeaving = true; // BUGBUG: EndGameServerRpc check for this being set to false and since we are not the host, this breaks as a result!
                playersManager.EndGameServerRpc((int)NpcController.Npc.playerClientId);
                playersManager.shipIsLeaving = true; // Do this here!
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.LeverAnimation"/>, but with support for bots
        /// </summary>
        /// <param name="startMatchLever"></param>
        private void DoLeverAnimation(StartMatchLever startMatchLever)
        {
            // Kinda hard to use the ship lever when dead
            if (!NpcController.Npc.isPlayerControlled
                || isEnemyDead
                || NpcController.Npc.isPlayerDead)
            {
                return;
            }

            StartOfRound playersManager = StartOfRound.Instance;
            if (playersManager.travellingToNewLevel)
            {
                return;
            }
            if (playersManager.inShipPhase && playersManager.connectedPlayersAmount + 1 <= 1 && !startMatchLever.singlePlayerEnabled)
            {
                return;
            }
            if (playersManager.beganLoadingNewLevel)
            {
                return;
            }
            if (SceneManager.sceneCount <= 1 || !playersManager.inShipPhase)
            {
                // Got to love the amount of reflection I have to do here, but oh well
                if (playersManager.shipHasLanded)
                {
                    startMatchLever.PullLeverAnim(leverPulled: false);
                    startMatchLever.clientSentRPC = true;
                    startMatchLever.PlayLeverPullEffectsServerRpc(leverPulled: false);
                }
                else if (playersManager.inShipPhase)
                {
                    startMatchLever.PullLeverAnim(leverPulled: true);
                    startMatchLever.clientSentRPC = true;
                    startMatchLever.SetStartingShipEffects();
                    startMatchLever.PlayLeverPullEffectsServerRpc(leverPulled: true);
                }
            }
        }

        #endregion

        #region Item Charger

        /// <summary>
        /// Makes the bot use the charging coil as if they had interacted with it.
        /// The bot will play the associated animations!
        /// </summary>
        /// <param name="itemCharger"></param>
        /// <returns></returns>
        public bool UseItemCharger(ItemCharger? itemCharger)
        {
            // No Item Charger?
            if (itemCharger == null)
            {
                // Fallback and find it ourselves
                if (!SingletonManager.ItemCharger.TryGet(out itemCharger))
                {
                    return false;
                }
            }

            // HACKHACK: We do the logic checks here!
            GrabbableObject? heldItem = this.HeldItem;
            if (heldItem == null
                || !heldItem.itemProperties.requiresBattery)
            {
                // We are not holding anything!
                // Or the item we are holding can't be recharged!
                return false;
            }

            // We have to wait until the InteractTrigger is available!
            InteractTrigger? itemChargerTrigger = itemCharger.triggerScript;
            if (itemChargerTrigger == null 
                || itemChargerTrigger.isPlayingSpecialAnimation) // Commented out since interactable is changed when the local client is holding a chargable item or not! !itemChargerTrigger.interactable
            {
                return false;
            }

            PlayerControllerB localPlayerController = NpcController.Npc;
            if (localPlayerController.inSpecialInteractAnimation)
            {
                return false;
            }

            itemCharger.PlayChargeItemEffectServerRpc((int)localPlayerController.playerClientId);
            if (useInteractTriggerCoroutine != null)
            {
                StopCoroutine(useInteractTriggerCoroutine);
            }
            useInteractTriggerCoroutine = StartCoroutine(useItemCharger(itemCharger, itemChargerTrigger, heldItem));
            return true;
        }

        /// <summary>
        /// The coroutine used to fake usage of the item charger!
        /// </summary>
        /// <param name="itemCharger"></param>
        /// <param name="itemChargerTrigger"></param>
        /// <param name="itemToCharge"></param>
        /// <returns></returns>
        private IEnumerator useItemCharger(ItemCharger itemCharger, InteractTrigger itemChargerTrigger, GrabbableObject itemToCharge)
        {
            PlayerControllerB localPlayerController = NpcController.Npc;
            itemChargerTrigger.UpdateUsedByPlayerServerRpc((int)localPlayerController.playerClientId);
            // Grandpa? Why don't we just call itemCharger.ChargeItem()
            // Well you see Timmy, they only work for the local player and since the bots are not the local player
            // we have to recreate them here
            //itemCharger.ChargeItem();
            itemChargerTrigger.isPlayingSpecialAnimation = true;
            localPlayerController.inSpecialInteractAnimation = true;
            localPlayerController.currentTriggerInAnimationWith = itemChargerTrigger;
            localPlayerController.playerBodyAnimator.ResetTrigger(itemChargerTrigger.animationString);
            localPlayerController.playerBodyAnimator.SetTrigger(itemChargerTrigger.animationString);
            localPlayerController.Crouch(crouch: false);
            localPlayerController.UpdateSpecialAnimationValue(specialAnimation: true, (short)itemChargerTrigger.playerPositionNode.eulerAngles.y);
            if ((bool)itemChargerTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = itemChargerTrigger.overridePlayerParent;
            }
            // NEEDTOVALIDATE: I may not need to play the zap audio and the animation here since the item charger
            // would do it automatically! This is because of the PlayChargeItemEffectServerRpc call above!
            itemCharger.zapAudio.Play();
            yield return new WaitForSeconds(0.75f);
            itemCharger.chargeStationAnimator.SetTrigger("zap");
            if (itemToCharge != null)
            {
                itemToCharge.insertedBattery = new Battery(isEmpty: false, 1f);
                if (itemToCharge.IsOwner)
                {
                    itemToCharge.SyncBatteryServerRpc(100);
                }
                else
                {
                    SyncBatteryLethalBotServerRpc(itemToCharge.NetworkObject, 100);
                }
            }
            // HACKHACK: Interact trigger would automatically handle this, but since we are recreating it,
            // we have to manually do its logic here. Subtracting the cooldown time is needed to keep the animations
            // and effects in sync!
            yield return new WaitForSeconds(Mathf.Max(itemChargerTrigger.animationWaitTime - 0.75f, 0f));
            itemChargerTrigger.StopSpecialAnimation();
            itemChargerTrigger.isPlayingSpecialAnimation = false;
            localPlayerController.inSpecialInteractAnimation = false;
            if ((bool)itemChargerTrigger.overridePlayerParent && localPlayerController.overridePhysicsParent == itemChargerTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = null;
            }
            localPlayerController.currentTriggerInAnimationWith = null;
            itemChargerTrigger.currentCooldownValue = itemChargerTrigger.cooldownTime;
            itemChargerTrigger.StopUsingServerRpc((int)localPlayerController.playerClientId); // Just in case
        }

        #endregion

        #region Jump RPC

        /// <summary>
        /// Sync the lethalBot doing a jump between server and clients
        /// </summary>
        public void SyncJump()
        {
            if (IsServer)
            {
                JumpClientRpc();
            }
            else
            {
                JumpServerRpc();
            }
        }

        /// <summary>
        /// Server side, call clients to update the lethalBot doing a jump
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void JumpServerRpc()
        {
            JumpClientRpc();
        }

        /// <summary>
        /// Client side, update the action of lethalBot doing a jump
        /// only for not the owner
        /// </summary>
        [ClientRpc]
        private void JumpClientRpc()
        {
            if (!IsClientOwnerOfLethalBot())
            {
                this.NpcController.Npc.PlayJumpAudio();
            }
        }

        #endregion

        #region Land from Jump RPC

        /// <summary>
        /// Sync the landing of the jump of the lethalBot, between server and clients
        /// </summary>
        /// <param name="fallHard"></param>
        public void SyncLandFromJump(bool fallHard)
        {
            if (IsServer)
            {
                JumpLandFromClientRpc(fallHard);
            }
            else
            {
                JumpLandFromServerRpc(fallHard);
            }
        }

        /// <summary>
        /// Server side, call clients to update the action of lethalBot land from jump
        /// </summary>
        /// <param name="fallHard"></param>
        [ServerRpc(RequireOwnership = false)]
        private void JumpLandFromServerRpc(bool fallHard)
        {
            JumpLandFromClientRpc(fallHard);
        }

        /// <summary>
        /// Client side, update the action of lethalBot land from jump
        /// </summary>
        /// <param name="fallHard"></param>
        [ClientRpc]
        private void JumpLandFromClientRpc(bool fallHard)
        {
            if (fallHard)
            {
                NpcController.Npc.movementAudio.PlayOneShot(StartOfRound.Instance.playerHitGroundHard, 1f);
                return;
            }
            NpcController.Npc.movementAudio.PlayOneShot(StartOfRound.Instance.playerHitGroundSoft, 0.7f);
        }

        #endregion

        #region Sinking Helper

        public void StopSinkingState()
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            lethalBotController.isSinking = false;
            lethalBotController.statusEffectAudio.Stop();
            lethalBotController.voiceMuffledByEnemy = false;
            lethalBotController.sourcesCausingSinking = 0;
            lethalBotController.isMovementHindered = 0;
            lethalBotController.hinderedMultiplier = 1f;

            lethalBotController.isUnderwater = false;
            lethalBotController.underwaterCollider = null;
        }

        #endregion

        #region Disable Jetpack RPC

        /// <summary>
        /// Sync the disabling of jetpack mode between server and clients
        /// </summary>
        public void SyncDisableJetpackMode()
        {
            if (IsServer)
            {
                DisableJetpackModeClientRpc();
            }
            else
            {
                DisableJetpackModeServerRpc();
            }
        }

        /// <summary>
        /// Server side, call clients to update the disabling of jetpack mode between server and clients
        /// </summary>
        [ServerRpc]
        private void DisableJetpackModeServerRpc()
        {
            DisableJetpackModeClientRpc();
        }

        /// <summary>
        /// Client side, update the disabling of jetpack mode between server and clients
        /// </summary>
        [ClientRpc]
        private void DisableJetpackModeClientRpc()
        {
            NpcController.Npc.DisableJetpackControlsLocally();
        }

        #endregion

        #region Stop performing emote RPC

        /// <summary>
        /// Sync the stopping the perfoming of emote between server and clients
        /// </summary>
        public void SyncStopPerformingEmote()
        {
            if (IsServer)
            {
                StopPerformingEmoteClientRpc();
            }
            else
            {
                StopPerformingEmoteServerRpc();
            }
        }

        /// <summary>
        /// Server side, call clients to update the stopping the perfoming of emote
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void StopPerformingEmoteServerRpc()
        {
            StopPerformingEmoteClientRpc();
        }

        /// <summary>
        /// Update the stopping the perfoming of emote
        /// </summary>
        [ClientRpc]
        private void StopPerformingEmoteClientRpc()
        {
            NpcController.Npc.performingEmote = false;
        }

        #endregion

        #region Bots suits

        [ServerRpc(RequireOwnership = false)]
        public void ChangeSuitLethalBotServerRpc(ulong idLethalBotController, int suitID, bool playAudio = true)
        {
            ChangeSuitLethalBotClientRpc(idLethalBotController, suitID, playAudio);
        }

        [ClientRpc]
        private void ChangeSuitLethalBotClientRpc(ulong idLethalBotController, int suitID, bool playAudio = true)
        {
            ChangeSuitLethalBot(idLethalBotController, suitID, playAudio: playAudio);
        }

        public void ChangeSuitLethalBot(ulong idLethalBotController, int suitID, bool playAudio = false)
        {
            if (suitID > StartOfRound.Instance.unlockablesList.unlockables.Count())
            {
                suitID = 0;
            }

            // Do we own the suit?
            if (!LethalBotIdentity.IsSuitOwned(suitID))
            {
                return;
            }

            PlayerControllerB lethalBotController = StartOfRound.Instance.allPlayerScripts[idLethalBotController];

            UnlockableSuit.SwitchSuitForPlayer(lethalBotController, suitID, playAudio);
            lethalBotController.thisPlayerModelArms.enabled = false;
            StartCoroutine(Wait2EndOfFrameToRefreshBillBoard());
            LethalBotIdentity.SuitID = suitID;

            Plugin.LogDebug($"Changed suit of lethalBot {lethalBotController.playerUsername} to {suitID}: {StartOfRound.Instance.unlockablesList.unlockables[suitID].unlockableName}");
        }

        private IEnumerator Wait2EndOfFrameToRefreshBillBoard()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            NpcController.RefreshBillBoardPosition();
            yield break;
        }

        #endregion

        #region Emotes

        [ServerRpc(RequireOwnership = false)]
        public void StartPerformingEmoteLethalBotServerRpc(int emoteID)
        {
            StartPerformingEmoteLethalBotClientRpc(emoteID);
        }

        [ClientRpc]
        private void StartPerformingEmoteLethalBotClientRpc(int emoteID)
        {
            NpcController.Npc.performingEmote = true;
            NpcController.Npc.playerBodyAnimator.SetInteger("emoteNumber", emoteID);
        }

        #endregion

        #region TooManyEmotes

        /// <summary>
        /// Makes the bot play or sync the entered emote and player
        /// </summary>
        /// <param name="tooManyEmoteID">The emote to play</param>
        /// <param name="playerToSync">The player to sync the emote with</param>
        public void PerformTooManyEmoteLethalBotAndSync(int tooManyEmoteID, int playerToSync = -1)
        {
            if (base.IsServer)
            {
                PerformTooManyLethalBotClientRpc(tooManyEmoteID, playerToSync);
            }
            else
            {
                PerformTooManyEmoteLethalBotServerRpc(tooManyEmoteID, playerToSync);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PerformTooManyEmoteLethalBotServerRpc(int tooManyEmoteID, int playerToSync = -1)
        {
            PerformTooManyLethalBotClientRpc(tooManyEmoteID, playerToSync);
        }

        [ClientRpc]
        private void PerformTooManyLethalBotClientRpc(int tooManyEmoteID, int playerToSync = -1)
        {
            NpcController.PerformTooManyEmote(tooManyEmoteID, playerToSync);
        }

        /// <summary>
        /// Makes the current bot stop preforming its too many emote
        /// </summary>
        public void StopPerformTooManyEmoteLethalBotAndSync()
        {
            if (base.IsServer)
            {
                StopPerformTooManyLethalBotClientRpc();
            }
            else
            {
                StopPerformTooManyEmoteLethalBotServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void StopPerformTooManyEmoteLethalBotServerRpc()
        {
            StopPerformTooManyLethalBotClientRpc();
        }

        [ClientRpc]
        private void StopPerformTooManyLethalBotClientRpc()
        {
            NpcController.StopPerformingTooManyEmote();
        }

        #endregion
    }
    public class TimedTouchingGroundCheck
    {
        private bool isTouchingGround = true;
        private RaycastHit groundHit;

        private const float TIMER = 0.2f;
        private float lastTimeCalculate;

        public bool IsTouchingGround(Vector3 lethalBotPosition)
        {
            if (!NeedToRecalculate())
            {
                return isTouchingGround;
            }

            CalculateTouchingGround(lethalBotPosition);
            return isTouchingGround;
        }

        public RaycastHit GetGroundHit(Vector3 lethalBotPosition)
        {
            if (!NeedToRecalculate())
            {
                return groundHit;
            }

            CalculateTouchingGround(lethalBotPosition);
            return groundHit;
        }

        public void ForceRecalculationNextThink(bool invalidateIsTouchingGround = false)
        {
            if (invalidateIsTouchingGround)
            {
                isTouchingGround = false;
                lastTimeCalculate = Time.realtimeSinceStartup + TIMER; // HACKHACK: Give time for the bridge to fall away before checking again!
            }
            else
            {
                lastTimeCalculate = 0;
            }
        }

        public bool NeedToRecalculate()
        {
            return lastTimeCalculate < Time.realtimeSinceStartup;
        }

        private void CalculateTouchingGround(Vector3 lethalBotPosition)
        {
            lastTimeCalculate = Time.realtimeSinceStartup + TIMER;
            isTouchingGround = Physics.Raycast(new Ray(lethalBotPosition + Vector3.up, Vector3.down),
                                               out groundHit,
                                               2.5f,
                                               StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore);
        }
    }
}