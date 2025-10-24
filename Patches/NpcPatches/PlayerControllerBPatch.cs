using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace LethalBots.Patches.NpcPatches
{
    /// <summary>
    /// Patch for <c>PlayerControllerB</c>
    /// </summary>
    [HarmonyPatch(typeof(PlayerControllerB))]
    public class PlayerControllerBPatch
    {
        #region Prefixes

        /// <summary>
        /// Patch for intercepting the update and using only the lethalBot update for lethalBot.<br/>
        /// Need to pass back and forth the private fields before and after modifying them.
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyAfter(Const.MOREEMOTES_GUID)]
        [HarmonyPrefix]
        static bool Update_PreFix(PlayerControllerB __instance,
                                  ref bool ___isCameraDisabled,
                                  bool ___isJumping,
                                  bool ___isFallingFromJump,
                                  ref float ___crouchMeter,
                                  ref bool ___isWalking,
                                  ref float ___playerSlidingTimer,
                                  ref bool ___disabledJetpackControlsThisFrame,
                                  ref bool ___startedJetpackControls,
                                  ref float ___upperBodyAnimationsWeight,
                                  ref float ___timeSinceSwitchingSlots,
                                  ref float ___timeSinceTakingGravityDamage,
                                  ref bool ___teleportingThisFrame,
                                  ref float ___previousFrameDeltaTime,
                                  ref float ___cameraUp,
                                  ref float ___updatePlayerLookInterval,
                                  ref float ___bloodDropTimer)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI == null)
            {
                return true;
            }

            // Use Bot update and pass all needed paramaters back and forth
            lethalBotAI.NpcController.IsCameraDisabled = ___isCameraDisabled;
            lethalBotAI.NpcController.IsJumping = ___isJumping;
            lethalBotAI.NpcController.IsFallingFromJump = ___isFallingFromJump;
            lethalBotAI.NpcController.CrouchMeter = ___crouchMeter;
            lethalBotAI.NpcController.IsWalking = ___isWalking;
            lethalBotAI.NpcController.PlayerSlidingTimer = ___playerSlidingTimer;

            lethalBotAI.NpcController.DisabledJetpackControlsThisFrame = ___disabledJetpackControlsThisFrame;
            lethalBotAI.NpcController.StartedJetpackControls = ___startedJetpackControls;
            lethalBotAI.NpcController.UpperBodyAnimationsWeight = ___upperBodyAnimationsWeight;
            lethalBotAI.NpcController.TimeSinceSwitchingSlots = ___timeSinceSwitchingSlots;
            lethalBotAI.NpcController.TimeSinceTakingGravityDamage = ___timeSinceTakingGravityDamage;
            lethalBotAI.NpcController.TeleportingThisFrame = ___teleportingThisFrame;
            lethalBotAI.NpcController.PreviousFrameDeltaTime = ___previousFrameDeltaTime;

            lethalBotAI.NpcController.CameraUp = ___cameraUp;
            lethalBotAI.NpcController.UpdatePlayerLookInterval = ___updatePlayerLookInterval;
            lethalBotAI.NpcController.BloodDropTimer = ___bloodDropTimer;

            lethalBotAI.UpdateController();

            ___isCameraDisabled = lethalBotAI.NpcController.IsCameraDisabled;
            ___crouchMeter = lethalBotAI.NpcController.CrouchMeter;
            ___isWalking = lethalBotAI.NpcController.IsWalking;
            ___playerSlidingTimer = lethalBotAI.NpcController.PlayerSlidingTimer;

            ___startedJetpackControls = lethalBotAI.NpcController.StartedJetpackControls;
            ___upperBodyAnimationsWeight = lethalBotAI.NpcController.UpperBodyAnimationsWeight;
            ___timeSinceSwitchingSlots = lethalBotAI.NpcController.TimeSinceSwitchingSlots;
            ___timeSinceTakingGravityDamage = lethalBotAI.NpcController.TimeSinceTakingGravityDamage;
            ___teleportingThisFrame = lethalBotAI.NpcController.TeleportingThisFrame;
            ___previousFrameDeltaTime = lethalBotAI.NpcController.PreviousFrameDeltaTime;

            ___cameraUp = lethalBotAI.NpcController.CameraUp;
            ___updatePlayerLookInterval = lethalBotAI.NpcController.UpdatePlayerLookInterval;
            ___bloodDropTimer = lethalBotAI.NpcController.BloodDropTimer;

            return false;
        }

        /// <summary>
        /// Patch for intercepting the LateUpdate and using only the lethalBot LateUpdate for lethalBot.<br/>
        /// Need to pass back and forth the private fields before and after modifying them.
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("LateUpdate")]
        [HarmonyPrefix]
        static bool LateUpdate_PreFix(PlayerControllerB __instance,
                                      ref bool ___isWalking,
                                      ref bool ___updatePositionForNewlyJoinedClient,
                                      ref float ___updatePlayerLookInterval,
                                      ref float ___limpMultiplier,
                                      int ___playerMask)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                lethalBotAI.NpcController.IsWalking = ___isWalking;
                lethalBotAI.NpcController.UpdatePositionForNewlyJoinedClient = ___updatePositionForNewlyJoinedClient;
                lethalBotAI.NpcController.UpdatePlayerLookInterval = ___updatePlayerLookInterval;
                lethalBotAI.NpcController.LimpMultiplier = ___limpMultiplier;
                lethalBotAI.NpcController.PlayerMask = ___playerMask;

                lethalBotAI.NpcController.LateUpdate();

                ___isWalking = lethalBotAI.NpcController.IsWalking;
                ___updatePositionForNewlyJoinedClient = lethalBotAI.NpcController.UpdatePositionForNewlyJoinedClient;
                ___updatePlayerLookInterval = lethalBotAI.NpcController.UpdatePlayerLookInterval;
                ___limpMultiplier = lethalBotAI.NpcController.LimpMultiplier;

                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to disabling the base game awake method for the lethalBot
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        static bool Awake_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch for calling the right method to damage lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DamagePlayer")]
        [HarmonyPrefix]
        static bool DamagePlayer_PreFix(PlayerControllerB __instance,
                                        int damageNumber,
                                        bool hasDamageSFX = true,
                                        bool callRPC = true,
                                        CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                        int deathAnimation = 0,
                                        bool fallDamage = false,
                                        Vector3 force = default(Vector3))
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"SyncDamageLethalBot called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                lethalBotAI.DamageLethalBot(damageNumber, hasDamageSFX, callRPC, causeOfDeath, deathAnimation, fallDamage, force);
                
                // Still do the vanilla damage player, for other mods prefixes (ex: peepers)
                // The damage will be ignored because the lethalBot playerController is not owned because not spawned
                return false;
            }

            if (DebugConst.NO_DAMAGE)
            {
                // Bootleg invulnerability
                Plugin.LogDebug($"Bootleg invulnerability (return false)");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method to damage lethalBot from other player
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DamagePlayerFromOtherClientServerRpc")]
        [HarmonyPrefix]
        static bool DamagePlayerFromOtherClientServerRpc_PreFix(PlayerControllerB __instance,
                                                                int damageAmount, Vector3 hitDirection, int playerWhoHit)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"DamagePlayerFromOtherClientServerRpc called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                //lethalBotAI.DamageLethalBotFromOtherClientServerRpc(damageAmount, hitDirection, playerWhoHit);

                // Send vanilla damage player, for other mods prefixes (ex: peepers)
                // The damage function will be ignored because the lethalBot playerController is not owned because not spawned
                return true;
            }

            return true;
        }

        /// <summary>
        /// Hook the update limp animation for all clients since the lethalBot may change owners.<br/>
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__runOriginal"></param>
        [HarmonyPatch("MakeCriticallyInjuredClientRpc")]
        [HarmonyPostfix]
        public static void MakeCriticallyInjuredClientRpc_Prefix(PlayerControllerB __instance, bool __runOriginal)
        {
            if (!__runOriginal)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"MakeCriticallyInjuredClientRpc called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                __instance.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_LIMP, true);
                return;
            }
        }

        /// <summary>
        /// Hook the update limp animation for all clients since the lethalBot may change owners.<br/>
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__runOriginal"></param>
        [HarmonyPatch("HealClientRpc")]
        [HarmonyPostfix]
        public static void HealClientRpc_Prefix(PlayerControllerB __instance, bool __runOriginal)
        {
            if (!__runOriginal)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"HealClientRpc called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                __instance.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_LIMP, false);
                return;
            }
        }

        /// <summary>
        /// Damage to call the right method to kill lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("KillPlayer")]
        [HarmonyPrefix]
        static bool KillPlayer_PreFix(PlayerControllerB __instance,
                                      Vector3 bodyVelocity,
                                      bool spawnBody = true,
                                      CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                      int deathAnimation = 0,
                                      Vector3 positionOffset = default(Vector3))
        {
            // Try to kill an lethalBot ?
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"SyncKillLethalBot called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, Bot #{lethalBotAI.BotId}");
                lethalBotAI.SyncKillLethalBot(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset);

                // Block vanilla kill player!
                // Other mods prefixes such as (ex: peepers) will not be ignored since HarmonyX calls them anyway!
                return false;
            }

            // A player is killed 
            if (DebugConst.NO_DEATH)
            {
                // Bootleg invincibility
                Plugin.LogDebug($"Bootleg invincibility");
                return false;
            }

            // NOTE: Disabled on purpose since this is now handled by RagdollGrabbablePatch!
            // Lets make sure the bots don't attempt to grab dead bodies as soon as a player is killed!
            /*GrabbableObject? deadBody = __instance.deadBody?.grabBodyObject;
            if (deadBody != null)
            {
                LethalBotAI.DictJustDroppedItems[deadBody] = Time.realtimeSinceStartup;
            }*/

            return true;
        }

        /// <summary>
        /// Patch to call our DisablePlayerModel method!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="playerObject"></param>
        /// <param name="enable"></param>
        /// <param name="disableLocalArms"></param>
        /// <returns></returns>
        [HarmonyPatch("DisablePlayerModel")]
        [HarmonyPrefix]
        static bool DisablePlayerModel_Prefix(PlayerControllerB __instance, GameObject playerObject, bool enable = false, bool disableLocalArms = false)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__instance))
            {
                LethalBotManager.Instance.DisableLethalBotControllerModel(playerObject, __instance, enable, disableLocalArms);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the drop item method on the bot rather than the player
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="placeObject"></param>
        /// <param name="parentObjectTo"></param>
        /// <param name="placePosition"></param>
        /// <param name="matchRotationOfParent"></param>
        /// <returns></returns>
        [HarmonyPatch("DiscardHeldObject")]
        [HarmonyPrefix]
        static bool DiscardHeldObject_Prefix(PlayerControllerB __instance, bool placeObject = false, NetworkObject parentObjectTo = null!, Vector3 placePosition = default(Vector3), bool matchRotationOfParent = true)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropItem(placeObject, parentObjectTo, placePosition, matchRotationOfParent);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Patch to call the right method for destroying an item for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DestroyItemInSlot")]
        [HarmonyPrefix]
        static bool DestroyItemInSlot_Prefix(PlayerControllerB __instance, int itemSlot)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DestroyItemInSlot(itemSlot);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for destroying an item for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DestroyItemInSlotAndSync")]
        [HarmonyPrefix]
        static bool DestroyItemInSlotAndSync_Prefix(PlayerControllerB __instance, int itemSlot)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DestroyItemInSlotAndSync(itemSlot);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for destroying an item for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DestroyItemInSlotClientRpc")]
        [HarmonyPrefix]
        static bool DestroyItemInSlotClientRpc_Prefix(PlayerControllerB __instance, int itemSlot)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DestroyItemInSlotClientRpc(itemSlot);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for destroying an item for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DestroyItemInSlotServerRpc")]
        [HarmonyPrefix]
        static bool DestroyItemInSlotServerRpc_Prefix(PlayerControllerB __instance, int itemSlot)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DestroyItemInSlotServerRpc(itemSlot);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for dropping all items for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DropAllHeldItems")]
        [HarmonyPrefix]
        static bool DropAllHeldItems_Prefix(PlayerControllerB __instance, bool itemsFall = true)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropAllHeldItems(itemsFall);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for dropping all items for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DropAllHeldItemsAndSync")]
        [HarmonyPrefix]
        static bool DropAllHeldItemsAndSync_Prefix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropAllHeldItemsAndSync();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for dropping all items for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DropAllHeldItemsServerRpc")]
        [HarmonyPrefix]
        static bool DropAllHeldItemsServerRpc_Prefix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropAllHeldItemsServerRpc();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for dropping all items for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DropAllHeldItemsClientRpc")]
        [HarmonyPrefix]
        static bool DropAllHeldItemsClientRpc_Prefix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropAllHeldItemsClientRpc();
                return false;
            }
            return true;
        }

        [HarmonyPatch("DespawnHeldObject")]
        [HarmonyPrefix]
        public static bool DespawnHeldObject_Prefix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DespawnHeldObject();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call the right method for update special animation value for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("UpdateSpecialAnimationValue")]
        [HarmonyPrefix]
        static bool UpdateSpecialAnimationValue_PreFix(PlayerControllerB __instance,
                                                       bool specialAnimation, float timed, bool climbingLadder)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                lethalBotAI.UpdateLethalBotSpecialAnimationValue(specialAnimation, timed, climbingLadder);
                return false;
            }

            return true;
        }

        [HarmonyPatch("CancelSpecialTriggerAnimations")]
        [HarmonyPrefix]
        public static bool CancelSpecialTriggerAnimations_Prefix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI == null)
            {
                return true; // Not a bot, run the original!
            }

            // Since the base game has a different check if the player is in the terminal, it only works for the local player.
            // We need to do our custom logic instead!
            // Don't allow default logic to run as it bugs out sometimes and kicks the local player off the terminal!
            if (__instance.inTerminalMenu)
            {
                lethalBotAI.LeaveTerminal();
            }
            else if (__instance.currentTriggerInAnimationWith != null)
            {
                __instance.currentTriggerInAnimationWith.StopSpecialAnimation();
            }
            return false;
        }

        /// <summary>
        /// Patch for player to be able to take item from lethalBot if pointing at item held in hands,<br/>
        /// makes the lethalBot drop and immediately grab by the player
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("BeginGrabObject")]
        [HarmonyPrefix]
        static bool BeginGrabObject_PreFix(PlayerControllerB __instance,
                                           ref Ray ___interactRay,
                                           ref RaycastHit ___hit,
                                           ref int ___interactableObjectsMask)
        {
            ___interactRay = new Ray(__instance.gameplayCamera.transform.position, __instance.gameplayCamera.transform.forward);
            if (Physics.Raycast(___interactRay, out ___hit, __instance.grabDistance, ___interactableObjectsMask)
                && ___hit.collider.gameObject.layer != 8
                && ___hit.collider.tag == "PhysicsProp")
            {
                GrabbableObject grabbableObject = ___hit.collider.transform.gameObject.GetComponent<GrabbableObject>();
                if (grabbableObject == null)
                {
                    // Quit and continue original method
                    return true;
                }

                if (!grabbableObject.isHeld)
                {
                    // Quit and continue original method
                    return true;
                }

                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAiOwnerOfObject(grabbableObject);
                if (lethalBotAI == null)
                {
                    // Quit and continue original method
                    Plugin.LogDebug($"no lethalBot found who hold item {grabbableObject.name}");
                    return true;
                }

                // FIXME: Make sure the player has room for the item!
                Plugin.LogDebug($"lethalBot {lethalBotAI.NpcController.Npc.playerUsername} drop item {grabbableObject.name} before grab by player");
                grabbableObject.isHeld = false;
                lethalBotAI.DropItem();
            }

            return true;
        }

        /// <summary>
        /// Patch to call the right the right method for sync dead body if the lethalBot is calling it
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("SyncBodyPositionClientRpc")]
        [HarmonyPrefix]
        static bool SyncBodyPositionClientRpc_PreFix(PlayerControllerB __instance, Vector3 newBodyPosition)
        {
            // send to server if lethalBot from controller
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"NetworkManager {__instance.NetworkManager}, newBodyPosition {newBodyPosition}, this.deadBody {__instance.deadBody}");
                lethalBotAI.SyncDeadBodyPositionServerRpc(newBodyPosition);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Patch for calling lethalBot method if lethalBot
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("PlayerHitGroundEffects")]
        [HarmonyPrefix]
        static bool PlayerHitGroundEffects_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                PlayerHitGroundEffects_ReversePatch(__instance);
                return false;
            }

            return true;
        }

        [HarmonyPatch("IncreaseFearLevelOverTime")]
        [HarmonyPrefix]
        static bool IncreaseFearLevelOverTime_PreFix(PlayerControllerB __instance, float amountMultiplier = 1f, float cap = 1f)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.FearLevelIncreasing.Value = true;
                if (!(lethalBotAI.FearLevel.Value > cap))
                {
                    lethalBotAI.FearLevel.Value += Time.deltaTime;
                    if (lethalBotAI.FearLevel.Value > 0.6f && __instance.timeSinceFearLevelUp > 8f)
                    {
                        __instance.timeSinceFearLevelUp = 0f;
                    }
                }
                return false;
            }

            return true;
        }

        [HarmonyPatch("JumpToFearLevel")]
        [HarmonyPrefix]
        static bool JumpToFearLevel_PreFix(PlayerControllerB __instance, float targetFearLevel, bool onlyGoUp = true)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(__instance);
            if (lethalBotAI != null)
            {
                if (!onlyGoUp || !(targetFearLevel - lethalBotAI.FearLevel.Value < 0.05))
                {
                    lethalBotAI.FearLevel.Value = targetFearLevel;
                    lethalBotAI.FearLevelIncreasing.Value = true;
                    if (__instance.timeSinceFearLevelUp > 8f)
                    {
                        __instance.timeSinceFearLevelUp = 0f;
                    }
                }
                return false;
            }

            return true;
        }

        [HarmonyPatch("PerformEmote")]
        [HarmonyPrefix]
        static bool PerformEmote_PreFix(PlayerControllerB __instance, int emoteID)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI == null)
            {
                return true;
            }

            if (!CheckConditionsForEmote_ReversePatch(__instance))
            {
                return false;
            }

            __instance.performingEmote = true;
            __instance.playerBodyAnimator.SetInteger("emoteNumber", emoteID);
            lethalBotAI.StartPerformingEmoteLethalBotServerRpc(emoteID);

            return false;
        }

        /// <summary>
        /// Prefix for using the lethalBot server rpc for emotes, for the ownership false
        /// </summary>
        /// <remarks>Calls from MoreEmotes mod typically</remarks>
        /// <returns></returns>
        [HarmonyPatch("StartPerformingEmoteServerRpc")]
        [HarmonyPrefix]
        static bool StartPerformingEmoteServerRpc_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI == null)
            {
                return true;
            }

            lethalBotAI.StartPerformingEmoteLethalBotServerRpc(__instance.playerBodyAnimator.GetInteger("emoteNumber"));
            return false;
        }

        [HarmonyPatch("ConnectClientToPlayerObject")]
        [HarmonyPrefix]
        static bool ConnectClientToPlayerObject_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lethalBotAI != null)
            {
                return false;
            }

            return true;
        }

        [HarmonyPatch("TeleportPlayer")]
        [HarmonyPrefix]
        static bool TeleportPlayer_PreFix(PlayerControllerB __instance,
                                          Vector3 pos,
                                          bool allowInteractTrigger = false)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                StartOfRound.Instance.playerTeleportedEvent.Invoke(__instance);
                lethalBotAI.TeleportLethalBot(pos, allowInteractTrigger: allowInteractTrigger);
                return false;
            }

            return true;
        }

        [HarmonyPatch("PlayFootstepServer")]
        [HarmonyPrefix]
        static bool PlayFootstepServer_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.NpcController.PlayFootstep(isServer: true);
                return false;
            }

            return true;
        }

        [HarmonyPatch("PlayFootstepLocal")]
        [HarmonyPrefix]
        static bool PlayFootstepLocal_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.NpcController.PlayFootstep(isServer: false);
                return false;
            }

            return true;
        }

        #endregion

        #region Reverse patches

        /// <summary>
        /// Reverse patch to call <c>UpdatePlayerPhysicsParentServerRpc</c>.<br/>
        /// </summary>
        /// <remarks>
        /// This is a stub for the reverse patch, it will be replaced by the actual implementation
        /// </remarks>
        /// <param name="instance"></param>
        /// <param name="playerClientId"></param>
        /// <param name="parentObject"></param>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("UpdatePlayerPhysicsParentServerRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void UpdatePlayerPhysicsParentServerRpc_ReversePatch(object instance, Vector3 newPos, NetworkObjectReference setPhysicsParent, bool isOverride, bool inElevator, bool isInShip) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.UpdatePlayerPhysicsParentServerRpc_ReversePatch");

        /// <summary>
        /// Reverse patch to call <c>RemovePlayerPhysicsParentServerRpc</c>.<br/>
        /// </summary>
        /// <remarks>
        /// This is a stub for the reverse patch, it will be replaced by the actual implementation
        /// </remarks>
        /// <param name="instance"></param>
        /// <param name="playerClientId"></param>
        /// <param name="parentObject"></param>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("RemovePlayerPhysicsParentServerRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void RemovePlayerPhysicsParentServerRpc_ReversePatch(object instance, Vector3 newPos, bool removeOverride, bool removeBoth, bool inElevator, bool isInShip) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.RemovePlayerPhysicsParentServerRpc_ReversePatch");

        /// <summary>
        /// Reverse patch to call <c>PlayJumpAudio</c>
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("PlayJumpAudio")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void PlayJumpAudio_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlayJumpAudio_ReversePatch");

        /// <summary>
        /// Reverse patch modified to use the right method to sync land from jump for the lethalBot
        /// </summary>
        [HarmonyPatch("PlayerHitGroundEffects")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void PlayerHitGroundEffects_ReversePatch(object instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var startIndex = -1;
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                // ----------------------------------------------------------------------
                for (var i = 0; i < codes.Count - 5; i++)
                {
                    if (codes[i].ToString().StartsWith("ldarg.0 NULL") // 33
                        && codes[i + 5].ToString().StartsWith("call void GameNetcodeStuff.PlayerControllerB::LandFromJumpServerRpc(")) // 38
                    {
                        startIndex = i;
                        break;
                    }
                }
                if (startIndex > -1)
                {
                    codes[startIndex + 5].operand = PatchesUtil.SyncLandFromJumpMethod;
                    codes.Insert(startIndex + 1, new CodeInstruction(OpCodes.Ldfld, PatchesUtil.FieldInfoPlayerClientId));
                    startIndex = -1;
                }
                else
                {
                    Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlayerHitGroundEffects_ReversePatch could not use jump from land method for lethalBot");
                }

                return codes.AsEnumerable();
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            _ = Transpiler(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        /// <summary>
        /// Reverse patch to call <c>CheckConditionsForEmote</c>
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("CheckConditionsForEmote")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static bool CheckConditionsForEmote_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlayerControllerBPatchCheckConditionsForEmote_ReversePatch");

        /// <summary>
        /// Reverse patch to call <c>OnDisable</c>
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("OnDisable")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void OnDisable_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.OnDisable_ReversePatch");

        [HarmonyPatch("InteractTriggerUseConditionsMet")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static bool InteractTriggerUseConditionsMet_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.InteractTriggerUseConditionsMet_ReversePatch");

        /// <summary>
        /// Reverse patch to be able to call <c>IsInSpecialAnimationClientRpc</c>
        /// </summary>
        /// <remarks>
        /// Bypassing all rpc condition, because the lethalBot is not owner of his body, no one is, the body <c>PlayerControllerB</c> of lethalBot is not spawned.<br/>
        /// </remarks>
        [HarmonyPatch("IsInSpecialAnimationClientRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void IsInSpecialAnimationClientRpc_ReversePatch(object instance, bool specialAnimation, float timed, bool climbingLadder)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var startIndex = -1;
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                // ----------------------------------------------------------------------
                for (var i = 0; i < codes.Count - 3; i++)
                {
                    if (codes[i].ToString().StartsWith("call bool Unity.Netcode.NetworkBehaviour::get_IsOwner()")// 70
                        && codes[i + 3].ToString().StartsWith("ldstr \"Setting animation on client\""))// 73
                    {
                        startIndex = i;
                        break;
                    }
                }
                if (startIndex > -1)
                {
                    codes.Insert(0, new CodeInstruction(OpCodes.Br, codes[startIndex + 3].labels[0]));
                    startIndex = -1;
                }
                else
                {
                    Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.IsInSpecialAnimationClientRpc_ReversePatch could not bypass rpc stuff");
                }

                return codes.AsEnumerable();
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            _ = Transpiler(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        /// <summary>
        /// Reverse patch to be able to call <c>SyncBodyPositionClientRpc</c>
        /// </summary>
        /// <remarks>
        /// Bypassing all rpc condition, because the lethalBot is not owner of his body, no one is, the body <c>PlayerControllerB</c> of lethalBot is not spawned.<br/>
        /// </remarks>
        [HarmonyPatch("SyncBodyPositionClientRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void SyncBodyPositionClientRpc_ReversePatch(object instance, Vector3 newBodyPosition)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var startIndex = -1;
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                // ----------------------------------------------------------------------
                for (var i = 0; i < codes.Count - 6; i++)
                {
                    if (codes[i].ToString().StartsWith("nop NULL")// 53
                        && codes[i + 9].ToString().StartsWith("call static float UnityEngine.Vector3::Distance(UnityEngine.Vector3 a, UnityEngine.Vector3 b)"))// 62
                    {
                        startIndex = i;
                        break;
                    }
                }
                if (startIndex > -1)
                {
                    codes.Insert(0, new CodeInstruction(OpCodes.Br, codes[startIndex].labels[0]));
                    startIndex = -1;
                }
                else
                {
                    Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.SyncBodyPositionClientRpc_ReversePatch could not bypass rpc stuff");
                }

                return codes.AsEnumerable();
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            _ = Transpiler(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        [HarmonyPatch("SetSpecialGrabAnimationBool")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void SetSpecialGrabAnimationBool_ReversePatch(object instance, bool setTrue, GrabbableObject currentItem) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.SetSpecialGrabAnimationBool_ReversePatch");

        [HarmonyPatch("SetNightVisionEnabled")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void SetNightVisionEnabled_ReversePatch(object instance, bool isNotLocalClient) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.SetNightVisionEnabled_ReversePatch");

        [HarmonyPatch("SetPlayerSanityLevel")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void SetPlayerSanityLevel_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.SetPlayerSanityLevel_ReversePatch");

        #endregion

        #region Transpilers

        /* [HarmonyPatch("ConnectClientToPlayerObject")]
         [HarmonyTranspiler]
         public static IEnumerable<CodeInstruction> ConnectClientToPlayerObject_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
         {
             var startIndex = -1;
             var codes = new List<CodeInstruction>(instructions);

             // ----------------------------------------------------------------------
             for (var i = 0; i < codes.Count - 3; i++)
             {
                 if (codes[i].ToString().StartsWith("ldarg.0 NULL")
                     && codes[i + 1].ToString() == "ldfld StartOfRound GameNetcodeStuff.PlayerControllerB::playersManager"
                     && codes[i + 2].ToString() == "ldfld UnityEngine.GameObject[] StartOfRound::allPlayerObjects"
                     && codes[i + 3].ToString() == "ldlen NULL")
                 {
                     startIndex = i;
                     break;
                 }
             }
             if (startIndex > -1)
             {
                 codes[startIndex].opcode = OpCodes.Nop;
                 codes[startIndex].operand = null;
                 codes[startIndex + 1].opcode = OpCodes.Nop;
                 codes[startIndex + 1].operand = null;
                 codes[startIndex + 2].opcode = OpCodes.Nop;
                 codes[startIndex + 2].operand = null;
                 codes[startIndex + 3].opcode = OpCodes.Call;
                 codes[startIndex + 3].operand = PatchesUtil.IndexBeginOfInternsMethod;
                 startIndex = -1;
             }
             else
             {
                 Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.ConnectClientToPlayerObject_Transpiler could not limit teleport to only not interns.");
             }

             return codes.AsEnumerable();
         }*/

        #endregion

        #region Postfixes

        /// <summary>
        /// Debug patch to spawn an lethalBot at will
        /// </summary>
        //[HarmonyPatch("PerformEmote")]
        //[HarmonyPostfix]
        //static void PerformEmote_PostFix(PlayerControllerB __instance)
        //{
        //    if (!DebugConst.SPAWN_INTERN_WITH_EMOTE)
        //    {
        //        return;
        //    }

        //    if (__instance.playerUsername != "Player #0")
        //    {
        //        return;
        //    }

        //    int identityID = -1;
        //    int[] selectedIdentities = IdentityManager.Instance.GetIdentitiesToDrop();
        //    if (selectedIdentities.Length > 0)
        //    {
        //        identityID = selectedIdentities[0];
        //    }

        //    if (identityID < 0)
        //    {
        //        identityID = IdentityManager.Instance.GetNewIdentityToSpawn();
        //    }

        //    LethalBotManager.Instance.SpawnThisLethalBotServerRpc(identityID, new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
        //    {
        //        enumSpawnAnimation = (int)EnumSpawnAnimation.None,
        //        SpawnPosition = __instance.transform.position,
        //        YRot = __instance.transform.eulerAngles.y,
        //        IsOutside = !__instance.isInsideFactory
        //    });
        //}

        /// <summary>
        /// Patch to add text when pointing at an lethalBot at grab range,<br/>
        /// shows the different possible actions for interacting with lethalBot
        /// </summary>
        [HarmonyPatch("SetHoverTipAndCurrentInteractTrigger")]
        [HarmonyPostfix]
        static void SetHoverTipAndCurrentInteractTrigger_PostFix(ref PlayerControllerB __instance,
                                                                 ref Ray ___interactRay,
                                                                 int ___playerMask,
                                                                 int ___interactableObjectsMask,
                                                                 ref RaycastHit ___hit)
        {
            ___interactRay = new Ray(__instance.gameplayCamera.transform.position, __instance.gameplayCamera.transform.forward);
            if (Physics.Raycast(___interactRay, out ___hit, __instance.grabDistance, ___interactableObjectsMask) && ___hit.collider.gameObject.layer != 8 && ___hit.collider.gameObject.layer != 30)
            {
                // Check if we are pointing to a ragdoll body of lethalBot (not grabbable)
                if (___hit.collider.tag == "PhysicsProp")
                {
                    RagdollGrabbableObject? ragdoll = ___hit.collider.gameObject.GetComponent<RagdollGrabbableObject>();
                    if (ragdoll == null)
                    {
                        return;
                    }

                    if (ragdoll.bodyID.Value == Const.INIT_RAGDOLL_ID)
                    {
                        // Remove tooltip text
                        __instance.cursorTip.text = string.Empty;
                        __instance.cursorIcon.enabled = false;
                        return;
                    }
                }
            }

            // Set tooltip when pointing at lethalBot
            RaycastHit[] raycastHits = new RaycastHit[3];
            int raycastResults = Physics.RaycastNonAlloc(___interactRay, raycastHits, __instance.grabDistance, ___playerMask);
            for (int i = 0; i < raycastResults; i++)
            {
                RaycastHit hit = raycastHits[i];
                if (hit.collider == null
                    || hit.collider.tag != "Player")
                {
                    continue;
                }

                PlayerControllerB lethalBotController = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (lethalBotController == null)
                {
                    continue;
                }

                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI((int)lethalBotController.playerClientId);
                if (lethalBot == null)
                {
                    continue;
                }

                // Name billboard
                lethalBot.NpcController.ShowFullNameBillboard();

                // No action if in spawning animation
                if (lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }

                StringBuilder sb = new StringBuilder();
                // Line item
                if (lethalBot.HasSomethingInInventory())
                {
                    sb.Append(string.Format(Const.TOOLTIP_DROP_ITEM, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.DropItem)))
                        .AppendLine();
                }
                /*else if (__instance.currentlyHeldObjectServer != null)
                {
                    sb.Append(string.Format(Const.TOOLTIP_TAKE_ITEM, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.DropItem)))
                        .AppendLine();
                }*/

                // Line Follow
                EnumAIStates currentBotState = lethalBot.State.GetAIState();
                if (lethalBot.OwnerClientId != __instance.actualClientId 
                    || !lethalBot.IsFollowingTargetPlayer())
                {
                    sb.Append(string.Format(Const.TOOLTIP_FOLLOW_ME, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.LeadBot)))
                        .AppendLine();
                }
                else if (currentBotState != EnumAIStates.SearchingForScrap)
                {
                    sb.Append(string.Format(Const.TOOLTIP_LEAD_THE_WAY, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.LeadBot)))
                        .AppendLine();
                }

                // Grab lethalBot
                //sb.Append(string.Format(Const.TOOLTIP_GRAB_INTERNS, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.GrabIntern)))
                    //.AppendLine();

                // Change suit lethalBot
                if (lethalBotController.currentSuitID != __instance.currentSuitID)
                {
                    sb.Append(string.Format(Const.TOOLTIP_CHANGE_SUIT_BOTS, InputManager.Instance.GetKeyAction(Plugin.InputActionsInstance.ChangeSuitBot)));
                }

                __instance.cursorTip.text = sb.ToString();

                break;
            }
        }

        /*[HarmonyPatch("IVisibleThreat.GetThreatTransform")]
        [HarmonyPostfix]
        static void GetThreatTransform_PostFix(PlayerControllerB __instance, ref Transform __result)
        {
            LethalBotAI? lehtalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)__instance.playerClientId);
            if (lehtalBotAI != null)
            {
                __result = lehtalBotAI.transform;
            }
        }*/

        #endregion
    }
}
