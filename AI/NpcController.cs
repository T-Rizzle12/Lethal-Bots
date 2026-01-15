using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.NetworkSerializers;
using LethalBots.Patches.NpcPatches;
using LethalBots.Utils;
using LethalInternship.AI;
using ModelReplacement;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TooManyEmotes;
using TooManyEmotes.Networking;
using Unity.IO.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace LethalBots.AI
{
    public class NpcController
    {
        public PlayerControllerB Npc { get; set; } = null!;

        // TODO: Create patches to PlayerPhysicsRegion to make them work with the bots
        public List<PlayerPhysicsRegion> CurrentLethalBotPhysicsRegions = new List<PlayerPhysicsRegion>();

        public bool HasToMove { private set; get; }
        public bool IsControllerInCruiser;
        public TimedSqrDistanceWithLocalPlayerCheck SqrDistanceWithLocalPlayerTimedCheck = null!;
        public TimedGetBounds GetBoundsTimedCheck = null!;
        public TimedUpdateBillboardLookAtCheck UpdateBillBoardLookAtTimedCheck = null!;

        // Public variables to pass to patch
        public bool IsCameraDisabled;
        public bool IsJumping;
        public bool IsFallingFromJump;
        public float CrouchMeter;
        public bool IsWalking;
        public float PlayerSlidingTimer;
        public float BloodDropTimer;
        public float LimpMultiplier = 0.2f;
        public bool DisabledJetpackControlsThisFrame;
        public bool StartedJetpackControls;
        public float UpperBodyAnimationsWeight;
        public Vector3 RightArmProceduralTargetBasePosition;
        public float TimeSinceSwitchingSlots;
        public float TimeSinceTakingGravityDamage;
        public bool TeleportingThisFrame;
        public float PreviousFrameDeltaTime;
        public float CameraUp;

        //Audio
        public OccludeAudio OccludeAudioComponent = null!;
        public AudioLowPassFilter AudioLowPassFilterComponent = null!;
        public AudioHighPassFilter AudioHighPassFilterComponent = null!;

        public Vector3 MoveVector;
        public bool UpdatePositionForNewlyJoinedClient;
        public bool GrabbedObjectValidated;
        public float UpdatePlayerLookInterval;
        public int PlayerMask;
        public bool IsTouchingGround;
        public EnemyAI? EnemyInAnimationWith;
        public bool ShouldAnimate;
        public Vector3 NearEntitiesPushVector;

        private LethalBotAI LethalBotAIController
        {
            get
            {
                if (_lethalBotAIController == null)
                {
                    _lethalBotAIController = LethalBotManager.Instance.GetLethalBotAI(Npc);
                    if (_lethalBotAIController == null)
                    {
                        throw new NullReferenceException($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION}: error no lethalBotAI attached to NpcController playerClientId {Npc.playerClientId}.");
                    }
                }
                return _lethalBotAIController;
            }
        }
        private LethalBotAI? _lethalBotAIController;

        private int movementHinderedPrev;
        private float sprintMultiplier = 1f;
        public bool WaitForFullStamina { get; private set; }
        //private float slopeModifier; // ignore for now
        private Vector3 walkForce;
        private bool isFallingNoJump;
        private int previousFootstepClip;

        private Dictionary<string, bool> dictAnimationBoolPerItem = null!;

        private float exhaustionEffectLerp;
        private bool disabledJetpackControlsThisFrame;

        private bool wasUnderwaterLastFrame;
        public float DrowningTimer { private set; get; } = 1f;
        private bool setFaceUnderwater;
        private float syncUnderwaterInterval;

        private LookAtTarget oldLookAtTarget = new LookAtTarget();
        public LookAtTarget LookAtTarget { private set; get; } = new LookAtTarget();

        private Vector3 lastDirectionToLookAt;
        private Quaternion cameraRotationToUpdateLookAt;

        public Vector2 lastMoveVector;
        private float floatSprint;
        private bool goDownLadder;

        private int[] animationHashLayers = null!;
        private List<int> currentAnimationStateHash = null!;
        private List<int> previousAnimationStateHash = null!;
        private float updatePlayerAnimationsInterval;
        private float currentAnimationSpeed;
        private float previousAnimationSpeed;

        private float timerPlayFootstep;

        public NpcController(PlayerControllerB npc)
        {
            this.Npc = npc;
            Init();
        }

        /// <summary>
        /// Initialize the <c>PlayerControllerB</c>
        /// </summary>
        public void Awake()
        {
            //Plugin.LogDebug("Awake bot controller.");
            Init();

            PatchesUtil.FieldInfoPreviousAnimationStateHash.SetValue(Npc, new List<int>(new int[Npc.playerBodyAnimator.layerCount]));
            PatchesUtil.FieldInfoCurrentAnimationStateHash.SetValue(Npc, new List<int>(new int[Npc.playerBodyAnimator.layerCount]));
        }

        private void Init()
        {
            Npc.isHostPlayerObject = false;
            Npc.serverPlayerPosition = Npc.transform.position;
            Npc.gameplayCamera.enabled = false;
            Npc.visorCamera.enabled = false;
            Npc.thisPlayerModel.enabled = true;
            Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
            Npc.thisPlayerModelArms.enabled = false;

            this.IsCameraDisabled = true;
            Npc.sprintMeter = 1f;
            Npc.ItemSlots = new GrabbableObject[4];
            RightArmProceduralTargetBasePosition = Npc.rightArmProceduralTarget.localPosition;

            Npc.usernameBillboardText.text = Npc.playerUsername;
            Npc.usernameAlpha.alpha = 1f;
            Npc.usernameCanvas.gameObject.SetActive(true);

            Npc.previousElevatorPosition = Npc.playersManager.elevatorTransform.position;
            if (Npc.gameObject.GetComponent<Rigidbody>())
            {
                Npc.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
            }
            Npc.gameObject.GetComponent<CharacterController>().enabled = true;

            AudioReverbPresets audioReverbPresets = UnityEngine.Object.FindObjectOfType<AudioReverbPresets>();
            if ((bool)audioReverbPresets)
            {
                audioReverbPresets.audioPresets[3].ChangeAudioReverbForPlayer(Npc);
            }

            foreach (var skinnedMeshRenderer in Npc.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skinnedMeshRenderer.updateWhenOffscreen = false;
            }

            animationHashLayers = new int[Npc.playerBodyAnimator.layerCount];
            currentAnimationStateHash = new List<int>(new int[Npc.playerBodyAnimator.layerCount]);
            previousAnimationStateHash = new List<int>(new int[Npc.playerBodyAnimator.layerCount]);

            GetBoundsTimedCheck = new TimedGetBounds();
            SqrDistanceWithLocalPlayerTimedCheck = new TimedSqrDistanceWithLocalPlayerCheck();
            UpdateBillBoardLookAtTimedCheck = new TimedUpdateBillboardLookAtCheck();
        }

        /// <summary>
        /// Update called from <see cref="PlayerControllerBPatch.Update_PreFix"><c>PlayerControllerBPatch.Update_PreFix</c></see> 
        /// instead of the real update from <c>PlayerControllerB</c>.
        /// </summary>
        /// <remarks>
        /// Update the move vector in regard of the field set with the order methods,<br/>
        /// update the movement of the <c>PlayerControllerB</c> against various hazards,<br/>
        /// while sinking, drowning, jumping, falling, in jetpack, in special interaction with enemies.<br/>
        /// Sync the rotation with other clients.
        /// </remarks>
        public void Update()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            // The owner of the bot (and the controller)
            // updates and moves the controller
            if (LethalBotAIController.IsClientOwnerOfLethalBot() && Npc.isPlayerControlled)
            {
                // Updates the state of the CharacterController and the animator controller
                UpdateOwnerChanged(true);

                Npc.rightArmProceduralRig.weight = Mathf.Lerp(Npc.rightArmProceduralRig.weight, 0f, 25f * Time.deltaTime);

                // Set the move input vector for moving the controller
                UpdateMoveInputVectorForOwner();

                // Force turn if needed
                ForceTurnTowardsTarget();

                // Turn the body towards the direction set beforehand
                UpdateTurnBodyTowardsDirection();

                // Manage the drowning state of the bot
                SetFaceUnderwaterFilters();

                // Update the animation of walking under numerous conditions
                UpdateWalkingStateForOwner();

                // Sync with clients if the bot is performing emote
                UpdateEmoteStateForOwner();

                // Update and sync with clients, if the bot is sinking or not and should die or not
                UpdateSinkingStateForOwner();

                // Update the center and the height of the <c>CharacterController</c>
                UpdateCenterAndHeightForOwner();

                // Update the rotation of the controller when using jetpack controls
                UpdateJetPackControlsForOwner();

                if (!Npc.inSpecialInteractAnimation || Npc.inShockingMinigame || instanceSOR.suckingPlayersOutOfShip)
                {
                    // Move the body of bot
                    UpdateMoveControllerForOwner();

                    // Check if the bot is falling and update values accordingly
                    UpdateFallValuesForOwner();

                    Npc.externalForces = Vector3.zero;
                    if (!TeleportingThisFrame && Npc.teleportedLastFrame)
                    {
                        Npc.ResetFallGravity();
                        Npc.teleportedLastFrame = false;
                    }

                    // Update movement when using jetpack controls
                    UpdateJetPackMoveValuesForOwner();
                }
                else if (Npc.isClimbingLadder)
                {
                    // Update movement when using ladder
                    UpdateMoveWhenClimbingLadder();
                }
                TeleportingThisFrame = false;

                // Rotations
                this.UpdateLookAt();

                Npc.playerEye.position = Npc.gameplayCamera.transform.position;
                Npc.playerEye.rotation = Npc.gameplayCamera.transform.rotation;

                // Update UpdatePlayerLookInterval
                if (NetworkManager.Singleton != null && Npc.playersManager.connectedPlayersAmount > 0)
                {
                    this.UpdatePlayerLookInterval += Time.deltaTime;
                }

                // Update animations
                UpdateAnimationsForOwner();
            }
            else // If not owner, the client just update the position and rotation of the controller
            {
                // Updates the state of the CharacterController and the animator controller
                UpdateOwnerChanged(false);

                // Sync position and rotations
                UpdateSyncPositionAndRotationForNotOwner();

                // Update animations
                UpdateLethalBotAnimationsLocalForNotOwner(animationHashLayers);
            }

            this.TimeSinceSwitchingSlots += Time.deltaTime;
            Npc.timeSincePlayerMoving += Time.deltaTime;
            Npc.timeSinceMakingLoudNoise += Time.deltaTime;
            Npc.timeSinceFearLevelUp += Time.deltaTime;

            // Update the localarms and rotation when in special interact animation
            UpdateInSpecialInteractAnimationEffect();

            // Update animation layer when using emotes
            UpdateEmoteEffects();

            // Update the sinking values and effect
            UpdateSinkingEffects();

            // Update the active audio reverb filter
            UpdateActiveAudioReverbFilter();

            // Update animations when holding items and exhausion
            UpdateAnimationUpperBody();

            // Update the bleed effects
            UpdateBleedEffects();

            // Update our stamina status
            UpdateStaminaTimer();

            // Update drunkness effects
            UpdateDrunknessEffects();

            // Update our player sanity
            PlayerControllerBPatch.SetPlayerSanityLevel_ReversePatch(Npc);

            // Play footstep sounds if we are not currently animating
            PlayFootstepIfCloseNoAnimation();
        }

        /// <summary>
        /// Updates the state of the <c>CharacterController</c>
        /// and update the animator controller with its animation
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateOwnerChanged(bool isOwner)
        {
            if (isOwner)
            {
                if (IsCameraDisabled)
                {
                    IsCameraDisabled = false;
                    Npc.gameplayCamera.enabled = false;
                    Npc.visorCamera.enabled = false;
                    Npc.thisPlayerModelArms.enabled = false;
                    Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    Npc.mapRadarDirectionIndicator.enabled = false;
                    Npc.thisController.enabled = true;
                    Npc.activeAudioReverbFilter = Npc.activeAudioListener.GetComponent<AudioReverbFilter>();
                    Npc.activeAudioReverbFilter.enabled = true;
                    // BUGBUG: This code creates issues where the audio follows the bot rather than the local player's camera.
                    /*Npc.activeAudioListener.transform.SetParent(Npc.gameplayCamera.transform);
                    Npc.activeAudioListener.transform.localEulerAngles = Vector3.zero;
                    Npc.activeAudioListener.transform.localPosition = Vector3.zero;*/
                    UpdateRuntimeAnimatorController(isOwner);
                }
                PlayerControllerBPatch.SetNightVisionEnabled_ReversePatch(Npc, true);
            }
            else
            {
                if (!this.IsCameraDisabled)
                {
                    this.IsCameraDisabled = true;
                    Npc.gameplayCamera.enabled = false;
                    Npc.visorCamera.enabled = false;
                    Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    Npc.thisPlayerModelArms.enabled = false;
                    Npc.mapRadarDirectionIndicator.enabled = false;
                    UpdateRuntimeAnimatorController(isOwner);
                    Npc.thisController.enabled = false;
                    if (Npc.gameObject.GetComponent<Rigidbody>())
                    {
                        Npc.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
                    }
                }
                PlayerControllerBPatch.SetNightVisionEnabled_ReversePatch(Npc, true);
            }
        }

        /// <summary>
        /// Updates the animator controller if the owner of bot has changed
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateRuntimeAnimatorController(bool isOwner)
        {
            // Save animations states
            AnimatorStateInfo[] layerInfo = new AnimatorStateInfo[Npc.playerBodyAnimator.layerCount];
            for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                layerInfo[i] = Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i);
            }

            // Change runtimeAnimatorController
            if (isOwner)
            {
                if (Npc.playerBodyAnimator.runtimeAnimatorController != Npc.playersManager.localClientAnimatorController)
                {
                    Npc.playerBodyAnimator.runtimeAnimatorController = Npc.playersManager.localClientAnimatorController;
                    if (!Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(5).IsTag("notInSpecialAnim"))
                    {
                        Npc.playerBodyAnimator.SetTrigger("SA_stopAnimation");
                    }
                }
            }
            else
            {
                if (Npc.playerBodyAnimator.runtimeAnimatorController != Npc.playersManager.otherClientsAnimatorController)
                {
                    Npc.playerBodyAnimator.runtimeAnimatorController = Npc.playersManager.otherClientsAnimatorController;
                }
            }

            // Push back animations states
            for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                if (Npc.playerBodyAnimator.HasState(i, layerInfo[i].fullPathHash))
                {
                    Npc.playerBodyAnimator.CrossFadeInFixedTime(layerInfo[i].fullPathHash, 0.1f);
                }
            }

            if (dictAnimationBoolPerItem != null)
            {
                foreach (var animationBool in dictAnimationBoolPerItem)
                {
                    Npc.playerBodyAnimator.SetBool(animationBool.Key, animationBool.Value);
                }
            }
        }

        #region Updates npc body for owner

        /// <summary>
        /// Set the move input vector for moving the controller
        /// </summary>
        /// <remarks>
        /// Basically the controller move forward and the rotation is changed in another method if needed (following the AI).
        /// </remarks>
        private void UpdateMoveInputVectorForOwner()
        {
            if (!HasToMove)
            {
                lastMoveVector = Npc.moveInputVector;
                Npc.moveInputVector = Vector2.zero;
                return;
            }

            // Get direction from current position to NavMeshAgent's steering target
            Vector3 worldDir = (LethalBotAIController.agent.steeringTarget - Npc.thisController.transform.position);
            worldDir.y = 0f; // Ignore vertical movement

            // Convert to local space (relative to the bot's forward direction)
            Vector3 localDir = Npc.thisController.transform.InverseTransformDirection(worldDir.normalized);

            // Set moveInputVector (X = sideways, Z = forward)
            lastMoveVector = Npc.moveInputVector;
            Npc.moveInputVector = new Vector2(localDir.x, localDir.z);
            Npc.moveInputVector.Normalize();
        }

        /// <summary>
        /// Update the animation of walking under numerous conditions
        /// </summary>
        private void UpdateWalkingStateForOwner()
        {
            if (IsWalking)
            {
                if (Npc.moveInputVector.sqrMagnitude <= 0.19f
                    || (Npc.inSpecialInteractAnimation && !Npc.isClimbingLadder && !Npc.inShockingMinigame))
                {
                    StopAnimations();
                }
                else if (floatSprint > 0.3f
                            && movementHinderedPrev <= 0
                            && !Npc.criticallyInjured
                            && Npc.sprintMeter > 0.1f)
                {
                    if (!Npc.isSprinting && Npc.sprintMeter < 0.3f)
                    {
                        if (!Npc.isExhausted)
                        {
                            Npc.isExhausted = true;
                        }
                    }
                    else
                    {
                        if (Npc.isCrouching && (!Plugin.Config.FollowCrouchWithPlayer 
                            || LethalBotAIController.targetPlayer == null 
                            || !LethalBotAIController.IsFollowingTargetPlayer()))
                        {
                            Npc.Crouch(false);
                        }

                        if (!Npc.isCrouching)
                        {
                            Npc.isSprinting = true;
                        }
                    }
                }
                else
                {
                    Npc.isSprinting = false;
                    if (Npc.sprintMeter < 0.1f)
                    {
                        Npc.isExhausted = true;
                    }
                }

                if (Npc.isSprinting)
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 2.25f, Time.deltaTime * 1f);
                }
                else
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 1f, 10f * Time.deltaTime);
                }

                if (Npc.moveInputVector.y < 0.2f && Npc.moveInputVector.y > -0.2f && !Npc.inSpecialInteractAnimation)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, true);
                }
                else
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
                }
                if (Npc.enteringSpecialAnimation)
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                else if (Npc.moveInputVector.y < 0.5f && Npc.moveInputVector.x < 0.5f)
                {
                    //Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, -1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, -1f);
                }
                else
                {
                    //Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
            }
            else
            {
                if (Npc.enteringSpecialAnimation)
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                if (Npc.moveInputVector.sqrMagnitude >= 0.001f && (!Npc.inSpecialInteractAnimation || Npc.isClimbingLadder || Npc.inShockingMinigame))
                {
                    IsWalking = true;
                }
            }

            if (Npc.isClimbingLadder)
            {
                Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 2f);
            }
        }

        /// <summary>
        /// Sync with clients if the bot is performing emote
        /// </summary>
        private void UpdateEmoteStateForOwner()
        {
            if (Npc.performingEmote)
            {
                if (this.Npc.inSpecialInteractAnimation
                    || this.Npc.isPlayerDead
                    || this.Npc.isCrouching
                    || this.Npc.isClimbingLadder
                    || this.Npc.isGrabbingObjectAnimation
                    || this.Npc.inTerminalMenu
                    || this.Npc.isTypingChat)
                {
                    Npc.performingEmote = false;
                    this.LethalBotAIController.SyncStopPerformingEmote();
                }
            }
        }

        /// <summary>
        /// Update and sync with clients, if the bot is sinking or not and should die or not
        /// </summary>
        private void UpdateSinkingStateForOwner()
        {
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_HINDEREDMOVEMENT, Npc.isMovementHindered > 0);
            if (Npc.sourcesCausingSinking == 0)
            {
                if (Npc.isSinking)
                {
                    Npc.isSinking = false;
                    this.LethalBotAIController.SyncChangeSinkingState(false);
                }
            }
            else
            {
                if (Npc.isSinking)
                {
                    Npc.GetCurrentMaterialStandingOn();
                    if (!CheckConditionsForSinkingInQuicksandLethalBot())
                    {
                        Npc.isSinking = false;
                        this.LethalBotAIController.SyncChangeSinkingState(false);
                    }
                }
                else if (!Npc.isSinking && CheckConditionsForSinkingInQuicksandLethalBot())
                {
                    Npc.isSinking = true;
                    this.LethalBotAIController.SyncChangeSinkingState(true, Npc.sinkingSpeedMultiplier, Npc.statusEffectAudioIndex);
                }
                if (Npc.sinkingValue >= 1f)
                {
                    Plugin.LogDebug($"SyncKillLethalBot from sinkingValue for LOCAL client #{Npc.NetworkManager.LocalClientId}, lethalBot object: Bot #{Npc.playerClientId}");
                    Npc.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Suffocation, 0, default);
                }
                else if (Npc.sinkingValue > 0.5f)
                {
                    Npc.Crouch(false);
                }
            }
        }

        /// <summary>
        /// Update the center and the height of the <c>CharacterController</c>
        /// </summary>
        private void UpdateCenterAndHeightForOwner()
        {
            if (Npc.isCrouching)
            {
                Npc.thisController.center = Vector3.Lerp(Npc.thisController.center, new Vector3(Npc.thisController.center.x, 0.72f, Npc.thisController.center.z), 8f * Time.deltaTime);
                Npc.thisController.height = Mathf.Lerp(Npc.thisController.height, 1.5f, 8f * Time.deltaTime);
            }
            else
            {
                CrouchMeter = Mathf.Max(CrouchMeter - Time.deltaTime * 2f, 0f);
                Npc.thisController.center = Vector3.Lerp(Npc.thisController.center, new Vector3(Npc.thisController.center.x, 1.28f, Npc.thisController.center.z), 8f * Time.deltaTime);
                Npc.thisController.height = Mathf.Lerp(Npc.thisController.height, 2.5f, 8f * Time.deltaTime);
            }
            // We update the radius of the controller to match the bot's radius
            // NEEDTOVALIDATE: Should I also update the height of the controller?
            // I run into the potential issue of where the bot is too tall and fails to path through some areas!
            LethalBotAIController.agent.radius = Npc.thisController.radius;
            //LethalBotAIController.agent.height = 1.5f; // For now set the crouched height! // Not used for now!
        }

        /// <summary>
        /// Update the rotation of the controller when using jetpack controls
        /// </summary>
        private void UpdateJetPackControlsForOwner()
        {
            if (this.disabledJetpackControlsThisFrame)
            {
                this.disabledJetpackControlsThisFrame = false;
            }
            if (Npc.jetpackControls)
            {
                if (Npc.disablingJetpackControls && IsTouchingGround)
                {
                    this.disabledJetpackControlsThisFrame = true;
                    this.LethalBotAIController.SyncDisableJetpackMode();
                }
                else if (!IsTouchingGround)
                {
                    if (!this.StartedJetpackControls)
                    {
                        this.StartedJetpackControls = true;
                        Npc.jetpackTurnCompass.rotation = Npc.transform.rotation;
                    }
                    Npc.thisController.radius = Mathf.Lerp(Npc.thisController.radius, 1.25f, 10f * Time.deltaTime);
                    Quaternion rotation = Npc.jetpackTurnCompass.rotation;
                    Npc.jetpackTurnCompass.Rotate(new Vector3(0f, 0f, -Npc.moveInputVector.x) * (180f * Time.deltaTime), Space.Self);
                    if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                    {
                        Npc.jetpackTurnCompass.rotation = rotation;
                    }
                    rotation = Npc.jetpackTurnCompass.rotation;
                    Npc.jetpackTurnCompass.Rotate(new Vector3(Npc.moveInputVector.y, 0f, 0f) * (180f * Time.deltaTime), Space.Self);
                    if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                    {
                        Npc.jetpackTurnCompass.rotation = rotation;
                    }
                    if (Npc.jetpackRandomIntensity != -1f)
                    {
                        rotation = Npc.jetpackTurnCompass.rotation;
                        Vector3 a2 = new Vector3(
                            Mathf.Clamp(
                                Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity),
                            -Npc.maxJetpackAngle, Npc.maxJetpackAngle),
                            Mathf.Clamp(
                                Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity), -Npc.maxJetpackAngle, Npc.maxJetpackAngle),
                            Mathf.Clamp(Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity), -Npc.maxJetpackAngle, Npc.maxJetpackAngle));
                        Npc.jetpackTurnCompass.Rotate(a2 * Time.deltaTime, Space.Self);
                        if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                        {
                            Npc.jetpackTurnCompass.rotation = rotation;
                        }
                    }
                    Npc.transform.rotation = Quaternion.Slerp(Npc.transform.rotation, Npc.jetpackTurnCompass.rotation, 8f * Time.deltaTime);
                }
            }
        }

        /// <summary>
        /// Move the body of bot
        /// </summary>
        private void UpdateMoveControllerForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (Npc.isFreeCamera)
            {
                Npc.moveInputVector = Vector2.zero;
            }
            float num3 = Npc.movementSpeed / Npc.carryWeight;
            if (Npc.sinkingValue > 0.73f)
            {
                num3 = 0f;
            }
            else
            {
                if (Npc.isCrouching)
                {
                    num3 /= 1.5f;
                }
                else if (Npc.criticallyInjured && !Npc.isCrouching)
                {
                    //Plugin.LogDebug($"Bot {Npc.playerUsername} Limp Multiplier: {LimpMultiplier}");
                    num3 *= LimpMultiplier;
                }
                if (Npc.isSpeedCheating)
                {
                    num3 *= 15f;
                }
                if (movementHinderedPrev > 0)
                {
                    num3 /= 2f * Npc.hinderedMultiplier;
                }
                if (Npc.drunkness > 0f)
                {
                    num3 *= instanceSOR.drunknessSpeedEffect.Evaluate(Npc.drunkness) / 5f + 1f;
                }
                if (!Npc.isCrouching && CrouchMeter > 1.2f)
                {
                    num3 *= 0.5f;
                }
            }
            if (Npc.isTypingChat || Npc.jetpackControls && !IsTouchingGround || instanceSOR.suckingPlayersOutOfShip)
            {
                Npc.moveInputVector = Vector2.zero;
            }

            float num7 = 1f;
            if (IsFallingFromJump || isFallingNoJump)
            {
                num7 = 1.33f;
            }
            else if (Npc.drunkness > 0.3f)
            {
                num7 = Mathf.Clamp(Mathf.Abs(Npc.drunkness - 2.25f), 0.3f, 2.5f);
            }
            else if (!Npc.isCrouching && CrouchMeter > 1f)
            {
                num7 = 15f;
            }
            else if (Npc.isSprinting)
            {
                num7 = 5f / (Npc.carryWeight * 1.5f);
            }
            else
            {
                num7 = 10f / Npc.carryWeight;
            }
            walkForce = Vector3.MoveTowards(walkForce, Npc.transform.right * Npc.moveInputVector.x + Npc.transform.forward * Npc.moveInputVector.y, num7 * Time.deltaTime);
            Vector3 vector2 = walkForce * num3 * sprintMultiplier + new Vector3(0f, Npc.fallValue, 0f) + NearEntitiesPushVector;
            vector2 += Npc.externalForces;
            if (Npc.externalForceAutoFade.magnitude > 0.05f)
            {
                vector2 += Npc.externalForceAutoFade;
                Npc.externalForceAutoFade = Vector3.Lerp(Npc.externalForceAutoFade, Vector3.zero, 2f * Time.deltaTime);
            }

            PlayerSlidingTimer = 0f;
            NearEntitiesPushVector = Vector3.zero;

            // Move
            MoveVector = vector2;
        }

        /// <summary>
        /// Check if the bot is falling and update values accordingly
        /// </summary>
        private void UpdateFallValuesForOwner()
        {
            if (Npc.inSpecialInteractAnimation && !Npc.inShockingMinigame)
            {
                return;
            }

            if (!IsTouchingGround)
            {
                if (Npc.jetpackControls && !Npc.disablingJetpackControls)
                {
                    Npc.fallValue = Mathf.MoveTowards(Npc.fallValue, Npc.jetpackCounteractiveForce, 9f * Time.deltaTime);
                    Npc.fallValueUncapped = -8f;
                }
                else
                {
                    Npc.fallValue = Mathf.Clamp(Npc.fallValue - 38f * Time.deltaTime, -150f, Npc.jumpForce);
                    if (Mathf.Abs(Npc.externalForceAutoFade.y) - Mathf.Abs(Npc.fallValue) < 5f)
                    {
                        if (Npc.disablingJetpackControls)
                        {
                            Npc.fallValueUncapped -= 26f * Time.deltaTime;
                        }
                        else
                        {
                            Npc.fallValueUncapped -= 38f * Time.deltaTime;
                        }
                    }
                }
                if (!IsJumping && !IsFallingFromJump)
                {
                    if (!isFallingNoJump)
                    {
                        isFallingNoJump = true;
                        //Plugin.LogDebug($"{Npc.playerUsername} isFallingNoJump true");
                        Npc.fallValue = -7f;
                        Npc.fallValueUncapped = -7f;
                    }
                    else if (Npc.fallValue < -20f)
                    {
                        Npc.isCrouching = false;
                        Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CROUCHING, false);
                        Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, true);
                    }
                }
                if (Npc.fallValueUncapped < -35f)
                {
                    Npc.takingFallDamage = true;
                }
            }
            else
            {
                movementHinderedPrev = Npc.isMovementHindered;
                if (!IsJumping)
                {
                    if (isFallingNoJump)
                    {
                        isFallingNoJump = false;
                        if (!Npc.isCrouching && Npc.fallValue < -9f)
                        {
                            Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_SHORTFALLLANDING);
                        }
                        //Plugin.LogDebug($"{Npc.playerUsername} JustTouchedGround fallValue {Npc.fallValue}");
                        PlayerControllerBPatch.PlayerHitGroundEffects_ReversePatch(this.Npc);
                    }
                    //if (!IsFallingFromJump)
                    //{
                    //    Npc.fallValue = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                    //    Npc.fallValueUncapped = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                    //}
                }
                Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, false);
            }
        }

        /// <summary>
        /// Update movement when using jetpack controls
        /// </summary>
        private void UpdateJetPackMoveValuesForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (Npc.jetpackControls || Npc.disablingJetpackControls)
            {
                if (!this.TeleportingThisFrame && !Npc.inSpecialInteractAnimation && !Npc.enteringSpecialAnimation && !Npc.isClimbingLadder && (instanceSOR.timeSinceRoundStarted > 1f || instanceSOR.testRoom != null))
                {
                    float magnitude2 = Npc.thisController.velocity.magnitude;
                    if (Npc.getAverageVelocityInterval <= 0f)
                    {
                        Npc.getAverageVelocityInterval = 0.04f;
                        Npc.velocityAverageCount++;
                        if (Npc.velocityAverageCount > Npc.velocityMovingAverageLength)
                        {
                            Npc.averageVelocity += (magnitude2 - Npc.averageVelocity) / (float)(Npc.velocityMovingAverageLength + 1);
                        }
                        else
                        {
                            Npc.averageVelocity += magnitude2;
                            if (Npc.velocityAverageCount == Npc.velocityMovingAverageLength)
                            {
                                Npc.averageVelocity /= (float)Npc.velocityAverageCount;
                            }
                        }
                    }
                    else
                    {
                        Npc.getAverageVelocityInterval -= Time.deltaTime;
                    }
                    if (TimeSinceTakingGravityDamage > 0.6f && Npc.velocityAverageCount > 4)
                    {
                        float num8 = Vector3.Angle(Npc.transform.up, Vector3.up);
                        if (Physics.CheckSphere(Npc.gameplayCamera.transform.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
                            || (num8 > 65f && Physics.CheckSphere(Npc.lowerSpine.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
                        {
                            if (Npc.averageVelocity > 17f)
                            {
                                TimeSinceTakingGravityDamage = 0f;
                                Npc.DamagePlayer(Mathf.Clamp(85, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                            }
                            else if (Npc.averageVelocity > 9f)
                            {
                                Npc.DamagePlayer(Mathf.Clamp(30, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                                TimeSinceTakingGravityDamage = 0.35f;
                            }
                            else if (num8 > 60f && Npc.averageVelocity > 6f)
                            {
                                Npc.DamagePlayer(Mathf.Clamp(30, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                                TimeSinceTakingGravityDamage = 0f;
                            }
                        }
                    }
                    else
                    {
                        TimeSinceTakingGravityDamage += Time.deltaTime;
                    }
                    Npc.velocityLastFrame = Npc.thisController.velocity;
                    PreviousFrameDeltaTime = Time.deltaTime;
                }
                else
                {
                    TeleportingThisFrame = false;
                }
            }
            else
            {
                Npc.averageVelocity = 0f;
                Npc.velocityAverageCount = 0;
                TimeSinceTakingGravityDamage = 0f;
            }
        }

        /// <summary>
        /// Update movement when using ladder
        /// </summary>
        private void UpdateMoveWhenClimbingLadder()
        {
            Vector3 direction = Npc.thisPlayerBody.up;
            Vector3 origin = Npc.gameplayCamera.transform.position + Npc.thisPlayerBody.up * 0.07f;
            if ((Npc.externalForces + Npc.externalForceAutoFade).sqrMagnitude > 8f * 8f)
            {
                Npc.CancelSpecialTriggerAnimations();
            }
            Npc.externalForces = Vector3.zero;
            Npc.externalForceAutoFade = Vector3.Lerp(Npc.externalForceAutoFade, Vector3.zero, 5f * Time.deltaTime);

            if (goDownLadder)
            {
                direction = -Npc.thisPlayerBody.up;
                origin = Npc.gameplayCamera.transform.position;
            }
            if (!Physics.Raycast(origin, direction, 0.15f, StartOfRound.Instance.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore))
            {
                Npc.thisPlayerBody.transform.position += direction * (Const.BASE_MAX_SPEED * Npc.climbSpeed * Time.deltaTime);
            }
        }

        private void UpdateAnimationsForOwner()
        {
            //Plugin.LogDebug($"animationSpeed {Npc.playerBodyAnimator.GetFloat("animationSpeed")}");
            //for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            //{
            //    Plugin.LogDebug($"layer {i}, {Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash}");
            //}

            // Update the "what should be the animation state"
            // Layer 0
            /*if (Npc.isCrouching)
            {
                if (IsWalking)
                {
                    animationHashLayers[0] = Const.CROUCHING_WALKING_STATE_HASH;
                }
                else
                {
                    animationHashLayers[0] = Const.CROUCHING_IDLE_STATE_HASH;
                }
            }
            else if (Npc.isSprinting)
            {
                animationHashLayers[0] = Const.SPRINTING_STATE_HASH;
            }
            else if (IsWalking)
            {
                animationHashLayers[0] = Const.WALKING_STATE_HASH;
            }
            else
            {
                animationHashLayers[0] = Const.IDLE_STATE_HASH;
            }

            if (IsControllerInCruiser)
            {
                animationHashLayers[0] = Const.IDLE_STATE_HASH;
            }*/

            if (ShouldAnimate)
            {
                if (Npc.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_WALKING) != IsWalking)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, IsWalking);
                }
                if (Npc.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING) != Npc.isSprinting)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, Npc.isSprinting);
                }
            }
            else
            {
                CutAnimations();
            }

            // Other layers
            for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                animationHashLayers[i] = Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash;
            }

            if (NetworkManager.Singleton != null && Npc.playersManager.connectedPlayersAmount > 0)
            {
                // Sync
                UpdateLethalBotAnimationsToOtherClients(animationHashLayers);
            }
        }

        #endregion

        #region Updates npc body for not owner

        /// <summary>
        /// Sync the position with the server position and the rotations
        /// </summary>
        private void UpdateSyncPositionAndRotationForNotOwner()
        {
            if (!Npc.isPlayerDead && Npc.isPlayerControlled)
            {
                if (!Npc.disableSyncInAnimation)
                {
                    if (Npc.snapToServerPosition)
                    {
                        Npc.transform.localPosition = Vector3.Lerp(Npc.transform.localPosition, Npc.serverPlayerPosition, 16f * Time.deltaTime);
                    }
                    else
                    {
                        float num10 = 8f;
                        if (Npc.jetpackControls)
                        {
                            num10 = 15f;
                        }
                        float num11 = Mathf.Clamp(num10 * Vector3.Distance(Npc.transform.localPosition, Npc.serverPlayerPosition), 0.9f, 300f);
                        Npc.transform.localPosition = Vector3.MoveTowards(Npc.transform.localPosition, Npc.serverPlayerPosition, num11 * Time.deltaTime);
                    }
                }

                // Rotations
                this.UpdateTurnBodyTowardsDirection();
                this.UpdateLookAt();
                Npc.playerEye.position = Npc.gameplayCamera.transform.position;
                Npc.playerEye.rotation = Npc.gameplayCamera.transform.rotation;
            }
            else if ((Npc.isPlayerDead || !Npc.isPlayerControlled) && Npc.setPositionOfDeadPlayer)
            {
                Npc.transform.position = Npc.playersManager.notSpawnedPosition.position;
            }
        }

        private void UpdateLethalBotAnimationsLocalForNotOwner(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            if (Npc.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;

                if (ShouldAnimate)
                {
                    // If animation
                    // Update animation if current != previous
                    this.currentAnimationSpeed = Npc.playerBodyAnimator.GetFloat("animationSpeed");
                    for (int i = 0; i < animationsStateHash.Length; i++)
                    {
                        this.currentAnimationStateHash[i] = animationsStateHash[i];
                        if (this.previousAnimationStateHash[i] != this.currentAnimationStateHash[i])
                        {
                            this.previousAnimationStateHash[i] = this.currentAnimationStateHash[i];
                            this.previousAnimationSpeed = this.currentAnimationSpeed;
                            ApplyUpdateLethalBotAnimationsNotOwner(this.currentAnimationStateHash[i], this.currentAnimationSpeed);
                            return;
                        }
                    }
                }
                else
                {
                    // If no animation
                    // Return to idle state and keep previous animation state to idle, for an update if animation resume
                    if (Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH)
                    {
                        for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                        {
                            if (Npc.playerBodyAnimator.HasState(i, Const.IDLE_STATE_HASH))
                            {
                                this.previousAnimationStateHash[i] = Const.IDLE_STATE_HASH;
                                Npc.playerBodyAnimator.CrossFadeInFixedTime(Const.IDLE_STATE_HASH, 0.1f);
                                return;
                            }
                        }
                    }
                }

                if (this.previousAnimationSpeed != this.currentAnimationSpeed)
                {
                    this.previousAnimationSpeed = this.currentAnimationSpeed;
                    ApplyUpdateLethalBotAnimationsNotOwner(0, this.currentAnimationSpeed);
                }
            }
        }

        #endregion

        #region Updates npc body for all (owner and not owner)

        /// <summary>
        /// Update the localarms and rotation when in special interact animation
        /// </summary>
        private void UpdateInSpecialInteractAnimationEffect()
        {
            if (!Npc.inSpecialInteractAnimation)
            {
                if (Npc.playingQuickSpecialAnimation)
                {
                    Npc.specialAnimationWeight = 1f;
                }
                else
                {
                    Npc.specialAnimationWeight = Mathf.Lerp(Npc.specialAnimationWeight, 0f, Time.deltaTime * 12f);
                }
                if (!Npc.localArmsMatchCamera)
                {
                    Npc.localArmsTransform.position = Npc.playerModelArmsMetarig.position + Npc.playerModelArmsMetarig.forward * -0.445f;
                    Npc.playerModelArmsMetarig.rotation = Quaternion.Lerp(Npc.playerModelArmsMetarig.rotation, Npc.localArmsRotationTarget.rotation, 15f * Time.deltaTime);
                }
            }
            else
            {
                if ((!Npc.isClimbingLadder && !Npc.inShockingMinigame) || Npc.freeRotationInInteractAnimation)
                {
                    CameraUp = Mathf.Lerp(CameraUp, 0f, 5f * Time.deltaTime);
                    Npc.gameplayCamera.transform.localEulerAngles = new Vector3(CameraUp, Npc.gameplayCamera.transform.localEulerAngles.y, Npc.gameplayCamera.transform.localEulerAngles.z);
                }
                Npc.specialAnimationWeight = Mathf.Lerp(Npc.specialAnimationWeight, 1f, Time.deltaTime * 20f);
                Npc.playerModelArmsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);
            }
        }
        /// <summary>
        /// Update animation layer when using emotes
        /// </summary>
        private void UpdateEmoteEffects()
        {
            if (Npc.doingUpperBodyEmote > 0f)
            {
                Npc.doingUpperBodyEmote -= Time.deltaTime;
            }

            if (Npc.performingEmote)
            {
                Npc.emoteLayerWeight = Mathf.Lerp(Npc.emoteLayerWeight, 1f, 10f * Time.deltaTime);
            }
            else
            {
                Npc.emoteLayerWeight = Mathf.Lerp(Npc.emoteLayerWeight, 0f, 10f * Time.deltaTime);
            }
            Npc.playerBodyAnimator.SetLayerWeight(Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_EMOTESNOARMS), Npc.emoteLayerWeight);
        }
        /// <summary>
        /// Update the sinking values and effect
        /// </summary>
        private void UpdateSinkingEffects()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            Npc.meshContainer.position = Vector3.Lerp(Npc.transform.position, Npc.transform.position - Vector3.up * 2.8f, instanceSOR.playerSinkingCurve.Evaluate(Npc.sinkingValue));
            if (Npc.isSinking && !Npc.inSpecialInteractAnimation && Npc.inAnimationWithEnemy == null)
            {
                Npc.sinkingValue = Mathf.Clamp(Npc.sinkingValue + Time.deltaTime * Npc.sinkingSpeedMultiplier, 0f, 1f);
            }
            else
            {
                Npc.sinkingValue = Mathf.Clamp(Npc.sinkingValue - Time.deltaTime * 0.75f, 0f, 1f);
            }
            if (Npc.sinkingValue > 0.73f || Npc.isUnderwater)
            {
                if (!this.wasUnderwaterLastFrame)
                {
                    this.wasUnderwaterLastFrame = true;
                    Npc.waterBubblesAudio.Play();
                }
                Npc.voiceMuffledByEnemy = true;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                OccludeAudioComponent.overridingLowPass = true;
                OccludeAudioComponent.lowPassOverride = 600f;
                Npc.waterBubblesAudio.volume = Mathf.Clamp(LethalBotAIController.LethalBotIdentity.Voice.GetVoiceAmplitude() * 120f, 0f, 1f);
            }
            else if (this.wasUnderwaterLastFrame)
            {
                Npc.waterBubblesAudio.Stop();
                this.wasUnderwaterLastFrame = false;
                Npc.voiceMuffledByEnemy = false;
            }
            else
            {
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
            }
        }
        /// <summary>
        /// Update the active audio reverb filter
        /// </summary>
        private void UpdateActiveAudioReverbFilter()
        {
            GameNetworkManager instanceGNM = GameNetworkManager.Instance;
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (Npc.activeAudioReverbFilter == null)
            {
                Npc.activeAudioReverbFilter = Npc.activeAudioListener.GetComponent<AudioReverbFilter>();
                Npc.activeAudioReverbFilter.enabled = true;
            }
            if (Npc.reverbPreset != null && instanceGNM != null && instanceGNM.localPlayerController != null
                && ((instanceGNM.localPlayerController == this.Npc
                && (!Npc.isPlayerDead || instanceSOR.overrideSpectateCamera)) || (instanceGNM.localPlayerController.spectatedPlayerScript == this.Npc && !instanceSOR.overrideSpectateCamera)))
            {
                AudioReverbFilter audioReverbFilter = Npc.activeAudioReverbFilter;
                ReverbPreset reverbPreset = Npc.reverbPreset;
                audioReverbFilter.dryLevel = Mathf.Lerp(audioReverbFilter.dryLevel, reverbPreset.dryLevel, 15f * Time.deltaTime);
                audioReverbFilter.roomLF = Mathf.Lerp(audioReverbFilter.roomLF, reverbPreset.lowFreq, 15f * Time.deltaTime);
                audioReverbFilter.roomLF = Mathf.Lerp(audioReverbFilter.roomHF, reverbPreset.highFreq, 15f * Time.deltaTime);
                audioReverbFilter.decayTime = Mathf.Lerp(audioReverbFilter.decayTime, reverbPreset.decayTime, 15f * Time.deltaTime);
                audioReverbFilter.room = Mathf.Lerp(audioReverbFilter.room, reverbPreset.room, 15f * Time.deltaTime);
                SoundManager.Instance.SetEchoFilter(reverbPreset.hasEcho);
            }
        }
        /// <summary>
        /// Update animations when holding items and exhausion
        /// </summary>
        private void UpdateAnimationUpperBody()
        {
            int indexLayerHoldingItemsRightHand = Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSRIGHTHAND);
            int indexLayerHoldingItemsBothHands = Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSBOTHHANDS);

            if (Npc.isHoldingObject || Npc.isGrabbingObjectAnimation || Npc.inShockingMinigame)
            {
                this.UpperBodyAnimationsWeight = Mathf.Lerp(this.UpperBodyAnimationsWeight, 1f, 25f * Time.deltaTime);
                Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, this.UpperBodyAnimationsWeight);
                if (Npc.twoHandedAnimation || Npc.inShockingMinigame)
                {
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, this.UpperBodyAnimationsWeight);
                }
                else
                {
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, Mathf.Abs(this.UpperBodyAnimationsWeight - 1f));
                }
            }
            else
            {
                this.UpperBodyAnimationsWeight = Mathf.Lerp(this.UpperBodyAnimationsWeight, 0f, 25f * Time.deltaTime);
                Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, this.UpperBodyAnimationsWeight);
                Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, this.UpperBodyAnimationsWeight);
            }

            Npc.playerBodyAnimator.SetLayerWeight(Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_SPECIALANIMATIONS), Npc.specialAnimationWeight);
            if (Npc.inSpecialInteractAnimation && !Npc.inShockingMinigame)
            {
                Npc.cameraLookRig1.weight = Mathf.Lerp(Npc.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
                Npc.cameraLookRig2.weight = Mathf.Lerp(Npc.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
            }
            else
            {
                Npc.cameraLookRig1.weight = 0.45f;
                Npc.cameraLookRig2.weight = 1f;
            }
            if (Npc.isExhausted)
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 1f, 10f * Time.deltaTime);
            }
            else
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 0f, 10f * Time.deltaTime);
            }
            Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_TIREDAMOUNT, this.exhaustionEffectLerp);
        }

        /// <summary>
        /// Update the bleeding effects for bots!
        /// </summary>
        private void UpdateBleedEffects()
        {
            if (Npc.bleedingHeavily && BloodDropTimer >= 0f)
            {
                BloodDropTimer -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Updates the <see cref="WaitForFullStamina"/> property based on how much stamina the bot has!
        /// </summary>
        private void UpdateStaminaTimer()
        {
            // We should walk for a bit if we become exhausted!
            // NEEDTOVALIDATE: Should I create a custom method to check how much stamina is considered
            // before we are allowed to start sprinting again?
            if (Npc.isExhausted)
            {
                WaitForFullStamina = true;
            }
            else if (WaitForFullStamina && Npc.sprintMeter >= 0.8f)
            {
                WaitForFullStamina = false;
            }
        }

        /// <summary>
        /// Updates the drunkness effects for the bot's <see cref="PlayerControllerB"/>!
        /// </summary>
        private void UpdateDrunknessEffects()
        {
            if (Npc.isPlayerDead)
            {
                Npc.drunkness = 0f;
                Npc.drunknessInertia = 0f;
            }
            else
            {
                Npc.drunkness = Mathf.Clamp(Npc.drunkness + Time.deltaTime / 12f * Npc.drunknessSpeed * Npc.drunknessInertia, 0f, 1f);
                if (!Npc.increasingDrunknessThisFrame)
                {
                    if (Npc.drunkness > 0f)
                    {
                        Npc.drunknessInertia = Mathf.Clamp(Npc.drunknessInertia - Time.deltaTime / 3f * Npc.drunknessSpeed / Mathf.Clamp(Mathf.Abs(Npc.drunknessInertia), 0.2f, 1f), -2.5f, 2.5f);
                    }
                    else
                    {
                        Npc.drunknessInertia = 0f;
                    }
                }
                else
                {
                    Npc.increasingDrunknessThisFrame = false;
                }
                float num11 = StartOfRound.Instance.drunknessSideEffect.Evaluate(Npc.drunkness);
                LethalBotVoice lethalBotVoice = LethalBotAIController.LethalBotIdentity.Voice;
                float botVoicePitch = lethalBotVoice.VoicePitch;
                if (num11 > 0.15f)
                {
                    SoundManager.Instance.playerVoicePitchTargets[Npc.playerClientId] = botVoicePitch + num11;
                }
                else
                {
                    SoundManager.Instance.playerVoicePitchTargets[Npc.playerClientId] = botVoicePitch;
                }
                //SoundManager.Instance.playerVoiceVolumes[Npc.playerClientId] = lethalBotVoice.Volume;
            }
        }

        #endregion

        #region Animations

        private void UpdateLethalBotAnimationsToOtherClients(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            if (Npc.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;
                this.currentAnimationSpeed = Npc.playerBodyAnimator.GetFloat("animationSpeed");
                for (int i = 0; i < animationsStateHash.Length; i++)
                {
                    this.currentAnimationStateHash[i] = animationsStateHash[i];
                    if (this.previousAnimationStateHash[i] != this.currentAnimationStateHash[i])
                    {
                        this.previousAnimationStateHash[i] = this.currentAnimationStateHash[i];
                        this.previousAnimationSpeed = this.currentAnimationSpeed;
                        LethalBotAIController.UpdateLethalBotAnimationServerRpc(this.currentAnimationStateHash[i], this.currentAnimationSpeed);
                        return;
                    }
                }

                if (this.previousAnimationSpeed != this.currentAnimationSpeed)
                {
                    this.previousAnimationSpeed = this.currentAnimationSpeed;
                    LethalBotAIController.UpdateLethalBotAnimationServerRpc(0, this.currentAnimationSpeed);
                }
            }
        }

        public void ApplyUpdateLethalBotAnimationsNotOwner(int animationState, float animationSpeed)
        {
            if (Npc.playerBodyAnimator.GetFloat("animationSpeed") != animationSpeed)
            {
                Npc.playerBodyAnimator.SetFloat("animationSpeed", animationSpeed);
            }

            if (ShouldAnimate)
            {
                if (animationState != 0 && Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != animationState)
                {
                    for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                    {
                        if (Npc.playerBodyAnimator.HasState(i, animationState))
                        {
                            animationHashLayers[i] = animationState;
                            Npc.playerBodyAnimator.CrossFadeInFixedTime(animationState, 0.1f);
                            break;
                        }
                    }
                }
                return;
            }
            else
            {
                if (Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH)
                {
                    for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                    {
                        if (Npc.playerBodyAnimator.HasState(i, Const.IDLE_STATE_HASH))
                        {
                            Npc.playerBodyAnimator.CrossFadeInFixedTime(Const.IDLE_STATE_HASH, 0.1f);
                            break;
                        }
                    }
                }

                for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                {
                    if (Npc.playerBodyAnimator.HasState(i, animationState))
                    {
                        animationHashLayers[i] = animationState;
                        break;
                    }
                }
            }
        }

        public void StopAnimations()
        {
            IsWalking = false;
            Npc.isSprinting = false;
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
        }

        private void CutAnimations()
        {
            Npc.playerBodyAnimator.SetInteger("emoteNumber", 0);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
        }

        private void PlayFootstepIfCloseNoAnimation()
        {
            if (ShouldAnimate)
            {
                return;
            }

            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            Vector3 localPlayerPos = localPlayer.transform.position;
            if (localPlayer.isPlayerDead && localPlayer.spectatedPlayerScript != null)
            {
                localPlayerPos = localPlayer.spectatedPlayerScript.transform.position;
            }
            if ((localPlayerPos - Npc.transform.position).sqrMagnitude > 20f * 20f)
            {
                return;
            }

            float threshold = 0f;
            if (Npc.isSprinting)
            {
                threshold = 0.170f + Random.Range(0f, 0.070f);
            }
            else if (IsWalking)
            {
                threshold = 0.498f;
            }

            if (threshold > 0f)
            {
                timerPlayFootstep += Time.deltaTime;
                if (timerPlayFootstep > threshold)
                {
                    timerPlayFootstep = 0f;
                    PlayFootstep(isServer: false);
                }
            }
        }

        public void PlayFootstep(bool isServer)
        {
            if (Npc.isClimbingLadder 
                || Npc.inSpecialInteractAnimation 
                || Npc.isCrouching)
            {
                return;
            }

            if ((isServer && !LethalBotAIController.IsOwner && Npc.isPlayerControlled)
                || (!isServer && LethalBotAIController.IsOwner && Npc.isPlayerControlled))
            {
                bool noiseIsInsideClosedShip = Npc.isInHangarShipRoom && Npc.playersManager.hangarDoorsClosed;
                if (Npc.isSprinting)
                {
                    PlayAudibleNoiseLethalBot(Npc.transform.position, 22f, 0.6f, 0, noiseIsInsideClosedShip, 6);
                }
                else
                {
                    PlayAudibleNoiseLethalBot(Npc.transform.position, 17f, 0.4f, 0, noiseIsInsideClosedShip, 6);
                }

                PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
                Vector3 localPlayerPos = localPlayer.transform.position;
                if (localPlayer.isPlayerDead && localPlayer.spectatedPlayerScript != null)
                {
                    localPlayerPos = localPlayer.spectatedPlayerScript.transform.position;
                }
                if ((localPlayerPos - Npc.transform.position).sqrMagnitude < 20f * 20f)
                {
                    PlayFootstepSound();
                }
            }
        }

        public void PlayAudibleNoiseLethalBot(Vector3 noisePosition,
                                           float noiseRange = 10f,
                                           float noiseLoudness = 0.5f,
                                           int timesPlayedInSameSpot = 0,
                                           bool noiseIsInsideClosedShip = false,
                                           int noiseID = 0)
        {
            if (noiseIsInsideClosedShip)
            {
                noiseRange /= 2f;
            }

            foreach (var enemyAINoiseListener in LethalBotManager.Instance.DictEnemyAINoiseListeners)
            {
                EnemyAI enemyAI = enemyAINoiseListener.Key;
                if (enemyAI == null)
                {
                    continue;
                }

                if ((Npc.transform.position - enemyAI.transform.position).sqrMagnitude > noiseRange * noiseRange)
                {
                    continue;
                }

                if (noiseIsInsideClosedShip
                    && !enemyAI.isInsidePlayerShip
                    && noiseLoudness < 0.9f)
                {
                    continue;
                }

                Plugin.LogDebug($"{Npc.playerUsername} Play audible noise for {enemyAI.name}");
                enemyAINoiseListener.Value.DetectNoise(noisePosition, noiseLoudness, timesPlayedInSameSpot, noiseID);
            }
        }

        private void PlayFootstepSound()
        {
            AudioClip[] currentFootstepAudioClips = StartOfRound.Instance.footstepSurfaces[Npc.currentFootstepSurfaceIndex].clips;
            int currentFootstepAudioClip = Random.Range(0, currentFootstepAudioClips.Length);
            if (currentFootstepAudioClip == this.previousFootstepClip)
            {
                currentFootstepAudioClip = (currentFootstepAudioClip + 1) % currentFootstepAudioClips.Length;
            }
            Npc.movementAudio.pitch = Random.Range(0.93f, 1.07f);

            float volumeScale = 0.9f;
            if (!Npc.isSprinting)
            {
                volumeScale = 0.6f;
            }

            Npc.movementAudio.PlayOneShot(currentFootstepAudioClips[currentFootstepAudioClip], volumeScale);
            this.previousFootstepClip = currentFootstepAudioClip;
            //WalkieTalkie.TransmitOneShotAudio(this.movementAudio, StartOfRound.Instance.footstepSurfaces[this.currentFootstepSurfaceIndex].clips[num], num2);
        }

        #endregion

        /// <summary>
        /// LateUpdate called from <see cref="PlayerControllerBPatch.LateUpdate_PreFix"><c>PlayerControllerBPatch.LateUpdate_PreFix</c></see> 
        /// instead of the real LateUpdate from <c>PlayerControllerB</c>.
        /// </summary>
        /// <remarks>
        /// Update username billboard, bot looking target, bot position to clients and other stuff
        /// </remarks>
        public void LateUpdate()
        {
            GameNetworkManager instanceGNM = GameNetworkManager.Instance;

            Npc.previousElevatorPosition = Npc.playersManager.elevatorTransform.position;

            if (NetworkManager.Singleton == null)
            {
                return;
            }

            // Text billboard
            // Only show the bot state indicator when debug logging is enabled,
            // since they are only useful for debugging purposes.
            if (Plugin.Config.EnableDebugLog.Value)
            { 
                Npc.usernameBillboardText.text = LethalBotAIController.GetSizedBillboardStateIndicator(); 
            }
            else
            {
                Npc.usernameBillboardText.text = string.Empty;
            }

            if (instanceGNM.localPlayerController != null && Npc.usernameAlpha.alpha >= 0f)
            {
                Npc.usernameBillboardText.text += $"\n{Npc.playerUsername}";
                if (LethalBotAIController.IsClientOwnerOfLethalBot())
                {
                    Npc.usernameBillboardText.text += $"\nv";
                }

                Npc.usernameAlpha.alpha -= Time.deltaTime;
                UpdateBillBoardLookAtTimedCheck.UpdateBillboardLookAt(Npc, SqrDistanceWithLocalPlayerTimedCheck.GetSqrDistanceWithLocalPlayer(Npc.transform.position) < 10f * 10f);
            }
            else if (Npc.usernameCanvas.gameObject.activeSelf)
            {
                Npc.usernameCanvas.gameObject.SetActive(value: false);
            }

            // Physics regions
            //int priority = 0;
            //Transform? transform = null;
            //for (int i = 0; i < CurrentLethalBotPhysicsRegions.Count; i++)
            //{
            //    if (CurrentLethalBotPhysicsRegions[i].priority > priority)
            //    {
            //        priority = CurrentLethalBotPhysicsRegions[i].priority;
            //        transform = CurrentLethalBotPhysicsRegions[i].physicsTransform;
            //    }
            //}
            //if (Npc.isInElevator && priority <= 0)
            //{
            //    transform = null;
            //}
            //Npc.physicsParent = transform;

            //if (Npc.physicsParent != null)
            //{
            //    ReParentNotSpawnedTransform(Npc.physicsParent);
            //}
            //else
            //{
            //    if (Npc.isInElevator)
            //    {
            //        ReParentNotSpawnedTransform(Npc.playersManager.elevatorTransform);
            //        if (!LethalBotAIController.AreHandsFree())
            //        {
            //            Npc.SetItemInElevator(Npc.isInHangarShipRoom, Npc.isInElevator, LethalBotAIController.HeldItem);
            //        }
            //    }
            //    else
            //    {
            //        if (!IsControllerInCruiser)
            //        {
            //            ReParentNotSpawnedTransform(Npc.playersManager.playersContainer);
            //        }
            //    }
            //}

            // Health regen
            LethalBotAIController.HealthRegen();

            if (LethalBotAIController.IsClientOwnerOfLethalBot())
            {
                this.LethalBotRotationAndLookUpdate();

                if (Npc.isPlayerControlled && !Npc.isPlayerDead)
                {
                    if (instanceGNM != null)
                    {
                        float distMaxBeforeUpdating;
                        if (Npc.inSpecialInteractAnimation)
                        {
                            distMaxBeforeUpdating = 0.06f;
                        }
                        else if (IsRealPlayerClose(Npc.transform.position, 10f))
                        {
                            distMaxBeforeUpdating = 0.1f;
                        }
                        else
                        {
                            distMaxBeforeUpdating = 0.24f;
                        }

                        if ((Npc.oldPlayerPosition - Npc.transform.localPosition).sqrMagnitude > distMaxBeforeUpdating || UpdatePositionForNewlyJoinedClient)
                        {
                            UpdatePositionForNewlyJoinedClient = false;
                            if (!Npc.playersManager.newGameIsLoading)
                            {
                                LethalBotAIController.SyncUpdateLethalBotPosition(Npc.thisPlayerBody.localPosition, Npc.isInElevator, Npc.isInHangarShipRoom, Npc.isExhausted, IsTouchingGround);
                                Npc.serverPlayerPosition = Npc.transform.localPosition;
                                Npc.oldPlayerPosition = Npc.serverPlayerPosition;
                            }
                        }

                        GrabbableObject? currentlyHeldObject = LethalBotAIController.HeldItem;
                        if (currentlyHeldObject != null && Npc.isHoldingObject && this.GrabbedObjectValidated)
                        {
                            currentlyHeldObject.transform.localPosition = currentlyHeldObject.itemProperties.positionOffset;
                            currentlyHeldObject.transform.localEulerAngles = currentlyHeldObject.itemProperties.rotationOffset;
                        }
                    }

                    float num2 = 1f;
                    if (Npc.drunkness > 0.02f)
                    {
                        num2 *= Mathf.Abs(StartOfRound.Instance.drunknessSpeedEffect.Evaluate(Npc.drunkness) - 1.25f);
                    }
                    if (Npc.isSprinting)
                    {
                        Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter - Time.deltaTime / Npc.sprintTime * Npc.carryWeight * num2, 0f, 1f);
                    }
                    else if (Npc.isMovementHindered > 0)
                    {
                        if (IsWalking)
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter - Time.deltaTime / Npc.sprintTime * num2 * 0.5f, 0f, 1f);
                        }
                    }
                    else
                    {
                        if (!IsWalking)
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter + Time.deltaTime / (Npc.sprintTime + 4f) * num2, 0f, 1f);
                        }
                        else
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter + Time.deltaTime / (Npc.sprintTime + 9f) * num2, 0f, 1f);
                        }
                        if (Npc.isExhausted && Npc.sprintMeter > 0.2f)
                        {
                            Npc.isExhausted = false;
                        }
                    }
                }
            }
            if (!Npc.inSpecialInteractAnimation && Npc.localArmsMatchCamera)
            {
                Npc.localArmsTransform.position = Npc.cameraContainerTransform.transform.position + Npc.gameplayCamera.transform.up * -0.5f;
                Npc.playerModelArmsMetarig.rotation = Npc.localArmsRotationTarget.rotation;
            }
        }

        public void ReParentNotSpawnedTransform(Transform newParent)
        {
            if (Npc.transform.parent != newParent)
            {
                foreach (NetworkObject networkObject in Npc.GetComponentsInChildren<NetworkObject>())
                {
                    networkObject.AutoObjectParentSync = false;
                }

                Plugin.LogDebug($"{Npc.playerUsername} ReParent parent before {Npc.transform.parent}");
                Npc.transform.parent = newParent;
                Plugin.LogDebug($"{Npc.playerUsername} ReParent parent after {Npc.transform.parent}");

                foreach (NetworkObject networkObject in Npc.GetComponentsInChildren<NetworkObject>())
                {
                    networkObject.AutoObjectParentSync = true;
                }
            }
        }

        public bool CheckConditionsForSinkingInQuicksandLethalBot()
        {
            if (!IsTouchingGround)
            {
                return false;
            }

            if (Npc.inSpecialInteractAnimation || (bool)Npc.inAnimationWithEnemy || Npc.isClimbingLadder)
            {
                return false;
            }

            if (Npc.physicsParent != null)
            {
                return false;
            }

            if (Npc.isInHangarShipRoom)
            {
                return false;
            }

            if (Npc.isInElevator)
            {
                return false;
            }

            if (Npc.currentFootstepSurfaceIndex != 1
                && Npc.currentFootstepSurfaceIndex != 4
                && Npc.currentFootstepSurfaceIndex != 8
                && Npc.currentFootstepSurfaceIndex != 7
                && (!Npc.isInsideFactory || Npc.currentFootstepSurfaceIndex != 5))
            {
                return false;
            }

            return true;
        }

        private bool IsRealPlayerClose(Vector3 thisPosition, float distance)
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            foreach (PlayerControllerB player in instanceSOR.allPlayerScripts)
            {
                if (!LethalBotManager.Instance.IsPlayerLethalBot(player) 
                    && (!Plugin.IsModLethalInternsLoaded || !LethalBotManager.IsPlayerIntern(player)))
                {
                    if ((player.transform.position - thisPosition).sqrMagnitude < distance * distance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #region Emotes

        public void MimicEmotes(PlayerControllerB playerToMimic)
        {
            if (playerToMimic.performingEmote)
            {
                if (Plugin.IsModTooManyEmotesLoaded)
                {
                    CheckAndPerformTooManyEmote(playerToMimic);
                }
                else
                {
                    PerformDefaultEmote(playerToMimic.playerBodyAnimator.GetInteger("emoteNumber"));
                }
            }
            else
            {
                StopPreformingEmote();
            }
        }

        /// <summary>
        /// Perform a random emote
        /// </summary>
        /// <param name="allowTooManyEmotes">Should the bot be allowed to pick a random emote using the TooManyEmotes mod?</param>
        public void PerformRandomEmote(bool allowTooManyEmotes = true)
        {
            if (!Npc.performingEmote && PlayerControllerBPatch.CheckConditionsForEmote_ReversePatch(Npc))
            {
                // 50% chance to use the TooManyEmotes mod if it is loaded
                if (allowTooManyEmotes && Plugin.IsModTooManyEmotesLoaded && Random.Range(1, 100) <= 50)
                {
                    // Pick a random emote the player has unlocked
                    PlayerControllerB? ourOwner = null;
                    PlayerControllerB? playerToMimic = null;
                    StartOfRound instanceSOR = StartOfRound.Instance;
                    foreach (PlayerControllerB player in instanceSOR.allPlayerScripts)
                    {
                        if (ourOwner != null && playerToMimic != null)
                        {
                            break;
                        }
                        if (ourOwner == null && Npc.OwnerClientId == player.actualClientId)
                        {
                            ourOwner = player;
                        }
                        if (playerToMimic == null && IsTargetPerformingTooManyEmote(player))
                        {
                            playerToMimic = player;
                        }
                    }

                    // Just copy someone else who is emoting!
                    // This is so the bots all don't do pure random emotes
                    // Of course they only mimic if on the ship!
                    if (playerToMimic != null && (Npc.isInElevator || Npc.isInHangarShipRoom))
                    {
                        // This not only performs the same emote, but has support for group emotes!
                        CheckAndPerformTooManyEmote(playerToMimic);
                        return;
                    }

                    // Don't have an owner!? HOW DID THAT HAPPEN, just use ourself
                    if (ourOwner == null)
                    {
                        ourOwner = Npc;
                    }

                    PreformRandomTooManyEmote(ourOwner);
                }
                else
                {
                    PerformDefaultEmote(Random.Range(1, 3)); // Set to 3 since its max exclusive
                }
            }
        }

        /// <summary>
        /// Helper method to preform a random toomany emote!
        /// </summary>
        /// <remarks>
        /// This function only exists to prevent loading the TooManyEmotes mod if it is not installed.
        /// </remarks>
        /// <param name="ourOwner">The player controller this bot is owned by</param>
        private void PreformRandomTooManyEmote(PlayerControllerB ourOwner)
        {
            List<UnlockableEmote> allUnlockableEmotes = SessionManager.unlockedEmotes;
            if (!ConfigSync.instance.syncShareEverything && ourOwner != StartOfRound.Instance.localPlayerController)
            {
                SessionManager.unlockedEmotesByPlayer.TryGetValue(ourOwner.playerUsername, out allUnlockableEmotes);
            }
            if (allUnlockableEmotes == null)
            {
                allUnlockableEmotes = SessionManager.unlockedEmotes;
            }
            int randomEmoteID = Random.Range(0, allUnlockableEmotes.Count);
            LethalBotAIController.PerformTooManyEmoteLethalBotAndSync(allUnlockableEmotes[randomEmoteID].emoteId);
        }

        /// <summary>
        /// Tells the bot to stop perfoming an emote!
        /// </summary>
        /// <param name="forceStop">Sends the stop emote event even if <see cref="PlayerControllerB.performingEmote"/> is set to false!</param>
        public void StopPreformingEmote(bool forceStop = false)
        {
            if (Npc.performingEmote || forceStop)
            {
                Npc.performingEmote = false;
                Npc.playerBodyAnimator.SetInteger("emoteNumber", 0);
                this.LethalBotAIController.SyncStopPerformingEmote();
                if (Plugin.IsModTooManyEmotesLoaded)
                {
                    this.LethalBotAIController.StopPerformTooManyEmoteLethalBotAndSync();
                }
            }
        }

        /// <summary>
        /// Checks if a player is preforming a TooManyEmote!
        /// </summary>
        /// <param name="playerToCheck"></param>
        /// <returns></returns>
        private bool IsTargetPerformingTooManyEmote(PlayerControllerB playerToCheck)
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerPlayerOfplayerToCheck = playerToCheck.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerPlayerOfplayerToCheck == null)
            {
                return false;
            }

            // Player performing emote but not tooManyEmote so default
            if (!emoteControllerPlayerOfplayerToCheck.isPerformingEmote)
            {
                return false;
            }

            // TooMany emotes
            if (emoteControllerPlayerOfplayerToCheck.performingEmote == null)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Attempts to mimic the given player's TooManyEmote.
        /// Checks if the given player is emoting in the first place
        /// </summary>
        /// <param name="playerToMimic"></param>
        private void CheckAndPerformTooManyEmote(PlayerControllerB playerToMimic)
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerPlayerOfplayerToMimic = playerToMimic.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerPlayerOfplayerToMimic == null)
            {
                return;
            }
            TooManyEmotes.EmoteControllerPlayer emoteControllerLethalBot = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerLethalBot == null)
            {
                return;
            }

            // Player performing emote but not tooManyEmote so default
            if (!emoteControllerPlayerOfplayerToMimic.isPerformingEmote)
            {
                if (emoteControllerLethalBot.isPerformingEmote)
                {
                    emoteControllerLethalBot.StopPerformingEmote();
                    LethalBotAIController.StopPerformTooManyEmoteLethalBotAndSync();
                }

                // Default emote
                PerformDefaultEmote(playerToMimic.playerBodyAnimator.GetInteger("emoteNumber"));
                return;
            }

            // TooMany emotes
            if (emoteControllerPlayerOfplayerToMimic.performingEmote == null)
            {
                return;
            }

            // Check if we are already doing the same emote!
            if (emoteControllerLethalBot.isPerformingEmote
                && emoteControllerPlayerOfplayerToMimic.performingEmote.emoteId == emoteControllerLethalBot.performingEmote?.emoteId)
            {
                return;
            }

            // Check if the emote we are already doing is in the same emote group!
            TooManyEmotes.UnlockableEmote playerToMimicEmote = TooManyEmotes.EmotesManager.allUnlockableEmotes[emoteControllerPlayerOfplayerToMimic.performingEmote.emoteId];
            if (playerToMimicEmote != null 
                && emoteControllerLethalBot.isPerformingEmote 
                && emoteControllerLethalBot.performingEmote != null)
            {
                TooManyEmotes.UnlockableEmote lethalBotEmote = TooManyEmotes.EmotesManager.allUnlockableEmotes[emoteControllerLethalBot.performingEmote.emoteId];
                if (lethalBotEmote.IsEmoteInEmoteGroup(playerToMimicEmote))
                {
                    return;
                }
            }

            // PerformEmote TooMany emote
            LethalBotAIController.PerformTooManyEmoteLethalBotAndSync(emoteControllerPlayerOfplayerToMimic.performingEmote.emoteId, (int)playerToMimic.playerClientId);
        }

        /// <summary>
        /// Makes the bot player the given emote!
        /// </summary>
        /// <param name="emoteNumberToMimic">The integer of the emote to play</param>
        private void PerformDefaultEmote(int emoteNumberToMimic)
        {
            int emoteNumberLethalBot = Npc.playerBodyAnimator.GetInteger("emoteNumber");
            if ((!Npc.performingEmote
                || emoteNumberLethalBot != emoteNumberToMimic)
                && PlayerControllerBPatch.CheckConditionsForEmote_ReversePatch(Npc))
            {
                Npc.performingEmote = true;
                Npc.PerformEmote(new UnityEngine.InputSystem.InputAction.CallbackContext(), emoteNumberToMimic);
            }
        }

        /// <summary>
        /// Performs the given TooManyEmote. If a playerToSync is given, the bot will sync to their emote instead!
        /// </summary>
        /// <remarks>
        /// This automatically picks the next group emote if the playerToMimic is preforming one!
        /// </remarks>
        /// <param name="tooManyEmoteID"></param>
        /// <param name="playerToSync"></param>
        public void PerformTooManyEmote(int tooManyEmoteID, int playerToSync = -1)
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerLethalBot = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerLethalBot == null)
            {
                return;
            }

            if (emoteControllerLethalBot.isPerformingEmote)
            {
                emoteControllerLethalBot.StopPerformingEmote();
            }

            // If we were syncing our emote with another player, we have to find them first!
            PlayerControllerB? playerToSyncWith = null;
            if (playerToSync != -1)
            {
                foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (player != null 
                        && (int)player.playerClientId == playerToSync
                        && player.isPlayerControlled 
                        && !player.isPlayerDead)
                    {
                        playerToSyncWith = player;
                        break;
                    }
                }
            }

            // If we are syncing an emote with someone, lets do so here
            if (playerToSyncWith != null)
            {
                // HACKHACK: The TooManyEmotes code checks if the bot is the local player, which they aren't
                // so we have to do the logic here!
                TooManyEmotes.EmoteControllerPlayer emoteControllerPlayerToSync = playerToSyncWith.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
                int overrideEmoteId = -1;
                emoteControllerLethalBot.SyncWithEmoteController(emoteControllerPlayerToSync, overrideEmoteId);
                if (emoteControllerLethalBot.performingEmote != null)
                {
                    if (emoteControllerLethalBot.performingEmote.inEmoteSyncGroup)
                    {
                        overrideEmoteId = emoteControllerLethalBot.performingEmote.emoteSyncGroup.IndexOf(emoteControllerLethalBot.performingEmote);
                    }

                    Npc.StartPerformingEmoteServerRpc();
                    SyncPerformingEmoteManager.SendSyncEmoteUpdateToServer(emoteControllerLethalBot, overrideEmoteId);
                    emoteControllerLethalBot.timeSinceStartingEmote = 0f;
                    Npc.performingEmote = true;
                    Plugin.LogDebug($"Lethal Bot {Npc.playerUsername} successfuly synced emote with {playerToSyncWith.playerUsername}!");
                    return;
                }
            }

            TooManyEmotes.UnlockableEmote unlockableEmote = TooManyEmotes.EmotesManager.allUnlockableEmotes[tooManyEmoteID];
            emoteControllerLethalBot.PerformEmote(unlockableEmote);
        }

        public void StopPerformingTooManyEmote()
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerLethalBotController = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerLethalBotController != null)
            {
                emoteControllerLethalBotController.StopPerformingEmote();
            }
        }

        #endregion

        /// <summary>
        /// Sync the rotation and the look at target to all clients
        /// </summary>
        private void LethalBotRotationAndLookUpdate()
        {
            if (!Npc.isPlayerControlled)
            {
                return;
            }

            if (Npc.playersManager.connectedPlayersAmount < 1
                || Npc.playersManager.newGameIsLoading
                || Npc.disableLookInput)
            {
                return;
            }

            if (this.oldLookAtTarget == this.LookAtTarget)
            {
                return;
            }

            // Update after some interval of time
            // Only if there's at least one player near
            // Disabling IsRealPlayerClose(Npc.transform.position, 35f) as it causes the bots not to update
            // if there are no alive players nearby which affects spectating players!
            // As well as players on the ship monitoring the bots!
            if (this.UpdatePlayerLookInterval > 0.25f)
            {
                this.UpdatePlayerLookInterval = 0f;
                LethalBotAIController.SyncUpdateLethalBotRotationAndLook(LethalBotAIController.State.GetBillboardStateIndicator(),
                                                                   LookAtTarget);
                this.oldLookAtTarget = this.LookAtTarget.Clone();
            }
        }

        /// <summary>
        /// Set the move vector to go forward
        /// </summary>
        public void OrderToMove()
        {
            HasToMove = true;
        }

        /// <summary>
        /// Set the move vector to 0
        /// </summary>
        public void OrderToStopMoving()
        {
            HasToMove = false;
            floatSprint = 0f;
        }

        /// <summary>
        /// Set the controller to sprint
        /// </summary>
        public void OrderToSprint()
        {
            if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }
            if (Npc.isExhausted)
            {
                floatSprint = 0f;
                return;
            }
            if (Npc.isSprinting)
            {
                return;
            }
            // Don't sprint if we are trying to crouch!
            if (LethalBotAIController != null && LethalBotAIController.State != null)
            {
                bool? shouldCrouch = LethalBotAIController.State.ShouldBotCrouch();
                if (shouldCrouch.HasValue && shouldCrouch.Value == true)
                {
                    floatSprint = 0f;
                    return;
                }
            }

            floatSprint = 1f;
        }
        /// <summary>
        /// Set the controller to stop sprinting
        /// </summary>
        public void OrderToStopSprint()
        {
            /*if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }*/
            if (!Npc.isSprinting)
            {
                return;
            }

            floatSprint = 0f;
        }
        /// <summary>
        /// Set the controller to crouch on/off
        /// </summary>
        public void OrderToToggleCrouch()
        {
            if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }
            if (Npc.isSprinting && !Npc.isCrouching)
            {
                return;
            }
            this.CrouchMeter = Mathf.Min(this.CrouchMeter + 0.3f, 1.3f);
            Npc.Crouch(!Npc.isCrouching);
        }

        /// <summary>
        /// Set the direction the controller should turn towards, using a vector position
        /// </summary>
        /// <param name="positionDirection">Position to turn to</param>
        public void SetTurnBodyTowardsDirectionWithPosition(Vector3 positionDirection)
        {
            this.LookAtTarget.directionToUpdateTurnBodyTowardsTo = positionDirection - Npc.thisController.transform.position;
        }
        /// <summary>
        /// Set the direction the controller should turn towards, using a vector direction
        /// </summary>
        /// <param name="direction">Direction to turn to</param>
        public void SetTurnBodyTowardsDirection(Vector3 direction)
        {
            this.LookAtTarget.directionToUpdateTurnBodyTowardsTo = direction;
        }

        /// <summary>
        /// Turn the body towards the direction set beforehand
        /// </summary>
        private void UpdateTurnBodyTowardsDirection()
        {
            if (IsControllerInCruiser)
            {
                return;
            }

            UpdateNowTurnBodyTowardsDirection(LookAtTarget.directionToUpdateTurnBodyTowardsTo);
        }

        public void UpdateNowTurnBodyTowardsDirection(Vector3 direction)
        {
            if (DirectionNotZero(direction.x) || DirectionNotZero(direction.z))
            {
                Quaternion targetRotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z));
                Npc.thisPlayerBody.rotation = Quaternion.Lerp(Npc.thisPlayerBody.rotation, targetRotation, Const.BODY_TURNSPEED * Time.deltaTime);
            }
        }

        /// <summary>
        /// Make the controller look at the eyes of a player
        /// </summary>
        /// <param name="positionPlayerEyeToLookAt"></param>
        public void OrderToLookAtPlayer(Vector3 positionPlayerEyeToLookAt)
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.LookAtTarget.AimHeadTowards(positionPlayerEyeToLookAt, EnumLookAtPriority.MEDIUM_PRIORITY, bypassSteadyCheck: true);
        }
        /// <summary>
        /// Make the controller look straight forward
        /// </summary>
        public void OrderToLookForward()
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
            this.LookAtTarget.lookAtPriority = EnumLookAtPriority.LOW_PRIORITY; // HACKHACK: Reset LookAtPriority
        }
        /// <summary>
        /// Make the controller look at an specific vector position
        /// </summary>
        /// <param name="positionToLookAt"></param>
        public void OrderToLookAtPosition(Vector3 positionToLookAt, EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, float duration = 0.0f, bool bypassSteadyCheck = false, float maxBodyFOV = Const.LETHAL_BOT_FOV)
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.LookAtTarget.AimHeadTowards(positionToLookAt, priority, duration, bypassSteadyCheck, maxBodyFOV);
        }

        /// <summary>
        /// Changes the current look at target for the given bot!
        /// </summary>
        /// <param name="lookAtTarget"></param>
        public void SetCurrentLookAt(LookAtTarget lookAtTarget)
        {
            this.oldLookAtTarget = this.LookAtTarget.Clone();
            this.LookAtTarget = lookAtTarget;
        }

        /// <summary>
        /// Update the head of the bot to look at what he is set to
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateLookAt()
        {
            this.LookAtTarget.Update(this, this.LethalBotAIController);
        }

        public bool IsMoving()
        {
            return MoveVector != Vector3.zero
                || Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH;
        }

        private void ForceTurnTowardsTarget()
        {
            if (Npc.inSpecialInteractAnimation && Npc.inShockingMinigame && Npc.shockingTarget != null)
            {
                OrderToLookAtPosition(Npc.shockingTarget.position, EnumLookAtPriority.MAXIMUM_PRIORITY);
            }
            else if (Npc.inAnimationWithEnemy
                     && EnemyInAnimationWith != null)
            {
                Vector3 pos;
                if (EnemyInAnimationWith.eye != null)
                {
                    pos = EnemyInAnimationWith.eye.position;
                }
                else
                {
                    pos = EnemyInAnimationWith.transform.position;
                }

                OrderToLookAtPosition(pos, EnumLookAtPriority.MAXIMUM_PRIORITY);
            }
        }

        /// <summary>
        /// Set the controller to go down or up on the ladder
        /// </summary>
        /// <param name="hasToGoDown"></param>
        public void OrderToGoUpDownLadder(bool hasToGoDown)
        {
            this.goDownLadder = hasToGoDown;
        }

        /// <summary>
        /// Checks if the bot can use the ladder
        /// </summary>
        /// <param name="ladder"></param>
        /// <returns></returns>
        public bool CanUseLadder(InteractTrigger ladder)
        {
            if (ladder.usingLadder)
            {
                return false;
            }

            // todo : ladder item holding configurable ?
            //if ((this.Npc.isHoldingObject && !ladder.oneHandedItemAllowed)
            //    || (this.Npc.twoHanded &&
            //                       (!ladder.twoHandedItemAllowed || ladder.specialCharacterAnimation)))
            //{
            //    Plugin.LogDebug("no ladder cuz holding things");
            //    return false;
            //}

            if (this.Npc.sinkingValue > 0.73f)
            {
                return false;
            }
            if (this.Npc.jetpackControls && (ladder.specialCharacterAnimation || ladder.isLadder))
            {
                return false;
            }
            if (this.Npc.isClimbingLadder)
            {
                /*if (ladder.isLadder)
                {
                    if (!ladder.usingLadder)
                    {
                        return false;
                    }
                }*/
                if (!ladder.isLadder && ladder.specialCharacterAnimation)
                {
                    return false;
                }
            }
            else if (this.Npc.inSpecialInteractAnimation)
            {
                return false;
            }

            if (ladder.isPlayingSpecialAnimation)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Save the different animation for an item and the state
        /// </summary>
        /// <param name="animationString">Name of the animation</param>
        /// <param name="value">active or not</param>
        public void SetAnimationBoolForItem(string animationString, bool value)
        {
            if (dictAnimationBoolPerItem == null)
            {
                dictAnimationBoolPerItem = new Dictionary<string, bool>();
            }

            dictAnimationBoolPerItem[animationString] = value;
        }

        /// <summary>
        /// Check if the direction is not close to <see cref="Const.EPSILON">Const.EPSILON</see>
        /// </summary>
        /// <param name="direction"></param>
        /// <returns></returns>
        internal bool DirectionNotZero(float direction)
        {
            return direction < -Const.EPSILON || Const.EPSILON < direction;
        }

        /// <summary>
        /// Manage the drowning state of the bot
        /// </summary>
        private void SetFaceUnderwaterFilters()
        {
            if (Npc.isPlayerDead)
            {
                return;
            }
            if (Npc.underwaterCollider != null
                && Npc.underwaterCollider.bounds.Contains(Npc.gameplayCamera.transform.position))
            {
                setFaceUnderwater = true;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                this.DrowningTimer -= Time.deltaTime / Const.LETHAL_BOT_DROWN_TIME;
                if (this.DrowningTimer < 0f)
                {
                    this.DrowningTimer = 1f;
                    Plugin.LogDebug($"SyncKillLethalBot from drowning for LOCAL client #{Npc.NetworkManager.LocalClientId}, bot object: Bot #{Npc.playerClientId}");
                    Npc.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Drowning, 0, default);
                }
            }
            else
            {
                setFaceUnderwater = false;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
                this.DrowningTimer = Mathf.Clamp(this.DrowningTimer + Time.deltaTime, 0.1f, 1f);
            }

            this.syncUnderwaterInterval -= Time.deltaTime;
            if (this.syncUnderwaterInterval <= 0f)
            {
                this.syncUnderwaterInterval = 0.5f;
                if (setFaceUnderwater && !Npc.isUnderwater)
                {
                    Npc.isUnderwater = true;
                    LethalBotAIController.SyncSetFaceUnderwaterServerRpc(Npc.isUnderwater);
                    return;
                }
                else if (!setFaceUnderwater && Npc.isUnderwater)
                {
                    Npc.isUnderwater = false;
                    LethalBotAIController.SyncSetFaceUnderwaterServerRpc(Npc.isUnderwater);
                    return;
                }
            }
        }

        /// <summary>
        /// Unused for now, can't find the true size of models...
        /// </summary>
        public void RefreshBillBoardPosition()
        {
            if (Plugin.IsModModelReplacementAPILoaded)
            {
                Npc.usernameCanvas.transform.localPosition = GetBillBoardPositionModelReplacementAPI(Npc.usernameCanvas.transform.localPosition);
            }
            else
            {
                Npc.usernameCanvas.transform.localPosition = GetBillBoardPosition(Npc.gameObject, Npc.usernameCanvas.transform.localPosition);
            }
        }

        private Vector3 GetBillBoardPositionModelReplacementAPI(Vector3 lastPosition)
        {
            BodyReplacementBase? bodyReplacement = Npc.gameObject.GetComponent<BodyReplacementBase>();
            if (bodyReplacement == null)
            {
                return GetBillBoardPosition(Npc.gameObject, lastPosition);
            }

            GameObject? model = bodyReplacement.replacementModel;
            if (model == null)
            {
                return GetBillBoardPosition(Npc.gameObject, lastPosition);
            }

            return GetBillBoardPosition(model, Npc.usernameCanvas.transform.localPosition);
        }

        private Vector3 GetBillBoardPosition(GameObject bodyModel, Vector3 lastPosition)
        {
            // Grab the model bounds using our cached method!
            Bounds modelBounds = GetBoundsTimedCheck.GetBoundsModel(bodyModel);
            return new Vector3(lastPosition.x,
                               (modelBounds.center.y - Npc.transform.position.y) + modelBounds.extents.y, // + 0.65f
                               lastPosition.z);
        }

        public class TimedGetBounds
        {
            private Bounds bounds;
            private GameObject? model;

            private long timer = 10000 * TimeSpan.TicksPerMillisecond;
            private long lastTimeCalculate;

            public Bounds GetBoundsModel(GameObject model)
            {
                if (model == this.model
                    && !NeedToRecalculate())
                {
                    return bounds;
                }

                this.model = model;
                CalculateBoundsModel(model);
                return bounds;
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

            private void CalculateBoundsModel(GameObject model)
            {
                // Shamelessly taken from ModelReplacementAPI, sorry, had to do optimizations with all these damn bots
                bounds = default(Bounds);
                IEnumerable<Bounds> enumerable = Enumerable.Select<SkinnedMeshRenderer, Bounds>(model.GetComponentsInChildren<SkinnedMeshRenderer>(), (SkinnedMeshRenderer r) => r.bounds);
                float x3 = Enumerable.First<Bounds>(Enumerable.OrderByDescending<Bounds, float>(enumerable, (Bounds x) => x.max.x)).max.x;
                float y = Enumerable.First<Bounds>(Enumerable.OrderByDescending<Bounds, float>(enumerable, (Bounds x) => x.max.y)).max.y;
                float z = Enumerable.First<Bounds>(Enumerable.OrderByDescending<Bounds, float>(enumerable, (Bounds x) => x.max.z)).max.z;
                float x2 = Enumerable.First<Bounds>(Enumerable.OrderBy<Bounds, float>(enumerable, (Bounds x) => x.min.x)).min.x;
                float y2 = Enumerable.First<Bounds>(Enumerable.OrderBy<Bounds, float>(enumerable, (Bounds x) => x.min.y)).min.y;
                float z2 = Enumerable.First<Bounds>(Enumerable.OrderBy<Bounds, float>(enumerable, (Bounds x) => x.min.z)).min.z;
                bounds.SetMinMax(new Vector3(x2, y2, z2), new Vector3(x3, y, z));
            }
        }

        public class TimedSqrDistanceWithLocalPlayerCheck
        {
            private float sqrDistance;

            private long timer = 100 * TimeSpan.TicksPerMillisecond;
            private long lastTimeCalculate;

            public float GetSqrDistanceWithLocalPlayer(Vector3 lethalBotBodyPos)
            {
                if (!NeedToRecalculate())
                {
                    return sqrDistance;
                }

                if (StartOfRound.Instance == null
                    || StartOfRound.Instance.localPlayerController == null)
                {
                    return sqrDistance;
                }

                CalculateSqrDistanceWithLocalPlayer(lethalBotBodyPos);
                return sqrDistance;
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

            private void CalculateSqrDistanceWithLocalPlayer(Vector3 lethalBotBodyPos)
            {
                sqrDistance = (StartOfRound.Instance.localPlayerController.transform.position - lethalBotBodyPos).sqrMagnitude;
            }
        }

        public class TimedUpdateBillboardLookAtCheck
        {
            private long timer = 100 * TimeSpan.TicksPerMillisecond;
            private long lastTimeCalculate;

            public void UpdateBillboardLookAt(PlayerControllerB player, bool forceUpdate)
            {
                if (!forceUpdate
                    && !NeedToRecalculate())
                {
                    return;
                }

                if (StartOfRound.Instance == null
                    || StartOfRound.Instance.localPlayerController == null)
                {
                    return;
                }

                CalculateUpdateBillboardLookAt(player);
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

            private void CalculateUpdateBillboardLookAt(PlayerControllerB player)
            {
                player.usernameBillboard.LookAt(StartOfRound.Instance.localPlayerController.localVisorTargetPoint);
            }
        }
    }
}