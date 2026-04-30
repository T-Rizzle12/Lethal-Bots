using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.AI.AIStates;
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
                                  ref bool ___throwingObject,
                                  ref float ___timeSinceSwitchingSlots,
                                  ref float ___timeSinceTakingGravityDamage,
                                  ref bool ___teleportingThisFrame,
                                  ref float ___previousFrameDeltaTime,
                                  ref float ___cameraUp,
                                  ref float ___updatePlayerLookInterval,
                                  ref float ___bloodDropTimer)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI == null)
            {
                return true;
            }

            // Use Bot update and pass all needed paramaters back and forth
            // The controller isn't set on bot creation, so null check it until our client receives the bot spawn RPC
            NpcController npcController = lethalBotAI.NpcController;
            if (npcController != null)
            {
                npcController.IsCameraDisabled = ___isCameraDisabled;
                npcController.IsJumping = ___isJumping;
                npcController.IsFallingFromJump = ___isFallingFromJump;
                npcController.CrouchMeter = ___crouchMeter;
                npcController.IsWalking = ___isWalking;
                npcController.PlayerSlidingTimer = ___playerSlidingTimer;

                npcController.DisabledJetpackControlsThisFrame = ___disabledJetpackControlsThisFrame;
                npcController.StartedJetpackControls = ___startedJetpackControls;
                npcController.UpperBodyAnimationsWeight = ___upperBodyAnimationsWeight;
                npcController.ThrowingObject.Apply(___throwingObject); // NOTE: ThrowingObject is updated in an RPC which is not during the standard update call
                npcController.TimeSinceSwitchingSlots.Apply(___timeSinceSwitchingSlots); // NOTE: TimeSinceSwitchingSlots can be updated in an RPC which is not during the standard update call
                npcController.TimeSinceTakingGravityDamage = ___timeSinceTakingGravityDamage;
                npcController.TeleportingThisFrame = ___teleportingThisFrame;
                npcController.PreviousFrameDeltaTime = ___previousFrameDeltaTime;

                npcController.CameraUp = ___cameraUp;
                npcController.UpdatePlayerLookInterval = ___updatePlayerLookInterval;
                npcController.BloodDropTimer = ___bloodDropTimer;

                lethalBotAI.UpdateController();

                ___isCameraDisabled = npcController.IsCameraDisabled;
                ___crouchMeter = npcController.CrouchMeter;
                ___isWalking = npcController.IsWalking;
                ___playerSlidingTimer = npcController.PlayerSlidingTimer;

                ___startedJetpackControls = npcController.StartedJetpackControls;
                ___upperBodyAnimationsWeight = npcController.UpperBodyAnimationsWeight;
                ___throwingObject = npcController.ThrowingObject;
                ___timeSinceSwitchingSlots = npcController.TimeSinceSwitchingSlots;
                ___timeSinceTakingGravityDamage = npcController.TimeSinceTakingGravityDamage;
                ___teleportingThisFrame = npcController.TeleportingThisFrame;
                ___previousFrameDeltaTime = npcController.PreviousFrameDeltaTime;

                ___cameraUp = npcController.CameraUp;
                ___updatePlayerLookInterval = npcController.UpdatePlayerLookInterval;
                ___bloodDropTimer = npcController.BloodDropTimer;
            }

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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                // The controller isn't set on bot creation, so null check it until our client receives the bot spawn RPC
                NpcController npcController = lethalBotAI.NpcController;
                if (npcController != null)
                {
                    npcController.IsWalking = ___isWalking;
                    npcController.UpdatePositionForNewlyJoinedClient = ___updatePositionForNewlyJoinedClient;
                    npcController.UpdatePlayerLookInterval = ___updatePlayerLookInterval;
                    npcController.LimpMultiplier = ___limpMultiplier;
                    npcController.PlayerMask = ___playerMask;

                    npcController.LateUpdate();

                    ___isWalking = npcController.IsWalking;
                    ___updatePositionForNewlyJoinedClient = npcController.UpdatePositionForNewlyJoinedClient;
                    ___updatePlayerLookInterval = npcController.UpdatePlayerLookInterval;
                    ___limpMultiplier = npcController.LimpMultiplier;
                }

                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch for calling the right method to damage lethalBot
        /// </summary>
        /// <remarks>
        /// TODO: Potentially use a transpiler here and allow the base damage player function to run.
        /// This would make Lethal Bots much more compatable with other mods.
        /// </remarks>
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
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
        [HarmonyPriority(Priority.Last)]
        static bool KillPlayer_PreFix(PlayerControllerB __instance,
                                      bool __runOriginal,
                                      Vector3 bodyVelocity,
                                      bool spawnBody = true,
                                      CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                      int deathAnimation = 0,
                                      Vector3 positionOffset = default(Vector3),
                                      bool setOverrideDropItems = false)
        {
            // If other mods block this call, we don't do anything!
            if (!__runOriginal)
            {
                return true; // Let the base game not run........
            }

            // Try to kill an lethalBot ?
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"SyncKillLethalBot called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, Bot #{lethalBotAI.BotId}");
                lethalBotAI.KillLethalBot(bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset, setOverrideDropItems);

                // We don't need to block the vanilla kill player!
                // We change the isPlayerDead flag to true in the function call above,
                // this stops the base game from doing anything while allowing Postfixes to still run!
                return true;
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

        [HarmonyPatch("KillPlayerClientRpc")]
        [HarmonyPostfix]
        static void KillPlayerClientRpc_Postfix(PlayerControllerB __instance, 
            ref int playerId, 
            ref bool spawnBody, 
            ref Vector3 bodyVelocity, 
            ref int causeOfDeath, 
            ref int deathAnimation, 
            ref Vector3 positionOffset, 
            ref bool setOverrideDropItems)
        {
            // We do nothing here for human players
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI == null)
            {
                return;
            }

            // Sigh, if the death animation is set to 9 the body has a chance to be null!
            if (__instance.deadBody != null)
            {
                // Replace body position or else disappear with shotgun or knife (don't know why)
                //__instance.deadBody.transform.position = __instance.transform.position + Vector3.up + positionOffset;
                lethalBotAI.LethalBotIdentity.DeadBody = __instance.deadBody;

                // Lets make sure the bots don't attempt to grab dead bodies as soon as a player is killed!
                GrabbableObject? deadBody = __instance.deadBody?.grabBodyObject;
                if (deadBody != null)
                {
                    LethalBotAI.DictJustDroppedItems[deadBody] = Time.realtimeSinceStartup;
                }
            }
            else if (spawnBody && !__instance.overrideDontSpawnBody)
            {
                Plugin.LogWarning($"Bot {__instance.playerUsername} dead body was not spawned. This is probably a bug with another mod or the base game itself!");
            }

            lethalBotAI.NpcController.CurrentLethalBotPhysicsRegions.Clear();
            lethalBotAI.isEnemyDead = true;
            lethalBotAI.LethalBotIdentity.Hp = 0;
            lethalBotAI.SetAgent(enabled: false);
            //this.LethalBotIdentity.Voice.StopAudioFadeOut();
            if (lethalBotAI.State == null || lethalBotAI.State.GetAIState() != EnumAIStates.BrainDead)
            {
                // If the bot was not in the BrainDead state, we set it to it so it doesn't do anything after this!
                lethalBotAI.State = new BrainDeadState(lethalBotAI);
            }
        }

        /// <summary>
        /// Patch to call our SwitchToItemSlot method!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="slot"></param>
        /// <param name="fillSlotWithItem"></param>
        /// <returns></returns>
        [HarmonyPatch("SwitchToItemSlot")]
        [HarmonyPrefix]
        static bool SwitchToItemSlot_Prefix(PlayerControllerB __instance, int slot, GrabbableObject fillSlotWithItem = null!)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.SwitchToItemSlot(slot, fillSlotWithItem);
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
        /// Patch to call the right method for dropping all items for the lethalBot
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("DropAllHeldItems")]
        [HarmonyPrefix]
        static bool DropAllHeldItems_Prefix(PlayerControllerB __instance, bool itemsFall = true, bool setInShip = false, bool setInElevator = false, Vector3 syncedPlayerPosition = default(Vector3), Vector3 syncedHeldObjectPosition = default(Vector3), Vector3 syncedHeldObjectRotation = default(Vector3), Vector3 syncedPlayerCamPosition = default(Vector3), Vector3 syncedPlayerCamRotation = default(Vector3))
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.DropAllHeldItems(itemsFall, setInShip, setInElevator, syncedPlayerPosition, syncedHeldObjectPosition, syncedHeldObjectRotation, syncedPlayerCamPosition, syncedPlayerCamRotation);
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
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

        ///// <summary>
        ///// Patch for calling lethalBot method if lethalBot
        ///// </summary>
        ///// <param name="__instance"></param>
        ///// <returns></returns>
        //[HarmonyPatch("PlayerHitGroundEffects")]
        //[HarmonyPrefix]
        //static bool PlayerHitGroundEffects_PreFix(PlayerControllerB __instance)
        //{
        //    LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
        //    if (lethalBotAI != null)
        //    {
        //        PlayerHitGroundEffects_ReversePatch(__instance);
        //        return false;
        //    }

        //    return true;
        //}

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
                    lethalBotAI.FearLevel.Value += Time.deltaTime * amountMultiplier;
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                return false;
            }

            return true;
        }

        [HarmonyPatch("SpawnPlayerAnimation")]
        [HarmonyPrefix]
        static bool SpawnPlayerAnimation_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI == null)
            {
                return true;
            }

            if (lethalBotAI.spawnAnimationCoroutine != null)
            {
                lethalBotAI.StopCoroutine(lethalBotAI.spawnAnimationCoroutine);
            }

            lethalBotAI.spawnAnimationCoroutine = lethalBotAI.BeginLethalBotSpawnAnimation(EnumSpawnAnimation.OnlyPlayerSpawnAnimation);
            return false;
        }

        [HarmonyPatch("TeleportPlayer")]
        [HarmonyPrefix]
        static bool TeleportPlayer_PreFix(PlayerControllerB __instance,
                                          Vector3 pos,
                                          ref bool ___teleportingThisFrame,
                                          bool withRotation = false,
                                          float rot = 0f,
                                          bool allowInteractTrigger = false)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                StartOfRound.Instance.playerTeleportedEvent.Invoke(__instance);
                ___teleportingThisFrame = true;
                lethalBotAI.NpcController.TeleportingThisFrame = true;
                __instance.teleportedLastFrame = true;
                lethalBotAI.TeleportLethalBot(pos, withRotation: withRotation, rot: rot, allowInteractTrigger: allowInteractTrigger);
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
        /// Reverse patch to call <c>DropHeldItem</c>.<br/>
        /// </summary>
        /// <remarks>
        /// This is a stub for the reverse patch, it will be replaced by the actual implementation
        /// </remarks>
        /// <param name="instance"></param>
        /// <param name="dropItem"></param>
        /// <param name="itemsFall"></param>
        /// <param name="disconnecting"></param>
        /// <exception cref="NotImplementedException"></exception>
        [HarmonyPatch("DropHeldItem")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void DropHeldItem_ReversePatch(object instance, GrabbableObject dropItem, bool itemsFall, bool disconnecting, Vector3 syncedPlayerPosition, Vector3 syncedHeldObjectPosition, Vector3 syncedHeldObjectRotation, Vector3 syncedPlayerCamPosition, Vector3 syncedPlayerCamRotation, bool setInShip, bool setInElevator) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DropHeldItem_ReversePatch");

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
        public static void PlayerHitGroundEffects_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlayerHitGroundEffects_ReversePatch");

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
        public static void IsInSpecialAnimationClientRpc_ReversePatch(object instance, bool specialAnimation, float timed, bool climbingLadder) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.PlayerControllerBPatch.IsInSpecialAnimationClientRpc_ReversePatch");

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

        [HarmonyPatch("Crouch")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Crouch_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo getStartOfRoundInstance = AccessTools.PropertyGetter(typeof(StartOfRound), "Instance");
            FieldInfo timeAtMakingLastPersonalMovementField = AccessTools.Field(typeof(StartOfRound), "timeAtMakingLastPersonalMovement");
            MethodInfo realtimeSinceStartupField = AccessTools.PropertyGetter(typeof(Time), "realtimeSinceStartup");

            // ----------------------------------------------------------------------
            // We need to fix the game setting timeAtMakingLastPersonalMovement when a bot calls this method
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].Calls(getStartOfRoundInstance)
                    && codes[i + 1].Calls(realtimeSinceStartupField)
                    && codes[i + 2].StoresField(timeAtMakingLastPersonalMovementField))
                {
                    // Prep our label to jump to
                    var skipLabel = generator.DefineLabel();

                    // Prep our new if statement
                    List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Ldarg_0), // Load `this`, PlayerControllerB
                        new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod), // Compare this == localPlayerController
                        new CodeInstruction(OpCodes.Brfalse, skipLabel) // Skip the timeAtMakingLastPersonalMovement if we are not the local player
                    };

                    // Add our new label!
                    codes[i + 3].labels.Add(skipLabel);

                    // Insert the new instruction to call the replacement method.
                    codes.InsertRange(i, codesToReplace);
                    i += codesToReplace.Count; // Move our index past the newly added codes!
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBots.Patches.NpcPatches.PlayerControllerBPatch.Crouch_Transpiler could not check if player local for Crouch");
            }
            else
            {
                Plugin.LogDebug($"Patched out timeAtMakingLastPersonalMovement for bots in Crouch {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

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

        [HarmonyPatch("SendNewPlayerValuesClientRpc")]
        [HarmonyPostfix]
        static void SendNewPlayerValuesClientRpc_PostFix(PlayerControllerB __instance)
        {
            if (LethalBotManager.Instance == null)
            {
                return; // No manager means no bots
            }

            // Update lethal bots names back to the way they were
            foreach (LethalBotAI lethalBotAI in LethalBotManager.Instance.GetLethalBotAIs())
            {
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                if (lethalBotController != null && lethalBotAI.LethalBotIdentity != null)
                {
                    string botName = lethalBotAI.LethalBotIdentity.Name;
                    lethalBotController.playerUsername = botName;
                    lethalBotController.usernameBillboardText.text = botName;
                    lethalBotController.quickMenuManager.AddUserToPlayerList(0ul, botName, (int)lethalBotController.playerClientId);
                    StartOfRound.Instance.mapScreen.radarTargets[(int)lethalBotController.playerClientId].name = botName;
                }
            }
        }

        [HarmonyPatch("KillPlayer")]
        [HarmonyPostfix]
        static void KillPlayer_PostFix(PlayerControllerB __instance)
        {
            // Remove player from their group
            if (__instance.IsOwner && __instance.isPlayerDead && __instance.AllowPlayerDeath())
            {
                GroupManager.Instance.RemoveFromCurrentGroupAndSync(__instance);
                LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(__instance);
            }
        }

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

                    if (ragdoll.bodyID == Const.INIT_RAGDOLL_ID)
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

                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(lethalBotController);
                if (lethalBot == null)
                {
                    continue;
                }

                // Name billboard
                if (!Plugin.Config.DisableNameBillBoards.Value)
                { 
                    lethalBot.NpcController.Npc.ShowNameBillboard(); 
                }

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
                    || !lethalBot.IsFollowingLocalPlayer())
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
