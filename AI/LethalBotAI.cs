using DunGen;
using FacilityMeltdown;
using FacilityMeltdown.MeltdownSequence.Behaviours;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.NetworkSerializers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Patches.MapPatches;
using LethalBots.Patches.ModPatches.ModelRplcmntAPI;
using LethalBots.Patches.NpcPatches;
using LethalBots.Utils;
using LethalInternship.AI;
using LethalLib.Modules;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Component = UnityEngine.Component;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Random = System.Random;
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
                    oldState.OnExitState();
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
        /// <summary>
        /// Currently held item by lethalBot
        /// </summary>
        /// <remarks>
        /// NEEDTOVALIDATE: Should I just make this a glorified call to <see cref="PlayerControllerB.currentlyHeldObjectServer"/>?<br/>
        /// After all <see cref="PlayerControllerB.currentlyHeldObjectServer"/> is used internally by most of the game's code!
        /// </remarks>
        public GrabbableObject? HeldItem = null!;
        public Collider LethalBotBodyCollider = null!;

        public int BotId = -1;
        public int MaxHealth = 100;
        public float TimeSinceTeleporting = 0f;

        public List<IBodyReplacementBase> ListModelReplacement = null!;

        public TimedTouchingGroundCheck IsTouchingGroundTimedCheck = null!;
        public TimedAngleFOVWithLocalPlayerCheck AngleFOVWithLocalPlayerTimedCheck = null!;

        private EnumStateControllerMovement StateControllerMovement;
        private static InteractTrigger[] laddersInteractTrigger = null!;
        public static EntranceTeleport[] EntrancesTeleportArray { private set; get; } = null!;
        public static QuicksandTrigger[] QuicksandArray { private set; get; } = null!;
        private static DoorLock[] doorLocksArray = null!;
        private static ShipTeleporter? _inverseTeleporter = null;
        public static ShipTeleporter? InverseTeleporter
        {
            get
            {
                if (_inverseTeleporter == null)
                {
                    _inverseTeleporter = FindInverseTeleporter();
                }
                return _inverseTeleporter;
            }
        }
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
                    return (NpcController.Npc.transform.position - ElevatorScript.elevatorInsidePoint.position).sqrMagnitude < 2f * 2f;
                }
                return false;
            }
        }
        private Dictionary<string, Component> dictComponentByCollider = null!;

        private Coroutine grabObjectCoroutine = null!;
        private Coroutine? spawnAnimationCoroutine = null;
        public Coroutine? useLadderCoroutine = null;
        private Coroutine? waitUntilEndOfOffMeshLinkCoroutine = null;
        internal Coroutine? useInteractTriggerCoroutine = null;

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

        private string stateIndicatorServer = string.Empty;
        private Vector3 previousWantedDestination;
        private bool hasDestinationChanged = true;
        private float updateDestinationIntervalLethalBotAI;
        private float updateDestinationTimer;
        private float healthRegenerateTimerMax;
        private float timerCheckDoor;
        private float timerCheckLockedDoor;
        private float nextExposedToEnemyTimer;
        private bool _areWeExposed;
        private float nextEyelessdogCheckTimer;
        private bool _isEyelessDogInPromimity;

        public LineRendererUtil LineRendererUtil = null!;
        private float stuckTimer; // Used for stuck detection

        private void Awake()
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
                Plugin.LogDebug($"LethalBot Area Mask {agent.areaMask}");
                agent.enabled = false;
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
        public void Init(EnumSpawnAnimation enumSpawnAnimation)
        {
            // Entrances
            EntrancesTeleportArray = Object.FindObjectsOfType<EntranceTeleport>(includeInactive: false);

            // Ladders
            laddersInteractTrigger = RefreshLaddersList();

            // Doors
            doorLocksArray = Object.FindObjectsOfType<DoorLock>(includeInactive: false);

            // Elevator
            ElevatorScript = Object.FindObjectOfType<MineshaftElevatorController>();

            // Find all patches of quicksand and water
            QuicksandArray = Object.FindObjectsOfType<QuicksandTrigger>();

            // Important colliders
            InitImportantColliders();

            // Grabbableobject
            LethalBotManager.Instance.RegisterItems();

            // Init controller
            this.NpcController.Awake();

            // Health
            MaxHealth = LethalBotIdentity.HpMax;
            NpcController.Npc.health = MaxHealth;
            healthRegenerateTimerMax = 100f / (float)MaxHealth;
            NpcController.Npc.healthRegenerateTimer = healthRegenerateTimerMax;

            // AI init
            this.ventAnimationFinished = true;
            this.isEnemyDead = false;
            this.enabled = true;
            addPlayerVelocityToDestination = 3f;

            // Body collider
            LethalBotBodyCollider = NpcController.Npc.GetComponentInChildren<Collider>();
            BoxCollider ourCollider = this.GetComponentInChildren<BoxCollider>();
            ourCollider?.size = LethalBotBodyCollider.bounds.extents; // Set the bounds of the collider to the body collider bounds

            // Bot voice
            InitLethalBotVoiceComponent();
            UpdateLethalBotVoiceEffects();

            // Line renderer used for debugging stuff
            LineRendererUtil = new LineRendererUtil(6, this.transform);

            TeleportAgentAIAndBody(NpcController.Npc.transform.position, !StartOfRound.Instance.shipHasLanded);
            StateControllerMovement = EnumStateControllerMovement.FollowAgent;

            // Start timed calulation
            IsTouchingGroundTimedCheck = new TimedTouchingGroundCheck();
            AngleFOVWithLocalPlayerTimedCheck = new TimedAngleFOVWithLocalPlayerCheck();

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
        }

        private void FixedUpdate()
        {
            UpdateSurfaceRayCast();
        }

        private void UpdateSurfaceRayCast()
        {
            NpcController.IsTouchingGround = IsTouchingGroundTimedCheck.IsTouchingGround(NpcController.Npc.thisPlayerBody.position);

            // Update current material standing on
            if (NpcController.IsTouchingGround)
            {
                /*RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position);
                if (LethalBotManager.Instance.DictTagSurfaceIndex.ContainsKey(groundRaycastHit.collider.tag))
                {
                    NpcController.Npc.currentFootstepSurfaceIndex = LethalBotManager.Instance.DictTagSurfaceIndex[groundRaycastHit.collider.tag];
                }*/
                NpcController.Npc.GetCurrentMaterialStandingOn();
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
            // Update identity
            LethalBotIdentity.Hp = NpcController.Npc.isPlayerDead ? 0 : NpcController.Npc.health;

            // Not owner no AI
            if (!IsOwner)
            {
                if (currentSearch.inProgress)
                {
                    StopSearch(currentSearch);
                }

                SetAgent(enabled: false);

                if (State == null
                    || State.GetAIState() != EnumAIStates.BrainDead)
                {
                    State = new BrainDeadState(this);
                }

                return;
            }

            if (NpcController == null
                || !NpcController.Npc.gameObject.activeSelf
                || !NpcController.Npc.isPlayerControlled
                || isEnemyDead
                || NpcController.Npc.isPlayerDead)
            {
                // Lethal Bot dead or
                // Not controlled we do nothing
                SetAgent(enabled: false);
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
                        updateDestinationIntervalLethalBotAI = AIIntervalTime;
                    }
                }
                else if (NpcController != null 
                    && NpcController.Npc.isPlayerDead)
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
            if (!NpcController.Npc.isClimbingLadder && !NpcController.Npc.inTerminalMenu
                && (NpcController.Npc.inSpecialInteractAnimation || NpcController.Npc.enteringSpecialAnimation))
            {
                // If we are using a trigger, set our position and rotation to it!
                InteractTrigger ourTrigger = NpcController.Npc.currentTriggerInAnimationWith;
                if (ourTrigger != null)
                {
                    NpcController.Npc.thisPlayerBody.localPosition = Vector3.Lerp(NpcController.Npc.thisPlayerBody.localPosition, NpcController.Npc.thisPlayerBody.parent.InverseTransformPoint(ourTrigger.playerPositionNode.position), Time.deltaTime * 20f);
                    NpcController.Npc.thisPlayerBody.rotation = Quaternion.Lerp(NpcController.Npc.thisPlayerBody.rotation, ourTrigger.playerPositionNode.rotation, Time.deltaTime * 20f);
                    NpcController.SetTurnBodyTowardsDirection(ourTrigger.playerPositionNode.rotation.eulerAngles); // NEEDTOVALIDATE: Is this correct?
                }
                SetAgent(enabled: false);
                return;
            }

            // Update if we are in the elevator start room or not!
            if (ElevatorScript != null)
            {
                // Give the bot a cooldown after the elevator finishing moving before we press the button
                // this gives players and other bots time to move into or out of the elevator
                if (ElevatorScript.elevatorFinishedMoving && ElevatorScript.elevatorDoorOpen)
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
                    if (isOutside || Vector3.Distance(NpcController.Npc.transform.position, ElevatorScript.elevatorBottomPoint.position) < 10f)
                    {
                        IsInElevatorStartRoom = false;
                    }
                }
                else if (Vector3.Distance(NpcController.Npc.transform.position, ElevatorScript.elevatorTopPoint.position) < 20f)
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

                if (!NpcController.Npc.isClimbingLadder
                    && !NpcController.Npc.inSpecialInteractAnimation
                    && !NpcController.Npc.enteringSpecialAnimation)
                {
                    // Npc is following ai agent position that follows destination path
                    if (NpcController.LookAtTarget.IsLookingForward())
                    {
                        NpcController.SetTurnBodyTowardsDirectionWithPosition(this.transform.position);
                    }
                }

                x = Mathf.Lerp(NpcController.Npc.transform.position.x, this.transform.position.x, 0.075f);
                z = Mathf.Lerp(NpcController.Npc.transform.position.z, this.transform.position.z, 0.075f);
            }
            else
            {
                // Clear stuck status
                stuckTimer = 0f;

                if (IsInsideElevator)
                {
                    // NOTE: We use the same code as above when in an elevator or we end up creating prediction issues
                    x = Mathf.Lerp(NpcController.Npc.transform.position.x, this.transform.position.x, 0.075f);
                    z = Mathf.Lerp(NpcController.Npc.transform.position.z, this.transform.position.z, 0.075f);
                }
                else
                {
                    SetAgent(enabled: false);
                    x = Mathf.Lerp(NpcController.Npc.transform.position.x + NpcController.MoveVector.x * Time.deltaTime, this.transform.position.x, 0.075f);
                    z = Mathf.Lerp(NpcController.Npc.transform.position.z + NpcController.MoveVector.z * Time.deltaTime, this.transform.position.z, 0.075f);
                }
            }

            // Movement free (falling from bridge, jetpack, tulip snake taking off...)
            bool shouldFreeMovement = ShouldFreeMovement();
            bool shouldFixedMovement = ShouldFixedMovement();

            // Update position
            if (shouldFreeMovement
                || StateControllerMovement == EnumStateControllerMovement.Free)
            {
                StateControllerMovement = EnumStateControllerMovement.Free;
                //Plugin.LogDebug($"{NpcController.Npc.playerUsername} falling ! NpcController.Npc.transform.position {NpcController.Npc.transform.position} MoveVector {NpcController.MoveVector}");
                /*Vector3 endPos = NpcController.Npc.transform.position + NpcController.MoveVector * Time.deltaTime;
                if (IsTouchingGroundTimedCheck.IsTouchingGround(NpcController.Npc.transform.position) && NpcController.MoveVector.y < 0)
                {
                    RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position);
                    endPos.y = groundRaycastHit.point.y;
                }
                NpcController.Npc.transform.position = endPos;*/
                // Just use the character controller as this fixes multiple issues the old addon had!
                NpcController.Npc.thisController.Move(NpcController.MoveVector * Time.deltaTime);
            }
            else if (!shouldFixedMovement && StateControllerMovement == EnumStateControllerMovement.FollowAgent)
            {
                Vector3 aiPosition = this.transform.position;
                //Plugin.LogDebug($"{NpcController.Npc.playerUsername} --> y {(NpcController.IsTouchingGround ? NpcController.GroundHit.point.y : aiPosition.y)} MoveVector {NpcController.MoveVector}");
                NpcController.Npc.transform.position = new Vector3(x,
                                                                   aiPosition.y,
                                                                   z); ;
                this.transform.position = aiPosition;
                NpcController.Npc.ResetFallGravity();
            }
            else if (shouldFixedMovement)
            {
                // If we are using a trigger, set our position and rotation to it!
                InteractTrigger ourTrigger = NpcController.Npc.currentTriggerInAnimationWith;
                if (ourTrigger != null)
                {
                    NpcController.Npc.thisPlayerBody.localPosition = Vector3.Lerp(NpcController.Npc.thisPlayerBody.localPosition, NpcController.Npc.thisPlayerBody.parent.InverseTransformPoint(ourTrigger.playerPositionNode.position), Time.deltaTime * 20f);
                    NpcController.Npc.thisPlayerBody.rotation = Quaternion.Lerp(NpcController.Npc.thisPlayerBody.rotation, ourTrigger.playerPositionNode.rotation, Time.deltaTime * 20f);
                    NpcController.SetTurnBodyTowardsDirection(ourTrigger.playerPositionNode.rotation.eulerAngles); // NEEDTOVALIDATE: Is this correct?
                }
                this.transform.position = NpcController.Npc.transform.position;
                this.serverPosition = NpcController.Npc.transform.position;
            }

            // Is still falling ?
            if (StateControllerMovement == EnumStateControllerMovement.Free
                && NpcController.IsTouchingGround
                && !shouldFreeMovement)
            {
                //Plugin.LogDebug($"{NpcController.Npc.playerUsername} ============= touch ground GroundHit.point {NpcController.GroundHit.point}");
                StateControllerMovement = EnumStateControllerMovement.FollowAgent;
                TeleportAgentAIAndBody(IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position).point);
                SetDestinationToPositionLethalBotAI(destination); // Refresh our path!
                hasDestinationChanged = true; // Just for good measure!
                //Plugin.LogDebug($"{NpcController.Npc.playerUsername} ============= NpcController.Npc.transform.position {NpcController.Npc.transform.position}");
            }

            // No AI when falling
            if (StateControllerMovement == EnumStateControllerMovement.Free)
            {
                return;
            }

            // Do stuck detection
            if (NpcController.HasToMove || !agent.isOnNavMesh)
            {
                // If we are stuck, teleport to the closest node!
                StartOfRound instanceSOR = StartOfRound.Instance;
                if (agent.velocity.sqrMagnitude < 0.002f
                    && !IsInsideElevator
                    && instanceSOR.shipHasLanded
                    && !instanceSOR.shipIsLeaving
                    && !instanceSOR.shipLeftAutomatically) // Mathf.Abs((NpcController.Npc.oldPlayerPosition - NpcController.Npc.transform.position).sqrMagnitude) < Const.EPSILON * Const.EPSILON
                {
                    if (stuckTimer > 4f)
                    {
                        Plugin.LogWarning($"Bot {NpcController.Npc.playerClientId} {NpcController.Npc.playerUsername} is stuck! Telporting to closest node!");
                        Plugin.LogWarning($"Agent velocity: {agent.velocity.sqrMagnitude} Previous distance from last position: {(NpcController.Npc.oldPlayerPosition - NpcController.Npc.transform.position).sqrMagnitude}");
                        State?.OnBotStuck(); // Call the OnBotStuck method of the current state if it exists
                        hasDestinationChanged = true; // Force a repath!
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

            // Update interval timer for AI calculation
            if (updateDestinationIntervalLethalBotAI >= 0f)
            {
                updateDestinationIntervalLethalBotAI -= Time.deltaTime;
            }
            else
            {
                SetAgent(enabled: !shouldFixedMovement);

                // Do the actual AI calculation
                DoAIInterval();
                updateDestinationIntervalLethalBotAI = AIIntervalTime;
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
            if (isEnemyDead
                || NpcController.Npc.isPlayerDead
                || State == null)
            {
                return;
            }

            // Do the AI calculation behaviour for the current state
            State.DoAI();

            // Doors
            OpenDoorIfNeeded();

            // Ladders
            // FIXME: This causes TOO many issues to be used right now!
            //UseLadderIfNeeded();

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
                if (NpcController.Npc.inTerminalMenu)
                {
                    LeaveTerminal();
                }
            }

            // TODO: Add more to this function!
            // Essentially what I want to do is make a function that manages equipment the bot is holding.
            // Such as flashlights, tzp-inhalent, using the stun-gun, flashbangs, etc.
            // Right now the only equipment the bot uses are walkie-talkies, keys, shovels, knifes, and shotguns.
            // Adding use of more equipment would be nice and add a more "human" aspect to them!
            if (CanUseHeldItem())
            {
                State.UseHeldItem();
            }
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
            }
        }

        private bool ShouldFreeMovement()
        {
            if (NpcController.IsTouchingGround)
            {
                RaycastHit groundRaycastHit = IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position);
                Collider? collider = groundRaycastHit.collider;
                if (collider != null && dictComponentByCollider.TryGetValue(collider.name, out Component component))
                {
                    BridgeTrigger? bridgeTrigger = component as BridgeTrigger;
                    if (bridgeTrigger != null
                        && bridgeTrigger.fallenBridgeColliders.Length > 0
                        && bridgeTrigger.fallenBridgeColliders[0].enabled)
                    {
                        Plugin.LogDebug($"{NpcController.Npc.playerUsername} on fallen bridge ! {IsTouchingGroundTimedCheck.GetGroundHit(NpcController.Npc.thisPlayerBody.position).collider.name}");
                        return true;
                    }
                }
            }

            if (NpcController.Npc.externalForces.y > 7.1f)
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} externalForces {NpcController.Npc.externalForces.y}");
                return true;
            }

            if (NpcController.Npc.externalForceAutoFade.sqrMagnitude > 2f * 2f)
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} externalForceAutoFade {NpcController.Npc.externalForceAutoFade.sqrMagnitude}");
                return true;
            }

            return false;
        }

        private bool ShouldFixedMovement()
        {
            if ((NpcController.Npc.isInElevator || NpcController.Npc.isInHangarShipRoom)
                && (StartOfRound.Instance.shipIsLeaving 
                    || !StartOfRound.Instance.shipHasLanded))
            {
                return true;
            }
            if (NpcController.Npc.currentTriggerInAnimationWith != null 
                && !NpcController.Npc.isClimbingLadder)
            {
                return true;
            }
            return false;
        }

        private void FollowCrouchStateIfCan()
        {
            // The state has TOTAL authority over crouching or not!
            if (State != null)
            {
                bool? shouldCrouch = State.ShouldBotCrouch();
                if (shouldCrouch.HasValue)
                {
                    // We can't crouch while sprinting!
                    if (shouldCrouch.Value == true && NpcController.Npc.isSprinting)
                    {
                        NpcController.OrderToStopSprint();
                    }
                    if (NpcController.Npc.isCrouching != shouldCrouch.Value)
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
                    && !NpcController.Npc.isCrouching)
                {
                    NpcController.OrderToToggleCrouch();
                }
                else if (!targetPlayer.isCrouching
                        && NpcController.Npc.isCrouching)
                {
                    NpcController.OrderToToggleCrouch();
                }
            }
        }

        public bool IsFollowingTargetPlayer()
        {
            switch(State.GetAIState())
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

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy)
        {
            if (!IsOwner)
            {
                return;
            }

            if (collidedEnemy == null
                || collidedEnemy.GetType() == typeof(LethalBotAI)
                || collidedEnemy.GetType() == typeof(FlowerSnakeEnemy))
            {
                return;
            }

            if ((NpcController.Npc.transform.position - other.transform.position).sqrMagnitude < collidedEnemy.enemyType.pushPlayerDistance * collidedEnemy.enemyType.pushPlayerDistance)
            {
                if (collidedEnemy.GetType() != typeof(FlowerSnakeEnemy))
                    NpcController.NearEntitiesPushVector += Vector3.Normalize((NpcController.Npc.transform.position - other.transform.position) * 100f) * collidedEnemy.enemyType.pushPlayerForce;
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
            if (NpcController == null
                || !NpcController.Npc.gameObject.activeSelf
                || !NpcController.Npc.isPlayerControlled
                || isEnemyDead
                || NpcController.Npc.isPlayerDead
                || State == null)
            {
                return;
            }

            // Player voice = 75 ?
            if (noiseID != 75)
            {
                return;
            }

            // Make the lethalBot stop talking for some time
            LethalBotIdentity.Voice.TryStopAudioFadeOut();

            if (IsOwner)
            {
                LethalBotIdentity.Voice.SetNewRandomCooldownAudio();
            }

            Plugin.LogDebug($"Lethal Bot {NpcController.Npc.playerUsername} detected noise noisePosition {noisePosition}, noiseLoudness {noiseLoudness}, timesPlayedInOneSpot {timesPlayedInOneSpot}, noiseID {noiseID}");
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
        /// <returns><see langword="true"/> if a valid and complete path exists; otherwise, <see langword="false"/></returns>

        public static bool IsValidPathToTarget(Vector3 startPosition, Vector3 endPosition, int areaMask, ref NavMeshPath path)
        {
            // Check if we can create a path there first!
            if (!NavMesh.CalculatePath(startPosition, endPosition, areaMask, path))
            {
                return false;
            }

            // Check to make sure the path is valid!
            if (path == null || path.corners.Length == 0)
            {
                return false;
            }

            // This may be a partial path, make sure the end of the path actually reaches our target destiniation!
            if (Vector3.Distance(path.corners[path.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(endPosition, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
            {
                return false;
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
            Plugin.LogDebug($"Is agent on NavMesh {agent.isOnNavMesh}?");
            Plugin.LogDebug($"Is agent enabled {agent.enabled}?");
            // Make sure the agent is enabled BEFORE we call the pathfind function!
            bool wasEnabled = agent.enabled;
            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.CalculatePath(targetPos, path))
            {
                Plugin.LogDebug("IsValidPathToTarget: Path could not be calculated");
                return false;
            }

            if (path == null || path.corners.Length == 0)
            {
                Plugin.LogDebug("IsValidPathToTarget: Path is invalid");
                return false;
            }

            if (Vector3.Distance(path.corners[path.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, nearestNavAreaRange)) > maxRangeToEnd)
            {
                Plugin.LogDebug($"IsValidPathToTarget: Path is not complete; final waypoint of path was too far from target position: {targetPos}");
                return false;
            }

            if (calculatePathDistance)
            {
                for (int i = 1; i < path.corners.Length; i++)
                {
                    pathDistance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                }
            }

            // No need for the agent to be active on non-owners!
            if (!base.IsOwner)
            {
                agent.enabled = false;
            }
            else
            {
                agent.enabled = wasEnabled;
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
                            if (checkLOSToTarget.isEnemyDead || !checkLOSToTarget.isOutside)
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

                            Vector3 enemyViewVector = useEyePosition ? checkLOSToTarget.eye.position : checkLOSToTarget.transform.position;
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

                    // Define a distance threshold (buffer) to avoid paths too close to quicksand
                    float quicksandBuffer = 3f;
                    Plugin.LogDebug($"Testing quicksand safety between current node {nodePos} and previous node {previousNode}");
                    foreach (var quicksand in QuicksandArray)
                    {
                        if (!quicksand.isActiveAndEnabled)
                            continue;

                        Collider? collider = quicksand.gameObject.GetComponent<Collider>();
                        if (collider == null)
                            continue;

                        Vector3 a = previousNode;
                        Vector3 b = nodePos;
                        Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(a, b, collider.bounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);

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
                            Vector3 testPoint = Physics.ClosestPoint(closestPoint, collider, collider.transform.position, collider.transform.rotation);
                            if ((testPoint - closestPoint).sqrMagnitude < quicksandBuffer * quicksandBuffer)
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
                            if (collider.bounds.Contains(simulatedHead))
                            {
                                // Test the amount of time we would spend underwater to get here
                                Plugin.LogDebug("Simulated head intersects water!");
                                float travelTime = tempDistance / moveSpeed;

                                float downingDelta = travelTime / 10f; // Match game logic
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
                            if (checkLOSToTarget.isEnemyDead
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

                            Vector3 enemyViewVector = useEyePosition ? checkLOSToTarget.eye.position : checkLOSToTarget.transform.position;
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

                    // Define a distance threshold (buffer) to avoid paths too close to quicksand
                    float quicksandBuffer = 3f;
                    Plugin.LogDebug($"Testing quicksand safety between current node {nodePos} and previous node {previousNode}");
                    foreach (var quicksand in QuicksandArray)
                    {
                        if (!quicksand.isActiveAndEnabled)
                            continue;

                        Collider? collider = quicksand.gameObject.GetComponent<Collider>();
                        if (collider == null)
                            continue;

                        Vector3 a = previousNode;
                        Vector3 b = nodePos;
                        Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(a, b, collider.bounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);

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
                            for (int i = 0; i < arraySize; i++)
                            {
                                var hitCollider = hitColliders[i];
                                if (hitCollider == collider)
                                {
                                    Plugin.LogDebug("Segment intersects solid quicksand!");
                                    return true;
                                }
                            }*/

                            // Check if the closest point is within or on the collider
                            Vector3 testPoint = Physics.ClosestPoint(closestPoint, collider, collider.transform.position, collider.transform.rotation);
                            if ((testPoint - closestPoint).sqrMagnitude < quicksandBuffer * quicksandBuffer)
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
                            if (collider.bounds.Contains(simulatedHead))
                            {
                                // Test the amount of time we would spend underwater to get here
                                Plugin.LogDebug("Simulated head intersects water!");
                                float travelTime = tempDistance / moveSpeed;

                                float downingDelta = travelTime / 10f; // Match game logic
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
        /// <inheritdoc cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, CancellationToken)"/>
        public Task<(bool isDangerous, float pathDistance)> TryStartPathDangerousAsync(Vector3 targetPos, bool calculatePathDistance = false, bool useEyePosition = true, bool checkForEnemies = true, CancellationToken token = default)
        {
            // Check if we were canceled before pathfinding!  
            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<(bool isDangerous, float pathDistance)>(token);
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
                return Task.FromResult((true, 0f));
            }

            Plugin.LogDebug($"Path found to target {targetPos}. Checking for danger...");
            return IsPathDangerousAsync(ourPath, calculatePathDistance, useEyePosition, checkForEnemies, token);
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
        /// <param name="token">The cancelation token, this allows you to stop the function early!</param>
        /// <returns>Task indicating if the path is safe or not</returns>
        private async Task<(bool isDangerous, float pathDistance)> IsPathDangerousAsync(NavMeshPath ourPath, bool calculatePathDistance = false, bool useEyePosition = true, bool checkForEnemies = true, CancellationToken token = default)
        {
            // Check if we were canceled before pathfinding!
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            // We need to make sure we have a valid path
            if (ourPath == null || ourPath.corners.Length == 0)
            {
                return (true, 0f);
            }

            // Cache stuff we use a lot, since this could get very expensive fast!
            bool skipLOSCheckThisSegment = false;
            float pathDistance = 0f; // We must cache this and set it when we finish, since we are running asynchronously
            float headOffset = NpcController.Npc.gameplayCamera.transform.position.y - NpcController.Npc.transform.position.y;
            float predictedDrownTimer = NpcController.DrowningTimer; // Travel based on how much air we have left. This makes us wait outside of water before we head back in to it!
            float moveSpeed = NpcController.Npc.movementSpeed > 0f ? NpcController.Npc.movementSpeed : 4.5f;
            moveSpeed /= NpcController.Npc.carryWeight;
            // FIXME: Rethink this, each water body has its own speed multiplier, so we should not use the NpcController's hindered multiplier here!
            //moveSpeed /= 2f * NpcController.Npc.hinderedMultiplier; // We need to account for the hindered multiplier, since moving in water is slower!
            for (int j = 1; j < ourPath.corners.Length; j++)
            {
                // Check if we were canceled before running danger checks!
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                // We cache the corners we are using for quicker lookups
                // also we always use the default distance function as we may be calculating path distance!
                Vector3 previousNode = ourPath.corners[j - 1];
                Vector3 nodePos = ourPath.corners[j];
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
                        return (false, pathDistance);
                    }
                    continue;
                }

                // Give the main thread a chance to think
                if (j % 10 == 0)
                {
                    await Task.Yield();
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
                        return (true, pathDistance);
                    }
                }

                // Check for if we walk into quicksand or water
                var (isDangerous, updatedDrownTimer) = await CheckQuicksandDanger(previousNode, nodePos, headOffset, tempDistance, moveSpeed, predictedDrownTimer, token);
                predictedDrownTimer = updatedDrownTimer; // Update the global drown timer!
                if (isDangerous)
                {
                    Plugin.LogDebug($"Danger detected due to quicksand or water at segment {j}. Path is dangerous!");
                    return (true, pathDistance);
                }
            }

            this.pathDistance = pathDistance;
            Plugin.LogDebug("Path is safe. No danger detected.");
            return (false, pathDistance); // NOTE: Return the path distance here since it may be modifed by other pathfind calls!
        }

        /// <summary>
        /// Checks if the line segment is exposed to enemies.
        /// Was specificaly made for use in <see cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, CancellationToken)"/>
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
                if (i % 10 == 0)
                {
                    await Task.Yield();
                }

                EnemyAI? enemy = instanceRM.SpawnedEnemies[i];
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} is checking {enemy.enemyType.enemyName} for exposure...");
                if (enemy == null)
                {
                    Plugin.LogDebug($"Enemy At Index {i}: Skipped (null)");
                    continue;
                }

                if (enemy.isEnemyDead || ourWeOutside != enemy.isOutside) 
                {
                    Plugin.LogDebug($"{enemy.enemyType.enemyName}: Skipped (dead or {skipText})");
                    continue; 
                }

                // Check if the target is a threat!
                float? dangerRange = GetFearRangeForEnemies(enemy, EnumFearQueryType.PathfindingAvoid);
                if (!dangerRange.HasValue)
                {
                    Plugin.LogDebug($"{enemy.enemyType.enemyName}: Skipped (not a threat)");
                    continue;
                }

                // Fog reduce the visibility
                if (ourWeOutside && !enemy.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
                {
                    dangerRange = Mathf.Clamp(dangerRange.Value, 0, 30);
                }

                // Do the actual check!
                Vector3 enemyPos = enemy.transform.position;
                Vector3 closestPoint = instanceRM.GetNavMeshPosition(GetClosestPointOnLineSegment(from, to, enemyPos), instanceRM.navHit, 2.7f);
                if ((closestPoint - enemyPos).sqrMagnitude > dangerRange * dangerRange)
                {
                    Plugin.LogDebug($"{enemy.enemyType.enemyName}: Skipped (outside danger range)");
                    continue;
                }

                // Check the closest point
                Vector3 viewPos = useEyePosition && enemy.eye != null ? enemy.eye.position : enemyPos;
                viewPos += Vector3.up * 0.3f;
                if (!Physics.Linecast(closestPoint + Vector3.up * headOffset, viewPos,
                    StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Plugin.LogDebug($"{enemy.enemyType.enemyName}: Segment is exposed from closest {closestPoint} to view position!");
                    return true;
                }

                // We check the midpoint as well since this path may be out in the open,
                // and the midpoint may just be out of range!
                if (!Physics.Linecast(travelMidPoint + Vector3.up * headOffset, viewPos,
                    StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Plugin.LogDebug($"{enemy.enemyType.enemyName}: Segment is exposed from midpoint {travelMidPoint} to view position!");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the line segment is dangerous.
        /// Was specificaly made for use in <see cref="IsPathDangerousAsync(NavMeshPath, bool, bool, bool, CancellationToken)"/>
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

            // Define a distance threshold (buffer) to avoid paths too close to quicksand
            float quicksandBuffer = 2.5f; // Was 1.5f, now testing 2.5f
            //Collider[] hitColliders = new Collider[10];
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} is testing quicksand safety between previous node {from} and current node {to}");
            for (int i = 0; i < QuicksandArray.Length; i++)
            {
                // Check if we were canceled before pathfinding!
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                // Give the main thread a chance to think
                if (i % 5 == 0)
                {
                    await Task.Yield();
                }

                var quicksand = QuicksandArray[i];
                if (!quicksand.isActiveAndEnabled)
                    continue;

                Collider? collider = quicksand.gameObject.GetComponent<Collider>();
                if (collider == null)
                    continue;

                float modifiedMoveSpeed = moveSpeed / (2f * (1f * quicksand.movementHinderance));
                Vector3 closestPoint = RoundManager.Instance.GetNavMeshPosition(GetClosestPointOnLineSegment(from, to, collider.bounds.center), RoundManager.Instance.navHit, 2.7f, agent.areaMask);
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
                    for (int i = 0; i < arraySize; i++)
                    {
                        var hitCollider = hitColliders[i];
                        if (hitCollider == collider)
                        {
                            Plugin.LogDebug("Segment intersects solid quicksand!");
                            return (true, predictedDrownTimer);
                        }
                    }*/

                    // Check if the closest point is within or on the collider
                    Vector3 testPoint = Physics.ClosestPoint(closestPoint, collider, collider.transform.position, collider.transform.rotation);
                    if ((testPoint - closestPoint).sqrMagnitude < quicksandBuffer * quicksandBuffer)
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
                    //if ((testPoint - simulatedHead).sqrMagnitude < quicksandBuffer * quicksandBuffer)
                    if (collider.bounds.Contains(simulatedHead))
                    {
                        // Test the amount of time we would spend underwater to get here
                        Plugin.LogDebug("Simulated head intersects water!");
                        float travelTime = tempDistance / modifiedMoveSpeed;

                        float downingDelta = travelTime / 10f; // Match game logic
                        predictedDrownTimer -= downingDelta;
                        Plugin.LogDebug($"Time left in water: {predictedDrownTimer:F2}");

                        if (predictedDrownTimer <= 0f)
                        {
                            Plugin.LogDebug("Path would drown the bot! Marking path as dangerous!");
                            return (true, predictedDrownTimer);
                        }
                    }
                }
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
        private static Vector3 GetClosestPointOnLineSegment(Vector3 vLineA, Vector3 vLineB, Vector3 point)
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

            if (path1 == null || path1.corners.Length == 0)
            {
                isPathVaild = false;
                return true;
            }

            if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
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
                Vector3 enemyPos = checkLOSToTarget != null ? (useEnemyEyePos && checkLOSToTarget.eye != null ? checkLOSToTarget.eye.position : checkLOSToTarget.transform.position) : Vector3.zero;
                enemyPos += Vector3.up * 0.3f;
                for (int j = 1; j < path1.corners.Length; j++)
                {
                    // We cache the corners we are using for quicker lookups
                    // also we always use the default distance function as we may be calculating path distance!
                    Vector3 previousNode = path1.corners[j - 1];
                    Vector3 currentNode = path1.corners[j];
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
                        if (!Physics.Linecast(closestPoint + Vector3.up * headOffset, enemyPos,
                            StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }

                        // Second, check the middle part of the path
                        Vector3 travelMidPoint = instanceRM.GetNavMeshPosition(Vector3.Lerp(previousNode, currentNode, 0.5f), instanceRM.navHit, 2.7f); // Make sure this is on the NavMesh!
                        if (!Physics.Linecast(travelMidPoint + Vector3.up * headOffset, enemyPos,
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAgent(bool enabled)
        {
            if (agent != null)
            {
                agent.enabled = enabled;
            }
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
                && (Time.timeSinceLevelLoad - updateDestinationTimer) <= 1f)
            {
                return;
            }

            if (agent.isActiveAndEnabled
                && agent.isOnNavMesh
                && !agent.isOnOffMeshLink
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
                updateDestinationTimer = Time.timeSinceLevelLoad;
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

        private int MaxHealthPercent(int percentage)
        {
            return LethalBotManager.Instance.MaxHealthPercent(percentage, MaxHealth);
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
                    || (proximityAwareness != -1 && Vector3.Distance(thisLethalBotCamera.position, posTargetCamera) < (float)proximityAwareness))
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
                    || !lethalBot.isPlayerControlled
                    || !LethalBotManager.Instance.IsPlayerLethalBot(lethalBot))
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
                        || (proximityAwareness != -1 && Vector3.Distance(thisLethalBotCamera.position, posLethalBotCamera) < (float)proximityAwareness))
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

            StartOfRound instanceSOR = StartOfRound.Instance;
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;
            float currentClosestDistance = 1000f;
            int indexPlayer = -1;
            for (int i = 0; i < instanceSOR.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = instanceSOR.allPlayerScripts[i];

                if (!player.isPlayerControlled || player.isPlayerDead || LethalBotManager.Instance.IsPlayerLethalBot(player))
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
                float distanceLethalBotToPlayer = Vector3.Distance(thisLethalBotCamera.position, cameraPlayerPosition);
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
            Transform thisLethalBotCamera = this.NpcController.Npc.gameplayCamera.transform;
            EnemyAI? closestEnemy = null;
            float closestEnemyDistSqr = float.MaxValue;
            foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
            {

                if (spawnedEnemy.isEnemyDead)
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

                // Obstructed
                Vector3 viewPos = spawnedEnemy.eye?.position ?? positionEnemy;
                if (Physics.Linecast(thisLethalBotCamera.position, viewPos, instanceSOR.collidersAndRoomMaskAndDefault))
                {
                    continue;
                }

                // Can they reach us?
                // FIXME: For now we only check the ship itself!
                int pathLayerMask = instanceRM.GetLayermaskForEnemySizeLimit(spawnedEnemy.enemyType);
                if ((NavSizeLimit)pathLayerMask != NavSizeLimit.NoLimit 
                    && !spawnedEnemy.isInsidePlayerShip)
                {
                    if (NpcController.Npc.isInHangarShipRoom)
                    {
                        continue;
                    }
                }

                // Fear range
                float? fearRange = GetFearRangeForEnemies(spawnedEnemy);
                if (!fearRange.HasValue
                    || sqrDistanceToEnemy > fearRange * fearRange)
                {
                    continue;
                }
                // Enemy in distance of fear range

                // Proximity awareness, danger
                if (proximityAwareness > -1
                    && sqrDistanceToEnemy < (float)proximityAwareness * (float)proximityAwareness)
                {
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} DANGER CLOSE \"{spawnedEnemy.enemyType.enemyName}\" {spawnedEnemy.enemyType.name}");
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
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} DANGER LOS \"{spawnedEnemy.enemyType.enemyName}\" {spawnedEnemy.enemyType.name}");
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
            if (!bypassCooldown && (Time.timeSinceLevelLoad - nextExposedToEnemyTimer) < Const.TIMER_CHECK_EXPOSED)
            {
                return _areWeExposed;
            }

            nextExposedToEnemyTimer = Time.timeSinceLevelLoad;

            Vector3 ourPos = NpcController.Npc.transform.position;
            float headOffset = NpcController.Npc.gameplayCamera.transform.position.y - ourPos.y;
            Vector3 headPos = ourPos + Vector3.up * headOffset;
            RoundManager instanceRM = RoundManager.Instance;
            foreach (EnemyAI checkLOSToTarget in instanceRM.SpawnedEnemies)
            {
                if (checkLOSToTarget.isEnemyDead || this.isOutside != checkLOSToTarget.isOutside)
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
                        Vector3 viewPos = checkLOSToTarget.eye?.position ?? enemyPos;
                        if (!Physics.Linecast(viewPos + Vector3.up * 0.25f, headPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            _areWeExposed = true;
                            return true;
                        }
                    }
                }
            }
            _areWeExposed = false;
            return false;
        }

        /// <summary>
        /// Check for, an enemy, the minimal distance from enemy to lethalBot before they will panik.
        /// </summary>
        /// <param name="enemy">Enemy to check</param>
        /// <returns>The minimal distance from enemy to lethalBot before panicking, null if nothing to worry about</returns>
        public float? GetFearRangeForEnemies(EnemyAI enemy, EnumFearQueryType queryType = EnumFearQueryType.BotPanic)
        {
            //Plugin.LogDebug($"enemy \"{enemy.enemyType.enemyName}\" {enemy.enemyType.name}");
            LethalBotFearQuery fearQuery = new LethalBotFearQuery(this, enemy, queryType);
            return LethalBotManager.GetFearRangeForEnemy(fearQuery);
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
            LethalBotFearQuery fearQuery = new LethalBotFearQuery(this, enemy, playerToCheck, EnumFearQueryType.PlayerTeleport);
            return LethalBotManager.GetFearRangeForEnemy(fearQuery);
        }

        /// <summary>
        /// Check for, an enemy, the minimal distance from enemy to lethalBot before based on the given fear query.
        /// </summary>
        /// <param name="fearQuery">Fear query to check</param>
        /// <returns>The minimal distance from enemy based on the given fear query, null if nothing to worry about</returns>
        public float? GetFearRangeForEnemies(LethalBotFearQuery fearQuery)
        {
            //Plugin.LogDebug($"enemy \"{enemy.enemyType.enemyName}\" {enemy.enemyType.name}");
            return LethalBotManager.GetFearRangeForEnemy(fearQuery);
        }

        /// <summary>
        /// Allows me to check if the nutcrackerIsInspecting field is true or not!
        /// </summary>
        /// <remarks>
        /// Not up top like the others since this is only used by <see cref="CanEnemyBeKilled(EnemyAI)"/>
        /// </remarks>
        private static readonly FieldInfo nutcrackerIsInspecting = AccessTools.Field(typeof(NutcrackerEnemyAI), "isInspecting");

        /// <summary>
        /// Returns true if the given EnemyAI can be killed!
        /// </summary>
        /// <remarks>
        /// <para>NOTE: This is a switch statement and doesn't work with custom enemies!</para>
        /// TODO: Move this into a Query system like the fear ranges!
        /// </remarks>
        /// <param name="enemy"></param>
        /// <returns>Can the enemy be killed?</returns>
        public bool CanEnemyBeKilled(EnemyAI enemy)
        {
            // FIXME: Only a few enemies can be targeted since
            // I need to check when its a good idea to fight!
            bool hasRangedWeapon = HasRangedWeapon();
            switch (enemy.enemyType.enemyName)
            {
                case "Centipede":
                case "Masked":
                case "Crawler":
                case "Hoarding bug":
                    return true;
                case "Nutcracker":
                    if (hasRangedWeapon 
                        && (enemy.currentBehaviourStateIndex == 2 
                            || (enemy is NutcrackerEnemyAI nutcracker 
                                && (bool)nutcrackerIsInspecting.GetValue(nutcracker))))
                    {
                        return true;
                    }
                    return false;
                case "Flowerman":
                case "Bunker Spider":
                case "Baboon hawk":
                    return hasRangedWeapon;
                case "Butler": // For now, don't kill them!
                case "ForestGiant":
                case "MouthDog":
                case "Maneater":
                    return false; // Don't even bother its suicide!
                default:
                    // Either they are not killable or not dangerous
                    // return enemy.enemyType.canDie;
                    return false; // NOTE: enemy.enemyType.canDie can be true on enemies that can't be killed by our weapons!
            }
            /*switch(enemy.enemyType.enemyName)
            {
                case "Masked":
                case "Maneater":
                case "Baboon hawk":
                case "Hoarding bug":
                case "Butler":
                case "Centipede":
                case "Flowerman":
                case "Bush Wolf":
                case "Crawler":
                case "Bunker Spider":
                case "ForestGiant":
                case "MouthDog":
                    return true;

                // The Nutcraker is a special case were only certain times are they killable!
                case "Nutcracker":
                    if ((bool)AccessTools.Field(typeof(NutcrackerEnemyAI), "isInspecting").GetValue(enemy) ||
                        (bool)AccessTools.Field(typeof(NutcrackerEnemyAI), "aimingGun").GetValue(enemy) ||
                        (bool)AccessTools.Field(typeof(NutcrackerEnemyAI), "reloadingGun").GetValue(enemy))
                    {
                        return true;
                    }
                    return false;

                case "Clay Surgeon":
                case "Earth Leviathan":
                case "RadMech":
                case "Jester":
                case "Spring":
                case "Butler Bees":
                case "Red Locust Bees":
                case "Blob":
                case "ImmortalSnail":
                    return false;

                // Set to false since this guy isn't really a threat!
                case "Puffer":
                    return false;

                default:
                    // Not killable or dangerous enemies

                    // "Docile Locust Bees"
                    // "Manticoil"
                    // "Girl"
                    // "Tulip Snake"
                    return false;
            }*/
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
            if (!bypassCooldown && (Time.timeSinceLevelLoad - nextEyelessdogCheckTimer) < Const.TIMER_CHECK_EXPOSED)
            {
                return _isEyelessDogInPromimity;
            }

            nextEyelessdogCheckTimer = Time.timeSinceLevelLoad;

            RoundManager instanceRM = RoundManager.Instance;
            Vector3 ourPos = NpcController.Npc.transform.position;
            foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
            {
                if (!spawnedEnemy.isEnemyDead && (spawnedEnemy is MouthDogAI || spawnedEnemy.enemyType.enemyName == "MouthDog"))
                {
                    // NOTE: We don't use GetFearRangeForEnemies since
                    // we don't want to trigger the dog in the first place
                    float fearRange = 30f; // NOTE: 22f is the footstep range when running!
                    if ((spawnedEnemy.transform.position - ourPos).sqrMagnitude < fearRange * fearRange)
                    {
                        _isEyelessDogInPromimity = true;
                        return true;
                    }
                }
            }
            _isEyelessDogInPromimity = false;
            return false;
        }

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

        private static ShipTeleporter? FindInverseTeleporter()
        {
            ShipTeleporter[] shipTeleporters = Object.FindObjectsOfType<ShipTeleporter>(includeInactive: false);
            foreach (var teleporter in shipTeleporters)
            {
                if (teleporter == null)
                {
                    continue;
                }

                if (teleporter.isInverseTeleporter)
                {
                    return teleporter;
                }
            }
            return null;
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
            if (!agent.isOnOffMeshLink)
            {
                return null;
            }

            Vector3 ourPos = NpcController.Npc.transform.position;
            OffMeshLinkData offMeshLinkData = agent.currentOffMeshLinkData;
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

            foreach (InteractTrigger ladder in laddersInteractTrigger)
            {
                Vector3 ladderBottomPos = ladder.bottomOfLadderPosition.position;
                Vector3 ladderTopPos = ladder.topOfLadderPosition.position;

                float ladderDistSqr = Const.DISTANCE_NPCBODY_FROM_LADDER * Const.DISTANCE_NPCBODY_FROM_LADDER;
                if ((ladderBottomPos - closestLinkPos).sqrMagnitude < ladderDistSqr)
                {
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} Path wants to climb UP ladder");
                    NpcController.OrderToGoUpDownLadder(hasToGoDown: false);
                    return ladder;
                }
                else if ((ladderTopPos - closestLinkPos).sqrMagnitude < ladderDistSqr)
                {
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} Path wants to climb DOWN ladder");
                    NpcController.OrderToGoUpDownLadder(hasToGoDown: true);
                    return ladder;
                }
            }
            return null;
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
            foreach (var entrance in EntrancesTeleportArray)
            {
                if ((entityPos1 - entrance.entrancePoint.position).sqrMagnitude < Const.DISTANCE_TO_ENTRANCE * Const.DISTANCE_TO_ENTRANCE
                    && (entityPos2 - entrance.entrancePoint.position).sqrMagnitude < Const.DISTANCE_TO_ENTRANCE * Const.DISTANCE_TO_ENTRANCE)
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

            /*for (int i = 0; i < entrancesTeleportArray.Length; i++)
            {
                EntranceTeleport entrance = entrancesTeleportArray[i];
                if (entrance.entranceId == entranceToUse.entranceId
                    && entrance.isEntranceToBuilding != entranceToUse.isEntranceToBuilding)
                {
                    return entrance.entrancePoint.position;
                }
            }*/
            return entranceToUse.exitPoint.position;
        }

        /// <summary>
        /// Check all doors to know if the lethalBot is close enough to it to open it if necessary.
        /// </summary>
        /// <returns></returns>
        public DoorLock? GetDoorIfWantsToOpen()
        {
            Vector3 npcBodyPos = NpcController.Npc.thisController.transform.position;
            foreach (var door in doorLocksArray.Where(x => !x.isLocked))
            {
                if ((door.transform.position - npcBodyPos).sqrMagnitude < Const.DISTANCE_NPCBODY_FROM_DOOR * Const.DISTANCE_NPCBODY_FROM_DOOR)
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
            if (timerCheckDoor > Const.TIMER_CHECK_DOOR)
            {
                timerCheckDoor = 0f;

                DoorLock? door = GetDoorIfWantsToOpen();
                if (door != null)
                {
                    // Prevent stuck behind open door
                    Physics.IgnoreCollision(this.NpcController.Npc.playerCollider, door.GetComponent<Collider>());

                    // Open door
                    door.OpenOrCloseDoor(NpcController.Npc);
                    door.OpenDoorAsEnemyServerRpc();
                    return true;
                }
            }
            timerCheckDoor += AIIntervalTime;
            return false;
        }

        /// <summary>
        /// Check the doors after some interval of ms to see if lethalBot can open one to unstuck himself.
        /// </summary>
        /// <returns>the locked door has been found by lethalBot. Else null</returns>
        public DoorLock? UnlockDoorIfNeeded(float lockedDoorRange = Const.DISTANCE_NPCBODY_FROM_DOOR, bool checkLineOfSight = false, float proximityRange = -1f, bool bypassCooldown = false)
        {
            if (bypassCooldown || (Time.timeSinceLevelLoad - timerCheckLockedDoor) > Const.TIMER_CHECK_DOOR)
            {
                timerCheckLockedDoor = Time.timeSinceLevelLoad;

                if (!HasKeyInInventory())
                {
                    return null;
                }

                Vector3 npcBodyPos = NpcController.Npc.thisController.transform.position;
                foreach (var lockedDoor in doorLocksArray)
                {
                    if (lockedDoor.isLocked && !lockedDoor.isPickingLock)
                    {
                        float distSqrFromDoor = (lockedDoor.transform.position - npcBodyPos).sqrMagnitude;
                        if (distSqrFromDoor < lockedDoorRange * lockedDoorRange)
                        {
                            // If we are nearby the door, we don't need to be able to see it!
                            if (proximityRange < 0 || distSqrFromDoor > proximityRange)
                            {
                                if (checkLineOfSight
                                && Physics.Linecast(eye.position, lockedDoor.transform.position + Vector3.up * 0.2f, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                                {
                                    // If the hit object is not the door, it's blocked
                                    if (hitInfo.collider.gameObject.GetComponentInParent<DoorLock>() != lockedDoor)
                                        continue;
                                }
                            }

                            // Get potential door positions
                            Vector3? doorPos1 = GetOffsetLockPickerPosition(lockedDoor);
                            Vector3? doorPos2 = GetOffsetLockPickerPosition(lockedDoor, true);

                            // Check path validity and distance for both positions
                            float? doorDistance1 = null;
                            float? doorDistance2 = null;

                            Plugin.LogDebug("[UnlockDoorIfNeeded] Checking path to front of door!");
                            if (doorPos1.HasValue && IsValidPathToTarget(doorPos1.Value, true))
                            {
                                Plugin.LogDebug("[UnlockDoorIfNeeded] Successfuly found path to front of door!");
                                doorDistance1 = pathDistance;
                            }

                            Plugin.LogDebug("[UnlockDoorIfNeeded] Checking path to back of door!");
                            if (doorPos2.HasValue && IsValidPathToTarget(doorPos2.Value, true))
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
        /// <returns>true: the lethalBot is using or is waiting to use the elevator, else false</returns>
        public bool UseElevator(bool goUp)
        {
            if (ElevatorScript == null || this.isOutside)
            {
                return false;
            }
            Vector3 vector = ((!goUp) ? ElevatorScript.elevatorTopPoint.position : ElevatorScript.elevatorBottomPoint.position);
            float distanceFromInsidePosition = Vector3.Distance(NpcController.Npc.transform.position, ElevatorScript.elevatorInsidePoint.position);
            if (ElevatorScript.elevatorFinishedMoving 
                && (distanceFromInsidePosition <= 1f || IsValidPathToTarget(ElevatorScript.elevatorInsidePoint.position, false)))
            {
                if (ElevatorScript.elevatorDoorOpen 
                    && distanceFromInsidePosition <= 1f 
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
                if (distanceFromInsidePosition > 1f)
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
            if (distanceFromInsidePosition > 1f && IsValidPathToTarget(vector, false))
            {
                float distanceFromVector = Vector3.Distance(NpcController.Npc.transform.position, vector);
                if (ElevatorScript.elevatorDoorOpen 
                    && distanceFromVector <= 1f 
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
                if (distanceFromVector > 1f)
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

            if (Vector3.Distance(player.transform.position, ElevatorScript.elevatorBottomPoint.position) < 10f)
            {
                return false;
            }
            else if (Vector3.Distance(player.transform.position, ElevatorScript.elevatorTopPoint.position) < 20f)
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
                if (Vector3.Distance(position, ElevatorScript.elevatorBottomPoint.position) < 10f)
                {
                    return false;
                }
                else if (Vector3.Distance(position, ElevatorScript.elevatorTopPoint.position) < 20f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check ladders if lethalBot needs to use one to follow player.
        /// </summary>
        /// <remarks>
        /// FIXME: This causes many issues that I can't seem to identify the cause yet!
        /// </remarks>
        /// <returns>true: the lethalBot is using or is waiting to use the ladder, else false</returns>
        private bool UseLadderIfNeeded()
        {
            if (NpcController.Npc.isClimbingLadder)
            {
                return true;
            }

            InteractTrigger? ladder = GetLadderIfWantsToUseLadder();
            if (ladder == null)
            {
                // If this is a gap, do the default logic instead!
                if (agent.isOnOffMeshLink && waitUntilEndOfOffMeshLinkCoroutine == null)
                {
                    waitUntilEndOfOffMeshLinkCoroutine = StartCoroutine(autoTraverseOffMeshLink());
                }
                return false;
            }

            // Lethal Bot wants to use ladder
            if (Plugin.Config.TeleportWhenUsingLadders.Value)
            {
                NpcController.Npc.transform.position = this.transform.position;
                agent.CompleteOffMeshLink();
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

        private IEnumerator autoTraverseOffMeshLink()
        {
            agent.autoTraverseOffMeshLink = true;
            yield return null;
            yield return new WaitUntil(() => !agent.isOnOffMeshLink);
            agent.autoTraverseOffMeshLink = false;
            waitUntilEndOfOffMeshLinkCoroutine = null;
        }

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
            if (!HasAmmoForWeapon(HeldItem))
            {
                return false;
            }
            return IsItemWeapon(HeldItem);
        }

        /// <summary>
        /// Is the lethalBot holding a ranged weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(true, nameof(HeldItem))]
        public bool IsHoldingRangedWeapon()
        {
            return IsItemRangedWeapon(HeldItem);
        }

        /// <summary>
        /// Is the lethalBot holding a key ?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MemberNotNullWhen(true, nameof(HeldItem))]
        public bool IsHoldingKey()
        {
            if (AreHandsFree())
            {
                return false;
            }
            return HeldItem is KeyItem || HeldItem is LockPicker;
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
            foreach (var weapon in NpcController.Npc.ItemSlots)
            {
                if (IsItemWeapon(weapon))
                {
                    // Do we need ammo in order to use this weapon?
                    if (!HasAmmoForWeapon(weapon))
                    {
                        continue;
                    }
                    return true;
                }
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
            foreach (var weapon in NpcController.Npc.ItemSlots)
            {
                if (IsItemRangedWeapon(weapon))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have a key ?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasKeyInInventory()
        {
            if (IsHoldingKey())
            {
                return true;
            }
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                if (item != null && (item is KeyItem || item is LockPicker))
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
        /// I will note that it only works on items derived off of the ShotgunItem class!
        /// </remarks>
        /// <returns>I mean come on</returns>
        public bool IsItemRangedWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            if (!IsItemWeapon(weapon))
            {
                return false;
            }
            return weapon is ShotgunItem;
        }

        /// <summary>
        /// Is the given item a weapon ?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool IsItemWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            if (weapon == null)
            {
                return false;
            }
            // HACKHACK: weapon.itemProperties.isDefensiveWeapon is set to false on the shovel and shotgun!?
            return weapon is Shovel || weapon is KnifeItem || weapon is ShotgunItem;
        }

        /// <summary>
        /// Checks if the current weapon has ammo.
        /// </summary>
        /// <param name="weapon"></param>
        /// <returns></returns>
        public bool HasAmmoForWeapon(GrabbableObject? weapon, bool spareOnly = false)
        {
            if (!IsItemWeapon(weapon))
            {
                return false;
            }

            // This weapon uses batteries!
            if (weapon.itemProperties.requiresBattery
                && (weapon.insertedBattery == null || weapon.insertedBattery.empty))
            {
                return false;
            }

            // We can only check for shotguns.....for now
            if (weapon is ShotgunItem shotgun)
            {
                // Ammo is in the gun!
                if (!spareOnly && shotgun.shellsLoaded > 0)
                {
                    return true;
                }

                // Using foreach with manual index tracking
                int index = 0;
                foreach (var item in NpcController.Npc.ItemSlots)
                {
                    if (item != null)
                    {
                        GunAmmo? gunAmmo = item as GunAmmo;
                        Plugin.LogDebug($"Ammo null in slot #{index}?: {gunAmmo == null}");
                        if (gunAmmo != null)
                        {
                            Plugin.LogDebug($"Ammo in slot #{index} id: {gunAmmo.ammoType}");
                        }
                        if (gunAmmo != null && gunAmmo.ammoType == shotgun.gunCompatibleAmmoID)
                        {
                            return true;
                        }
                    }
                    index++;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Check if the lethalBot has the given object in its inventory.
        /// </summary>
        /// <param name="grabbableObject">The object to check if the bot has in its inventory</param>
        /// <param name="objectSlot">The slot of where the object was found at! Is set to -1 if item was not found!</param>
        /// <returns>true: the bot has the object in its inventory, false: the bot doesn't have the given object in its inventory</returns>
        public bool HasGrabbableObjectInInventory(GrabbableObject? grabbableObject, out int objectSlot)
        {
            objectSlot = -1;
            if (grabbableObject == null)
            {
                return false;
            }

            // Check if the lethalBot is holding the object
            if (HeldItem == grabbableObject)
            {
                objectSlot = NpcController.Npc.currentItemSlot;
                return true;
            }

            // Check if the lethalBot has the object in its inventory
            int index = 0;
            foreach(var item in NpcController.Npc.ItemSlots)
            {
                if (item == grabbableObject)
                {
                    objectSlot = index;
                    return true;
                }
                index++;
            }
            return false;
        }

        /// <summary>
        /// Check if the lethalBot has the given object of the entered type in its inventory.
        /// </summary>
        /// <remarks>
        /// WARNING: Its not recommened to use this function, you are better off using <see cref="HasGrabbableObjectInInventory(GrabbableObject?, out int)"/>
        /// </remarks>
        /// <param name="typeOfObject">The type of the object in the inventory!</param>
        /// <param name="objectSlot">The slot of where the object was found at! Is set to -1 if item was not found!</param>
        /// <returns>true: the bot has the object in its inventory, false: the bot doesn't have the given object in its inventory</returns>
        public bool HasGrabbableObjectInInventory(Type typeOfObject, out int objectSlot)
        {
            // Check if the lethalBot is holding the object
            objectSlot = -1;
            if (typeOfObject.IsInstanceOfType(HeldItem))
            {
                objectSlot = NpcController.Npc.currentItemSlot;
                return true;
            }

            // Check if the lethalBot has the object in its inventory
            int index = 0;
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                if (typeOfObject.IsInstanceOfType(item))
                {
                    objectSlot = index;
                    return true;
                }
                index++;
            }
            return false;
        }

        /// <summary>
        /// Does the lethalBot have room for another item?
        /// </summary>
        /// <returns>I mean come on</returns>
        public bool HasSpaceInInventory()
        {
            if (AreHandsFree())
            {
                return true;
            }
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                if (item == null)
                {
                    return true;
                }
            }

            return false;
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
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                if (item != null)
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
            if (!AreHandsFree() && HeldItem.itemProperties.isScrap)
            {
                return true;
            }
            foreach (var scrap in NpcController.Npc.ItemSlots)
            {
                if (scrap != null 
                    && scrap.itemProperties.isScrap)
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
            if (!AreHandsFree() && IsGrabbableObjectSellable(HeldItem, true, true))
            {
                return true;
            }
            foreach (var item in NpcController.Npc.ItemSlots)
            {
                if (item != null
                    && IsGrabbableObjectSellable(item, true, true))
                {
                    return true;
                }
            }
            return false;
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
            if (AreHandsFree())
            {
                return false;
            }
            if (NpcController.Npc.isPlayerDead)
            {
                return false;
            }
            if (!HeldItem.itemProperties.usableInSpecialAnimations 
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
        }

        /// <summary>
        /// Change the ownership of the lethalBot inventory to the player that owns the bot.
        /// </summary>
        /// <remarks>
        /// This is called on an interval inside of <see cref="StartOfRound.LateUpdate"/> to ensure items are owned by the correct player.
        /// </remarks>
        public void UpdateOwnershipOfBotInventoryServer()
        {
            // NetworkObject ownership is only updated on the server
            if (!IsServer && !IsHost)
            {
                ChangeOwnershipOfBotInventoryServerRpc(this.OwnerClientId);
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
        }

        /// <summary>
        /// Check all object array <c>LethalBotManager.grabbableObjectsInMap</c>, 
        /// if lethalBot is close and can see an item to grab.
        /// </summary>
        /// <returns><c>GrabbableObject</c> if lethalBot sees an item he can grab, else null.</returns>
        public GrabbableObject? LookingForObjectToGrab()
        {
            GrabbableObject? closestObject = null;
            float closestObjectDistSqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                // Object not outside when ai inside and vice versa
                // NEEDTOVALIDATE: Should I use grabbableObject.isInFactory to check this?
                Vector3 gameObjectPosition = gameObject.transform.position;
                if (isOutside && gameObjectPosition.y < -100f)
                {
                    continue;
                }
                else if (!isOutside && gameObjectPosition.y > -80f)
                {
                    continue;
                }

                // Object in range ?
                // Check if object is further away from the closest object
                // FIXME: This should be PATH distance not elucian!
                float sqrDistanceEyeGameObject = (gameObjectPosition - this.eye.position).sqrMagnitude;
                if (sqrDistanceEyeGameObject > Const.LETHAL_BOT_OBJECT_RANGE * Const.LETHAL_BOT_OBJECT_RANGE 
                    || sqrDistanceEyeGameObject > closestObjectDistSqr)
                {
                    continue;
                }

                // Get grabbable object infos
                GrabbableObject? grabbableObject = gameObject.GetComponent<GrabbableObject>();
                if (grabbableObject == null)
                {
                    continue;
                }

                // Black listed ? 
                if (IsGrabbableObjectBlackListed(grabbableObject))
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

                // Object in cruiser vehicle
                // TODO: Let the bot move items between the ship and the cruiser
                if (grabbableObject.transform.parent != null
                    && grabbableObject.transform.parent.name.StartsWith("CompanyCruiser"))
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
                if (sqrDistanceEyeGameObject < Const.LETHAL_BOT_OBJECT_AWARNESS * Const.LETHAL_BOT_OBJECT_AWARNESS)
                {
                    Plugin.LogDebug($"awareness {grabbableObject.name}");
                }
                // Object visible ?
                else if (!Physics.Linecast(eye.position, grabbableObject.GetItemFloorPosition(default(Vector3)) + Vector3.up * grabbableObject.itemProperties.verticalOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    Vector3 to = gameObjectPosition - eye.position;
                    if (Vector3.Angle(eye.forward, to) < Const.LETHAL_BOT_FOV)
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
                closestObjectDistSqr = sqrDistanceEyeGameObject;
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
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                // Object not outside when ai inside and vice versa
                Vector3 gameObjectPosition = gameObject.transform.position;
                if (isOutside && gameObjectPosition.y < -100f)
                {
                    continue;
                }
                else if (!isOutside && gameObjectPosition.y > -80f)
                {
                    continue;
                }

                // Get grabbable object infos
                GrabbableObject? grabbableObject = gameObject.GetComponent<GrabbableObject>();
                if (grabbableObject == null)
                {
                    continue;
                }

                // Black listed ? 
                if (IsGrabbableObjectBlackListed(grabbableObject, true))
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
                float gameObjectDistSqr = (gameObjectPosition - ourPos).sqrMagnitude;
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
        /// Check all conditions for deciding if an item is grabbable or not.
        /// </summary>
        /// <param name="grabbableObject">Item to check</param>
        /// <returns></returns>
        public bool IsGrabbableObjectGrabbable(GrabbableObject grabbableObject)
        {
            if (grabbableObject == null
                || !grabbableObject.gameObject.activeSelf)
            {
                return false;
            }

            if (grabbableObject.isHeld
                || !grabbableObject.grabbable
                || grabbableObject.deactivated)
            {
                return false;
            }

            // Don't steal from loot bugs, unless the object was already stolen
            // FIXME: If the loot bugs are dead, the bots will still ignore their loot!
            foreach (HoarderBugItem item in HoarderBugAI.HoarderBugItems)
            {
                if (item != null
                    && grabbableObject.isInFactory
                    && item.itemGrabbableObject == grabbableObject
                    && item.status != HoarderBugItemStatus.Any
                    && item.status != HoarderBugItemStatus.Stolen)
                {
                    // Lets not anger them now.....
                    return false;
                }
            }
            
            RagdollGrabbableObject? ragdollGrabbableObject = grabbableObject as RagdollGrabbableObject;
            if (ragdollGrabbableObject != null)
            {
                if (ragdollGrabbableObject.ragdoll.attachedTo != null)
                {
                    SpikeRoofTrap? attachedToTrap = ragdollGrabbableObject.ragdoll.attachedTo.gameObject.GetComponent<SpikeRoofTrap>();
                    if (attachedToTrap != null)
                    {
                        // Don't try to grab bodies stuck in traps
                        return false;
                    }
                }
            }

            // Item just dropped, should wait a bit before grab it again
            if (DictJustDroppedItems.TryGetValue(grabbableObject, out float justDroppedItemTime))
            {
                if (Time.realtimeSinceStartup - justDroppedItemTime < Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS)
                {
                    return false;
                }
            }

            // Are we holding a two handed item and is the item we are grabbing two handed
            if (!AreHandsFree() && HeldItem.itemProperties.twoHanded)
            {
                // If the item requires one hand then we can set down our large item and pick up the small one!
                if (grabbableObject.itemProperties.twoHanded 
                    && (grabbableObject is not CaveDwellerPhysicsProp caveDwellerGrabbableObject 
                        || !caveDwellerGrabbableObject.caveDwellerScript.babyCrying))
                {
                    return false;
                }
            }

            // Is item too close to entrance (with config option enabled)
            // NEEDTOVALIDATE: Should this only happen if the player they are following sets down their loot or something?
            if (!Plugin.Config.GrabItemsNearEntrances.Value 
                && !LethalBotManager.Instance.AreAllHumanPlayersDead())
            {
                for (int j = 0; j < EntrancesTeleportArray.Length; j++)
                {
                    if ((grabbableObject.transform.position - EntrancesTeleportArray[j].entrancePoint.position).sqrMagnitude < Const.DISTANCE_ITEMS_TO_ENTRANCE * Const.DISTANCE_ITEMS_TO_ENTRANCE)
                    {
                        return false;
                    }
                }
            }

            // Trim dictionnary if too large
            TrimDictJustDroppedItems();

            // Is the item reachable with the agent pathfind ? (only owner knows and calculate) real position of ai lethalBot)
            Vector3 objectPos = RoundManager.Instance.GetNavMeshPosition(grabbableObject.GetItemFloorPosition(default(Vector3)), default, NpcController.Npc.grabDistance, NavMesh.AllAreas);
            if (IsOwner
                && !this.IsValidPathToTarget(objectPos, false, maxRangeToEnd: NpcController.Npc.grabDistance))
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
        /// <returns></returns>
        public bool IsGrabbableObjectSellable(GrabbableObject grabbableObject, bool ignoreHeldFlag = false, bool skipPathCheck = false)
        {
            if (grabbableObject == null
                || !grabbableObject.gameObject.activeSelf)
            {
                return false;
            }

            if ((!ignoreHeldFlag && grabbableObject.isHeld)
                || !grabbableObject.grabbable
                || grabbableObject.deactivated 
                || !grabbableObject.itemProperties.isScrap)
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

            // Actually allow selling of bodies as they could be the diffrence between meeting quota or not
            // TODO: Add a desperation mechanic so the bot will sell bodies if they won't meet quota
            /*RagdollGrabbableObject? ragdollGrabbableObject = grabbableObject as RagdollGrabbableObject;
            if (ragdollGrabbableObject != null)
            {
                if (!ragdollGrabbableObject.grabbableToEnemies)
                {
                    return false;
                }
            }*/

            // Ignore drop cooldowns when selling!
            // Item just dropped, should wait a bit before grab it again
            /*if (DictJustDroppedItems.TryGetValue(grabbableObject, out float justDroppedItemTime))
            {
                if (Time.realtimeSinceStartup - justDroppedItemTime < Const.WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS)
                {
                    return false;
                }
            }*/

            // Are we holding a two handed item and is the item we are grabbing two handed
            if (!ignoreHeldFlag)
            {
                if (!AreHandsFree() && HeldItem.itemProperties.twoHanded)
                {
                    // If the item requires one hand then we can set down our large item and pick up the small one!
                    if (grabbableObject.itemProperties.twoHanded)
                    {
                        return false;
                    }
                }
            }

            // Trim dictionnary if too large
            TrimDictJustDroppedItems();

            // Is the item reachable with the agent pathfind ? (only owner knows and calculate) real position of ai lethalBot)
            Vector3 objectPos = RoundManager.Instance.GetNavMeshPosition(grabbableObject.GetItemFloorPosition(default(Vector3)), default, NpcController.Npc.grabDistance, NavMesh.AllAreas);
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
        private static void TrimDictJustDroppedItems()
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
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (this.NpcController == null)
            {
                return;
            }

            PlayerControllerB lethalBotController = this.NpcController.Npc;
            if (base.IsOwner && lethalBotController.isPlayerControlled)
            {
                bool wasInHangarShipRoom = lethalBotController.isInHangarShipRoom;
                if (!lethalBotController.isInElevator
                    && instanceSOR.shipBounds.bounds.Contains(lethalBotController.transform.position))
                {
                    lethalBotController.isInElevator = true;
                }

                if (lethalBotController.isInElevator
                    && !wasInHangarShipRoom
                    && instanceSOR.shipStrictInnerRoomBounds.bounds.Contains(lethalBotController.transform.position))//&& instanceSOR.shipInnerRoomBounds.bounds.Contains(lethalBotController.transform.position)
                {
                    lethalBotController.isInHangarShipRoom = true;
                    this.isInsidePlayerShip = true;
                }
                else if (lethalBotController.isInElevator
                    && !instanceSOR.shipBounds.bounds.Contains(lethalBotController.transform.position))
                {
                    lethalBotController.isInElevator = false;
                    lethalBotController.isInHangarShipRoom = false;
                    this.isInsidePlayerShip = false;
                    wasInHangarShipRoom = false;

                    if (!this.AreHandsFree())
                    {
                        lethalBotController.SetItemInElevator(droppedInShipRoom: false, droppedInElevator: false, HeldItem);
                    }

                    /*if (this.HasSomethingInInventory())
                    {
                        for (int i = 0; i < this.NpcController.Npc.ItemSlots.Length; i++)
                        {
                            if (this.NpcController.Npc.ItemSlots[i] != null
                                && this.NpcController.Npc.ItemSlots[i].isHeld)
                            {
                                this.NpcController.Npc.SetItemInElevator(droppedInShipRoom: false, droppedInElevator: false, this.NpcController.Npc.ItemSlots[i]);
                            }
                        }
                    }*/
                }

                if (wasInHangarShipRoom != lethalBotController.isInHangarShipRoom
                    && !lethalBotController.isInHangarShipRoom
                    && !this.AreHandsFree())
                {
                    lethalBotController.SetItemInElevator(droppedInShipRoom: false, droppedInElevator: true, HeldItem);
                }

                if (lethalBotController.overridePhysicsParent != null)
                {
                    if (lethalBotController.overridePhysicsParent != lethalBotController.lastSyncedPhysicsParent)
                    {
                        lethalBotController.parentedToElevatorLastFrame = false;
                        lethalBotController.lastSyncedPhysicsParent = lethalBotController.overridePhysicsParent;
                        this.ReParentLethalBot(lethalBotController.overridePhysicsParent);
                        PlayerControllerBPatch.UpdatePlayerPhysicsParentServerRpc_ReversePatch(lethalBotController, lethalBotController.thisPlayerBody.localPosition, lethalBotController.overridePhysicsParent.GetComponent<NetworkObject>(), isOverride: true, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
                    }
                }
                else if (lethalBotController.physicsParent != null)
                {
                    if (lethalBotController.physicsParent != lethalBotController.lastSyncedPhysicsParent)
                    {
                        lethalBotController.parentedToElevatorLastFrame = false;
                        lethalBotController.lastSyncedPhysicsParent = lethalBotController.physicsParent;
                        this.ReParentLethalBot(lethalBotController.physicsParent);
                        PlayerControllerBPatch.UpdatePlayerPhysicsParentServerRpc_ReversePatch(lethalBotController, lethalBotController.thisPlayerBody.localPosition, lethalBotController.physicsParent.GetComponent<NetworkObject>(), isOverride: false, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
                    }
                }
                else
                {
                    if (lethalBotController.lastSyncedPhysicsParent != null)
                    {
                        lethalBotController.lastSyncedPhysicsParent = null;
                        this.ReParentLethalBot(lethalBotController.playersManager.playersContainer);
                        PlayerControllerBPatch.RemovePlayerPhysicsParentServerRpc_ReversePatch(lethalBotController, lethalBotController.thisPlayerBody.localPosition, removeOverride: false, removeBoth: true, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom);
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
        /// <param name="isSelling">Is the bot going to sell this object</param>
        /// <returns>true: this object is blacklisted. false: we are allowed to pick up this object</returns>
        private bool IsGrabbableObjectBlackListed(GrabbableObject grabbableObjectToEvaluate, bool isSelling = false)
        {
            // Bee nest
            GameObject gameObject = grabbableObjectToEvaluate.gameObject;
            if (!Plugin.Config.GrabBeesNest.Value 
                && !isSelling
                && gameObject.name.Contains("RedLocustHive"))
            {
                return true;
            }

            // Dead bodies
            // TODO: Probably should add a desperation mechanic where they only sell if we won't make the quota
            if (!Plugin.Config.GrabDeadBodies.Value
                && !isSelling
                && gameObject.name.Contains("RagdollGrabbableObject")
                && gameObject.tag == "PhysicsProp"
                && gameObject.GetComponentInParent<DeadBodyInfo>() != null)
            {
                return true;
            }

            // Maneater
            if (grabbableObjectToEvaluate is CaveDwellerPhysicsProp caveDwellerGrabbableObject) // Was gameObject.name.Contains("CaveDwellerEnemy"), but CaveDwellerPhysicsProp is better and more reliable
            {
                // The host has config options to allow or disallow the bot to grab the maneater baby
                if (!Plugin.Config.GrabManeaterBaby.Value)
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
                BabyPlayerMemory? playerMemory = CaveDwellerAIPatch.GetBabyMemoryOfPlayer_ReversePatch(caveDwellerAI, NpcController.Npc);
                if ((playerMemory == null 
                    || playerMemory.likeMeter < 0.1f) 
                    && !caveDwellerAI.babyCrying)
                {
                    Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} will not pickup the maneater, maneater is not crying and doesn't like us!");
                    return true;
                }
            }

            // Wheelbarrow
            if ((!Plugin.Config.GrabWheelbarrow.Value || isSelling)
                && gameObject.name.Contains("Wheelbarrow"))
            {
                return true;
            }

            // ShoppingCart
            if ((!Plugin.Config.GrabShoppingCart.Value || isSelling)
                && gameObject.name.Contains("ShoppingCart"))
            {
                return true;
            }

            // ZedDogs!
            if (isSelling && gameObject.name.Contains("ZeddogPlushie"))
            {
                return true;
            }

            // Lockpickers in use!
            if (grabbableObjectToEvaluate is LockPicker lockPicker && lockPicker.isPickingLock)
            {
                return true;
            }

            // Don't pickup used flashbangs!
            if (grabbableObjectToEvaluate is StunGrenadeItem flashbang && ((flashbang.pinPulled && !flashbang.explodeOnCollision ) || flashbang.hasExploded))
            {
                return true;
            }

            return false;
        }

        private bool IsGrabbableObjectInContainerMod(GrabbableObject grabbableObject)
        {
            return CustomItemBehaviourLibrary.AbstractItems.ContainerBehaviour.CheckIfItemInContainer(grabbableObject);
        }

        private bool IsGrabbableObjectHeldByPikminMod(GrabbableObject grabbableObject)
        {
            List<LethalMin.PikminItem> listPickMinItems = LethalMin.PikminManager.GetPikminItemsInMap();
            if (listPickMinItems == null
                || listPickMinItems.Count == 0)
            {
                return false;
            }

            foreach (var item in listPickMinItems)
            {
                if(item != null
                    && item.Root == grabbableObject
                    && item.PikminOnItem > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void InitImportantColliders()
        {
            if (dictComponentByCollider == null)
            {
                dictComponentByCollider = new Dictionary<string, Component>();
            }
            else
            {
                dictComponentByCollider.Clear();
            }

            BridgeTrigger[] bridgeTriggers = Object.FindObjectsOfType<BridgeTrigger>(includeInactive: false);
            foreach (var bridge in bridgeTriggers)
            {
                Component[] bridgePhysicsPartsContainerComponents = bridge.bridgePhysicsPartsContainer.gameObject.GetComponentsInChildren<Transform>();
                foreach (var component in bridgePhysicsPartsContainerComponents)
                {
                    if (component.name == "Mesh")
                    {
                        continue;
                    }

                    if (!dictComponentByCollider.ContainsKey(component.name))
                    {
                        dictComponentByCollider.Add(component.name, bridge);
                    }
                }
            }

            //foreach (var a in dictComponentByCollider)
            //{
            //    Plugin.LogDebug($"dictComponentByCollider {a.Key} {a.Value}");
            //    ComponentUtil.ListAllComponents(((BridgeTrigger)a.Value).bridgePhysicsPartsContainer.gameObject);
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
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayAudioServerRpc(string smallPathAudioClip, int enumTalkativeness)
        {
            PlayAudioClientRpc(smallPathAudioClip, enumTalkativeness);
        }

        [ClientRpc]
        private void PlayAudioClientRpc(string smallPathAudioClip, int enumTalkativeness)
        {
            if (enumTalkativeness == Plugin.Config.Talkativeness.Value
                || LethalBotIdentity.Voice.CanPlayAudioAfterCooldown())
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
        public void SyncTeleportLethalBot(Vector3 pos, bool? setOutside = null, EntranceTeleport? targetEntrance = null, bool allowInteractTrigger = false, bool skipNavMeshCheck = false)
        {
            if (!IsOwner)
            {
                return;
            }
            TeleportLethalBot(pos, setOutside, targetEntrance, allowInteractTrigger, skipNavMeshCheck);
            if (targetEntrance != null)
            {
                if (targetEntrance.NetworkObject == null || !targetEntrance.NetworkObject.IsSpawned)
                {
                    Plugin.LogWarning($"{NpcController.Npc.playerUsername}: Tried to teleport using an unspawned object! Networking to other clients, but without entrance sounds!");
                    TeleportLethalBotServerRpc(new TeleportLethalBotNetworkSerializable()
                    {
                        Pos = pos,
                        SetOutside = setOutside,
                        AllowInteractTrigger = allowInteractTrigger,
                        SkipNavMeshCheck = skipNavMeshCheck
                    });
                    return;
                }

                TeleportLethalBotServerRpc(new TeleportLethalBotNetworkSerializable()
                {
                    Pos = pos,
                    SetOutside = setOutside,
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
                    AllowInteractTrigger = allowInteractTrigger,
                    SkipNavMeshCheck = skipNavMeshCheck
                });
            }
        }
        /// <summary>
        /// Server side, call clients to sync teleport lethalBot
        /// </summary>
        /// <param name="pos">Position destination</param>
        /// <param name="setOutside">Is the teleport destination outside of the facility</param>
        /// <param name="targetEntrance">Is the lethalBot actually using entrance to teleport ?</param>
        [ServerRpc]
        private void TeleportLethalBotServerRpc(TeleportLethalBotNetworkSerializable teleportData)
        {
            TeleportLethalBotClientRpc(teleportData);
        }
        /// <summary>
        /// Client side, teleport lethalBot on client, only for not the owner
        /// </summary>
        /// <param name="pos">Position destination</param>
        /// <param name="setOutside">Is the teleport destination outside of the facility</param>
        /// <param name="targetEntrance">Is the lethalBot actually using entrance to teleport ?</param>
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

            TeleportLethalBot(teleportLethalBotNetworkSerializable.Pos, teleportLethalBotNetworkSerializable.SetOutside, targetEntrance, teleportLethalBotNetworkSerializable.AllowInteractTrigger, teleportLethalBotNetworkSerializable.SkipNavMeshCheck);
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
        public void TeleportLethalBot(Vector3 pos, bool? setOutside = null, EntranceTeleport? targetEntrance = null, bool allowInteractTrigger = false, bool skipNavMeshCheck = false)
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
                StopCoroutine(useInteractTriggerCoroutine);
                useInteractTriggerCoroutine = null;
            }

            if ((bool)lethalBotController.inAnimationWithEnemy)
            {
                lethalBotController.inAnimationWithEnemy.CancelSpecialAnimationWithPlayer();
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
                thisPlayerBody.eulerAngles = new Vector3(thisPlayerBody.eulerAngles.x, targetEntrance.exitPoint.eulerAngles.y, thisPlayerBody.eulerAngles.z);
                TimeSinceTeleporting = Time.timeSinceLevelLoad;
                targetEntrance.timeAtLastUse = Time.realtimeSinceStartup;
                //EntranceTeleport entranceTeleport = RoundManager.FindMainEntranceScript(setOutside.Value);
                AudioReverbPresets audioReverbPresets = Object.FindObjectOfType<AudioReverbPresets>();
                //audioReverbPresets.audioPresets[targetEntrance.audioReverbPreset].ChangeAudioReverbForPlayer(NpcController.Npc);
                if (targetEntrance.audioReverbPreset != -1)
                {
                    audioReverbPresets.audioPresets[targetEntrance.audioReverbPreset].ChangeAudioReverbForPlayer(lethalBotController);
                    if (targetEntrance.entrancePointAudio != null)
                    {
                        targetEntrance.PlayAudioAtTeleportPositions();
                    }
                }

            }
        }

        /// <summary>
        /// Teleport the brain and body of lethalBot
        /// </summary>
        /// <param name="pos"></param>
        private void TeleportAgentAIAndBody(Vector3 pos, bool skipNavMeshCheck = false)
        {
            Vector3 navMeshPosition = skipNavMeshCheck ? pos : RoundManager.Instance.GetNavMeshPosition(pos, default, 5f); // Was 2.7f, but created issues sometimes with the adamance fire exit!
            serverPosition = navMeshPosition;
            NpcController.Npc.transform.position = navMeshPosition;

            if (agent == null
                || !agent.enabled)
            {
                this.transform.position = navMeshPosition;
            }
            else
            {
                agent.enabled = false;
                this.transform.position = navMeshPosition;
                agent.enabled = true;
            }

            // For CullFactory mod
            if (!AreHandsFree())
            {
                HeldItem.EnableItemMeshes(true);
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
                    agent.enabled = false;
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

                this.StopSinkingState();
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
        /// or outside nodes for <see cref="AISearchRoutine"/>s and for safe pathfinding checks!
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
            foreach (var node in shipNodes)
            {
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
            if (this.OwnerClientId != newTarget.actualClientId)
            {
                // Changes the ownership of the lethalBot, on server and client directly
                ChangeOwnershipOfEnemy(newTarget.actualClientId);

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

            SetDestinationToPositionLethalBotAI(this.targetPlayer.transform.position);

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
            foreach(var item in lethalBotController.ItemSlots)
            {
                if (item != null && item.isHeld)
                {
                    if (item.isInShipRoom != isInShip)
                    {
                        lethalBotController.SetItemInElevator(droppedInShipRoom: isInShip, droppedInElevator: inElevator, item);
                    }
                    item.isInElevator = inElevator;
                }
            }

            // NEEDTOVAILDATE: Make sure the player movement code works as expected
            // The following code should be found under UpdatePlayerPositionClientRpc!
            lethalBotController.oldPlayerPosition = lethalBotController.serverPlayerPosition;
            if (!lethalBotController.inVehicleAnimation)
            {
                lethalBotController.serverPlayerPosition = newPos;
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
        /// <param name="direction">Direction to turn body towards to</param>
        /// <param name="intEnumObjectsLookingAt">State to know where the lethalBot should look</param>
        /// <param name="playerEyeToLookAt">Position of the player eyes to look at</param>
        /// <param name="positionToLookAt">Position to look at</param>
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
        /// <param name="direction">Direction to turn body towards to</param>
        /// <param name="intEnumObjectsLookingAt">State to know where the lethalBot should look</param>
        /// <param name="playerEyeToLookAt">Position of the player eyes to look at</param>
        /// <param name="positionToLookAt">Position to look at</param>
        [ServerRpc(RequireOwnership = false)]
        private void UpdatelLethalBotRotationAndLookServerRpc(string stateIndicator, LookAtTarget lookAtTarget)
        {
            UpdateLethalBotRotationAndLookClientRpc(stateIndicator, lookAtTarget);
        }

        /// <summary>
        /// Client side, update the lethalBot body rotation and rotation of head (where he looks).
        /// </summary>
        /// <param name="direction">Direction to turn body towards to</param>
        /// <param name="intEnumObjectsLookingAt">State to know where the lethalBot should look</param>
        /// <param name="playerEyeToLookAt">Position of the player eyes to look at</param>
        /// <param name="positionToLookAt">Position to look at</param>
        [ClientRpc]
        private void UpdateLethalBotRotationAndLookClientRpc(string stateIndicator, LookAtTarget lookAtTarget)
        {
            if (NpcController == null)
            {
                return;
            }

            NpcController.Npc.playersManager.gameStats.allPlayerStats[NpcController.Npc.playerClientId].turnAmount++;
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

            PlayerControllerBPatch.IsInSpecialAnimationClientRpc_ReversePatch(NpcController.Npc, specialAnimation, timed, climbingLadder);
            NpcController.Npc.ResetZAndXRotation();
        }

        #endregion

        #region SyncDeadBodyPosition RPC

        /// <summary>
        /// Server side, call the clients to update the dead body of the lethalBot
        /// </summary>
        /// <param name="newBodyPosition">New dead body position</param>
        [ServerRpc(RequireOwnership = false)]
        public void SyncDeadBodyPositionServerRpc(Vector3 newBodyPosition)
        {
            SyncDeadBodyPositionClientRpc(newBodyPosition);
        }

        /// <summary>
        /// Client side, update the dead body of the lethalBot
        /// </summary>
        /// <param name="newBodyPosition">New dead body position</param>
        [ClientRpc]
        private void SyncDeadBodyPositionClientRpc(Vector3 newBodyPosition)
        {
            PlayerControllerBPatch.SyncBodyPositionClientRpc_ReversePatch(NpcController.Npc, newBodyPosition);
        }

        #endregion

        #region SyncFaceUnderwater

        [ServerRpc(RequireOwnership = false)]
        public void SyncSetFaceUnderwaterServerRpc(bool isUnderwater)
        {
            SyncSetFaceUnderwaterClientRpc(isUnderwater);
        }

        [ClientRpc]
        private void SyncSetFaceUnderwaterClientRpc(bool isUnderwater)
        {
            NpcController.Npc.isUnderwater = isUnderwater;
        }

        #endregion

        #region Grab item RPC

        /// <summary>
        /// Server side, call clients to make the lethalBot grab item on their side to sync everyone
        /// </summary>
        /// <param name="networkObjectReference">Item reference over the network</param>
        [ServerRpc(RequireOwnership = false)]
        public void GrabItemServerRpc(NetworkObjectReference networkObjectReference, bool itemGiven)
        {
            if (!networkObjectReference.TryGet(out NetworkObject networkObject))
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GrabItem for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Unknown to get network object from network object reference (Grab item RPC)");
                return;
            }

            GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
            if (grabbableObject == null)
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GrabItem for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Unknown to get GrabbableObject component from network object (Grab item RPC)");
                return;
            }

            if (!itemGiven)
            {
                if (!IsGrabbableObjectGrabbable(grabbableObject) && !IsGrabbableObjectSellable(grabbableObject))
                {
                    Plugin.LogDebug($"{NpcController.Npc.playerUsername} grabbableObject {grabbableObject} not grabbable");
                    return;
                }
            }

            // Items need to be owned by the owner of the bot so client code
            // can be executed properly
            // NOTE: We do this here since we should be on the server
            if (networkObject.OwnerClientId != this.OwnerClientId)
            { 
                networkObject.ChangeOwnership(this.OwnerClientId);
            }
            GrabItemClientRpc(networkObjectReference);
        }

        /// <summary>
        /// Client side, make the lethalBot grab item
        /// </summary>
        /// <param name="networkObjectReference">Item reference over the network</param>
        [ClientRpc]
        private void GrabItemClientRpc(NetworkObjectReference networkObjectReference)
        {
            if (!networkObjectReference.TryGet(out NetworkObject networkObject))
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GrabItem for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Unknown to get network object from network object reference (Grab item RPC)");
                return;
            }

            GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
            if (grabbableObject == null)
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GrabItem for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Unknown to get GrabbableObject component from network object (Grab item RPC)");
                return;
            }

            if (this.HasGrabbableObjectInInventory(grabbableObject, out _))
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} cannot grab already held item {grabbableObject} on client #{NetworkManager.LocalClientId}");
                return;
            }

            GrabItem(grabbableObject);
        }

        /// <summary>
        /// Make the lethalBot grab an item like an enemy would, but update the body (<c>PlayerControllerB</c>) too.
        /// </summary>
        /// <param name="grabbableObject">Item to grab</param>
        private void GrabItem(GrabbableObject grabbableObject)
        {
            PlayerControllerB lethalBotController = NpcController.Npc;
            Plugin.LogDebug($"{lethalBotController.playerUsername} try to grab item {grabbableObject} on client #{NetworkManager.LocalClientId}");
            int itemSlot = FirstEmptyItemSlot();
            if (itemSlot == -1)
            {
                Plugin.LogDebug($"{lethalBotController.playerUsername} failed to grab item on client #{NetworkManager.LocalClientId}, no free slots!");
                return;
            }
            //this.HeldItem = grabbableObject;

            //grabbableObject.GrabItemFromEnemy(this); //NEEDTOVALIDATE: Should I keep this?
            //grabbableObject.GrabItem();
            SwitchToItemSlot(itemSlot, grabbableObject);
            grabbableObject.EnablePhysics(enable: false);
            grabbableObject.isHeld = true;
            grabbableObject.hasHitGround = false;
            grabbableObject.isInFactory = lethalBotController.isInsideFactory;
            lethalBotController.SetItemInElevator(lethalBotController.isInHangarShipRoom, lethalBotController.isInElevator, grabbableObject);
            lethalBotController.twoHanded = grabbableObject.itemProperties.twoHanded;
            lethalBotController.twoHandedAnimation = grabbableObject.itemProperties.twoHandedAnimation;
            grabbableObject.parentObject = lethalBotController.serverItemHolder;
            lethalBotController.isHoldingObject = true;
            if (grabbableObject.itemProperties.grabSFX != null)
            {
                lethalBotController.itemAudio.PlayOneShot(grabbableObject.itemProperties.grabSFX, 1f);
            }

            lethalBotController.carryWeight += Mathf.Clamp(grabbableObject.itemProperties.weight - 1f, 0f, 10f);
            NpcController.GrabbedObjectValidated = true;

            // Only call this on the owner, it will be networked if needed!
            if (IsOwner)
            { 
                grabbableObject.GrabItemOnClient(); 
            }
            // Should I just call GrabItemOnClient instead?
            /*if (IsOwner || grabbableObject.itemProperties.syncGrabFunction)
            {
                grabbableObject.GrabItem();
            }*/
            /*if (grabbableObject.itemProperties.grabSFX != null)
            {
                NpcController.Npc.itemAudio.PlayOneShot(grabbableObject.itemProperties.grabSFX, 1f);
            }*/

            // animations
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABINVALIDATED, false);
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, false);
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, false);
            lethalBotController.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);
            this.SetSpecialGrabAnimationBool(true, grabbableObject);

            if (this.grabObjectCoroutine != null)
            {
                base.StopCoroutine(this.grabObjectCoroutine);
            }
            this.grabObjectCoroutine = base.StartCoroutine(this.GrabAnimationCoroutine());

            Plugin.LogDebug($"{lethalBotController.playerUsername} Grabbed item {grabbableObject} on client #{NetworkManager.LocalClientId}");
        }

        /// <summary>
        /// Returns the first empty slot the bot has
        /// Returns -1 if no slot is avilable!
        /// </summary>
        /// <returns>Returns the open slot <c>int</c> or -1 </returns>
        private int FirstEmptyItemSlot()
        {
            int result = -1;
            if (NpcController.Npc.ItemSlots[NpcController.Npc.currentItemSlot] == null)
            {
                result = NpcController.Npc.currentItemSlot;
            }
            else
            {
                for (int i = 0; i < NpcController.Npc.ItemSlots.Length; i++)
                {
                    if (NpcController.Npc.ItemSlots[i] == null)
                    {
                        result = i;
                        break;
                    }
                }
            }
            return result;
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
        private bool CanSwitchItemSlot()
        {
            PlayerControllerB thisBot = NpcController.Npc;
            if (thisBot.isGrabbingObjectAnimation 
                || thisBot.inSpecialInteractAnimation 
                || (bool)AccessTools.Field(typeof(PlayerControllerB), "throwingObject").GetValue(thisBot) 
                || thisBot.isTypingChat 
                || thisBot.twoHanded 
                || thisBot.activatingItem 
                || thisBot.jetpackControls 
                || thisBot.disablingJetpackControls
                || NpcController.TimeSinceSwitchingSlots < 0.3f)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, tells the bot to swap to the specified slot 
        /// </summary>
        private void SwitchToItemSlot(int slot, GrabbableObject? fillSlotWithItem = null)
        {
            if (!CanSwitchItemSlot() && fillSlotWithItem == null)
            {
                return;
            }
            NpcController.Npc.currentItemSlot = slot;
            if (fillSlotWithItem != null)
            {
                NpcController.Npc.ItemSlots[slot] = fillSlotWithItem;
            }
            if (!this.AreHandsFree())
            {
                this.HeldItem.playerHeldBy = NpcController.Npc;
                this.SetSpecialGrabAnimationBool(false, this.HeldItem);
                this.HeldItem.PocketItem();
                if (NpcController.Npc.ItemSlots[slot] != null && !string.IsNullOrEmpty(NpcController.Npc.ItemSlots[slot].itemProperties.pocketAnim))
                {
                    NpcController.Npc.playerBodyAnimator.SetTrigger(NpcController.Npc.ItemSlots[slot].itemProperties.pocketAnim);
                }
            }
            if (NpcController.Npc.ItemSlots[slot] != null)
            {
                NpcController.Npc.ItemSlots[slot].playerHeldBy = NpcController.Npc;
                NpcController.Npc.ItemSlots[slot].EquipItem();
                this.SetSpecialGrabAnimationBool(true, NpcController.Npc.ItemSlots[slot]);
                if (!this.AreHandsFree())
                {
                    if (NpcController.Npc.ItemSlots[slot].itemProperties.twoHandedAnimation || this.HeldItem.itemProperties.twoHandedAnimation)
                    {
                        NpcController.Npc.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATIONTWOHANDED);
                        NpcController.Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATIONTWOHANDED);
                    }
                    NpcController.Npc.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATION);
                    NpcController.Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATION);
                }
                NpcController.Npc.twoHandedAnimation = NpcController.Npc.ItemSlots[slot].itemProperties.twoHandedAnimation;
                NpcController.Npc.twoHanded = NpcController.Npc.ItemSlots[slot].itemProperties.twoHanded;
                NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, true);
                NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, false);
                NpcController.Npc.isHoldingObject = true;
                NpcController.Npc.currentlyHeldObjectServer = NpcController.Npc.ItemSlots[slot];
                this.HeldItem = NpcController.Npc.ItemSlots[slot];
                if (fillSlotWithItem == null)
                {
                    this.HeldItem.gameObject.GetComponent<AudioSource>().PlayOneShot(this.HeldItem.itemProperties.grabSFX, 0.6f);
                }
            }
            else
            {
                this.HeldItem = null;
                NpcController.Npc.currentlyHeldObject = null;
                NpcController.Npc.currentlyHeldObjectServer = null;
                NpcController.Npc.isHoldingObject = false;
                NpcController.Npc.twoHanded = false;
                NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, true);
            }
            NpcController.TimeSinceSwitchingSlots = 0f;
        }

        /// <summary>
        /// Coroutine for the grab animation
        /// </summary>
        /// <returns></returns>
        private IEnumerator GrabAnimationCoroutine()
        {
            if (!this.AreHandsFree())
            {
                float grabAnimationTime = this.HeldItem.itemProperties.grabAnimationTime > 0f ? this.HeldItem.itemProperties.grabAnimationTime : 0.4f;
                yield return new WaitForSeconds(grabAnimationTime - 0.2f);
                NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_GRABVALIDATED, true);
                NpcController.Npc.isGrabbingObjectAnimation = false;
            }
            yield break;
        }

        /// <summary>
        /// Set the animation of body to something special if the item has a special grab animation.
        /// </summary>
        /// <param name="setBool">Activate or deactivate special animation</param>
        /// <param name="item">Item that has the special grab animation</param>
        private void SetSpecialGrabAnimationBool(bool setBool, GrabbableObject? item)
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

        /// <summary>
        /// Make the lethalBot drop his item like an enemy, but update the body (<c>PlayerControllerB</c>) too.
        /// </summary>
        public void DropItem(bool placeObject = false, NetworkObject parentObjectTo = null!, Vector3 placePosition = default(Vector3), bool matchRotationOfParent = true)
        {
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} Try to drop item on client #{NetworkManager.LocalClientId}");
            if (this.AreHandsFree())
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} Try to drop not held item on client #{NetworkManager.LocalClientId}");
                return;
            }

            GrabbableObject grabbableObject = this.HeldItem;
            Vector3 vector;
            NetworkObject physicsRegionOfDroppedObject = grabbableObject.GetPhysicsRegionOfDroppedObject(NpcController.Npc, out vector);
            if (!placeObject)
            {
                if (physicsRegionOfDroppedObject != null)
                {
                    placePosition = vector;
                    parentObjectTo = physicsRegionOfDroppedObject;
                    placeObject = true;
                    matchRotationOfParent = false;
                }
            }

            if (placeObject)
            {
                if (parentObjectTo == null)
                {
                    if (NpcController.Npc.isInElevator)
                    {
                        placePosition = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(placePosition);
                    }
                    else
                    {
                        placePosition = StartOfRound.Instance.propsContainer.InverseTransformPoint(placePosition);
                    }
                    int floorYRot2 = (int)base.transform.localEulerAngles.y;

                    // on client
                    SetObjectAsNoLongerHeld(grabbableObject,
                                            this.NpcController.Npc.isInElevator,
                                            this.NpcController.Npc.isInHangarShipRoom,
                                            placePosition,
                                            floorYRot2);

                    if (grabbableObject.NetworkObject == null || !grabbableObject.NetworkObject.IsSpawned)
                    {
                        Plugin.LogWarning($"{NpcController.Npc.playerUsername}: Tried to drop an unspawned object! Not networking to other clients!");
                        return;
                    }

                    // for other clients
                    SetObjectAsNoLongerHeldServerRpc(new DropItemNetworkSerializable()
                    {
                        DroppedInElevator = this.NpcController.Npc.isInElevator,
                        DroppedInShipRoom = this.NpcController.Npc.isInHangarShipRoom,
                        FloorYRot = floorYRot2,
                        GrabbedObject = grabbableObject.NetworkObject,
                        TargetFloorPosition = placePosition,
                        SkipOwner = true
                    });
                }
                else
                {
                    // on client
                    PlaceGrabbableObject(grabbableObject, parentObjectTo.transform, placePosition, matchRotationOfParent);

                    if (grabbableObject.NetworkObject == null || !grabbableObject.NetworkObject.IsSpawned)
                    {
                        Plugin.LogWarning($"{NpcController.Npc.playerUsername}: Tried to drop an unspawned object! Not networking to other clients!");
                        return;
                    }

                    // for other clients
                    PlaceGrabbableObjectServerRpc(new PlaceItemNetworkSerializable()
                    {
                        GrabbedObject = grabbableObject.NetworkObject,
                        MatchRotationOfParent = matchRotationOfParent,
                        ParentObject = parentObjectTo,
                        PlacePositionOffset = placePosition,
                        SkipOwner = true
                    });
                }
            }
            else
            {
                bool droppedInElevator = this.NpcController.Npc.isInElevator;
                Vector3 targetFloorPosition;
                if (!NpcController.Npc.isInElevator)
                {
                    Vector3 vector2;
                    if (grabbableObject.itemProperties.allowDroppingAheadOfPlayer)
                    {
                        vector2 = DropItemAheadOfPlayer(grabbableObject, NpcController.Npc);
                    }
                    else
                    {
                        vector2 = grabbableObject.GetItemFloorPosition(default(Vector3));
                    }
                    if (!NpcController.Npc.playersManager.shipBounds.bounds.Contains(vector2))
                    {
                        targetFloorPosition = NpcController.Npc.playersManager.propsContainer.InverseTransformPoint(vector2);
                    }
                    else
                    {
                        droppedInElevator = true;
                        targetFloorPosition = NpcController.Npc.playersManager.elevatorTransform.InverseTransformPoint(vector2);
                    }
                }
                else
                {
                    Vector3 vector2 = grabbableObject.GetItemFloorPosition(default(Vector3));
                    if (!NpcController.Npc.playersManager.shipBounds.bounds.Contains(vector2))
                    {
                        droppedInElevator = false;
                        targetFloorPosition = NpcController.Npc.playersManager.propsContainer.InverseTransformPoint(vector2);
                    }
                    else
                    {
                        targetFloorPosition = NpcController.Npc.playersManager.elevatorTransform.InverseTransformPoint(vector2);
                    }
                }
                int floorYRot = (int)base.transform.localEulerAngles.y;

                // on client
                SetObjectAsNoLongerHeld(grabbableObject,
                                        droppedInElevator,
                                        this.NpcController.Npc.isInHangarShipRoom,
                                        targetFloorPosition,
                                        floorYRot);

                if (grabbableObject.NetworkObject == null || !grabbableObject.NetworkObject.IsSpawned)
                {
                    Plugin.LogWarning($"{NpcController.Npc.playerUsername}: Tried to drop an unspawned object! Not networking to other clients!");
                    return;
                }

                // for other clients
                SetObjectAsNoLongerHeldServerRpc(new DropItemNetworkSerializable()
                {
                    DroppedInElevator = droppedInElevator,
                    DroppedInShipRoom = this.NpcController.Npc.isInHangarShipRoom,
                    FloorYRot = floorYRot,
                    GrabbedObject = grabbableObject.NetworkObject,
                    TargetFloorPosition = targetFloorPosition,
                    SkipOwner = true
                });
            }


        }

        [ServerRpc(RequireOwnership = false)]
        public void DiscardItemServerRpc(NetworkObjectReference discardObjectNetwork)
        {
            if (discardObjectNetwork.TryGet(out NetworkObject networkObject, null))
            {
                DiscardItemClientRpc(networkObject);
            }
            else
            {
                Plugin.LogError($"Lethal Bot {this.NpcController.Npc.playerUsername} on client #{NetworkManager.LocalClientId} (server) discard item : Object was not discarded because it does not exist on the server.");
            }
        }

        [ClientRpc]
        public void DiscardItemClientRpc(NetworkObjectReference discardObjectNetwork)
        {
            if (discardObjectNetwork.TryGet(out NetworkObject networkObject, null))
            {
                GrabbableObject? itemToDiscard = networkObject.GetComponent<GrabbableObject>();
                if (itemToDiscard != null)
                {
                    if (!itemToDiscard.IsOwner)
                    {
                        itemToDiscard.DiscardItem();
                    }
                }
                else
                {
                    Plugin.LogError($"Lethal Bot {this.NpcController.Npc.playerUsername} on client #{NetworkManager.LocalClientId} discard item : The server did not have a reference to the grabbable object");
                }
            }
            else
            {
                Plugin.LogError($"Lethal Bot {this.NpcController.Npc.playerUsername} on client #{NetworkManager.LocalClientId} discard item : The server did not have a reference to the object");
            }
        }

        /// <summary>
        /// Destorys the grabbable object in the given slot and syncs with other clients!
        /// </summary>
        /// <param name="itemSlot"></param>
        public void DestroyItemInSlotAndSync(int itemSlot)
        {
            if (base.IsOwner)
            {
                PlayerControllerB lethalBotController = NpcController.Npc;
                if (itemSlot >= lethalBotController.ItemSlots.Length || lethalBotController.ItemSlots[itemSlot] == null)
                {
                    Plugin.LogError($"Destroy item in slot called for a slot (slot {itemSlot}) which is empty or incorrect");
                }

                NpcController.TimeSinceSwitchingSlots = 0f;
                DestroyItemInSlot(itemSlot);
                DestroyItemInSlotServerRpc(itemSlot);
            }
        }

        /// <summary>
        /// Server rpc to destory the grabbable object in the given slot
        /// </summary>
        /// <param name="itemSlot"></param>
        [ServerRpc]
        public void DestroyItemInSlotServerRpc(int itemSlot)
        {
            DestroyItemInSlotClientRpc(itemSlot);
        }

        /// <summary>
        /// Client rpc to destory the grabbable object in the given slot
        /// </summary>
        /// <param name="itemSlot"></param>
        [ClientRpc]
        public void DestroyItemInSlotClientRpc(int itemSlot)
        {
            if (!base.IsOwner)
            {
                DestroyItemInSlot(itemSlot);
            }
        }

        /// <summary>
        /// Destorys the grabbable object in the given slot!
        /// </summary>
        /// <param name="itemSlot"></param>
        public void DestroyItemInSlot(int itemSlot)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.ShutdownInProgress)
            {
                return;
            }

            PlayerControllerB lethalBotController = NpcController.Npc;
            Plugin.LogDebug($"Destroying item in slot {itemSlot}; {lethalBotController.currentItemSlot}; is helditem null: {AreHandsFree()}");
            if (!AreHandsFree())
            {
                Plugin.LogDebug("HeldItem: " + HeldItem.itemProperties.itemName);
            }

            GrabbableObject? grabbableObject = lethalBotController.ItemSlots[itemSlot];
            if (lethalBotController.isHoldingObject && !AreHandsFree())
            {
                if (lethalBotController.currentItemSlot == itemSlot)
                {
                    lethalBotController.carryWeight = Mathf.Clamp(lethalBotController.carryWeight - (HeldItem.itemProperties.weight - 1f), 1f, 10f);
                    lethalBotController.isHoldingObject = false;
                    lethalBotController.twoHanded = false;
                    lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, value: true);
                    lethalBotController.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);
                    lethalBotController.activatingItem = false;
                }

                if (!AreHandsFree() && HeldItem == lethalBotController.ItemSlots[itemSlot])
                {
                    SetSpecialGrabAnimationBool(setBool: false, HeldItem);
                    if (IsOwner)
                    {
                        HeldItem.DiscardItemOnClient();
                    }

                    HeldItem = null;
                    lethalBotController.currentlyHeldObjectServer = null;
                }
            }

            lethalBotController.ItemSlots[itemSlot] = null;
            if (IsServer)
            {
                grabbableObject?.NetworkObject.Despawn();
            }
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, makes the bot drop everthing held! 
        /// </summary>
        public void DropAllHeldItems(bool itemsFall = true)
        {
            for (int i = 0; i < NpcController.Npc.ItemSlots.Length; i++)
            {
                GrabbableObject grabbableObject = NpcController.Npc.ItemSlots[i];
                if (grabbableObject == null)
                {
                    continue;
                }
                if (itemsFall)
                {
                    // If the object is not a maneater baby, we should add it to the just dropped items dictionary!
                    if (grabbableObject is not CaveDwellerPhysicsProp)
                    {
                        DictJustDroppedItems[grabbableObject] = Time.realtimeSinceStartup;
                    }
                    grabbableObject.parentObject = null;
                    grabbableObject.heldByPlayerOnServer = false;
                    if (NpcController.Npc.isInElevator)
                    {
                        grabbableObject.transform.SetParent(NpcController.Npc.playersManager.elevatorTransform, worldPositionStays: true);
                    }
                    else
                    {
                        grabbableObject.transform.SetParent(NpcController.Npc.playersManager.propsContainer, worldPositionStays: true);
                    }
                    NpcController.Npc.SetItemInElevator(NpcController.Npc.isInHangarShipRoom, NpcController.Npc.isInElevator, grabbableObject);
                    grabbableObject.EnablePhysics(enable: true);
                    grabbableObject.EnableItemMeshes(enable: true);
                    grabbableObject.transform.localScale = grabbableObject.originalScale;
                    grabbableObject.isHeld = false;
                    grabbableObject.isPocketed = false;
                    grabbableObject.startFallingPosition = grabbableObject.transform.parent.InverseTransformPoint(grabbableObject.transform.position);
                    grabbableObject.FallToGround(randomizePosition: true);
                    grabbableObject.fallTime = UnityEngine.Random.Range(-0.3f, 0.05f);
                    if (base.IsOwner)
                    {
                        grabbableObject.DiscardItemOnClient();
                    }
                    else if (!grabbableObject.itemProperties.syncDiscardFunction)
                    {
                        grabbableObject.playerHeldBy = null;
                    }
                }

                NpcController.Npc.ItemSlots[i] = null;
            }
            if (NpcController.Npc.isHoldingObject)
            {
                NpcController.Npc.isHoldingObject = false;
                if (!this.AreHandsFree())
                {
                    this.SetSpecialGrabAnimationBool(false, this.HeldItem);
                }
                NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, true);
                NpcController.Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);
            }
            this.HeldItem = null;
            NpcController.Npc.activatingItem = false;
            NpcController.Npc.twoHanded = false;
            NpcController.Npc.carryWeight = 1f;
            NpcController.Npc.currentlyHeldObjectServer = null;
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, makes the bot drop everthing held! 
        /// </summary>
        public void DropAllHeldItemsAndSync()
        { 
            DropAllHeldItems();
            DropAllHeldItemsServerRpc();
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, makes the bot drop everthing held! 
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void DropAllHeldItemsServerRpc()
        {
            DropAllHeldItemsClientRpc();
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, makes the bot drop everthing held! 
        /// </summary>
        [ClientRpc]
        public void DropAllHeldItemsClientRpc()
        {
            DropAllHeldItems();
        }

        /// <summary>
        /// Copied from <c>PlayerControllerB</c>, despawns the bot's currently held object! 
        /// </summary>
        public void DespawnHeldObject()
        {
            GrabbableObject? grabbableObject = this.HeldItem;
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} Try to despawn held item");
            if (grabbableObject == null)
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} Try to despawn, but bot is holding nothing");
                return;
            }

            // Discard for player
            PlayerControllerB lethalBot = NpcController.Npc;
            Plugin.LogDebug($"{lethalBot.playerUsername} Try to despawn held item {this.HeldItem} on owner #{this.OwnerClientId} and sync");
            lethalBot.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, true);
            lethalBot.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);

            // Despawn and sync with others!
            DespawnHeldObjectOnClient();
            DespawnHeldObjectOnServerRpc();
        }

        /// <summary>
        /// Actually does the action of despawning the object on this client
        /// </summary>
        private void DespawnHeldObjectOnClient()
        {
            // Discard for player
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} Try to despawn held item on client #{NetworkManager.LocalClientId}");
            if (this.AreHandsFree())
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} Try to despawn, but bot is holding nothing on client #{NetworkManager.LocalClientId}");
                return;
            }

            Plugin.LogDebug($"{NpcController.Npc.playerUsername} Try to despawn held item {this.HeldItem} on client #{NetworkManager.LocalClientId}");
            PlayerControllerB lethalBot = NpcController.Npc;
            for (int i = 0; i < lethalBot.ItemSlots.Length; i++)
            {
                if (lethalBot.ItemSlots[i] == this.HeldItem)
                {
                    lethalBot.ItemSlots[i] = null;
                    break;
                }
            }

            SetSpecialGrabAnimationBool(false, this.HeldItem);
            lethalBot.isHoldingObject = false;
            lethalBot.twoHanded = false;
            lethalBot.twoHandedAnimation = false;

            float weightToLose = this.HeldItem.itemProperties.weight - 1f < 0f ? 0f : this.HeldItem.itemProperties.weight - 1f;
            lethalBot.carryWeight = Mathf.Clamp(lethalBot.carryWeight - weightToLose, 1f, 10f);
        }

        /// <summary>
        /// Sends the despawn held object client RPC and despawns the network object as well!
        /// </summary>
        [ServerRpc]
        private void DespawnHeldObjectOnServerRpc()
        {
            if (!this.AreHandsFree())
            {
                this.HeldItem.gameObject.GetComponent<NetworkObject>().Despawn();
            }
            DespawnHeldObjectClientRpc();
        }

        /// <summary>
        /// Despawns the bot's currently held object for this client! 
        /// </summary>
        [ClientRpc]
        private void DespawnHeldObjectClientRpc()
        {
            if (!IsOwner)
            {
                DespawnHeldObjectOnClient();
            }
        }

        private Vector3 DropItemAheadOfPlayer(GrabbableObject grabbableObject, PlayerControllerB player)
        {
            Vector3 vector;
            Ray ray = new Ray(base.transform.position + Vector3.up * 0.4f, player.gameplayCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 1.7f, 268438273, QueryTriggerInteraction.Ignore))
            {
                vector = ray.GetPoint(Mathf.Clamp(hit.distance - 0.3f, 0.01f, 2f));
            }
            else
            {
                vector = ray.GetPoint(1.7f);
            }
            Vector3 itemFloorPosition = grabbableObject.GetItemFloorPosition(vector);
            if (itemFloorPosition == vector)
            {
                itemFloorPosition = grabbableObject.GetItemFloorPosition(default(Vector3));
            }
            return itemFloorPosition;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetObjectAsNoLongerHeldServerRpc(DropItemNetworkSerializable dropItemNetworkSerializable)
        {
            NetworkObject networkObject;
            if (dropItemNetworkSerializable.GrabbedObject.TryGet(out networkObject, null))
            {
                SetObjectAsNoLongerHeldClientRpc(dropItemNetworkSerializable);
            }
            else
            {
                Plugin.LogError($"Lethal Bot {this.NpcController.Npc.playerUsername} on client #{NetworkManager.LocalClientId} (server) drop item : Object was not thrown because it does not exist on the server.");
            }
        }

        [ClientRpc]
        private void SetObjectAsNoLongerHeldClientRpc(DropItemNetworkSerializable dropItemNetworkSerializable)
        {
            // Skip the owner if we were told to!
            if (dropItemNetworkSerializable.SkipOwner && base.IsOwner)
            {
                return;
            }

            if (this.AreHandsFree())
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} held item already dropped, on client #{NetworkManager.LocalClientId}");
                return;
            }

            NetworkObject networkObject;
            if (dropItemNetworkSerializable.GrabbedObject.TryGet(out networkObject, null))
            {
                SetObjectAsNoLongerHeld(networkObject.GetComponent<GrabbableObject>(),
                                        dropItemNetworkSerializable.DroppedInElevator,
                                        dropItemNetworkSerializable.DroppedInShipRoom,
                                        dropItemNetworkSerializable.TargetFloorPosition,
                                        dropItemNetworkSerializable.FloorYRot);
            }
            else
            {
                Plugin.LogError($"Lethal Bot {this.NpcController.Npc.playerUsername} on client #{NetworkManager.LocalClientId} drop item : The server did not have a reference to the held object");
            }
        }

        private void SetObjectAsNoLongerHeld(GrabbableObject grabbableObject,
                                             bool droppedInElevator,
                                             bool droppedInShipRoom,
                                             Vector3 targetFloorPosition,
                                             int floorYRot = -1)
        {
            grabbableObject.heldByPlayerOnServer = false;
            grabbableObject.parentObject = null;
            if (droppedInElevator)
            {
                grabbableObject.transform.SetParent(NpcController.Npc.playersManager.elevatorTransform, true);
            }
            else
            {
                grabbableObject.transform.SetParent(NpcController.Npc.playersManager.propsContainer, true);
            }

            NpcController.Npc.SetItemInElevator(droppedInShipRoom, droppedInElevator, grabbableObject);
            grabbableObject.EnablePhysics(true);
            grabbableObject.EnableItemMeshes(true);
            grabbableObject.isHeld = false;
            grabbableObject.isPocketed = false;
            grabbableObject.fallTime = 0f;
            grabbableObject.startFallingPosition = grabbableObject.transform.parent.InverseTransformPoint(grabbableObject.transform.position);
            grabbableObject.targetFloorPosition = targetFloorPosition;
            grabbableObject.floorYRot = floorYRot;

            EndDropItem(grabbableObject);
        }

        [ServerRpc(RequireOwnership = false)]
        private void PlaceGrabbableObjectServerRpc(PlaceItemNetworkSerializable placeItemNetworkSerializable)
        {
            NetworkObject networkObject;
            NetworkObject networkObject2;
            if (placeItemNetworkSerializable.GrabbedObject.TryGet(out networkObject, null)
                && placeItemNetworkSerializable.ParentObject.TryGet(out networkObject2, null))
            {
                PlaceGrabbableObjectClientRpc(placeItemNetworkSerializable);
                return;
            }

            NetworkObject networkObject3;
            if (!placeItemNetworkSerializable.GrabbedObject.TryGet(out networkObject3, null))
            {
                Plugin.LogError($"Object placement not synced to clients, missing reference to a network object: placing object with id: {placeItemNetworkSerializable.GrabbedObject.NetworkObjectId}; lethalBot {NpcController.Npc.playerUsername}");
                return;
            }
            NetworkObject networkObject4;
            if (!placeItemNetworkSerializable.ParentObject.TryGet(out networkObject4, null))
            {
                Plugin.LogError($"Object placement not synced to clients, missing reference to a network object: parent object with id: {placeItemNetworkSerializable.ParentObject.NetworkObjectId}; lethalBot {NpcController.Npc.playerUsername}");
            }
        }

        [ClientRpc]
        private void PlaceGrabbableObjectClientRpc(PlaceItemNetworkSerializable placeItemNetworkSerializable)
        {
            // Skip the owner if we were told to!
            if (placeItemNetworkSerializable.SkipOwner && base.IsOwner)
            {
                return;
            }

            NetworkObject networkObject;
            if (placeItemNetworkSerializable.GrabbedObject.TryGet(out networkObject, null))
            {
                GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
                NetworkObject networkObject2;
                if (placeItemNetworkSerializable.ParentObject.TryGet(out networkObject2, null))
                {
                    this.PlaceGrabbableObject(grabbableObject,
                                              networkObject2.transform,
                                              placeItemNetworkSerializable.PlacePositionOffset,
                                              placeItemNetworkSerializable.MatchRotationOfParent);
                }
                else
                {
                    Plugin.LogError($"Reference to parent object when placing was missing. object: {grabbableObject} placed by lethalBot #{NpcController.Npc.playerUsername}");
                }
            }
            else
            {
                Plugin.LogError("The server did not have a reference to the held object (when attempting to PLACE object on client.)");
            }
        }

        private void PlaceGrabbableObject(GrabbableObject placeObject, Transform parentObject, Vector3 positionOffset, bool matchRotationOfParent)
        {
            if (this.AreHandsFree())
            {
                Plugin.LogDebug($"{NpcController.Npc.playerUsername} held item already placed, on client #{NetworkManager.LocalClientId}");
                return;
            }

            PlayerPhysicsRegion componentInChildren = parentObject.GetComponentInChildren<PlayerPhysicsRegion>();
            if (componentInChildren != null && componentInChildren.allowDroppingItems)
            {
                parentObject = componentInChildren.physicsTransform;
            }
            placeObject.EnablePhysics(true);
            placeObject.EnableItemMeshes(true);
            placeObject.isHeld = false;
            placeObject.isPocketed = false;
            placeObject.heldByPlayerOnServer = false;
            NpcController.Npc.SetItemInElevator(NpcController.Npc.isInHangarShipRoom, NpcController.Npc.isInElevator, placeObject);
            placeObject.parentObject = null;
            placeObject.transform.SetParent(parentObject, true);
            placeObject.startFallingPosition = placeObject.transform.localPosition;
            placeObject.transform.localScale = placeObject.originalScale;
            placeObject.transform.localPosition = positionOffset;
            placeObject.targetFloorPosition = positionOffset;
            if (!matchRotationOfParent)
            {
                placeObject.fallTime = 0f;
            }
            else
            {
                placeObject.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
                placeObject.fallTime = 1.1f;
            }
            placeObject.OnPlaceObject();

            EndDropItem(placeObject);
        }

        private void EndDropItem(GrabbableObject grabbableObject)
        {
            grabbableObject.DiscardItemOnClient();
            this.SetSpecialGrabAnimationBool(false, grabbableObject);
            NpcController.Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CANCELHOLDING, true);
            NpcController.Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_THROW);

            // If the object is not a maneater baby, we should add it to the just dropped items dictionary!
            if (grabbableObject is not CaveDwellerPhysicsProp)
            {
                DictJustDroppedItems[grabbableObject] = Time.realtimeSinceStartup;
            }
            this.HeldItem = null;
            NpcController.Npc.isHoldingObject = false;
            NpcController.Npc.currentlyHeldObjectServer = null;
            NpcController.Npc.twoHanded = false;
            NpcController.Npc.twoHandedAnimation = false;
            NpcController.GrabbedObjectValidated = false;
            for (int i = 0; i < NpcController.Npc.ItemSlots.Length; i++)
            {
                if (NpcController.Npc.ItemSlots[i] == grabbableObject)
                {
                    NpcController.Npc.ItemSlots[i] = null;
                    break;
                }
            }

            float weightToLose = grabbableObject.itemProperties.weight - 1f < 0f ? 0f : grabbableObject.itemProperties.weight - 1f;
            NpcController.Npc.carryWeight = Mathf.Clamp(NpcController.Npc.carryWeight - weightToLose, 1f, 10f);

            SyncBatteryLethalBot(grabbableObject, (int)(grabbableObject.insertedBattery.charge * 100f));
            Plugin.LogDebug($"{NpcController.Npc.playerUsername} dropped {grabbableObject}, on client #{NetworkManager.LocalClientId}");
        }

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

        #region Vote to leave early RPC

        /*[ServerRpc(RequireOwnership = false)]
        public void LethalBotVoteToLeaveEarlyServerRpc()
        {
            // I had to recreate the the functions as its easier than editing the base functions!
            TimeOfDay instanceTOD = TimeOfDay.Instance;
            int num = StartOfRound.Instance.connectedPlayersAmount + 1 - StartOfRound.Instance.livingPlayers;
            instanceTOD.votesForShipToLeaveEarly++;
            if (instanceTOD.votesForShipToLeaveEarly >= num)
            {
                instanceTOD.SetShipLeaveEarlyClientRpc(instanceTOD.normalizedTimeOfDay + 0.1f, instanceTOD.votesForShipToLeaveEarly);
            }
            else
            {
                instanceTOD.AddVoteForShipToLeaveEarlyClientRpc();
            }
        }*/

        /*[ClientRpc]
        public void AddVoteForShipToLeaveEarlyClientRpc()
        {
            // If we are the host or server, we shouldn't increment this as we would be doing it twice!
            if (!IsServer && !IsHost)
            {
                TimeOfDay.Instance.votesForShipToLeaveEarly++;
            }
            HUDManager.Instance.SetShipLeaveEarlyVotesText(TimeOfDay.Instance.votesForShipToLeaveEarly);
        }*/

        /*[ClientRpc]
        public void SetShipLeaveEarlyClientRpc(float timeToLeaveEarly, int votes)
        {
            TimeOfDay instanceTOD = TimeOfDay.Instance;
            instanceTOD.votesForShipToLeaveEarly = votes;
            HUDManager.Instance.SetShipLeaveEarlyVotesText(votes);
            instanceTOD.shipLeaveAutomaticallyTime = timeToLeaveEarly;
            instanceTOD.shipLeavingAlertCalled = true;
            instanceTOD.shipLeavingEarlyDialogue[0].bodyText = "WARNING! Please return by " + HUDManager.Instance.SetClock(timeToLeaveEarly, instanceTOD.numberOfHours, createNewLine: false) + ". A vote has been cast, and the autopilot ship will leave early.";
            HUDManager.Instance.ReadDialogue(instanceTOD.shipLeavingEarlyDialogue);
            HUDManager.Instance.shipLeavingEarlyIcon.enabled = true;
        }*/

        #endregion

        // TODO: This needs A LOT of work, hopefully this will pay off in the long run.
        // This would require some workarounds to get this to work!
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
            InteractTriggerPatch.UpdateUsedByPlayerServerRpc_ReversePatch(terminalTrigger, (int)localPlayerController.playerClientId);
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
            ourTerminal.SetTerminalInUseLocalClient(true);
            terminalTrigger.interactable = false; // Don't let other player use the terminal!
            ourTerminal.terminalAudio.PlayOneShot(ourTerminal.enterTerminalSFX);
        }

        /// <summary>
        /// Makes the bot leave the terminal, this has proper support for animations!
        /// </summary>
        /// <param name="syncTerminalInUse">Should the terminal update its status on all clients?</param>
        public void LeaveTerminal(bool syncTerminalInUse = true)
        {
            // Terminal is invalid for some reason, report the error!
            Terminal ourTerminal = Managers.TerminalManager.Instance.GetTerminal();
            if (ourTerminal == null)
            {
                NpcController.Npc.inTerminalMenu = false;
                NpcController.Npc.playerBodyAnimator.ResetTrigger(Const.PLAYER_ANINATION_TRIGGER_TERMINAL);
                Plugin.LogError($"[ERROR] Bot {NpcController.Npc.playerUsername} was unable to properly leave the terminal. Issues may occur!");
                return;
            }

            PlayerControllerB localPlayerController = NpcController.Npc;
            if (!localPlayerController.inTerminalMenu)
            {
                Plugin.LogWarning($"Bot {localPlayerController.playerUsername} was told to leave a terminal when they were not using it!");
                return;
            }

            InteractTrigger terminalTrigger = ourTerminal.gameObject.GetComponent<InteractTrigger>();
            InteractTriggerPatch.StopUsingServerRpc_ReversePatch(terminalTrigger, (int)localPlayerController.playerClientId);
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

        private IEnumerator waitUntilFrameEndToSetActive(bool active, Terminal? ourTerminal)
        {
            yield return new WaitForEndOfFrame();
            ourTerminal?.terminalUIScreen.gameObject.SetActive(active);
        }

        #endregion

        /*#region Give item to intern RPC

        [ServerRpc(RequireOwnership = false)]
        public void GiveItemToInternServerRpc(ulong playerClientIdGiver, NetworkObjectReference networkObjectReference)
        {
            if (!networkObjectReference.TryGet(out NetworkObject networkObject))
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GiveItemToInternServerRpc for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Failed to get network object from network object reference (Grab item RPC)");
                return;
            }

            GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
            if (grabbableObject == null)
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GiveItemToInternServerRpc for LethalBotAI {this.BotId} {NpcController.Npc.playerUsername}: Failed to get GrabbableObject component from network object (Grab item RPC)");
                return;
            }

            GiveItemToInternClientRpc(playerClientIdGiver, networkObjectReference);
        }

        [ClientRpc]
        private void GiveItemToInternClientRpc(ulong playerClientIdGiver, NetworkObjectReference networkObjectReference)
        {
            if (!networkObjectReference.TryGet(out NetworkObject networkObject))
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GiveItemToInternClientRpc for LethalBotAI {this.BotId}: Failed to get network object from network object reference (Grab item RPC)");
                return;
            }

            GrabbableObject grabbableObject = networkObject.GetComponent<GrabbableObject>();
            if (grabbableObject == null)
            {
                Plugin.LogError($"{NpcController.Npc.playerUsername} GiveItemToInternClientRpc for LethalBotAI {this.BotId}: Failed to get GrabbableObject component from network object (Grab item RPC)");
                return;
            }

            GiveItemToIntern(playerClientIdGiver, grabbableObject);
        }

        private void GiveItemToIntern(ulong playerClientIdGiver, GrabbableObject grabbableObject)
        {
            Plugin.LogDebug($"GiveItemToIntern playerClientIdGiver {playerClientIdGiver}, localPlayerController {StartOfRound.Instance.localPlayerController.playerClientId}");
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[playerClientIdGiver];

            // Discard for player
            if (player.playerClientId == StartOfRound.Instance.localPlayerController.playerClientId)
            {
                PlayerControllerBPatch.SetSpecialGrabAnimationBool_ReversePatch(player, false, player.currentlyHeldObjectServer);
                player.playerBodyAnimator.SetBool("cancelHolding", true);
                player.playerBodyAnimator.SetTrigger("Throw");
                HUDManager.Instance.itemSlotIcons[player.currentItemSlot].enabled = false;
                HUDManager.Instance.holdingTwoHandedItem.enabled = false;
                HUDManager.Instance.ClearControlTips();
            }

            for (int i = 0; i < player.ItemSlots.Length; i++)
            {
                if (player.ItemSlots[i] == grabbableObject)
                {
                    player.ItemSlots[i] = null;
                }
            }

            grabbableObject.EnablePhysics(true);
            grabbableObject.EnableItemMeshes(true);
            grabbableObject.parentObject = null;
            grabbableObject.heldByPlayerOnServer = false;
            grabbableObject.DiscardItem();

            player.isHoldingObject = false;
            player.currentlyHeldObjectServer = null;
            player.twoHanded = false;
            player.twoHandedAnimation = false;

            float weightToLose = grabbableObject.itemProperties.weight - 1f < 0f ? 0f : grabbableObject.itemProperties.weight - 1f;
            player.carryWeight = Mathf.Clamp(player.carryWeight - weightToLose, 1f, 10f);

            SyncBatteryLethalBotServerRpc(grabbableObject.NetworkObject, (int)(grabbableObject.insertedBattery.charge * 100f));

            // Intern grab item
            GrabItem(grabbableObject);
        }

        #endregion*/

        /*#region Damage bot from client players RPC

        /// <summary>
        /// Server side, call client to sync the damage to the lethalBot coming from a player
        /// </summary>
        /// <param name="damageAmount"></param>
        /// <param name="hitDirection"></param>
        /// <param name="playerWhoHit"></param>
        [ServerRpc(RequireOwnership = false)]
        public void DamageLethalBotFromOtherClientServerRpc(int damageAmount, Vector3 hitDirection, int playerWhoHit)
        {
            DamageLethalBotFromOtherClientClientRpc(damageAmount, hitDirection, playerWhoHit);
        }

        /// <summary>
        /// Client side, update and apply the damage to the lethalBot coming from a player
        /// </summary>
        /// <param name="damageAmount"></param>
        /// <param name="hitDirection"></param>
        /// <param name="playerWhoHit"></param>
        [ClientRpc]
        private void DamageLethalBotFromOtherClientClientRpc(int damageAmount, Vector3 hitDirection, int playerWhoHit)
        {
            DamageLethalBotFromOtherClient(damageAmount, hitDirection, playerWhoHit);
        }

        /// <summary>
        /// Update and apply the damage to the lethalBot coming from a player
        /// </summary>
        /// <param name="damageAmount"></param>
        /// <param name="hitDirection"></param>
        /// <param name="playerWhoHit"></param>
        private void DamageLethalBotFromOtherClient(int damageAmount, Vector3 hitDirection, int playerWhoHit)
        {
            if (NpcController == null)
            {
                return;
            }

            if (!NpcController.Npc.AllowPlayerDeath())
            {
                return;
            }

            if (NpcController.Npc.isPlayerControlled)
            {
                CentipedeAI[] array = Object.FindObjectsByType<CentipedeAI>(FindObjectsSortMode.None);
                foreach (CentipedeAI snareFlea in array)
                {
                    if (snareFlea.clingingToPlayer == this)
                    {
                        return;
                    }
                }
                this.DamageLethalBot(damageAmount, CauseOfDeath.Bludgeoning, 0, false, default(Vector3));
            }

            NpcController.Npc.movementAudio.PlayOneShot(StartOfRound.Instance.hitPlayerSFX);
            if (NpcController.Npc.health < MaxHealthPercent(6))
            {
                NpcController.Npc.DropBlood(hitDirection, true, false);
                NpcController.Npc.bodyBloodDecals[0].SetActive(true);
                NpcController.Npc.playersManager.allPlayerScripts[playerWhoHit].AddBloodToBody();
                NpcController.Npc.playersManager.allPlayerScripts[playerWhoHit].movementAudio.PlayOneShot(StartOfRound.Instance.bloodGoreSFX);
            }
        }

        #endregion*/

        #region Damage bot RPC

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null!, bool playHitSFX = false, int hitID = -1)
        {
            // The HitEnemy function works with player controller instead
            return;
        }

        /*/// <summary>
        /// Sync the damage taken by the lethalBot between server and clients
        /// </summary>
        /// <remarks>
        /// Better to call <see cref="PlayerControllerB.DamagePlayer"><c>PlayerControllerB.DamagePlayer</c></see> so prefixes from other mods can activate. (ex : peepers)
        /// The base game function will be ignored because the lethalBot playerController is not owned because not spawned
        /// </remarks>
        /// <param name="damageNumber"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        /// <param name="fallDamage">Coming from a long fall ?</param>
        /// <param name="force">Force applied to the lethalBot when taking the hit</param>
        public void SyncDamageLethalBot(int damageNumber,
                                     CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                     int deathAnimation = 0,
                                     bool fallDamage = false,
                                     Vector3 force = default)
        {
            Plugin.LogDebug($"SyncDamageLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {NpcController.Npc.playerUsername}");

            if (NpcController.Npc.isPlayerDead)
            {
                return;
            }
            if (!NpcController.Npc.AllowPlayerDeath())
            {
                return;
            }

            if (base.IsServer)
            {
                DamageLethalBotClientRpc(damageNumber, causeOfDeath, deathAnimation, fallDamage, force);
            }
            else
            {
                DamageLethalBotServerRpc(damageNumber, causeOfDeath, deathAnimation, fallDamage, force);
            }
        }

        /// <summary>
        /// Server side, call clients to update and apply the damage taken by the lethalBot
        /// </summary>
        /// <param name="damageNumber"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        /// <param name="fallDamage">Coming from a long fall ?</param>
        /// <param name="force">Force applied to the lethalBot when taking the hit</param>
        [ServerRpc]
        private void DamageLethalBotServerRpc(int damageNumber,
                                           CauseOfDeath causeOfDeath,
                                           int deathAnimation,
                                           bool fallDamage,
                                           Vector3 force)
        {
            DamageLethalBotClientRpc(damageNumber, causeOfDeath, deathAnimation, fallDamage, force);
        }

        /// <summary>
        /// Client side, update and apply the damage taken by the lethalBot
        /// </summary>
        /// <param name="damageNumber"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        /// <param name="fallDamage">Coming from a long fall ?</param>
        /// <param name="force">Force applied to the lethalBot when taking the hit</param>
        [ClientRpc]
        private void DamageLethalBotClientRpc(int damageNumber,
                                           CauseOfDeath causeOfDeath,
                                           int deathAnimation,
                                           bool fallDamage,
                                           Vector3 force)
        {
            DamageLethalBot(damageNumber, causeOfDeath, deathAnimation, fallDamage, force);
        }*/

        /// <summary>
        /// Apply the damage to the lethalBot, kill him if needed, or make critically injured
        /// </summary>
        /// <param name="damageNumber"></param>
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
            Plugin.LogDebug(@$"DamageLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {NpcController.Npc.playerUsername},
                            damageNumber {damageNumber}, causeOfDeath {causeOfDeath}, deathAnimation {deathAnimation}, fallDamage {fallDamage}, force {force}");
            if (!base.IsOwner
                || NpcController.Npc.isPlayerDead
                || !NpcController.Npc.AllowPlayerDeath())
            {
                return;
            }

            // Apply damage, if not killed, set the minimum health to 5
            if (NpcController.Npc.health - damageNumber <= 0
                && !NpcController.Npc.criticallyInjured
                && damageNumber < MaxHealthPercent(50)
                && MaxHealthPercent(10) != MaxHealthPercent(20))
            {
                NpcController.Npc.health = MaxHealthPercent(5);
            }
            else
            {
                NpcController.Npc.health = Mathf.Clamp(NpcController.Npc.health - damageNumber, 0, MaxHealth);
            }

            // Kill lethalBot if necessary
            if (NpcController.Npc.health <= 0)
            {
                // Kill and network death to other players!
                bool spawnBody = deathAnimation != -1;
                NpcController.Npc.KillPlayer(force, spawnBody: spawnBody, causeOfDeath, deathAnimation, positionOffset: default);
            }
            else
            {
                // Critically injured
                if ((NpcController.Npc.health < MaxHealthPercent(10) || NpcController.Npc.health == MaxHealthPercent(5))
                    && !NpcController.Npc.criticallyInjured)
                {
                    // Client side only, since we are already in an rpc send to all clients
                    NpcController.Npc.MakeCriticallyInjured(true);
                }
                else
                {
                    // Limit sprinting when close to death
                    if (damageNumber >= MaxHealthPercent(10))
                    {
                        NpcController.Npc.sprintMeter = Mathf.Clamp(NpcController.Npc.sprintMeter + (float)damageNumber / 125f, 0f, 1f);
                    }
                    if (callRPC)
                    {
                        if (base.IsServer)
                        {
                            NpcController.Npc.DamagePlayerClientRpc(damageNumber, NpcController.Npc.health);
                        }
                        else
                        {
                            NpcController.Npc.DamagePlayerServerRpc(damageNumber, NpcController.Npc.health);
                        }
                    }
                }
                if (fallDamage)
                {
                    NpcController.Npc.movementAudio.PlayOneShot(StartOfRound.Instance.fallDamageSFX, 1f);
                    WalkieTalkie.TransmitOneShotAudio(NpcController.Npc.movementAudio, StartOfRound.Instance.fallDamageSFX);
                    NpcController.Npc.BreakLegsSFXClientRpc();
                }
                else if (hasDamageSFX)
                {
                    NpcController.Npc.movementAudio.PlayOneShot(StartOfRound.Instance.damageSFX, 1f);
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
                    IsLethalBotInside = NpcController.Npc.isInsideFactory,
                    AllowSwearing = Plugin.Config.AllowSwearing.Value
                });
            }

            NpcController.Npc.takingFallDamage = false;
            if (!NpcController.Npc.inSpecialInteractAnimation && !NpcController.Npc.twoHandedAnimation)
            {
                NpcController.Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_DAMAGE);
            }
            NpcController.Npc.specialAnimationWeight = 1f;
            NpcController.Npc.PlayQuickSpecialAnimation(0.7f);
        }

        public void HealthRegen()
        {
            if (NpcController.LimpMultiplier > 0f)
            {
                NpcController.LimpMultiplier -= Time.deltaTime / 1.8f;
            }
            if (NpcController.Npc.health < MaxHealthPercent(20)
                || NpcController.Npc.health == MaxHealthPercent(5))
            {
                if (NpcController.Npc.healthRegenerateTimer <= 0f)
                {
                    NpcController.Npc.healthRegenerateTimer = healthRegenerateTimerMax;
                    NpcController.Npc.health = NpcController.Npc.health + 1 > MaxHealth ? MaxHealth : NpcController.Npc.health + 1;
                    if (NpcController.Npc.criticallyInjured &&
                        (NpcController.Npc.health >= MaxHealthPercent(20) || MaxHealth == 1))
                    {
                        NpcController.Npc.MakeCriticallyInjured(false);
                    }
                }
                else
                {
                    NpcController.Npc.healthRegenerateTimer -= Time.deltaTime;
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
        /// Sync the action to kill lethalBot between server and clients
        /// </summary>
        /// <remarks>
        /// Better to call <see cref="PlayerControllerB.KillPlayer"><c>PlayerControllerB.KillPlayer</c></see> so prefixes from other mods can activate. (ex : peepers)
        /// The base game function will be ignored because this addon blocks the default call to it
        /// </remarks>
        /// <param name="bodyVelocity"></param>
        /// <param name="spawnBody">Should a body be spawned ?</param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        public void SyncKillLethalBot(Vector3 bodyVelocity,
                                   bool spawnBody = true,
                                   CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                   int deathAnimation = 0,
                                   Vector3 positionOffset = default(Vector3))
        {
            Plugin.LogDebug($"SyncKillLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {NpcController.Npc.playerUsername}");

            if (!base.IsOwner 
                || NpcController.Npc.isPlayerDead 
                || !NpcController.Npc.AllowPlayerDeath())
            {
                return;
            }

            if (base.IsServer)
            {
                KillLethalBotSpawnBody(spawnBody, deathAnimation);
                KillLethalBotClientRpc(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset);
            }
            else
            {
                KillLethalBotServerRpc(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset);
            }
        }

        /// <summary>
        /// Server side, call clients to do the action to kill lethalBot
        /// </summary>
        /// <param name="bodyVelocity"></param>
        /// <param name="spawnBody"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        [ServerRpc]
        private void KillLethalBotServerRpc(Vector3 bodyVelocity,
                                         bool spawnBody,
                                         CauseOfDeath causeOfDeath,
                                         int deathAnimation,
                                         Vector3 positionOffset)
        {
            KillLethalBotSpawnBody(spawnBody, deathAnimation);
            KillLethalBotClientRpc(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset);
        }

        /// <summary>
        /// Server side, spawn the ragdoll of the dead body, despawn held object if no dead body to spawn
        /// (lethalBot eaten or disappeared in some way)
        /// </summary>
        /// <param name="spawnBody">Is there a dead body to spawn following the death of the lethalBot ?</param>
        [ServerRpc]
        private void KillLethalBotSpawnBodyServerRpc(bool spawnBody, int deathAnimation)
        {
            KillLethalBotSpawnBody(spawnBody, deathAnimation);
        }

        /// <summary>
        /// Spawn the ragdoll of the dead body, despawn held object if no dead body to spawn
        /// (lethalBot eaten or disappeared in some way)
        /// </summary>
        /// <param name="spawnBody">Is there a dead body to spawn following the death of the lethalBot ?</param>
        private void KillLethalBotSpawnBody(bool spawnBody, int deathAnimation)
        {
            this.ReParentLethalBot(NpcController.Npc.playersManager.playersContainer);
            if (!spawnBody)
            {
                foreach (var grabbableObject in NpcController.Npc.ItemSlots)
                {
                    if (grabbableObject != null)
                    {
                        grabbableObject.gameObject.GetComponent<NetworkObject>().Despawn(true);
                    }
                }
            }
            else if (deathAnimation != 9)
            {
                GameObject gameObject = Object.Instantiate<GameObject>(StartOfRound.Instance.ragdollGrabbableObjectPrefab, NpcController.Npc.playersManager.propsContainer);
                gameObject.GetComponent<NetworkObject>().Spawn(false);
                gameObject.GetComponent<RagdollGrabbableObject>().bodyID.Value = (int)NpcController.Npc.playerClientId;
            }
        }

        /// <summary>
        /// Client side, do the action to kill lethalBot
        /// </summary>
        /// <param name="bodyVelocity"></param>
        /// <param name="spawnBody"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        [ClientRpc]
        private void KillLethalBotClientRpc(Vector3 bodyVelocity,
                                         bool spawnBody,
                                         CauseOfDeath causeOfDeath,
                                         int deathAnimation,
                                         Vector3 positionOffset)
        {


            KillLethalBot(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset);
        }

        /// <summary>
        /// Do the action of killing the lethalBot
        /// </summary>
        /// <param name="bodyVelocity"></param>
        /// <param name="spawnBody"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="deathAnimation"></param>
        private void KillLethalBot(Vector3 bodyVelocity,
                                bool spawnBody,
                                CauseOfDeath causeOfDeath,
                                int deathAnimation,
                                Vector3 positionOffset)
        {
            Plugin.LogDebug(@$"KillLethalBot for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {NpcController.Npc.playerUsername}
                            bodyVelocity {bodyVelocity}, spawnBody {spawnBody}, causeOfDeath {causeOfDeath}, deathAnimation {deathAnimation}, positionOffset {positionOffset}");
            if (NpcController.Npc.isPlayerDead)
            {
                return;
            }
            if (!NpcController.Npc.AllowPlayerDeath())
            {
                return;
            }

            // Mark the bot as dead to the Round Manager
            StartOfRound.Instance.gameStats.deaths++;
            NpcController.Npc.playersManager.livingPlayers--;
            if (NpcController.Npc.playersManager.livingPlayers == 0)
            {
                NpcController.Npc.playersManager.allPlayersDead = true;
                NpcController.Npc.playersManager.ShipLeaveAutomatically();
            }

            // Reset body
            NpcController.Npc.isPlayerDead = true;
            NpcController.Npc.isPlayerControlled = false;
            NpcController.Npc.thisPlayerModelArms.enabled = false;
            NpcController.Npc.localVisor.position = NpcController.Npc.playersManager.notSpawnedPosition.position;
            LethalBotManager.Instance.DisableLethalBotControllerModel(NpcController.Npc.gameObject, NpcController.Npc, enable: false, disableLocalArms: false);
            NpcController.Npc.isInsideFactory = false;
            NpcController.Npc.IsInspectingItem = false;
            if (NpcController.Npc.inTerminalMenu)
            {
                // If we were using the terminal, we should "leave" it so other players can use it!
                LeaveTerminal();
            }
            NpcController.Npc.inTerminalMenu = false;
            NpcController.Npc.twoHanded = false;
            NpcController.Npc.isHoldingObject = false;
            NpcController.Npc.currentlyHeldObjectServer = null;
            NpcController.Npc.carryWeight = 1f;
            NpcController.Npc.fallValue = 0f;
            NpcController.Npc.fallValueUncapped = 0f;
            NpcController.Npc.takingFallDamage = false;
            StopSinkingState();
            NpcController.Npc.sinkingValue = 0f;
            NpcController.Npc.hinderedMultiplier = 1f;
            NpcController.Npc.isMovementHindered = 0;
            NpcController.Npc.inAnimationWithEnemy = null;
            NpcController.Npc.bleedingHeavily = false;
            NpcController.Npc.setPositionOfDeadPlayer = true;
            NpcController.Npc.snapToServerPosition = false;
            NpcController.Npc.causeOfDeath = causeOfDeath;
            AccessTools.Field(typeof(PlayerControllerB), "positionOfDeath").SetValue(NpcController.Npc, NpcController.Npc.transform.position);
            if (spawnBody)
            {
                NpcController.Npc.SpawnDeadBody((int)NpcController.Npc.playerClientId, bodyVelocity, (int)causeOfDeath, NpcController.Npc, deathAnimation, null, positionOffset);
                
                // Sigh, if the death animation is set to 9 the body has a chance to be null!
                if (NpcController.Npc.deadBody != null)
                {
                    ResizeRagdoll(NpcController.Npc.deadBody.transform);
                    // Replace body position or else disappear with shotgun or knife (don't know why)
                    NpcController.Npc.deadBody.transform.position = NpcController.Npc.transform.position + Vector3.up + positionOffset;
                    // Need to be set to true (don't know why) (so many mysteries unsolved tonight)
                    NpcController.Npc.deadBody.canBeGrabbedBackByPlayers = true;
                    this.LethalBotIdentity.DeadBody = NpcController.Npc.deadBody;
                    // Lets make sure the bots don't attempt to grab dead bodies as soon as a player is killed!
                    GrabbableObject? deadBody = NpcController.Npc.deadBody?.grabBodyObject;
                    if (deadBody != null)
                    {
                        DictJustDroppedItems[deadBody] = Time.realtimeSinceStartup;
                    }
                }
                else
                {
                    Plugin.LogWarning($"Bot {NpcController.Npc.playerUsername} dead body was not spawned. This is probably a bug with another mod or the base game itself!");
                }
            }
            NpcController.Npc.physicsParent = null;
            NpcController.Npc.overridePhysicsParent = null;
            NpcController.Npc.lastSyncedPhysicsParent = null;
            NpcController.CurrentLethalBotPhysicsRegions.Clear();
            this.ReParentLethalBot(NpcController.Npc.playersManager.playersContainer);
            SoundManager.Instance.playerVoicePitchTargets[NpcController.Npc.playerClientId] = 1f;
            SoundManager.Instance.playerVoicePitchLerpSpeed[NpcController.Npc.playerClientId] = 3f;
            NpcController.Npc.DropAllHeldItems(spawnBody);
            if (this.State?.TargetItem != null)
            {
                // If the bot died trying to pickup an item, we need to make sure no other bot tries to pick it up!
                // As it may be too dangerous around the item
                DictJustDroppedItems[this.State.TargetItem] = Time.realtimeSinceStartup;
            }
            NpcController.Npc.DisableJetpackControlsLocally();
            NpcController.IsControllerInCruiser = false;
            if (GameNetworkManager.Instance.localPlayerController.isPlayerDead)
            {
                HUDManager.Instance.UpdateBoxesSpectateUI();
            }
            this.isEnemyDead = true;
            this.LethalBotIdentity.Hp = 0;
            SetAgent(enabled: false);
            this.LethalBotIdentity.Voice.StopAudioFadeOut();
            this.State = new BrainDeadState(this);
            Plugin.LogDebug($"Ran kill lethalBot function for LOCAL client #{NetworkManager.LocalClientId}, lethalBot object: Bot #{this.BotId} {NpcController.Npc.playerUsername}");

            // Compat with revive company mod
            if (Plugin.IsModReviveCompanyLoaded)
            {
                ReviveCompanySetPlayerDiedAt((int)NpcController.Npc.playerClientId);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSpawningAnimationRunning()
        {
            return spawnAnimationCoroutine != null;
        }

        public bool IsInSpecialAnimation()
        {
            return NpcController.Npc.inSpecialInteractAnimation || NpcController.Npc.enteringSpecialAnimation;
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
            SyncAssignTargetAndSetMovingTo(GetClosestIrlPlayer());
            //agent.autoTraverseOffMeshLink = false;

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
            SyncAssignTargetAndSetMovingTo(GetClosestIrlPlayer());
            //agent.autoTraverseOffMeshLink = false;

            spawnAnimationCoroutine = null;
            yield break;
        }

        internal PlayerControllerB GetClosestIrlPlayer()
        {
            PlayerControllerB closest = null!;
            float closestDistSqr = float.MaxValue;
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!player.isPlayerControlled
                    || player.isPlayerDead
                    || LethalBotManager.Instance.IsPlayerLethalBot(player) 
                    || (Plugin.IsModLethalInternsLoaded && LethalBotManager.IsPlayerIntern(player)))
                {
                    continue;
                }

                float playerDistSqr = (player.transform.position - this.NpcController.Npc.transform.position).sqrMagnitude;
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
                shipLever = Object.FindObjectOfType<StartMatchLever>();
                if (shipLever == null)
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
            InteractTriggerPatch.UpdateUsedByPlayerServerRpc_ReversePatch(leverTrigger, (int)localPlayerController.playerClientId);
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
            InteractTriggerPatch.StopUsingServerRpc_ReversePatch(leverTrigger, (int)localPlayerController.playerClientId);
            leverTrigger.isPlayingSpecialAnimation = false;
            localPlayerController.inSpecialInteractAnimation = false;
            if ((bool)leverTrigger.overridePlayerParent && localPlayerController.overridePhysicsParent == leverTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = null;
            }
            localPlayerController.currentTriggerInAnimationWith = null;
            leverTrigger.currentCooldownValue = leverTrigger.cooldownTime;
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
            Plugin.LogDebug($"Bot {NpcController.Npc.playerUsername} has successfuly pulled the ship lever to end the round!");

            StartOfRound playersManager = startMatchLever.playersManager;
            if (playersManager.shipHasLanded && !playersManager.shipIsLeaving && !playersManager.shipLeftAutomatically)
            {
                // As much as I would love to do this, most of the class is set to internal making this impossible!
                // Unless I use a lot of hacks.........
                /*if (Plugin.IsModFacilityMeltdownLoaded)
                {
                    MeltdownHandler? meltdown = (MeltdownHandler)AccessTools.Property(typeof(MeltdownHandler), "Instance").GetValue(null);
                    if (meltdown != null && AccessTools.Property(typeof(MeltdownPlugin), "config").GetValue(null).ShortenMeltdownTimerOnShipLeave)
                    {
                        AccessTools.Field(typeof(MeltdownHandler), "meltdownTimer").SetValue(meltdown, 3f);
                    }
                }*/
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
            if (!playersManager.travellingToNewLevel && (!playersManager.inShipPhase || playersManager.connectedPlayersAmount + 1 > 1 || startMatchLever.singlePlayerEnabled))
            {
                if (playersManager.shipHasLanded)
                {
                    PullLeverAnim(startMatchLever, leverPulled: false);
                    PlayLeverPullEffectsServerRpc(startMatchLever.NetworkObject, leverPulled: false, true);
                }
                else if (playersManager.inShipPhase)
                {
                    PullLeverAnim(startMatchLever, leverPulled: true);
                    PlayLeverPullEffectsServerRpc(startMatchLever.NetworkObject, leverPulled: true, true);
                }
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.PullLeverAnim"/>, but with support for bots
        /// </summary>
        /// <param name="startMatchLever"></param>
        /// <param name="leverPulled"></param>
        private void PullLeverAnim(StartMatchLever startMatchLever, bool leverPulled)
        {
            Plugin.LogDebug($"Lever animation: setting bool to {leverPulled}");
            startMatchLever.leverAnimatorObject.SetBool("pullLever", leverPulled);
            startMatchLever.leverHasBeenPulled = leverPulled;
            startMatchLever.triggerScript.interactable = false;
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.PlayLeverPullEffectsServerRpc"/>, but with support for bots
        /// </summary>
        /// <param name="networkObjectReference"></param>
        /// <param name="leverPulled"></param>
        /// <param name="skipOwner"></param>
        [ServerRpc(RequireOwnership = false)]
        public void PlayLeverPullEffectsServerRpc(NetworkObjectReference networkObjectReference, bool leverPulled, bool skipOwner = false)
        {
            PlayLeverPullEffectsClientRpc(networkObjectReference, leverPulled, skipOwner);
        }

        /// <summary>
        /// Carbon copy of <see cref="StartMatchLever.PlayLeverPullEffectsClientRpc"/>, but with support for bots
        /// </summary>
        /// <param name="networkObjectReference"></param>
        /// <param name="leverPulled"></param>
        /// <param name="skipOwner"></param>
        [ClientRpc]
        private void PlayLeverPullEffectsClientRpc(NetworkObjectReference networkObjectReference, bool leverPulled, bool skipOwner = false)
        {
            // Don't call this for the owner
            if (skipOwner && IsOwner)
            {
                return;
            }
            if (networkObjectReference.TryGet(out NetworkObject networkObject, null))
            {
                StartMatchLever startMatchLever = networkObject.GetComponent<StartMatchLever>();
                PullLeverAnim(startMatchLever, leverPulled);
            }
            else
            {
                Plugin.LogError($"PlayLeverPullEffectsClientRpc for client {GameNetworkManager.Instance.localPlayerController.playerUsername}: Unknown to get network object from network object reference (PlayLeverPullEffects RPC)");
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
                itemCharger = Object.FindObjectOfType<ItemCharger>();
                if (itemCharger == null)
                {
                    return false;
                }
            }

            // HACKHACK: We do the logic checks here!
            if (AreHandsFree() 
                || !HeldItem.itemProperties.requiresBattery)
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
            useInteractTriggerCoroutine = StartCoroutine(useItemCharger(itemCharger, itemChargerTrigger, HeldItem));
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
            InteractTriggerPatch.UpdateUsedByPlayerServerRpc_ReversePatch(itemChargerTrigger, (int)localPlayerController.playerClientId);
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
            InteractTriggerPatch.StopUsingServerRpc_ReversePatch(itemChargerTrigger, (int)localPlayerController.playerClientId);
            itemChargerTrigger.isPlayingSpecialAnimation = false;
            localPlayerController.inSpecialInteractAnimation = false;
            if ((bool)itemChargerTrigger.overridePlayerParent && localPlayerController.overridePhysicsParent == itemChargerTrigger.overridePlayerParent)
            {
                localPlayerController.overridePhysicsParent = null;
            }
            localPlayerController.currentTriggerInAnimationWith = null;
            itemChargerTrigger.currentCooldownValue = itemChargerTrigger.cooldownTime;
            itemChargerTrigger.StopSpecialAnimation();
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
                PlayerControllerBPatch.PlayJumpAudio_ReversePatch(this.NpcController.Npc);
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

        #region Sinking RPC

        /// <summary>
        /// Sync the state of sink of the lethalBot between server and clients
        /// </summary>
        /// <param name="startSinking"></param>
        /// <param name="sinkingSpeed"></param>
        /// <param name="audioClipIndex"></param>
        public void SyncChangeSinkingState(bool startSinking, float sinkingSpeed = 0f, int audioClipIndex = 0)
        {
            if (IsServer)
            {
                ChangeSinkingStateClientRpc(startSinking, sinkingSpeed, audioClipIndex);
            }
            else
            {
                ChangeSinkingStateServerRpc(startSinking, sinkingSpeed, audioClipIndex);
            }
        }

        /// <summary>
        /// Server side, call clients to update the state of sink of the lethalBot
        /// </summary>
        /// <param name="startSinking"></param>
        /// <param name="sinkingSpeed"></param>
        /// <param name="audioClipIndex"></param>
        [ServerRpc]
        private void ChangeSinkingStateServerRpc(bool startSinking, float sinkingSpeed, int audioClipIndex)
        {
            ChangeSinkingStateClientRpc(startSinking, sinkingSpeed, audioClipIndex);
        }

        /// <summary>
        /// Client side, update the state of sink of the lethalBot
        /// </summary>
        /// <param name="startSinking"></param>
        /// <param name="sinkingSpeed"></param>
        /// <param name="audioClipIndex"></param>
        [ClientRpc]
        private void ChangeSinkingStateClientRpc(bool startSinking, float sinkingSpeed, int audioClipIndex)
        {
            if (startSinking)
            {
                NpcController.Npc.sinkingSpeedMultiplier = sinkingSpeed;
                NpcController.Npc.isSinking = true;
                NpcController.Npc.statusEffectAudio.clip = StartOfRound.Instance.statusEffectClips[audioClipIndex];
                NpcController.Npc.statusEffectAudio.Play();
            }
            else
            {
                StopSinkingState();
            }
        }

        public void StopSinkingState()
        {
            NpcController.Npc.isSinking = false;
            NpcController.Npc.statusEffectAudio.Stop();
            NpcController.Npc.voiceMuffledByEnemy = false;
            NpcController.Npc.sourcesCausingSinking = 0;
            NpcController.Npc.isMovementHindered = 0;
            NpcController.Npc.hinderedMultiplier = 1f;

            NpcController.Npc.isUnderwater = false;
            NpcController.Npc.underwaterCollider = null;
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
        [ServerRpc]
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
        public void ChangeSuitLethalBotServerRpc(ulong idLethalBotController, int suitID)
        {
            ChangeSuitLethalBotClientRpc(idLethalBotController, suitID);
        }

        [ClientRpc]
        private void ChangeSuitLethalBotClientRpc(ulong idLethalBotController, int suitID)
        {
            ChangeSuitLethalBot(idLethalBotController, suitID, playAudio: true);
        }

        public void ChangeSuitLethalBot(ulong idLethalBotController, int suitID, bool playAudio = false)
        {
            if (suitID > StartOfRound.Instance.unlockablesList.unlockables.Count())
            {
                suitID = 0;
            }

            PlayerControllerB lethalBotController = StartOfRound.Instance.allPlayerScripts[idLethalBotController];

            UnlockableSuit.SwitchSuitForPlayer(lethalBotController, suitID, playAudio);
            lethalBotController.thisPlayerModelArms.enabled = false;
            StartCoroutine(Wait2EndOfFrameToRefreshBillBoard());
            LethalBotIdentity.SuitID = suitID;

            Plugin.LogDebug($"Changed suit of lethalBot {NpcController.Npc.playerUsername} to {suitID}: {StartOfRound.Instance.unlockablesList.unlockables[suitID].unlockableName}");
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

		private long timer = 200 * TimeSpan.TicksPerMillisecond;
		private long lastTimeCalculate;

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

		private bool NeedToRecalculate()
		{
			long elapsedTime = DateTime.Now.Ticks - lastTimeCalculate;
			if (elapsedTime > timer)
			{
				lastTimeCalculate = DateTime.Now.Ticks;
				return true;
			}
            // BUGBUG: We always recalulate if we are not on the ground.
            // We have issues if we don't and could fall out of the map!
            else if (!isTouchingGround)
            {
                return true;
            }
            else
            {
                return false;
            }
		}

		private void CalculateTouchingGround(Vector3 lethalBotPosition)
		{
			isTouchingGround = Physics.Raycast(new Ray(lethalBotPosition + Vector3.up, Vector3.down),
											   out groundHit,
											   2.5f,
											   StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
		}
	}

	public class TimedAngleFOVWithLocalPlayerCheck
	{
		private float angle;

		private long timer = 50 * TimeSpan.TicksPerMillisecond;
		private long lastTimeCalculate;

		public float GetAngleFOVWithLocalPlayer(Transform localPlayerCameraTransform, Vector3 lethalBotBodyPos)
		{
			if (!NeedToRecalculate())
			{
				return angle;
			}

			CalculateAngleFOVWithLocalPlayer(localPlayerCameraTransform, lethalBotBodyPos);
			return angle;
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

		private void CalculateAngleFOVWithLocalPlayer(Transform localPlayerCameraTransform, Vector3 lethalBotBodyPos)
		{
			angle = Vector3.Angle(localPlayerCameraTransform.forward, lethalBotBodyPos - localPlayerCameraTransform.position);
		}
	}
}