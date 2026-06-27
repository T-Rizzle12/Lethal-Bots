using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.NetworkSerializers;
using LethalBots.Patches.NpcPatches;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
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
using Unity.Collections;
using Unity.IO.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.HID;
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

        //Audio
        public OccludeAudio OccludeAudioComponent = null!;
        public AudioLowPassFilter AudioLowPassFilterComponent = null!;
        public AudioHighPassFilter AudioHighPassFilterComponent = null!;

        public Vector3 PreviousExternalForces { get; private set; }
        public Vector3 MoveVector;
        public bool IsTouchingGround;
        public EnemyAI? EnemyInAnimationWith;
        public Vector3 NearEntitiesPushVector;

        private LethalBotAI LethalBotAIController
        {
            get
            {
                if (field == null)
                {
                    field = LethalBotManager.Instance.GetLethalBotAI(Npc);
                    if (field == null)
                    {
                        throw new NullReferenceException($"{Plugin.ModGUID} v{MyPluginInfo.PLUGIN_VERSION}: error no lethalBotAI attached to NpcController playerClientId {Npc.playerClientId}.");
                    }
                }
                return field;
            }
        }

        private int movementHinderedPrev;
        private float sprintMultiplier = 1f;
        public bool WaitForFullStamina { get; private set; }
        //private float slopeModifier; // ignore for now
        private Vector3 walkForce;

        private Dictionary<string, bool> dictAnimationBoolPerItem = null!;

        private float exhaustionEffectLerp;
        private bool disabledJetpackControlsThisFrame;

        private bool wasUnderwaterLastFrame;
        public float DrowningTimer { set; get; } = 1f;
        private bool setFaceUnderwater;
        private float syncUnderwaterInterval;

        private LookAtTarget oldLookAtTarget = new LookAtTarget();
        public LookAtTarget LookAtTarget { private set; get; } = new LookAtTarget();

        public Vector2 lastMoveVector;
        private float floatSprint;
        internal bool goDownLadder;

        private int[] animationHashLayers = null!;
        private List<int> currentAnimationStateHash = null!;
        private List<int> previousAnimationStateHash = null!;
        private float updatePlayerAnimationsInterval;
        private float currentAnimationSpeed;
        private float previousAnimationSpeed;

        public NpcController(PlayerControllerB npc)
        {
            this.Npc = npc;
            Init();
        }

        /// <summary>
        /// Initialize the <c>PlayerControllerB</c>
        /// </summary>
        public void Awake(bool clientJoining = false)
        {
            //Plugin.LogDebug("Awake bot controller.");
            Init(clientJoining);
        }

        private void Init(bool clientJoining = false)
        {
            Npc.isHostPlayerObject = false;
            Npc.serverPlayerPosition = Npc.transform.position;
            Npc.gameplayCamera.enabled = false;
            Npc.visorCamera.enabled = false;
            Npc.thisPlayerModel.enabled = true;
            Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
            Npc.thisPlayerModelArms.enabled = false;

            Npc.isCameraDisabled = true;
            Npc.sprintMeter = 1f;
            if (!clientJoining) Npc.ItemSlots ??= new GrabbableObject[4]; // Only create new array if it doesn't exist!
            Npc.testGroundPositions = new Vector3[5];
            Npc.rightArmProceduralTargetBasePosition = Npc.rightArmProceduralTarget.localPosition;

            Npc.usernameBillboardText.text = Npc.playerUsername;
            Npc.usernameAlpha.alpha = 1f;
            Npc.usernameCanvas.gameObject.SetActive(true);

            Npc.previousElevatorPosition = Npc.playersManager.elevatorTransform.position;
            if (Npc.gameObject.GetComponent<Rigidbody>())
            {
                Npc.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
            }
            Npc.gameObject.GetComponent<CharacterController>().enabled = true;

            // Sigh, try-catch here since SetPlayerSafeInShip doesn't null check the
            // EnemyAI renderers, which causes the whole damn thing to error out
            try
            {
                if (SingletonManager.AudioReverbPresets.TryGet(out AudioReverbPresets? audioReverbPresets))
                {
                    audioReverbPresets.audioPresets[3].ChangeAudioReverbForPlayer(Npc);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error occured when setting audio reverb for bot: {ex}");
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
            // The owner of the bot (and the controller)
            // updates and moves the controller
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = Npc;
            if (LethalBotAIController.IsClientOwnerOfLethalBot() && lethalBotController.isPlayerControlled)
            {
                // Updates the state of the CharacterController and the animator controller
                UpdateOwnerChanged(true);

                lethalBotController.rightArmProceduralRig.weight = Mathf.Lerp(lethalBotController.rightArmProceduralRig.weight, 0f, 25f * Time.deltaTime);

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

                if (!lethalBotController.inSpecialInteractAnimation || lethalBotController.inShockingMinigame || instanceSOR.suckingPlayersOutOfShip)
                {
                    // Move the body of bot
                    UpdateMoveControllerForOwner();

                    // Check if the bot is falling and update values accordingly
                    UpdateFallValuesForOwner();

                    PreviousExternalForces = lethalBotController.externalForces;
                    lethalBotController.externalForces = Vector3.zero;
                    if (!lethalBotController.teleportingThisFrame && lethalBotController.teleportedLastFrame)
                    {
                        lethalBotController.ResetFallGravity();
                        lethalBotController.teleportedLastFrame = false;
                    }

                    // Update movement when using jetpack controls
                    UpdateJetPackMoveValuesForOwner();
                }
                else if (lethalBotController.isClimbingLadder)
                {
                    // Update movement when using ladder
                    UpdateMoveWhenClimbingLadder();
                }
                lethalBotController.teleportingThisFrame = false;

                // Rotations
                this.UpdateLookAt();

                lethalBotController.playerEye.position = lethalBotController.gameplayCamera.transform.position;
                lethalBotController.playerEye.rotation = lethalBotController.gameplayCamera.transform.rotation;

                // Update UpdatePlayerLookInterval
                if (NetworkManager.Singleton != null)
                {
                    lethalBotController.updatePlayerLookInterval += Time.deltaTime;
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

            lethalBotController.timeSinceSwitchingSlots += Time.deltaTime;
            lethalBotController.timeSincePlayerMoving += Time.deltaTime;
            lethalBotController.timeSinceMakingLoudNoise += Time.deltaTime;
            lethalBotController.timeSinceFearLevelUp += Time.deltaTime;

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

            // Update drunkness and poison effects
            UpdateDrunknessAndPoisonEffects();

            // Update the bot's line of sight cube
            UpdateLineOfSightCube();

            // Update our player sanity
            lethalBotController.SetPlayerSanityLevel();
        }

        /// <summary>
        /// Updates the state of the <c>CharacterController</c>
        /// and update the animator controller with its animation
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateOwnerChanged(bool isOwner)
        {
            PlayerControllerB lethalBotController = Npc;
            if (isOwner)
            {
                if (lethalBotController.isCameraDisabled)
                {
                    lethalBotController.isCameraDisabled = false;
                    lethalBotController.gameplayCamera.enabled = false;
                    lethalBotController.visorCamera.enabled = false;
                    lethalBotController.thisPlayerModelArms.enabled = false;
                    lethalBotController.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    lethalBotController.mapRadarDirectionIndicator.enabled = false;
                    lethalBotController.thisController.enabled = true;
                    lethalBotController.activeAudioReverbFilter = lethalBotController.activeAudioListener.GetComponent<AudioReverbFilter>();
                    lethalBotController.activeAudioReverbFilter.enabled = true;
                    // BUGBUG: This code creates issues where the audio follows the bot rather than the local player's camera.
                    /*lethalBotController.activeAudioListener.transform.SetParent(lethalBotController.gameplayCamera.transform);
                    lethalBotController.activeAudioListener.transform.localEulerAngles = Vector3.zero;
                    lethalBotController.activeAudioListener.transform.localPosition = Vector3.zero;*/
                    UpdateRuntimeAnimatorController(isOwner);
                }
                lethalBotController.SetNightVisionEnabled(isNotLocalClient: true);
            }
            else
            {
                if (!lethalBotController.isCameraDisabled)
                {
                    lethalBotController.isCameraDisabled = true;
                    lethalBotController.gameplayCamera.enabled = false;
                    lethalBotController.visorCamera.enabled = false;
                    lethalBotController.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    lethalBotController.thisPlayerModelArms.enabled = false;
                    lethalBotController.mapRadarDirectionIndicator.enabled = false;
                    UpdateRuntimeAnimatorController(isOwner);
                    lethalBotController.thisController.enabled = false;
                    if (lethalBotController.gameObject.GetComponent<Rigidbody>())
                    {
                        lethalBotController.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
                    }
                }
                lethalBotController.SetNightVisionEnabled(isNotLocalClient: true);
            }
        }

        /// <summary>
        /// Updates the animator controller if the owner of bot has changed
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateRuntimeAnimatorController(bool isOwner)
        {
            // Save animations states
            PlayerControllerB lethalBotController = Npc;
            AnimatorStateInfo[] layerInfo = new AnimatorStateInfo[lethalBotController.playerBodyAnimator.layerCount];
            for (int i = 0; i < lethalBotController.playerBodyAnimator.layerCount; i++)
            {
                layerInfo[i] = lethalBotController.playerBodyAnimator.GetCurrentAnimatorStateInfo(i);
            }

            // Change runtimeAnimatorController
            if (isOwner)
            {
                if (lethalBotController.playerBodyAnimator.runtimeAnimatorController != lethalBotController.playersManager.localClientAnimatorController)
                {
                    lethalBotController.playerBodyAnimator.runtimeAnimatorController = lethalBotController.playersManager.localClientAnimatorController;
                    if (!lethalBotController.playerBodyAnimator.GetCurrentAnimatorStateInfo(5).IsTag("notInSpecialAnim"))
                    {
                        lethalBotController.playerBodyAnimator.SetTrigger("SA_stopAnimation");
                    }
                }
            }
            else
            {
                if (lethalBotController.playerBodyAnimator.runtimeAnimatorController != lethalBotController.playersManager.otherClientsAnimatorController)
                {
                    lethalBotController.playerBodyAnimator.runtimeAnimatorController = lethalBotController.playersManager.otherClientsAnimatorController;
                }
            }

            // Push back animations states
            for (int i = 0; i < lethalBotController.playerBodyAnimator.layerCount; i++)
            {
                if (lethalBotController.playerBodyAnimator.HasState(i, layerInfo[i].fullPathHash))
                {
                    lethalBotController.playerBodyAnimator.CrossFadeInFixedTime(layerInfo[i].fullPathHash, 0.1f);
                }
            }

            if (dictAnimationBoolPerItem != null)
            {
                foreach (var animationBool in dictAnimationBoolPerItem)
                {
                    lethalBotController.playerBodyAnimator.SetBool(animationBool.Key, animationBool.Value);
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
            PlayerControllerB lethalBotController = Npc;
            if (!HasToMove)
            {
                lastMoveVector = lethalBotController.moveInputVector;
                lethalBotController.moveInputVector = Vector2.zero;
                return;
            }

            Vector2 moveInput = new Vector2();
            if (lethalBotController.isClimbingLadder)
            {
                // Pick move direction based on if we want to go up or down the ladder
                float x = 1f;
                float y = 1f;
                if (goDownLadder)
                {
                    x = -1f;
                    y = -1f;
                }
                moveInput.x = x;
                moveInput.y = y;
            }
            else
            {
                // Get direction from current position to NavMeshAgent's steering target
                Vector3 worldDir = (LethalBotAIController.agent.steeringTarget - lethalBotController.thisController.transform.position);
                worldDir.y = 0f; // Ignore vertical movement

                // Convert to local space (relative to the bot's forward direction)
                Vector3 localDir = lethalBotController.thisController.transform.InverseTransformDirection(worldDir.normalized);
                moveInput.x = localDir.x;
                moveInput.y = localDir.z;
            }

            // Set moveInputVector (X = sideways, Z = forward)
            lastMoveVector = lethalBotController.moveInputVector;
            lethalBotController.moveInputVector = moveInput;
            lethalBotController.moveInputVector.Normalize();
        }

        /// <summary>
        /// Update the animation of walking under numerous conditions
        /// </summary>
        private void UpdateWalkingStateForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isWalking)
            {
                if (lethalBotController.moveInputVector.sqrMagnitude <= 0.19f
                    || (lethalBotController.inSpecialInteractAnimation && !lethalBotController.isClimbingLadder && !lethalBotController.inShockingMinigame))
                {
                    StopAnimations();
                }
                else if (floatSprint > 0.3f
                            && movementHinderedPrev <= 0
                            && !lethalBotController.criticallyInjured
                            && lethalBotController.sprintMeter > 0.1f)
                {
                    if (!lethalBotController.isSprinting && lethalBotController.sprintMeter < 0.3f)
                    {
                        if (!lethalBotController.isExhausted)
                        {
                            lethalBotController.isExhausted = true;
                        }
                    }
                    else
                    {
                        if (lethalBotController.isCrouching && (!Plugin.Config.FollowCrouchWithPlayer 
                            || LethalBotAIController.targetPlayer == null 
                            || !LethalBotAIController.IsFollowingTargetPlayer()))
                        {
                            lethalBotController.Crouch(false);
                        }

                        if (!lethalBotController.isCrouching)
                        {
                            lethalBotController.isSprinting = true;
                        }
                    }
                }
                else
                {
                    lethalBotController.isSprinting = false;
                    if (lethalBotController.sprintMeter < 0.1f)
                    {
                        lethalBotController.isExhausted = true;
                    }
                }

                if (lethalBotController.isSprinting)
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 2.25f, Time.deltaTime * 1f);
                }
                else
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 1f, 10f * Time.deltaTime);
                }

                if (lethalBotController.moveInputVector.y < 0.2f && lethalBotController.moveInputVector.y > -0.2f && !lethalBotController.inSpecialInteractAnimation)
                {
                    lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, true);
                }
                else
                {
                    lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
                }
                if (lethalBotController.enteringSpecialAnimation)
                {
                    lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                else if (lethalBotController.moveInputVector.y < 0.5f && lethalBotController.moveInputVector.x < 0.5f)
                {
                    //lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, -1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                    lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, -1f);
                }
                else
                {
                    //lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                    lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
            }
            else
            {
                if (lethalBotController.enteringSpecialAnimation)
                {
                    lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                else if (lethalBotController.isClimbingLadder)
                {
                    lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 0f);
                }
                if (lethalBotController.moveInputVector.sqrMagnitude >= 0.001f && (!lethalBotController.inSpecialInteractAnimation || lethalBotController.isClimbingLadder || lethalBotController.inShockingMinigame))
                {
                    lethalBotController.isWalking = true;
                }
            }
        }

        /// <summary>
        /// Sync with clients if the bot is performing emote
        /// </summary>
        private void UpdateEmoteStateForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.performingEmote)
            {
                if (lethalBotController.inSpecialInteractAnimation
                    || lethalBotController.isPlayerDead
                    || lethalBotController.isCrouching
                    || lethalBotController.isClimbingLadder
                    || lethalBotController.isGrabbingObjectAnimation
                    || lethalBotController.inTerminalMenu
                    || lethalBotController.isTypingChat)
                {
                    lethalBotController.performingEmote = false;
                    this.LethalBotAIController.SyncStopPerformingEmote();
                }
            }
        }

        /// <summary>
        /// Update and sync with clients, if the bot is sinking or not and should die or not
        /// </summary>
        private void UpdateSinkingStateForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_HINDEREDMOVEMENT, lethalBotController.isMovementHindered > 0);
            if (lethalBotController.sourcesCausingSinking == 0)
            {
                if (lethalBotController.isSinking)
                {
                    lethalBotController.isSinking = false;
                    lethalBotController.StopSinkingServerRpc();
                }
            }
            else
            {
                if (lethalBotController.isSinking)
                {
                    lethalBotController.GetCurrentMaterialStandingOn(checkStandingOnTerrain: true);
                    if (!CheckConditionsForSinkingInQuicksandLethalBot())
                    {
                        lethalBotController.isSinking = false;
                        lethalBotController.StopSinkingServerRpc();
                    }
                }
                else if (!lethalBotController.isSinking && CheckConditionsForSinkingInQuicksandLethalBot())
                {
                    lethalBotController.isSinking = true;
                    lethalBotController.StartSinkingServerRpc(lethalBotController.sinkingSpeedMultiplier, lethalBotController.statusEffectAudioIndex);
                }
                if (lethalBotController.sinkingValue >= 1f)
                {
                    Plugin.LogDebug($"SyncKillLethalBot from sinkingValue for LOCAL client #{lethalBotController.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotController.playerClientId}");
                    lethalBotController.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Suffocation);
                }
                else if (lethalBotController.sinkingValue > 0.5f)
                {
                    lethalBotController.Crouch(false);
                }
            }
        }

        /// <summary>
        /// Update the center and the height of the <c>CharacterController</c>
        /// </summary>
        private void UpdateCenterAndHeightForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isCrouching)
            {
                lethalBotController.thisController.center = Vector3.Lerp(lethalBotController.thisController.center, new Vector3(lethalBotController.thisController.center.x, 0.72f, lethalBotController.thisController.center.z), 8f * Time.deltaTime);
                lethalBotController.thisController.height = Mathf.Lerp(lethalBotController.thisController.height, 1.5f, 8f * Time.deltaTime);
            }
            else
            {
                lethalBotController.crouchMeter = Mathf.Max(lethalBotController.crouchMeter - Time.deltaTime * 2f, 0f);
                lethalBotController.thisController.center = Vector3.Lerp(lethalBotController.thisController.center, new Vector3(lethalBotController.thisController.center.x, 1.28f, lethalBotController.thisController.center.z), 8f * Time.deltaTime);
                lethalBotController.thisController.height = Mathf.Lerp(lethalBotController.thisController.height, 2.5f, 8f * Time.deltaTime);
            }
            // We update the radius of the controller to match the bot's radius
            // NEEDTOVALIDATE: Should I also update the height of the controller?
            // I run into the potential issue of where the bot is too tall and fails to path through some areas!
            LethalBotAIController.agent.radius = lethalBotController.thisController.radius;
            //LethalBotAIController.agent.height = 1.5f; // For now set the crouched height! // Not used for now!
        }

        /// <summary>
        /// Update the rotation of the controller when using jetpack controls
        /// </summary>
        private void UpdateJetPackControlsForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            if (this.disabledJetpackControlsThisFrame)
            {
                this.disabledJetpackControlsThisFrame = false;
            }
            if (lethalBotController.jetpackControls)
            {
                if (lethalBotController.disablingJetpackControls && IsTouchingGround)
                {
                    this.disabledJetpackControlsThisFrame = true;
                    this.LethalBotAIController.SyncDisableJetpackMode();
                }
                else if (!IsTouchingGround)
                {
                    if (!lethalBotController.startedJetpackControls)
                    {
                        lethalBotController.startedJetpackControls = true;
                        lethalBotController.jetpackTurnCompass.rotation = lethalBotController.transform.rotation;
                    }
                    lethalBotController.thisController.radius = Mathf.Lerp(lethalBotController.thisController.radius, 1.25f, 10f * Time.deltaTime);
                    Quaternion rotation = lethalBotController.jetpackTurnCompass.rotation;
                    lethalBotController.jetpackTurnCompass.Rotate(new Vector3(0f, 0f, -lethalBotController.moveInputVector.x) * (180f * Time.deltaTime), Space.Self);
                    if (lethalBotController.maxJetpackAngle != -1f && Vector3.Angle(lethalBotController.jetpackTurnCompass.up, Vector3.up) > lethalBotController.maxJetpackAngle)
                    {
                        lethalBotController.jetpackTurnCompass.rotation = rotation;
                    }
                    rotation = lethalBotController.jetpackTurnCompass.rotation;
                    lethalBotController.jetpackTurnCompass.Rotate(new Vector3(lethalBotController.moveInputVector.y, 0f, 0f) * (180f * Time.deltaTime), Space.Self);
                    if (lethalBotController.maxJetpackAngle != -1f && Vector3.Angle(lethalBotController.jetpackTurnCompass.up, Vector3.up) > lethalBotController.maxJetpackAngle)
                    {
                        lethalBotController.jetpackTurnCompass.rotation = rotation;
                    }
                    if (lethalBotController.jetpackRandomIntensity != -1f)
                    {
                        rotation = lethalBotController.jetpackTurnCompass.rotation;
                        Vector3 a2 = new Vector3(
                            Mathf.Clamp(
                                Random.Range(-lethalBotController.jetpackRandomIntensity, lethalBotController.jetpackRandomIntensity),
                            -lethalBotController.maxJetpackAngle, lethalBotController.maxJetpackAngle),
                            Mathf.Clamp(
                                Random.Range(-lethalBotController.jetpackRandomIntensity, lethalBotController.jetpackRandomIntensity), -lethalBotController.maxJetpackAngle, lethalBotController.maxJetpackAngle),
                            Mathf.Clamp(Random.Range(-lethalBotController.jetpackRandomIntensity, lethalBotController.jetpackRandomIntensity), -lethalBotController.maxJetpackAngle, lethalBotController.maxJetpackAngle));
                        lethalBotController.jetpackTurnCompass.Rotate(a2 * Time.deltaTime, Space.Self);
                        if (lethalBotController.maxJetpackAngle != -1f && Vector3.Angle(lethalBotController.jetpackTurnCompass.up, Vector3.up) > lethalBotController.maxJetpackAngle)
                        {
                            lethalBotController.jetpackTurnCompass.rotation = rotation;
                        }
                    }
                    lethalBotController.transform.rotation = Quaternion.Slerp(lethalBotController.transform.rotation, lethalBotController.jetpackTurnCompass.rotation, 8f * Time.deltaTime);
                }
            }
        }

        /// <summary>
        /// Move the body of bot
        /// </summary>
        private void UpdateMoveControllerForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isFreeCamera)
            {
                lethalBotController.moveInputVector = Vector2.zero;
            }
            float num3 = lethalBotController.movementSpeed / lethalBotController.carryWeight;
            if (lethalBotController.sinkingValue > 0.73f)
            {
                num3 = 0f;
            }
            else
            {
                if (lethalBotController.isCrouching)
                {
                    num3 /= 1.5f;
                }
                else if (lethalBotController.criticallyInjured && !lethalBotController.isCrouching)
                {
                    //Plugin.LogDebug($"Bot {lethalBotController.playerUsername} Limp Multiplier: {LimpMultiplier}");
                    num3 *= lethalBotController.limpMultiplier;
                }
                if (lethalBotController.isSpeedCheating)
                {
                    num3 *= 15f;
                }
                if (movementHinderedPrev > 0)
                {
                    num3 /= 2f * lethalBotController.hinderedMultiplier;
                }
                if (lethalBotController.drunkness > 0f)
                {
                    num3 *= instanceSOR.drunknessSpeedEffect.Evaluate(lethalBotController.drunkness) / 5f + 1f;
                }
                if (lethalBotController.poison > 0f)
                {
                    num3 *= 0.75f;
                }
                if (!lethalBotController.isCrouching && lethalBotController.crouchMeter > 1.2f)
                {
                    num3 *= 0.5f;
                }
            }
            if (lethalBotController.isTypingChat || lethalBotController.disableMoveInput || lethalBotController.jetpackControls && !IsTouchingGround || instanceSOR.suckingPlayersOutOfShip)
            {
                lethalBotController.moveInputVector = Vector2.zero;
            }

            float num7 = 1f;
            if (lethalBotController.isFallingFromJump || lethalBotController.isFallingNoJump)
            {
                num7 = 1.33f;
            }
            else if (lethalBotController.drunkness > 0.3f)
            {
                num7 = Mathf.Clamp(Mathf.Abs(lethalBotController.drunkness - 2.25f), 0.3f, 2.5f);
            }
            else if (lethalBotController.poison > 0.3f)
            {
                num7 = Mathf.Clamp(Mathf.Abs(lethalBotController.poison - 2.25f), 0.3f, 2.5f);
            }
            else if (!lethalBotController.isCrouching && lethalBotController.crouchMeter > 1f)
            {
                num7 = 15f;
            }
            else if (lethalBotController.isSprinting)
            {
                num7 = 5f / (lethalBotController.carryWeight * 1.5f);
            }
            else
            {
                num7 = 10f / lethalBotController.carryWeight;
            }
            walkForce = Vector3.MoveTowards(walkForce, lethalBotController.transform.right * lethalBotController.moveInputVector.x + lethalBotController.transform.forward * lethalBotController.moveInputVector.y, num7 * Time.deltaTime);
            Vector3 vector2 = walkForce * num3 * sprintMultiplier + new Vector3(0f, lethalBotController.fallValue, 0f) + NearEntitiesPushVector;
            vector2 += lethalBotController.externalForces;
            if (lethalBotController.externalForceAutoFade.sqrMagnitude > 0.05f * 0.05f)
            {
                vector2 += lethalBotController.externalForceAutoFade;
                lethalBotController.externalForceAutoFade = Vector3.Lerp(lethalBotController.externalForceAutoFade, Vector3.zero, 2f * Time.deltaTime);
            }

            lethalBotController.playerSlidingTimer = 0f;
            NearEntitiesPushVector = Vector3.zero;

            // Move
            MoveVector = vector2;
        }

        /// <summary>
        /// Check if the bot is falling and update values accordingly
        /// </summary>
        private void UpdateFallValuesForOwner()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation && !lethalBotController.inShockingMinigame)
            {
                return;
            }

            if (!IsTouchingGround)
            {
                if (lethalBotController.jetpackControls && !lethalBotController.disablingJetpackControls)
                {
                    lethalBotController.fallValue = Mathf.MoveTowards(lethalBotController.fallValue, lethalBotController.jetpackCounteractiveForce, 9f * Time.deltaTime);
                    lethalBotController.fallValueUncapped = -8f;
                }
                else
                {
                    lethalBotController.fallValue = Mathf.Clamp(lethalBotController.fallValue - 38f * Time.deltaTime, -150f, lethalBotController.jumpForce);
                    if (Mathf.Abs(lethalBotController.externalForceAutoFade.y) - Mathf.Abs(lethalBotController.fallValue) < 5f)
                    {
                        if (lethalBotController.disablingJetpackControls)
                        {
                            lethalBotController.fallValueUncapped -= 26f * Time.deltaTime;
                        }
                        else
                        {
                            lethalBotController.fallValueUncapped -= 38f * Time.deltaTime;
                        }
                    }
                }
                if (!lethalBotController.isJumping && !lethalBotController.isFallingFromJump)
                {
                    if (!lethalBotController.isFallingNoJump)
                    {
                        lethalBotController.isFallingNoJump = true;
                        //Plugin.LogDebug($"{lethalBotController.playerUsername} isFallingNoJump true");
                        lethalBotController.fallValue = -7f;
                        lethalBotController.fallValueUncapped = -7f;
                    }
                    else if (lethalBotController.fallValue < -20f)
                    {
                        lethalBotController.isCrouching = false;
                        lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CROUCHING, false);
                        lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, true);
                    }
                }
                if (lethalBotController.fallValueUncapped < -35f)
                {
                    lethalBotController.takingFallDamage = true;
                }
            }
            else
            {
                movementHinderedPrev = lethalBotController.isMovementHindered;
                if (!lethalBotController.isJumping)
                {
                    if (lethalBotController.isFallingNoJump)
                    {
                        lethalBotController.isFallingNoJump = false;
                        if (!lethalBotController.isCrouching && lethalBotController.fallValue < -9f)
                        {
                            lethalBotController.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_SHORTFALLLANDING);
                        }
                        //Plugin.LogDebug($"{lethalBotController.playerUsername} JustTouchedGround fallValue {lethalBotController.fallValue}");
                        lethalBotController.PlayerHitGroundEffects();
                    }
                    //if (!IsFallingFromJump)
                    //{
                    //    lethalBotController.fallValue = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                    //    lethalBotController.fallValueUncapped = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                    //}
                }
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, false);
            }
        }

        /// <summary>
        /// Update movement when using jetpack controls
        /// </summary>
        private void UpdateJetPackMoveValuesForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.jetpackControls || lethalBotController.disablingJetpackControls)
            {
                if (!lethalBotController.teleportingThisFrame && !lethalBotController.inSpecialInteractAnimation && !lethalBotController.enteringSpecialAnimation && !lethalBotController.isClimbingLadder && (instanceSOR.timeSinceRoundStarted > 1f || instanceSOR.testRoom != null))
                {
                    if (lethalBotController.getAverageVelocityInterval <= 0f)
                    {
                        float magnitude2 = lethalBotController.thisController.velocity.magnitude;
                        lethalBotController.getAverageVelocityInterval = 0.04f;
                        lethalBotController.velocityAverageCount++;
                        if (lethalBotController.velocityAverageCount > lethalBotController.velocityMovingAverageLength)
                        {
                            lethalBotController.averageVelocity += (magnitude2 - lethalBotController.averageVelocity) / (float)(lethalBotController.velocityMovingAverageLength + 1);
                        }
                        else
                        {
                            lethalBotController.averageVelocity += magnitude2;
                            if (lethalBotController.velocityAverageCount == lethalBotController.velocityMovingAverageLength)
                            {
                                lethalBotController.averageVelocity /= (float)lethalBotController.velocityAverageCount;
                            }
                        }
                    }
                    else
                    {
                        lethalBotController.getAverageVelocityInterval -= Time.deltaTime;
                    }
                    if (lethalBotController.timeSinceTakingGravityDamage > 0.6f && lethalBotController.velocityAverageCount > 4)
                    {
                        float num8 = Vector3.Angle(lethalBotController.transform.up, Vector3.up);
                        if (Physics.CheckSphere(lethalBotController.gameplayCamera.transform.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
                            || (num8 > 65f && Physics.CheckSphere(lethalBotController.lowerSpine.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
                        {
                            if (lethalBotController.averageVelocity > 17f)
                            {
                                lethalBotController.timeSinceTakingGravityDamage = 0f;
                                lethalBotController.DamagePlayer(Mathf.Clamp(85, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(lethalBotController.velocityLastFrame, 50f));
                            }
                            else if (lethalBotController.averageVelocity > 9f)
                            {
                                lethalBotController.DamagePlayer(Mathf.Clamp(30, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(lethalBotController.velocityLastFrame, 50f));
                                lethalBotController.timeSinceTakingGravityDamage = 0.35f;
                            }
                            else if (num8 > 60f && lethalBotController.averageVelocity > 6f)
                            {
                                lethalBotController.DamagePlayer(Mathf.Clamp(30, 20, 100), hasDamageSFX: true, callRPC: true, CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(lethalBotController.velocityLastFrame, 50f));
                                lethalBotController.timeSinceTakingGravityDamage = 0f;
                            }
                        }
                    }
                    else
                    {
                        lethalBotController.timeSinceTakingGravityDamage += Time.deltaTime;
                    }
                    lethalBotController.velocityLastFrame = lethalBotController.thisController.velocity;
                    lethalBotController.previousFrameDeltaTime = Time.deltaTime;
                }
                else
                {
                    lethalBotController.teleportingThisFrame = false;
                }
            }
            else
            {
                lethalBotController.averageVelocity = 0f;
                lethalBotController.velocityAverageCount = 0;
                lethalBotController.timeSinceTakingGravityDamage = 0f;
            }
        }

        /// <summary>
        /// Update movement when using ladder
        /// </summary>
        private void UpdateMoveWhenClimbingLadder()
        {
            PlayerControllerB lethalBotController = Npc;
            Vector3 direction = lethalBotController.thisPlayerBody.up;
            if ((lethalBotController.externalForces + lethalBotController.externalForceAutoFade).sqrMagnitude > 8f * 8f)
            {
                lethalBotController.CancelSpecialTriggerAnimations();
            }
            PreviousExternalForces = lethalBotController.externalForces;
            lethalBotController.externalForces = Vector3.zero;
            lethalBotController.externalForceAutoFade = Vector3.Lerp(lethalBotController.externalForceAutoFade, Vector3.zero, 5f * Time.deltaTime);

            if (goDownLadder)
            {
                direction = -lethalBotController.thisPlayerBody.up;
            }
            lethalBotController.thisPlayerBody.transform.position += direction * (Const.BASE_MAX_SPEED * lethalBotController.climbSpeed * Time.deltaTime);
            //if (!Physics.Raycast(origin, direction, 0.15f, StartOfRound.Instance.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore))
            //{
            //    lethalBotController.thisPlayerBody.transform.position += direction * (Const.BASE_MAX_SPEED * lethalBotController.climbSpeed * Time.deltaTime);
            //}
        }

        private void UpdateAnimationsForOwner()
        {
            //Plugin.LogDebug($"animationSpeed {lethalBotController.playerBodyAnimator.GetFloat("animationSpeed")}");
            //for (int i = 0; i < lethalBotController.playerBodyAnimator.layerCount; i++)
            //{
            //    Plugin.LogDebug($"layer {i}, {lethalBotController.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash}");
            //}

            // Update this so we can send the layers to other clients!
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_WALKING) != lethalBotController.isWalking)
            {
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, lethalBotController.isWalking);
            }
            if (lethalBotController.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING) != lethalBotController.isSprinting)
            {
                lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, lethalBotController.isSprinting);
            }

            // Save current layers to be sent to other players
            for (int i = 0; i < lethalBotController.playerBodyAnimator.layerCount; i++)
            {
                animationHashLayers[i] = lethalBotController.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash;
            }

            if (NetworkManager.Singleton != null)
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
            PlayerControllerB lethalBotController = Npc;
            if (!lethalBotController.isPlayerDead && lethalBotController.isPlayerControlled)
            {
                if (!lethalBotController.disableSyncInAnimation)
                {
                    if (lethalBotController.snapToServerPosition)
                    {
                        lethalBotController.transform.localPosition = Vector3.Lerp(lethalBotController.transform.localPosition, lethalBotController.serverPlayerPosition, 16f * Time.deltaTime);
                    }
                    else
                    {
                        float num10 = 8f;
                        if (lethalBotController.jetpackControls)
                        {
                            num10 = 15f;
                        }
                        float num11 = Mathf.Clamp(num10 * Vector3.Distance(lethalBotController.transform.localPosition, lethalBotController.serverPlayerPosition), 0.9f, 300f);
                        lethalBotController.transform.localPosition = Vector3.MoveTowards(lethalBotController.transform.localPosition, lethalBotController.serverPlayerPosition, num11 * Time.deltaTime);
                    }
                }

                // Rotations
                this.UpdateTurnBodyTowardsDirection();
                this.UpdateLookAt();
                lethalBotController.playerEye.position = lethalBotController.gameplayCamera.transform.position;
                lethalBotController.playerEye.rotation = lethalBotController.gameplayCamera.transform.rotation;
            }
            else if ((lethalBotController.isPlayerDead || !lethalBotController.isPlayerControlled) && lethalBotController.setPositionOfDeadPlayer)
            {
                lethalBotController.transform.position = lethalBotController.playersManager.notSpawnedPosition.position;
            }
        }

        private void UpdateLethalBotAnimationsLocalForNotOwner(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;

                // If animation
                // Update animation if current != previous
                this.currentAnimationSpeed = lethalBotController.playerBodyAnimator.GetFloat("animationSpeed");
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
            PlayerControllerB lethalBotController = Npc;
            if (!lethalBotController.inSpecialInteractAnimation)
            {
                if (lethalBotController.playingQuickSpecialAnimation)
                {
                    lethalBotController.specialAnimationWeight = 1f;
                }
                else
                {
                    lethalBotController.specialAnimationWeight = Mathf.Lerp(lethalBotController.specialAnimationWeight, 0f, Time.deltaTime * 12f);
                }
                if (!lethalBotController.localArmsMatchCamera)
                {
                    lethalBotController.localArmsTransform.position = lethalBotController.playerModelArmsMetarig.position + lethalBotController.playerModelArmsMetarig.forward * -0.445f;
                    lethalBotController.playerModelArmsMetarig.rotation = Quaternion.Lerp(lethalBotController.playerModelArmsMetarig.rotation, lethalBotController.localArmsRotationTarget.rotation, 15f * Time.deltaTime);
                }
            }
            else
            {
                if ((!lethalBotController.isClimbingLadder && !lethalBotController.inShockingMinigame) || lethalBotController.freeRotationInInteractAnimation)
                {
                    lethalBotController.cameraUp = Mathf.Lerp(lethalBotController.cameraUp, 0f, 5f * Time.deltaTime);
                    lethalBotController.gameplayCamera.transform.localEulerAngles = new Vector3(lethalBotController.cameraUp, lethalBotController.gameplayCamera.transform.localEulerAngles.y, lethalBotController.gameplayCamera.transform.localEulerAngles.z);
                }
                lethalBotController.specialAnimationWeight = Mathf.Lerp(lethalBotController.specialAnimationWeight, 1f, Time.deltaTime * 20f);
                lethalBotController.playerModelArmsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);
            }
        }
        /// <summary>
        /// Update animation layer when using emotes
        /// </summary>
        private void UpdateEmoteEffects()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.doingUpperBodyEmote > 0f)
            {
                lethalBotController.doingUpperBodyEmote -= Time.deltaTime;
            }

            if (lethalBotController.performingEmote)
            {
                lethalBotController.emoteLayerWeight = Mathf.Lerp(lethalBotController.emoteLayerWeight, 1f, 10f * Time.deltaTime);
            }
            else
            {
                lethalBotController.emoteLayerWeight = Mathf.Lerp(lethalBotController.emoteLayerWeight, 0f, 10f * Time.deltaTime);
            }
            lethalBotController.playerBodyAnimator.SetLayerWeight(lethalBotController.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_EMOTESNOARMS), lethalBotController.emoteLayerWeight);
        }
        /// <summary>
        /// Update the sinking values and effect
        /// </summary>
        private void UpdateSinkingEffects()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = Npc;
            lethalBotController.meshContainer.position = Vector3.Lerp(lethalBotController.transform.position, lethalBotController.transform.position - Vector3.up * 2.8f, instanceSOR.playerSinkingCurve.Evaluate(lethalBotController.sinkingValue));
            if (lethalBotController.isSinking && !lethalBotController.inSpecialInteractAnimation && lethalBotController.inAnimationWithEnemy == null)
            {
                lethalBotController.sinkingValue = Mathf.Clamp(lethalBotController.sinkingValue + Time.deltaTime * lethalBotController.sinkingSpeedMultiplier, 0f, 1f);
            }
            else
            {
                lethalBotController.sinkingValue = Mathf.Clamp(lethalBotController.sinkingValue - Time.deltaTime * 0.75f, 0f, 1f);
            }
            if (lethalBotController.sinkingValue > 0.73f || lethalBotController.isUnderwater)
            {
                if (!this.wasUnderwaterLastFrame)
                {
                    this.wasUnderwaterLastFrame = true;
                    lethalBotController.waterBubblesAudio.Play();
                }
                lethalBotController.voiceMuffledByEnemy = true;
                lethalBotController.statusEffectAudio.volume = Mathf.Lerp(lethalBotController.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                OccludeAudioComponent.overridingLowPass = true;
                OccludeAudioComponent.lowPassOverride = 600f;
                lethalBotController.waterBubblesAudio.volume = Mathf.Clamp(LethalBotAIController.LethalBotIdentity.Voice.GetVoiceAmplitude() * 120f, 0f, 1f);
            }
            else if (this.wasUnderwaterLastFrame)
            {
                lethalBotController.waterBubblesAudio.Stop();
                this.wasUnderwaterLastFrame = false;
                lethalBotController.voiceMuffledByEnemy = false;
            }
            else
            {
                lethalBotController.statusEffectAudio.volume = Mathf.Lerp(lethalBotController.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
            }
        }
        /// <summary>
        /// Update the active audio reverb filter
        /// </summary>
        private void UpdateActiveAudioReverbFilter()
        {
            GameNetworkManager instanceGNM = GameNetworkManager.Instance;
            StartOfRound instanceSOR = StartOfRound.Instance;
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.activeAudioReverbFilter == null)
            {
                lethalBotController.activeAudioReverbFilter = lethalBotController.activeAudioListener.GetComponent<AudioReverbFilter>();
                lethalBotController.activeAudioReverbFilter.enabled = true;
            }
            if (lethalBotController.reverbPreset != null && instanceGNM != null && instanceGNM.localPlayerController != null
                && ((instanceGNM.localPlayerController == lethalBotController
                && (!lethalBotController.isPlayerDead || instanceSOR.overrideSpectateCamera)) || (instanceGNM.localPlayerController.spectatedPlayerScript == lethalBotController && !instanceSOR.overrideSpectateCamera)))
            {
                AudioReverbFilter audioReverbFilter = lethalBotController.activeAudioReverbFilter;
                ReverbPreset reverbPreset = lethalBotController.reverbPreset;
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
            PlayerControllerB lethalBotController = Npc;
            int indexLayerHoldingItemsRightHand = lethalBotController.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSRIGHTHAND);
            int indexLayerHoldingItemsBothHands = lethalBotController.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSBOTHHANDS);
            if (lethalBotController.isHoldingObject || lethalBotController.isGrabbingObjectAnimation || lethalBotController.inShockingMinigame)
            {
                lethalBotController.upperBodyAnimationsWeight = Mathf.Lerp(lethalBotController.upperBodyAnimationsWeight, 1f, 25f * Time.deltaTime);
                lethalBotController.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, lethalBotController.upperBodyAnimationsWeight);
                if (lethalBotController.twoHandedAnimation || lethalBotController.inShockingMinigame)
                {
                    lethalBotController.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, lethalBotController.upperBodyAnimationsWeight);
                }
                else
                {
                    lethalBotController.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, Mathf.Abs(lethalBotController.upperBodyAnimationsWeight - 1f));
                }
            }
            else
            {
                lethalBotController.upperBodyAnimationsWeight = Mathf.Lerp(lethalBotController.upperBodyAnimationsWeight, 0f, 25f * Time.deltaTime);
                lethalBotController.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, lethalBotController.upperBodyAnimationsWeight);
                lethalBotController.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, lethalBotController.upperBodyAnimationsWeight);
            }

            lethalBotController.playerBodyAnimator.SetLayerWeight(lethalBotController.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_SPECIALANIMATIONS), lethalBotController.specialAnimationWeight);
            if (lethalBotController.inSpecialInteractAnimation && !lethalBotController.inShockingMinigame)
            {
                lethalBotController.cameraLookRig1.weight = Mathf.Lerp(lethalBotController.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
                lethalBotController.cameraLookRig2.weight = Mathf.Lerp(lethalBotController.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
            }
            else
            {
                lethalBotController.cameraLookRig1.weight = 0.45f;
                lethalBotController.cameraLookRig2.weight = 1f;
            }
            if (lethalBotController.isExhausted)
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 1f, 10f * Time.deltaTime);
            }
            else
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 0f, 10f * Time.deltaTime);
            }
            lethalBotController.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_TIREDAMOUNT, this.exhaustionEffectLerp);
        }

        /// <summary>
        /// Update the bleeding effects for bots!
        /// </summary>
        private void UpdateBleedEffects()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.bleedingHeavily && lethalBotController.bloodDropTimer >= 0f)
            {
                lethalBotController.bloodDropTimer -= Time.deltaTime;
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isExhausted)
            {
                WaitForFullStamina = true;
            }
            else if (WaitForFullStamina && lethalBotController.sprintMeter >= 0.8f)
            {
                WaitForFullStamina = false;
            }
        }

        /// <summary>
        /// Updates the drunkness and posion effects for the bot's <see cref="PlayerControllerB"/>!
        /// </summary>
        private void UpdateDrunknessAndPoisonEffects()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isPlayerDead)
            {
                lethalBotController.drunkness = 0f;
                lethalBotController.drunknessInertia = 0f;
                lethalBotController.poison = 0f;
            }
            else
            {
                if (lethalBotController.slimeOnFace >= 0f)
                {
                    lethalBotController.slimeOnFace -= Time.deltaTime;
                    lethalBotController.slimeOnFaceDecals[0].fadeFactor = Mathf.Min(lethalBotController.slimeOnFace, 1f);
                    lethalBotController.slimeOnFaceDecals[1].fadeFactor = Mathf.Min(lethalBotController.slimeOnFace, 1f);
                }
                lethalBotController.drunkness = Mathf.Clamp(lethalBotController.drunkness + Time.deltaTime / 12f * lethalBotController.drunknessSpeed * lethalBotController.drunknessInertia, 0f, 1f);
                if (!lethalBotController.increasingDrunknessThisFrame)
                {
                    if (lethalBotController.drunkness > 0f)
                    {
                        lethalBotController.drunknessInertia = Mathf.Clamp(lethalBotController.drunknessInertia - Time.deltaTime / 3f * lethalBotController.drunknessSpeed / Mathf.Clamp(Mathf.Abs(lethalBotController.drunknessInertia), 0.2f, 1f), -2.5f, 2.5f);
                    }
                    else
                    {
                        lethalBotController.drunknessInertia = 0f;
                    }
                }
                else
                {
                    lethalBotController.increasingDrunknessThisFrame = false;
                }
                if (!lethalBotController.overridePoisonValue)
                {
                    lethalBotController.poison = Mathf.Clamp(lethalBotController.poison + Time.deltaTime / 12f * lethalBotController.poisonSpeed * lethalBotController.poisonInertia, 0f, 1f);
                }
                if (!lethalBotController.increasingPoisonThisFrame)
                {
                    if (lethalBotController.poison > 0f)
                    {
                        lethalBotController.poisonInertia = Mathf.Clamp(lethalBotController.poisonInertia - Time.deltaTime / 3f * lethalBotController.poisonSpeed / Mathf.Clamp(Mathf.Abs(lethalBotController.poisonInertia), 0.2f, 1f), -2.5f, 2.5f);
                    }
                    else
                    {
                        lethalBotController.poisonInertia = 0f;
                    }
                }
                else
                {
                    lethalBotController.increasingPoisonThisFrame = false;
                }
                float num11 = StartOfRound.Instance.drunknessSideEffect.Evaluate(lethalBotController.drunkness);
                LethalBotVoice lethalBotVoice = LethalBotAIController.LethalBotIdentity.Voice;
                float botVoicePitch = lethalBotVoice.VoicePitch;
                if (num11 > 0.15f)
                {
                    SoundManager.Instance.playerVoicePitchTargets[lethalBotController.playerClientId] = botVoicePitch + num11;
                }
                else
                {
                    SoundManager.Instance.playerVoicePitchTargets[lethalBotController.playerClientId] = botVoicePitch;
                }
                //SoundManager.Instance.playerVoiceVolumes[lethalBotController.playerClientId] = lethalBotVoice.Volume;
            }
        }

        /// <summary>
        /// I have no idea what the line of sight cube is, but it exists, so I need to update it!
        /// </summary>
        /// <remarks>
        /// Apparently its used by <see cref="EnemyAI.PathIsIntersectedByLineOfSight(Vector3, bool, bool, bool)"/>. Bit of a weird way to check LOS, but oh well.
        /// </remarks>
        private void UpdateLineOfSightCube()
        {
            PlayerControllerB lethalBotController = Npc;
            if (Physics.Raycast(lethalBotController.lineOfSightCube.position, lethalBotController.lineOfSightCube.forward, out var hit, 10f, lethalBotController.playersManager.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
            {
                lethalBotController.lineOfSightCube.localScale = new Vector3(1.5f, 1.5f, hit.distance);
            }
            else
            {
                lethalBotController.lineOfSightCube.localScale = new Vector3(1.5f, 1.5f, 10f);
            }
        }

        #endregion

        #region Animations

        private void UpdateLethalBotAnimationsToOtherClients(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;
                this.currentAnimationSpeed = lethalBotController.playerBodyAnimator.GetFloat("animationSpeed");
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.playerBodyAnimator.GetFloat("animationSpeed") != animationSpeed)
            {
                lethalBotController.playerBodyAnimator.SetFloat("animationSpeed", animationSpeed);
            }

            if (animationState != 0 && lethalBotController.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != animationState)
            {
                for (int i = 0; i < lethalBotController.playerBodyAnimator.layerCount; i++)
                {
                    if (lethalBotController.playerBodyAnimator.HasState(i, animationState))
                    {
                        animationHashLayers[i] = animationState;
                        lethalBotController.playerBodyAnimator.CrossFadeInFixedTime(animationState, 0.1f);
                        break;
                    }
                }
            }
        }

        public void StopAnimations()
        {
            PlayerControllerB lethalBotController = Npc;
            lethalBotController.isWalking = false;
            lethalBotController.isSprinting = false;
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, false);
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, false);
            lethalBotController.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
        }

        public void PlayFootstep(bool isServer)
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isClimbingLadder 
                || lethalBotController.inSpecialInteractAnimation 
                || lethalBotController.isCrouching)
            {
                return;
            }

            if ((isServer && !LethalBotAIController.IsOwner && lethalBotController.isPlayerControlled)
                || (!isServer && LethalBotAIController.IsOwner && lethalBotController.isPlayerControlled))
            {
                bool noiseIsInsideClosedShip = lethalBotController.isInHangarShipRoom && lethalBotController.playersManager.hangarDoorsClosed;
                if (lethalBotController.isSprinting)
                {
                    PlayAudibleNoiseLethalBot(lethalBotController.transform.position, 22f, 0.6f, 0, noiseIsInsideClosedShip, 6);
                }
                else
                {
                    PlayAudibleNoiseLethalBot(lethalBotController.transform.position, 17f, 0.4f, 0, noiseIsInsideClosedShip, 6);
                }

                PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
                Vector3 localPlayerPos = localPlayer.transform.position;
                if (localPlayer.isPlayerDead && localPlayer.spectatedPlayerScript != null)
                {
                    localPlayerPos = localPlayer.spectatedPlayerScript.transform.position;
                }
                if ((localPlayerPos - lethalBotController.transform.position).sqrMagnitude < 20f * 20f)
                {
                    lethalBotController.PlayFootstepSound();
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

            PlayerControllerB lethalBotController = Npc;
            foreach (var enemyAINoiseListener in LethalBotManager.Instance.DictEnemyAINoiseListeners)
            {
                EnemyAI enemyAI = enemyAINoiseListener.Key;
                if (enemyAI == null)
                {
                    continue;
                }

                if ((lethalBotController.transform.position - enemyAI.transform.position).sqrMagnitude > noiseRange * noiseRange)
                {
                    continue;
                }

                if (noiseIsInsideClosedShip
                    && !enemyAI.isInsidePlayerShip
                    && noiseLoudness < 0.9f)
                {
                    continue;
                }

                Plugin.LogDebug($"{lethalBotController.playerUsername} Play audible noise for {enemyAI.name}");
                enemyAINoiseListener.Value.DetectNoise(noisePosition, noiseLoudness, timesPlayedInSameSpot, noiseID);
            }
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
            PlayerControllerB lethalBotController = Npc;
            lethalBotController.previousElevatorPosition = lethalBotController.playersManager.elevatorTransform.position;

            if (NetworkManager.Singleton == null)
            {
                return;
            }

            // Text billboard
            if (Plugin.Config.EnableDebugLog.Value || Plugin.Config.ShowBillboardStateIndicator.Value)
            { 
                lethalBotController.usernameBillboardText.text = $"{LethalBotAIController.GetSizedBillboardStateIndicator()}\n"; 
            }
            else
            {
                lethalBotController.usernameBillboardText.text = string.Empty;
            }

            if (instanceGNM.localPlayerController != null && lethalBotController.usernameAlpha.alpha >= 0f)
            {
                lethalBotController.usernameBillboardText.text += lethalBotController.playerUsername;
                if (LethalBotAIController.IsClientOwnerOfLethalBot())
                {
                    lethalBotController.usernameBillboardText.text += $"\nv";
                }

                lethalBotController.usernameAlpha.alpha -= Time.deltaTime;
                UpdateBillBoardLookAtTimedCheck.UpdateBillboardLookAt(lethalBotController, SqrDistanceWithLocalPlayerTimedCheck.GetSqrDistanceWithLocalPlayer(lethalBotController.transform.position) < 10f * 10f);
            }
            else if (lethalBotController.usernameCanvas.gameObject.activeSelf)
            {
                lethalBotController.usernameCanvas.gameObject.SetActive(value: false);
            }

            // Health regen
            LethalBotAIController.HealthRegen();

            if (LethalBotAIController.IsClientOwnerOfLethalBot())
            {
                this.LethalBotRotationAndLookUpdate();

                if (lethalBotController.isPlayerControlled && !lethalBotController.isPlayerDead)
                {
                    if (instanceGNM != null)
                    {
                        float distMaxBeforeUpdating;
                        if (lethalBotController.inSpecialInteractAnimation)
                        {
                            distMaxBeforeUpdating = 0.06f;
                        }
                        else if (IsRealPlayerClose(lethalBotController.transform.position, 10f))
                        {
                            distMaxBeforeUpdating = 0.1f;
                        }
                        else
                        {
                            distMaxBeforeUpdating = 0.24f;
                        }

                        if ((lethalBotController.oldPlayerPosition - lethalBotController.transform.localPosition).sqrMagnitude > distMaxBeforeUpdating || lethalBotController.updatePositionForNewlyJoinedClient)
                        {
                            lethalBotController.updatePositionForNewlyJoinedClient = false;
                            if (!lethalBotController.playersManager.newGameIsLoading)
                            {
                                LethalBotAIController.SyncUpdateLethalBotPosition(lethalBotController.thisPlayerBody.localPosition, lethalBotController.isInElevator, lethalBotController.isInHangarShipRoom, lethalBotController.isExhausted, IsTouchingGround);
                                lethalBotController.serverPlayerPosition = lethalBotController.transform.localPosition;
                                lethalBotController.oldPlayerPosition = lethalBotController.serverPlayerPosition;
                            }
                        }

                        GrabbableObject? currentlyHeldObject = LethalBotAIController.HeldItem;
                        if (currentlyHeldObject != null && lethalBotController.isHoldingObject && lethalBotController.grabbedObjectValidated)
                        {
                            currentlyHeldObject.transform.localPosition = currentlyHeldObject.itemProperties.positionOffset;
                            currentlyHeldObject.transform.localEulerAngles = currentlyHeldObject.itemProperties.rotationOffset;
                        }
                    }

                    float num2 = 1f;
                    if (lethalBotController.drunkness > 0.02f)
                    {
                        num2 *= Mathf.Abs(StartOfRound.Instance.drunknessSpeedEffect.Evaluate(lethalBotController.drunkness) - 1.25f);
                    }
                    if (lethalBotController.isSprinting)
                    {
                        lethalBotController.sprintMeter = Mathf.Clamp(lethalBotController.sprintMeter - Time.deltaTime / lethalBotController.sprintTime * lethalBotController.carryWeight * num2, 0f, 1f);
                    }
                    else if (lethalBotController.isMovementHindered > 0)
                    {
                        if (lethalBotController.isWalking)
                        {
                            lethalBotController.sprintMeter = Mathf.Clamp(lethalBotController.sprintMeter - Time.deltaTime / lethalBotController.sprintTime * num2 * 0.5f, 0f, 1f);
                        }
                    }
                    else
                    {
                        if (!lethalBotController.isWalking)
                        {
                            lethalBotController.sprintMeter = Mathf.Clamp(lethalBotController.sprintMeter + Time.deltaTime / (lethalBotController.sprintTime + 4f) * num2, 0f, 1f);
                        }
                        else
                        {
                            lethalBotController.sprintMeter = Mathf.Clamp(lethalBotController.sprintMeter + Time.deltaTime / (lethalBotController.sprintTime + 9f) * num2, 0f, 1f);
                        }
                        if (lethalBotController.isExhausted && lethalBotController.sprintMeter > 0.2f)
                        {
                            lethalBotController.isExhausted = false;
                        }
                    }
                }
            }
            if (!lethalBotController.inSpecialInteractAnimation && lethalBotController.localArmsMatchCamera)
            {
                lethalBotController.localArmsTransform.position = lethalBotController.cameraContainerTransform.transform.position + lethalBotController.gameplayCamera.transform.up * -0.5f;
                lethalBotController.playerModelArmsMetarig.rotation = lethalBotController.localArmsRotationTarget.rotation;
            }
        }

        public void ReParentNotSpawnedTransform(Transform newParent)
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.transform.parent != newParent)
            {
                foreach (NetworkObject networkObject in lethalBotController.GetComponentsInChildren<NetworkObject>())
                {
                    networkObject.AutoObjectParentSync = false;
                }

                Plugin.LogDebug($"{lethalBotController.playerUsername} ReParent parent before {lethalBotController.transform.parent}");
                lethalBotController.transform.parent = newParent;
                Plugin.LogDebug($"{lethalBotController.playerUsername} ReParent parent after {lethalBotController.transform.parent}");

                foreach (NetworkObject networkObject in lethalBotController.GetComponentsInChildren<NetworkObject>())
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

            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation || (bool)lethalBotController.inAnimationWithEnemy || lethalBotController.isClimbingLadder)
            {
                return false;
            }

            if (lethalBotController.physicsParent != null)
            {
                return false;
            }

            if (lethalBotController.isInHangarShipRoom)
            {
                return false;
            }

            if (lethalBotController.isInElevator)
            {
                return false;
            }

            int currentFootstepSurfaceIndex = lethalBotController.currentFootstepSurfaceIndex;
            if (!lethalBotController.standingOnTerrain 
                && currentFootstepSurfaceIndex != 1
                && currentFootstepSurfaceIndex != 4
                && currentFootstepSurfaceIndex != 8
                && currentFootstepSurfaceIndex != 7
                && (!lethalBotController.isInsideFactory || currentFootstepSurfaceIndex != 5))
            {
                return false;
            }

            return true;
        }

        private bool IsRealPlayerClose(Vector3 thisPosition, float distance)
        {
            PlayerControllerB[] playerControllers = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < playerControllers.Length; i++)
            {
                PlayerControllerB player = playerControllers[i];
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
            PlayerControllerB lethalBotController = Npc;
            if (!lethalBotController.performingEmote && lethalBotController.CheckConditionsForEmote())
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
                        if (ourOwner == null && lethalBotController.OwnerClientId == player.actualClientId)
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
                    if (playerToMimic != null && (lethalBotController.isInElevator || lethalBotController.isInHangarShipRoom))
                    {
                        // This not only performs the same emote, but has support for group emotes!
                        CheckAndPerformTooManyEmote(playerToMimic);
                        return;
                    }

                    // Don't have an owner!? HOW DID THAT HAPPEN, just use ourself
                    if (ourOwner == null)
                    {
                        ourOwner = lethalBotController;
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.performingEmote || forceStop)
            {
                lethalBotController.performingEmote = false;
                lethalBotController.playerBodyAnimator.SetInteger("emoteNumber", 0);
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
            PlayerControllerB lethalBotController = Npc;
            int emoteNumberLethalBot = lethalBotController.playerBodyAnimator.GetInteger("emoteNumber");
            if ((!lethalBotController.performingEmote
                || emoteNumberLethalBot != emoteNumberToMimic)
                && lethalBotController.CheckConditionsForEmote())
            {
                lethalBotController.performingEmote = true;
                lethalBotController.PerformEmote(new UnityEngine.InputSystem.InputAction.CallbackContext(), emoteNumberToMimic);
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
                    // Can't do this, as it assumes the local player if this is the server.
                    // We just recreate the logic intead!
                    //SyncPerformingEmoteManager.SendSyncEmoteUpdateToServer(emoteControllerLethalBot, overrideEmoteId);
                    Plugin.LogInfo("Sending sync emote update to server. Sync with emote controller id: " + emoteControllerLethalBot);
                    FastBufferWriter messageStream = new FastBufferWriter(4, Allocator.Temp);
                    messageStream.WriteValue<ushort>((ushort)emoteControllerLethalBot.emoteControllerId, default(FastBufferWriter.ForPrimitives));
                    messageStream.WriteValue<short>((short)overrideEmoteId, default(FastBufferWriter.ForPrimitives));
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("TooManyEmotes.SyncEmoteServerRpc", 0uL, messageStream);
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
            PlayerControllerB lethalBotController = Npc;
            if (!lethalBotController.isPlayerControlled)
            {
                return;
            }

            if (lethalBotController.playersManager.newGameIsLoading
                || lethalBotController.disableLookInput)
            {
                return;
            }

            if (this.oldLookAtTarget == this.LookAtTarget)
            {
                return;
            }

            // Update after some interval of time
            // Only if there's at least one player near
            // Disabling IsRealPlayerClose(lethalBotController.transform.position, 35f) as it causes the bots not to update
            // if there are no alive players nearby which affects spectating players!
            // As well as players on the ship monitoring the bots!
            if (lethalBotController.updatePlayerLookInterval > 0.25f)
            {
                lethalBotController.updatePlayerLookInterval = 0f;
                LethalBotAIController.SyncUpdateLethalBotRotationAndLook(LethalBotAIController.State?.GetBillboardStateIndicator() ?? string.Empty,
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation || !IsTouchingGround || lethalBotController.isClimbingLadder)
            {
                return;
            }
            if (lethalBotController.isJumping)
            {
                return;
            }
            if (lethalBotController.isExhausted)
            {
                floatSprint = 0f;
                return;
            }
            if (lethalBotController.isSprinting)
            {
                return;
            }
            // Don't sprint if we are trying to crouch!
            if (LethalBotAIController != null)
            {
                bool? shouldCrouch = LethalBotAIController.State?.ShouldBotCrouch();
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation || !IsTouchingGround || lethalBotController.isClimbingLadder)
            {
                return;
            }
            if (lethalBotController.isJumping)
            {
                return;
            }
            if (lethalBotController.isSprinting && !lethalBotController.isCrouching)
            {
                return;
            }
            lethalBotController.crouchMeter = Mathf.Min(lethalBotController.crouchMeter + 0.3f, 1.3f);
            lethalBotController.Crouch(!lethalBotController.isCrouching);
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
        /// <param name="playerToLookAt"></param>
        public void OrderToLookAtPlayer(PlayerControllerB playerToLookAt)
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.LookAtTarget.AimHeadTowards(playerToLookAt.NetworkObject, EnumLookAtPriority.MEDIUM_PRIORITY, bypassSteadyCheck: true);
        }

        /// <summary>
        /// Make the controller look straight forward
        /// </summary>
        public void OrderToLookForward()
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
            this.LookAtTarget.lookAtPriority = EnumLookAtPriority.LOW_PRIORITY; // HACKHACK: Reset LookAtPriority
        }

        /// <inheritdoc cref="LookAtTarget.AimHeadTowards(Vector3, EnumLookAtPriority, float, bool, float)"/>
        public void OrderToLookAtPosition(Vector3 lookAtPos, EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, float duration = 0.0f, bool bypassSteadyCheck = false, float maxBodyFOV = Const.LETHAL_BOT_FOV)
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.LookAtTarget.AimHeadTowards(lookAtPos, priority, duration, bypassSteadyCheck, maxBodyFOV);
        }

        /// <inheritdoc cref="LookAtTarget.AimHeadTowards(NetworkObjectReference, EnumLookAtPriority, float, bool, float)"/>
        public void OrderToLookAtPosition(NetworkObject lookAtSubject, EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, float duration = 0.0f, bool bypassSteadyCheck = false, float maxBodyFOV = Const.LETHAL_BOT_FOV)
        {
            this.LookAtTarget.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.LookAtTarget.AimHeadTowards(lookAtSubject, priority, duration, bypassSteadyCheck, maxBodyFOV);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMoving()
        {
            return MoveVector != Vector3.zero
                || animationHashLayers[0] != Const.IDLE_STATE_HASH
                || Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH;
        }

        private void ForceTurnTowardsTarget()
        {
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.inSpecialInteractAnimation && lethalBotController.inShockingMinigame && lethalBotController.shockingTarget != null)
            {
                // Tell the bot to keep the beam steady
                const float maxFOV = 60f; // Found in source code!
                OrderToLookAtPosition(lethalBotController.shockingTarget.position, EnumLookAtPriority.MAXIMUM_PRIORITY, maxBodyFOV: maxFOV);

                // 1:1 copy of the code that would normally be run in default PlayerControllerB update!
                lethalBotController.targetScreenPos = lethalBotController.turnCompassCamera.WorldToViewportPoint(lethalBotController.shockingTarget.position);
                lethalBotController.shockMinigamePullPosition = lethalBotController.targetScreenPos.x - 0.5f;
                float num = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
                if (lethalBotController.targetScreenPos.x > 0.54f)
                {
                    lethalBotController.turnCompass.Rotate(Vector3.up * 2000f * num * Mathf.Abs(lethalBotController.shockMinigamePullPosition));
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraRight", value: false);
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraLeft", value: true);
                }
                else if (lethalBotController.targetScreenPos.x < 0.46f)
                {
                    lethalBotController.turnCompass.Rotate(Vector3.up * -2000f * num * Mathf.Abs(lethalBotController.shockMinigamePullPosition));
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraLeft", value: false);
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraRight", value: true);
                }
                else
                {
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraLeft", value: false);
                    lethalBotController.playerBodyAnimator.SetBool("PullingCameraRight", value: false);
                }
                lethalBotController.targetScreenPos = lethalBotController.gameplayCamera.WorldToViewportPoint(lethalBotController.shockingTarget.position + Vector3.up * 0.35f);
                if (lethalBotController.targetScreenPos.y > 0.6f)
                {
                    lethalBotController.cameraUp = Mathf.Clamp(Mathf.Lerp(lethalBotController.cameraUp, lethalBotController.cameraUp - 25f, 25f * num * Mathf.Abs(lethalBotController.targetScreenPos.y - 0.5f)), -89f, 89f);
                }
                else if (lethalBotController.targetScreenPos.y < 0.35f)
                {
                    lethalBotController.cameraUp = Mathf.Clamp(Mathf.Lerp(lethalBotController.cameraUp, lethalBotController.cameraUp + 25f, 25f * num * Mathf.Abs(lethalBotController.targetScreenPos.y - 0.5f)), -89f, 89f);
                }
                lethalBotController.gameplayCamera.transform.localEulerAngles = new Vector3(lethalBotController.cameraUp, lethalBotController.gameplayCamera.transform.localEulerAngles.y, lethalBotController.gameplayCamera.transform.localEulerAngles.z);
                Vector3 zero = Vector3.zero;
                zero.y = lethalBotController.turnCompass.eulerAngles.y;
                lethalBotController.thisPlayerBody.rotation = Quaternion.Lerp(lethalBotController.thisPlayerBody.rotation, Quaternion.Euler(zero), Time.deltaTime * 20f * (1f - Mathf.Abs(lethalBotController.shockMinigamePullPosition)));
            }
            else if (lethalBotController.inAnimationWithEnemy
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
            if (LethalBotAIController.useLadderCoroutine != null)
            {
                return false;
            }

            // todo : ladder item holding configurable ?
            //if ((this.lethalBotController.isHoldingObject && !ladder.oneHandedItemAllowed)
            //    || (this.lethalBotController.twoHanded &&
            //                       (!ladder.twoHandedItemAllowed || ladder.specialCharacterAnimation)))
            //{
            //    Plugin.LogDebug("no ladder cuz holding things");
            //    return false;
            //}

            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.sinkingValue > 0.73f)
            {
                return false;
            }
            if (lethalBotController.jetpackControls && (ladder.specialCharacterAnimation || ladder.isLadder))
            {
                return false;
            }
            if (lethalBotController.isClimbingLadder)
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
            else if (lethalBotController.inSpecialInteractAnimation)
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
            PlayerControllerB lethalBotController = Npc;
            if (lethalBotController.isPlayerDead)
            {
                return;
            }

            if (lethalBotController.isMovementHindered <= 0 && lethalBotController.isUnderwater && lethalBotController.underwaterCollider != null && CheckConditionsForSinkingInQuicksandLethalBot())
            {
                QuicksandTrigger component = lethalBotController.underwaterCollider.gameObject.GetComponent<QuicksandTrigger>();
                if (component != null)
                {
                    component.OnExit(lethalBotController.gameObject.GetComponent<Collider>());
                    lethalBotController.isUnderwater = false;
                    setFaceUnderwater = false;
                }
            }

            if (lethalBotController.underwaterCollider != null
                && lethalBotController.underwaterCollider.bounds.Contains(lethalBotController.gameplayCamera.transform.position))
            {
                setFaceUnderwater = true;
                lethalBotController.statusEffectAudio.volume = Mathf.Lerp(lethalBotController.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                this.DrowningTimer -= Time.deltaTime / Const.LETHAL_BOT_DROWN_TIME;
                if (this.DrowningTimer < 0f)
                {
                    this.DrowningTimer = 1f;
                    Plugin.LogDebug($"SyncKillLethalBot from drowning for LOCAL client #{lethalBotController.NetworkManager.LocalClientId}, bot object: Bot #{lethalBotController.playerClientId}");
                    lethalBotController.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Drowning, 0, default);
                }
            }
            else
            {
                setFaceUnderwater = false;
                lethalBotController.statusEffectAudio.volume = Mathf.Lerp(lethalBotController.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
                this.DrowningTimer = Mathf.Clamp(this.DrowningTimer + Time.deltaTime, 0.1f, 1f);
            }

            this.syncUnderwaterInterval -= Time.deltaTime;
            if (this.syncUnderwaterInterval <= 0f)
            {
                this.syncUnderwaterInterval = 0.5f;
                if (setFaceUnderwater && !lethalBotController.isUnderwater)
                {
                    lethalBotController.isUnderwater = true;
                    lethalBotController.SetFaceUnderwaterServerRpc();
                    return;
                }
                else if (!setFaceUnderwater && lethalBotController.isUnderwater)
                {
                    lethalBotController.isUnderwater = false;
                    lethalBotController.SetFaceOutOfWaterServerRpc();
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