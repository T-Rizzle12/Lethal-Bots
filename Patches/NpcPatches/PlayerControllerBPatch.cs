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
using static Unity.Netcode.NetworkBehaviour;
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
        static bool Update_PreFix(PlayerControllerB __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI == null)
            {
                return true;
            }

            // The controller isn't set on bot creation, so null check it until our client receives the bot spawn RPC
            if (lethalBotAI.NpcController != null)
            {
                lethalBotAI.UpdateController();
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
                if (lethalBotAI.NpcController != null)
                {
                    npcController.LateUpdate();
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
        [HarmonyPriority(Priority.Last)]
        static bool DamagePlayer_PreFix(PlayerControllerB __instance,
                                        bool __runOriginal,
                                        int damageNumber,
                                        bool hasDamageSFX = true,
                                        bool callRPC = true,
                                        CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
                                        int deathAnimation = 0,
                                        bool fallDamage = false,
                                        Vector3 force = default(Vector3))
        {
            // If other mods block this call, we don't do anything!
            if (!__runOriginal)
            {
                return true; // Let the base game not run........
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"SyncDamageLethalBot called from game code on LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                lethalBotAI.DamageLethalBot(damageNumber, hasDamageSFX, callRPC, causeOfDeath, deathAnimation, fallDamage, force);
                
                // Still do the vanilla damage player, for other mods prefixes (ex: peepers)
                // The damage will be ignored because of the transpiler patch I made to skip all damage logic for LethalBots!
                return true;
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

        /// <summary>
        /// Patch to call our SetSpecialGrabAnimationBool method!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="___currentlyGrabbingObject"></param>
        /// <param name="setTrue"></param>
        /// <param name="currentItem"></param>
        /// <returns></returns>
        [HarmonyPatch("SetSpecialGrabAnimationBool")]
        [HarmonyPrefix]
        static bool SetSpecialGrabAnimationBool_Prefix(PlayerControllerB __instance, 
                                                    ref GrabbableObject ___currentlyGrabbingObject, 
                                                    bool setTrue, 
                                                    GrabbableObject currentItem)
        {
            if (currentItem == null)
            {
                #pragma warning disable Harmony003 // Harmony non-ref patch parameters modified
                currentItem = ___currentlyGrabbingObject;
                #pragma warning restore Harmony003 // Harmony non-ref patch parameters modified
            }
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                lethalBotAI.SetSpecialGrabAnimationBool(setBool: setTrue, item: currentItem);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Patch to call our FirstEmptyItemSlot method!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <param name="attemptingGrab"></param>
        /// <returns></returns>
        [HarmonyPatch("FirstEmptyItemSlot")]
        [HarmonyPrefix]
        static bool FirstEmptyItemSlot_Prefix(PlayerControllerB __instance, ref int __result, GrabbableObject attemptingGrab = null!)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                __result = lethalBotAI.FirstEmptyItemSlot(attemptingGrab);
                return false;
            }
            return true;
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

            if (!__instance.CheckConditionsForEmote())
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
            if (__instance.__rpc_exec_stage != __RpcExecStage.Execute || (!__instance.IsClient && !__instance.IsHost))
            {
                return true;
            }

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
                                          bool withRotation = false,
                                          float rot = 0f,
                                          bool allowInteractTrigger = false)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance);
            if (lethalBotAI != null)
            {
                StartOfRound.Instance.playerTeleportedEvent.Invoke(__instance);
                __instance.teleportingThisFrame = true;
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

        #region Transpilers

        [HarmonyPatch("DestroyItemInSlot")]
        [HarmonyTranspiler]
        [HarmonyPriority(Priority.First)]
        public static IEnumerable<CodeInstruction> DestroyItemInSlot_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            FieldInfo itemSlotIconsField = AccessTools.Field(typeof(HUDManager), "itemSlotIcons");
            FieldInfo holdingTwoHandedItemField = AccessTools.Field(typeof(HUDManager), "holdingTwoHandedItem");
            MethodInfo clearControlTipsMethod = AccessTools.Method(typeof(HUDManager), "ClearControlTips");

            // Other needed methods
            FieldInfo currentItemSlotField = AccessTools.Field(typeof(PlayerControllerB), "currentItemSlot");
            MethodInfo enabledMethod = AccessTools.PropertySetter(typeof(Behaviour), "enabled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].LoadsField(holdingTwoHandedItemField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 3].Calls(enabledMethod)
                    && codes[i + 4].Calls(getHUDManagerInstance)
                    && codes[i + 5].Calls(clearControlTipsMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 6, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogWarning($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DestroyItemInSlot_Transpiler could not bypass HUDManager if player is bot");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].LoadsField(itemSlotIconsField)
                    && codes[i + 2].IsLdarg(1)
                    && codes[i + 3].opcode == OpCodes.Ldelem_Ref
                    && codes[i + 4].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 5].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 6, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogWarning($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DestroyItemInSlot_Transpiler could not bypass HUDManager if player is bot 2");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DespawnHeldObject")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DespawnHeldObject_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            FieldInfo itemOnlySlotIconField = AccessTools.Field(typeof(HUDManager), "itemOnlySlotIcon");
            FieldInfo holdingTwoHandedItemField = AccessTools.Field(typeof(HUDManager), "holdingTwoHandedItem");

            // Other needed methods
            FieldInfo currentItemSlotField = AccessTools.Field(typeof(PlayerControllerB), "currentItemSlot");
            MethodInfo enabledMethod = AccessTools.PropertySetter(typeof(Behaviour), "enabled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(currentItemSlotField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_S //&& codes[i + 2].operand is int num && num == 50
                    && (codes[i + 3].opcode == OpCodes.Bne_Un_S || codes[i + 3].opcode == OpCodes.Bne_Un)
                    && codes[i + 4].Calls(getHUDManagerInstance)
                    && codes[i + 5].LoadsField(itemOnlySlotIconField)
                    && codes[i + 6].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 7].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if this != localPlayer skip calling HUDManager stuff)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count - 3; j++)
                {
                    if (codes[j].Calls(getHUDManagerInstance)
                        && codes[j + 1].LoadsField(holdingTwoHandedItemField)
                        && codes[j + 2].opcode == OpCodes.Ldc_I4_0
                        && codes[j + 3].Calls(enabledMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find holdingTwoHandedItem disable call!");
                    endIndex = startIndex + 10;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 4, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DespawnHeldObject_Transpiler could not bypass HUDManager if player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DropAllHeldItems")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DropAllHeldItems_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var nextStartIndex = 0;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            FieldInfo holdingTwoHandedItemField = AccessTools.Field(typeof(HUDManager), "holdingTwoHandedItem");
            MethodInfo clearControlTipsMethod = AccessTools.Method(typeof(HUDManager), "ClearControlTips");

            // Other needed methods
            MethodInfo enabledMethod = AccessTools.PropertySetter(typeof(Behaviour), "enabled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].LoadsField(holdingTwoHandedItemField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 3].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if this != localPlayer skip calling HUDManager stuff)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count - 2; j++)
                {
                    if (codes[j].Calls(getHUDManagerInstance)
                        && codes[j + 1].Calls(clearControlTipsMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find ClearControlTips call!");
                    endIndex = startIndex + 10;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 2, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1; // Don't set this back to -1, since there is another identical call later in this!
                nextStartIndex = endIndex + 2 + codesToAdd.Count; // Skip the codes we just added
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DropAllHeldItems_Transpiler could not bypass HUDManager if player is bot");
            }

            // ----------------------------------------------------------------------
            for (var i = nextStartIndex; i < codes.Count - 7; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].LoadsField(holdingTwoHandedItemField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 3].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if this != localPlayer skip calling HUDManager stuff)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count - 2; j++)
                {
                    if (codes[j].Calls(getHUDManagerInstance)
                        && codes[j + 1].Calls(clearControlTipsMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find ClearControlTips call!");
                    endIndex = startIndex + 10;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 2, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DropAllHeldItems_Transpiler could not bypass HUDManager if player is bot 2");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DiscardHeldObject")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DiscardHeldObject_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // StartOfRound
            MethodInfo getStartOfRoundInstance = AccessTools.PropertyGetter(typeof(StartOfRound), "Instance");
            MethodInfo sendChangedWeightEventMethod = AccessTools.Method(typeof(StartOfRound), "SendChangedWeightEvent");

            // HUDManager
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            FieldInfo itemOnlySlotIconField = AccessTools.Field(typeof(HUDManager), "itemOnlySlotIcon");
            FieldInfo holdingTwoHandedItemField = AccessTools.Field(typeof(HUDManager), "holdingTwoHandedItem");

            // Other needed methods
            FieldInfo currentItemSlotField = AccessTools.Field(typeof(PlayerControllerB), "currentItemSlot");
            MethodInfo enabledMethod = AccessTools.PropertySetter(typeof(Behaviour), "enabled");
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            FieldInfo isPlayerControlledField = AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].LoadsField(isPlayerControlledField)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)
                    && codes[i + 6].Calls(getStartOfRoundInstance) 
                    && codes[i + 7].Calls(sendChangedWeightEventMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Grab the label's destination and add our custom call
                Label skipSnapshot = (Label)codes[startIndex + 2].operand;
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DiscardHeldObject_Transpiler could not bypass SendChangedWeightEvent if player is bot");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(currentItemSlotField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_S //&& codes[i + 2].operand is int num && num == 50
                    && (codes[i + 3].opcode == OpCodes.Bne_Un_S || codes[i + 3].opcode == OpCodes.Bne_Un)
                    && codes[i + 4].Calls(getHUDManagerInstance)
                    && codes[i + 5].LoadsField(itemOnlySlotIconField)
                    && codes[i + 6].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 7].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if this != localPlayer skip calling HUDManager stuff)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count - 2; j++)
                {
                    if (codes[j].LoadsField(holdingTwoHandedItemField)
                        && codes[j + 1].opcode == OpCodes.Ldc_I4_0
                        && codes[j + 2].Calls(enabledMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find holdingTwoHandedItem disable call!");
                    endIndex = startIndex + 17;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 3, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.DiscardHeldObject_Transpiler could not bypass HUDManager if player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("SetObjectAsNoLongerHeld")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SetObjectAsNoLongerHeld_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // StartOfRound
            MethodInfo getStartOfRoundInstance = AccessTools.PropertyGetter(typeof(StartOfRound), "Instance");
            MethodInfo sendChangedWeightEventMethod = AccessTools.Method(typeof(StartOfRound), "SendChangedWeightEvent");

            // Other needed methods
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            FieldInfo isPlayerControlledField = AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].LoadsField(isPlayerControlledField)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)
                    && codes[i + 6].Calls(getStartOfRoundInstance)
                    && codes[i + 7].Calls(sendChangedWeightEventMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Grab the label's destination and add our custom call
                Label skipSnapshot = (Label)codes[startIndex + 2].operand;
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.SetObjectAsNoLongerHeld_Transpiler could not bypass SendChangedWeightEvent if player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("PlaceGrabbableObject")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> PlaceGrabbableObject_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // StartOfRound
            MethodInfo getStartOfRoundInstance = AccessTools.PropertyGetter(typeof(StartOfRound), "Instance");
            MethodInfo sendChangedWeightEventMethod = AccessTools.Method(typeof(StartOfRound), "SendChangedWeightEvent");

            // Other needed methods
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            FieldInfo isPlayerControlledField = AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].LoadsField(isPlayerControlledField)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)
                    && codes[i + 6].Calls(getStartOfRoundInstance)
                    && codes[i + 7].Calls(sendChangedWeightEventMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Grab the label's destination and add our custom call
                Label skipSnapshot = (Label)codes[startIndex + 2].operand;
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlaceGrabbableObject_Transpiler could not bypass SendChangedWeightEvent if player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("PlaceObjectClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> PlaceObjectClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            FieldInfo itemOnlySlotIconField = AccessTools.Field(typeof(HUDManager), "itemOnlySlotIcon");

            // Other needed methods
            FieldInfo currentItemSlotField = AccessTools.Field(typeof(PlayerControllerB), "currentItemSlot");
            MethodInfo enabledMethod = AccessTools.PropertySetter(typeof(Behaviour), "enabled");
            FieldInfo throwingObjectField = AccessTools.Field(typeof(PlayerControllerB), "throwingObject");
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(currentItemSlotField)
                    && codes[i + 2].opcode == OpCodes.Ldc_I4_S //&& codes[i + 2].operand is int num && num == 50
                    && (codes[i + 3].opcode == OpCodes.Bne_Un_S || codes[i + 3].opcode == OpCodes.Bne_Un)
                    && codes[i + 4].Calls(getHUDManagerInstance)
                    && codes[i + 5].LoadsField(itemOnlySlotIconField)
                    && codes[i + 6].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 7].Calls(enabledMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if this != localPlayer skip calling HUDManager stuff)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count - 3; j++)
                {
                    if (codes[j].LoadsField(currentItemSlotField)
                        && codes[j + 1].opcode == OpCodes.Ldelem_Ref
                        && codes[j + 2].opcode == OpCodes.Ldc_I4_0
                        && codes[j + 3].Calls(enabledMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find itemSlotIcons disable call!");
                    endIndex = startIndex + 10;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 4, nop); // Set label to the instruction **after** the HUDManager stuff

                // Insert new method call to skip HUDManager stuff if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlaceObjectClientRpc_Transpiler could not bypass HUDManager if player is bot");
            }

            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 5].StoresField(throwingObjectField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace original code
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = PatchesUtil.IsPlayerLocalOrLethalBotMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.PlaceObjectClientRpc_Transpiler could not force throwingObject false for all clients if player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("GrabObjectClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> GrabObjectClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Needed methods
            FieldInfo grabInvalidatedField = AccessTools.Field(typeof(PlayerControllerB), "grabInvalidated");
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 7; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 5].IsLdarg(0)
                    && codes[i + 6].opcode == OpCodes.Ldc_I4_1
                    && codes[i + 7].StoresField(grabInvalidatedField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace original code
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = PatchesUtil.IsPlayerLocalOrLethalBotMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.GrabObjectClientRpc_Transpiler could not force grabInvalidated true for all clients if player is bot and the grab was invalidated");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("ThrowObjectClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ThrowObjectClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Needed methods
            FieldInfo throwingObjectField = AccessTools.Field(typeof(PlayerControllerB), "throwingObject");
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(isOwnerGetter)
                    && (codes[i + 2].opcode == OpCodes.Brfalse_S || codes[i + 2].opcode == OpCodes.Brfalse)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 5].StoresField(throwingObjectField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace original code
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = PatchesUtil.IsPlayerLocalOrLethalBotMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.ThrowObjectClientRpc_Transpiler could not force throwingObject false for all clients if player is bot");
            }

            return codes.AsEnumerable();
        }

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

        /// <summary>
        /// So you might ask, why do we need to transpile the damage player method when we have a prefix for it?
        /// You see unlike the KillPlayer function, I can't just skip it by changing the isPlayerDead flag to true.
        /// My solution, I patch this function to check if the player is a bot.
        /// If so, I force the function to return early and let my prefix handle the damage logic instead.
        /// This allows Postfixes from other mods to still run and not break compatibility, while also allowing the lethalBot to take damage
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("DamagePlayer")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DamagePlayer_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // The start index is 0 since we want to insert our check at the very start of the method to skip all the
            // damage logic for bots and let our prefix handle it instead!
            const int startIndex = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Create our label's destination....
            Label skipSnapshot = generator.DefineLabel();
            codes[startIndex].labels.Add(skipSnapshot);

            // Insert new method call to return early if this is a bot controller
            List<CodeInstruction> codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                new CodeInstruction(OpCodes.Brfalse, skipSnapshot),
                new CodeInstruction(OpCodes.Ret)
            };

            // Insert the new instruction to call the replacement method.
            codes.InsertRange(startIndex, codesToAdd);

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

        [HarmonyPatch("DropHeldItem")]
        [HarmonyPostfix]
        static void DropHeldItem_PostFix(PlayerControllerB __instance, GrabbableObject dropItem)
        {
            // If a bot drops an item, add it to the just dropped item list
            if (LethalBotManager.Instance.IsPlayerLethalBot(__instance))
            {
                LethalBotAI.DictJustDroppedItems[dropItem] = Time.realtimeSinceStartup;
            }
        }

        [HarmonyPatch("SetObjectAsNoLongerHeld")]
        [HarmonyPostfix]
        static void SetObjectAsNoLongerHeld_PostFix(PlayerControllerB __instance, GrabbableObject dropObject)
        {
            // If a bot drops an item, add it to the just dropped item list
            if (LethalBotManager.Instance.IsPlayerLethalBot(__instance))
            {
                LethalBotAI.DictJustDroppedItems[dropObject] = Time.realtimeSinceStartup;
            }
        }

        [HarmonyPatch("PlaceGrabbableObject")]
        [HarmonyPostfix]
        static void PlaceGrabbableObject_PostFix(PlayerControllerB __instance, GrabbableObject placeObject)
        {
            // If a bot drops an item, add it to the just dropped item list
            if (LethalBotManager.Instance.IsPlayerLethalBot(__instance))
            {
                LethalBotAI.DictJustDroppedItems[placeObject] = Time.realtimeSinceStartup;
            }
        }

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
                                                                 int ___playerMask)
        {
            // Set tooltip when pointing at lethalBot
            RaycastHit[] raycastHits = new RaycastHit[3];
            ___interactRay = new Ray(__instance.gameplayCamera.transform.position, __instance.gameplayCamera.transform.forward);
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

        #endregion
    }
}
