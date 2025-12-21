using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using LethalBots.Utils;
using System.Reflection;
using GameNetcodeStuff;
using System.ComponentModel;
using LethalBots.Managers;
using LethalBots.AI;
using System.Collections;
using System;

namespace LethalBots.Patches.MapPatches
{
    /// <summary>
    /// Patch for <c>InteractTrigger</c>
    /// </summary>
    [HarmonyPatch(typeof(InteractTrigger))]
    public class InteractTriggerPatch
    {
        // Cache our private field and method lookups!
        private static readonly FieldInfo lockedPlayerField = AccessTools.Field(typeof(InteractTrigger), "lockedPlayer");

        private static readonly FieldInfo hasTriggeredField = AccessTools.Field(typeof(InteractTrigger), "hasTriggered");

        private static readonly FieldInfo playerScriptInSpecialAnimationField = AccessTools.Field(typeof(InteractTrigger), "playerScriptInSpecialAnimation");

        private static readonly MethodInfo ladderPositionObstructedMethod = AccessTools.Method(typeof(InteractTrigger), "LadderPositionObstructed");

        /*/// <summary>
        /// Patch for not making the intern able to cancel the ladder animation of a player already on the ladder 
        /// </summary>
        /// <remarks>
        /// Behaviour still can't fully understand, not more than one player on the ladder ? can/should a player cancel another player on ladder ? not clear
        /// </remarks>
        /// <param name="instance"></param>
        /// <param name="playerTransform"></param>
        [HarmonyPatch("Interact")]
        [HarmonyReversePatch]
        public static void Interact_ReversePatch(object instance, Transform playerTransform)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var startIndex = -1;
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                // ----------------------------------------------------------------------
                for (var i = 0; i < codes.Count - 1; i++)
                {
                    if (codes[i].ToString() == "ldarg.0 NULL" //36
                        && codes[i + 1].ToString() == "call void InteractTrigger::CancelLadderAnimation()")
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
                    startIndex = -1;
                }
                else
                {
                    Plugin.LogError($"LethalBot.Patches.MapPatches.InteractTriggerPatch.Interact_ReversePatch could not remove CancelLadderAnimation");
                }

                return codes.AsEnumerable();
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            _ = Transpiler(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }*/

        /// <summary>
        /// Reverse patch to call <c>Interact</c>
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        //[HarmonyPatch("Interact")]
        //[HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        //[HarmonyPriority(Priority.Last)]
        //public static void Interact_ReversePatch(object instance, Transform playerTransform) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.InteractTriggerPatch.Interact_Transpiler");

        ///<summary>
        /// Reverse patch to call <c>StopUsingServerRpc</c>
        ///</summary>
        [HarmonyPatch("StopUsingServerRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void StopUsingServerRpc_ReversePatch(object instance, int playerNum) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.InteractTriggerPatch.StopUsingServerRpc_ReversePatch");

        ///<summary>
        /// Reverse patch to call <c>UpdateUsedByPlayerServerRpc</c>
        ///</summary>
        [HarmonyPatch("UpdateUsedByPlayerServerRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void UpdateUsedByPlayerServerRpc_ReversePatch(object instance, int playerNum) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.InteractTriggerPatch.UpdateUsedByPlayerServerRpc_ReversePatch");

        ///<summary>
        /// Reverse patch to call <c>LadderPositionObstructed</c>
        ///</summary>
        [HarmonyPatch("LadderPositionObstructed")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static bool LadderPositionObstructed_ReversePatch(object instance, PlayerControllerB playerController) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.InteractTriggerPatch.LadderPositionObstructed_ReversePatch");

        /*/// <summary>
        /// Patch for not making the bot not cancel the player ladder coroutine 
        /// </summary>
        /// <remarks>
        /// Due to the bot running its code on the local client, it causes player related events to happen to the Local Player!
        /// I have to change it to make the code check for the human player if possible so nothing breaks as a result!
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        [HarmonyPatch("Interact")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Interact_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            Plugin.LogInfo("Starting InteractTrigger::Interact transpiler!");
            for (var i = 0; i < codes.Count; i++)
            {
                Plugin.LogInfo($"Index: {i} Code: {codes[i].ToString()}");
                if (codes[i].ToString() == "ldarg.0 NULL" //36
                    && codes[i + 1].ToString() == "ldfld UnityEngine.Coroutine InteractTrigger::useLadderCoroutine")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod),
                    new CodeInstruction(OpCodes.Brfalse, codes[startIndex + 1].labels[0]),
                };
                codes.InsertRange(startIndex, codesToAdd);
                //codes[startIndex].operand = null;
                //codes[startIndex + 1].opcode = OpCodes.Nop;
               // codes[startIndex + 1].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.InteractTriggerPatch.Interact_Transpiler could not fix bots deleting the ladder coroutine!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch for not making the bot able to cancel the ladder animation of a player already on the ladder 
        /// </summary>
        /// <remarks>
        /// Due to the bot running its code on the local client, it causes player related events to happen to the Local Player!
        /// I have to change it to make the code check for the human player if possible so nothing breaks as a result!
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        [HarmonyPatch("ladderClimbAnimation")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ladderClimbAnimation_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            Plugin.LogInfo("Starting InteractTrigger::ladderClimbAnimation transpiler!");
            for (var i = 0; i < codes.Count; i++)
            {
                Plugin.LogInfo($"Index: {i} Code: {codes[i].ToString()}");
                if (codes[i].ToString() == "ldarg.0 NULL" //36
                    && codes[i + 1].ToString() == "call void InteractTrigger::CancelLadderAnimation()")
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.InteractTriggerPatch.ladderClimbAnimation_Transpiler could not fix bots creating issues with the human player and ladders!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch for not making the bot not clear the control tips of its owner
        /// </summary>
        /// <remarks>
        /// Due to the bot running its code on the local client, it causes player related events to happen to the Local Player!
        /// I have to change it to make the code check for the human player if possible so nothing breaks as a result!
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        [HarmonyPatch("specialInteractAnimation")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> specialInteractAnimation_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            Plugin.LogInfo("Starting InteractTrigger::specialInteractAnimation transpiler!");
            for (var i = 0; i < codes.Count; i++)
            {
                Plugin.LogInfo($"Index: {i} Code: {codes[i].ToString()}");
                if (codes[i].ToString() == "ldarg.0 NULL" //36
                    && codes[i + 1].ToString() == "call void InteractTrigger::CancelLadderAnimation()")
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.InteractTriggerPatch.specialInteractAnimation_Transpiler could not stop bots from clearing control tips!");
            }

            return codes.AsEnumerable();
        }*/

        private static IEnumerator ladderClimbAnimation(InteractTrigger ladder, PlayerControllerB playerController)
        {
            ladder.onInteractEarly.Invoke(null);
            playerController.UpdateSpecialAnimationValue(specialAnimation: true, (short)ladder.ladderPlayerPositionNode.eulerAngles.y, 0f, climbingLadder: true);
            playerController.enteringSpecialAnimation = true;
            playerController.inSpecialInteractAnimation = true;
            playerController.currentTriggerInAnimationWith = ladder;
            playerController.isCrouching = false;
            playerController.playerBodyAnimator.SetBool("crouching", value: false);
            playerController.playerBodyAnimator.SetTrigger("EnterLadder");
            playerController.thisController.enabled = false;
            float timer = 0f;
            Vector3 ladderPosition = ladder.ladderPlayerPositionNode.position;
            Quaternion ladderAngle = ladder.ladderPlayerPositionNode.rotation;
            while (timer <= ladder.animationWaitTime)
            {
                yield return null;
                timer += Time.deltaTime;
                playerController.thisPlayerBody.position = Vector3.Lerp(playerController.thisPlayerBody.position, ladderPosition, Mathf.SmoothStep(0f, 1f, timer / ladder.animationWaitTime));
                playerController.thisPlayerBody.rotation = Quaternion.Lerp(playerController.thisPlayerBody.rotation, ladderAngle, Mathf.SmoothStep(0f, 1f, timer / ladder.animationWaitTime));
            }
            playerController.TeleportPlayer(ladderPosition, withRotation: false, 0f, allowInteractTrigger: true);
            Plugin.LogDebug("Finished snapping to ladder");
            playerController.playerBodyAnimator.SetBool("ClimbingLadder", value: true);
            playerController.isClimbingLadder = true;
            playerController.enteringSpecialAnimation = false;
            playerController.ladderCameraHorizontal = 0f;
            playerController.clampCameraRotation = ladder.bottomOfLadderPosition.eulerAngles;
            int finishClimbingLadder = 0;
            while (finishClimbingLadder == 0)
            {
                yield return null;
                if (playerController.thisPlayerBody.position.y < ladder.bottomOfLadderPosition.position.y)
                {
                    finishClimbingLadder = 1;
                }
                else if (playerController.thisPlayerBody.position.y + 2f > ladder.topOfLadderPosition.position.y)
                {
                    finishClimbingLadder = 2;
                }
            }
            //playerController.isClimbingLadder = false;
            playerController.playerBodyAnimator.SetBool("ClimbingLadder", value: false);
            if (finishClimbingLadder == 1)
            {
                ladderPosition = ladder.bottomOfLadderPosition.position;
            }
            else if (!ladder.useRaycastToGetTopPosition)
            {
                ladderPosition = ladder.topOfLadderPosition.position;
            }
            else
            {
                Ray ray = new Ray(playerController.transform.position + Vector3.up, ladder.topOfLadderPosition.position + Vector3.up - playerController.transform.position + Vector3.up);
                if (Physics.Linecast(playerController.transform.position + Vector3.up, ladder.topOfLadderPosition.position + Vector3.up, out var hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Debug.DrawLine(playerController.transform.position + Vector3.up, ladder.topOfLadderPosition.position + Vector3.up, Color.red, 10f);
                    ladderPosition = ray.GetPoint(Mathf.Max(hitInfo.distance - 1.2f, 0f));
                    Debug.DrawRay(ladderPosition, Vector3.up * 0.5f, Color.yellow, 10f);
                }
                else
                {
                    Debug.DrawLine(playerController.transform.position + Vector3.up, ladder.topOfLadderPosition.position + Vector3.up, Color.green, 10f);
                    ladderPosition = ladder.topOfLadderPosition.position;
                }
            }
            timer = 0f;
            float shorterWaitTime = ladder.animationWaitTime / 2f;
            while (timer <= shorterWaitTime)
            {
                yield return null;
                timer += Time.deltaTime;
                playerController.thisPlayerBody.position = Vector3.Lerp(playerController.thisPlayerBody.position, ladderPosition, Mathf.SmoothStep(0f, 1f, timer / shorterWaitTime));
                playerController.thisPlayerBody.rotation = Quaternion.Lerp(playerController.thisPlayerBody.rotation, ladderAngle, Mathf.SmoothStep(0f, 1f, timer / shorterWaitTime));
                playerController.gameplayCamera.transform.rotation = Quaternion.Slerp(playerController.gameplayCamera.transform.rotation, playerController.gameplayCamera.transform.parent.rotation, Mathf.SmoothStep(0f, 1f, timer / shorterWaitTime));
            }
            playerController.gameplayCamera.transform.localEulerAngles = Vector3.zero;
            Debug.Log("Finished ladder sequence");
            playerController.UpdateSpecialAnimationValue(specialAnimation: false, 0);
            playerController.isClimbingLadder = false;
            playerController.inSpecialInteractAnimation = false;
            playerController.currentTriggerInAnimationWith = null; // HACKHACK: Where does the base game set this to null?
            playerController.thisController.enabled = true; // NEEDTOVALIDATE: What happens if this is true for players that are not the local player?
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerController);
            if (lethalBotAI != null && lethalBotAI.agent.isOnOffMeshLink)
            {
                lethalBotAI.agent.CompleteOffMeshLink();
                lethalBotAI.TeleportLethalBot(ladderPosition, lethalBotAI.isOutside, allowInteractTrigger: true);
            }
            if (lethalBotAI != null)
            {
                lethalBotAI.useLadderCoroutine = null;
            }
            ladder.currentCooldownValue = ladder.cooldownTime;
            ladder.onInteract.Invoke(null);
        }

        private static IEnumerator specialInteractAnimation(InteractTrigger trigger, PlayerControllerB playerController)
        {
            UpdateUsedByPlayerServerRpc_ReversePatch(trigger, (int)playerController.playerClientId);
            trigger.onInteractEarly.Invoke(null);
            trigger.isPlayingSpecialAnimation = true;
            lockedPlayerField.SetValue(trigger, playerController.thisPlayerBody);
            playerScriptInSpecialAnimationField.SetValue(trigger, playerController);
            if (trigger.clampLooking)
            {
                playerController.minVerticalClamp = trigger.minVerticalClamp;
                playerController.maxVerticalClamp = trigger.maxVerticalClamp;
                playerController.horizontalClamp = trigger.horizontalClamp;
                playerController.clampLooking = true;
            }
            if ((bool)trigger.overridePlayerParent)
            {
                playerController.overridePhysicsParent = trigger.overridePlayerParent;
            }
            if (trigger.setVehicleAnimation)
            {
                playerController.inVehicleAnimation = true;
            }
            if (trigger.hidePlayerItem && playerController.currentlyHeldObjectServer != null)
            {
                playerController.currentlyHeldObjectServer.EnableItemMeshes(enable: false);
            }
            playerController.Crouch(crouch: false);
            playerController.UpdateSpecialAnimationValue(specialAnimation: true, (short)trigger.playerPositionNode.eulerAngles.y);
            playerController.inSpecialInteractAnimation = true;
            playerController.currentTriggerInAnimationWith = trigger;
            playerController.playerBodyAnimator.ResetTrigger(trigger.animationString);
            playerController.playerBodyAnimator.SetTrigger(trigger.animationString);
            //HUDManager.Instance.ClearControlTips();
            if (!trigger.stopAnimationManually)
            {
                yield return new WaitForSeconds(trigger.animationWaitTime);
                trigger.StopSpecialAnimation();
            }
        }

        private static void CancelLadderAnimation(InteractTrigger ladder, PlayerControllerB playerController, ref Transform ___lockedPlayer)
        {
            LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(playerController);
            if (lethalBot == null)
            {
                Plugin.LogWarning("Attempted to call custom CancelLadderAnimation on a human player!");
                return;
            }
            if (lethalBot.useLadderCoroutine != null)
            {
                ladder.StopCoroutine(lethalBot.useLadderCoroutine);
                lethalBot.useLadderCoroutine = null;
            }
            ladder.onCancelAnimation.Invoke(playerController);
            playerController.currentTriggerInAnimationWith = null;
            playerController.isClimbingLadder = false;
            playerController.thisController.enabled = true; // NEEDTOVALIDATE: What happens if this is true for players that are not the local player?
            playerController.playerBodyAnimator.SetBool("ClimbingLadder", value: false);
            playerController.gameplayCamera.transform.localEulerAngles = Vector3.zero;
            playerController.UpdateSpecialAnimationValue(specialAnimation: false, 0);
            playerController.inSpecialInteractAnimation = false;
            ___lockedPlayer = null!;
            ladder.currentCooldownValue = ladder.cooldownTime;
            if (ladder.hidePlayerItem && playerController.currentlyHeldObjectServer != null)
            {
                playerController.currentlyHeldObjectServer.EnableItemMeshes(enable: true);
            }
            ladder.onInteract.Invoke(null);
        }

        /// <summary>
        /// Patch for not making the bot not cancel the player ladder coroutine 
        /// </summary>
        /// <remarks>
        /// Due to the bot running its code on the local client, it causes player related events to happen to the Local Player!
        /// I have to change it to make the code check for the human player if possible so nothing breaks as a result!
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        [HarmonyPatch("Interact")]
        [HarmonyPrefix]
        public static bool Interact_Prefix(InteractTrigger __instance, Transform playerTransform, ref bool ___hasTriggered, ref Transform ___lockedPlayer)
        {
            if (playerTransform == null || playerTransform == GameNetworkManager.Instance.localPlayerController.transform)
            {
                Plugin.LogDebug("Default interact called! Player is local playerController!");
                return true; // Run the default!
            }

            // We are a bot, run the logic here without affecting the human player!
            PlayerControllerB player = playerTransform.GetComponent<PlayerControllerB>();
            LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(player);
            if (lethalBot == null)
            {
                Plugin.LogDebug("Default interact called! Player is not a bot!");
                return true; // Run the default if the player isn't a bot!
            }

            // Run the code, but with changes for bots!
            // WARNING: This HAS to be manually updated when the game updates,
            // this is just the way it goes until I find a good way to use a transpiler!
            Plugin.LogDebug("Custom interact called! Player is a bot!");
            if ((__instance.triggerOnce && ___hasTriggered) || StartOfRound.Instance.firingPlayersCutsceneRunning)
            {
                return false;
            }
            ___hasTriggered = true;
            if (__instance.RandomChanceTrigger && UnityEngine.Random.Range(0, 101) > __instance.randomChancePercentage)
            {
                return false;
            }
            if (!__instance.interactable || __instance.isPlayingSpecialAnimation || lethalBot.useLadderCoroutine != null) // Don't check usingLadder, since it should only be set for the local client!
            {
                if (lethalBot.useLadderCoroutine != null)
                {
                    CancelLadderAnimation(__instance, player, ref ___lockedPlayer);
                }
                return false;
            }
            //PlayerControllerB component = playerTransform.GetComponent<PlayerControllerB>();
            if (player.inSpecialInteractAnimation && !player.isClimbingLadder && !__instance.allowUseWhileInAnimation)
            {
                return false;
            }
            if (__instance.interactCooldown)
            {
                if (__instance.currentCooldownValue >= 0f)
                {
                    return false;
                }
                __instance.currentCooldownValue = __instance.cooldownTime;
            }
            if (!__instance.specialCharacterAnimation && !__instance.isLadder)
            {
                __instance.onInteract.Invoke(player);
                return false;
            }
            player.ResetFallGravity();
            if (__instance.isLadder)
            {
                if (player.isInHangarShipRoom)
                {
                    return false;
                }
                __instance.ladderPlayerPositionNode.position = new Vector3(__instance.ladderHorizontalPosition.position.x, Mathf.Clamp(player.thisPlayerBody.position.y, __instance.bottomOfLadderPosition.position.y + 0.3f, __instance.topOfLadderPosition.position.y - 2.2f), __instance.ladderHorizontalPosition.position.z);
                if (!LadderPositionObstructed_ReversePatch(__instance, player))
                {
                    if (lethalBot.useLadderCoroutine != null)
                    {
                        __instance.StopCoroutine(lethalBot.useLadderCoroutine);
                    }
                    lethalBot.useLadderCoroutine = __instance.StartCoroutine(ladderClimbAnimation(__instance, player));
                }
            }
            else
            {
                __instance.StartCoroutine(specialInteractAnimation(__instance, player));
            }
            return false;
        }

        [HarmonyPatch("StopSpecialAnimation")]
        [HarmonyPrefix]
        public static bool StopSpecialAnimation_Prefix(InteractTrigger __instance, ref Transform ___lockedPlayer)
        {
            // Check if the locked player is invaild or the local player,
            // if so we don't want to use our custom logic!
            Transform playerTransform = ___lockedPlayer;
            if (playerTransform == GameNetworkManager.Instance?.localPlayerController?.transform)
            {
                Plugin.LogDebug($"Default stop special animation called! Player {playerTransform} is local playerController!");
                return true; // Run the default!
            }

            // We are a bot, run the logic here without affecting the human player!
            PlayerControllerB? player = playerTransform?.GetComponent<PlayerControllerB>();
            if (player != null && !LethalBotManager.Instance.IsPlayerLethalBot(player))
            {
                Plugin.LogDebug($"Default stop special animation called! Player {playerTransform} is not a bot!");
                return true; // Run the default if the player isn't a bot!
            }

            // Now we need to find the bot using this interact trigger!
            LethalBotAI? lethalBotAI = null;
            foreach (PlayerControllerB tempPlayer in StartOfRound.Instance.allPlayerScripts)
            {
                LethalBotAI? tempLethalBotAI = LethalBotManager.Instance.GetLethalBotAI(tempPlayer);
                if (tempLethalBotAI != null 
                    && (tempPlayer.isPlayerControlled 
                        || tempPlayer.isPlayerDead) 
                    && tempPlayer.currentTriggerInAnimationWith == __instance)
                {
                    lethalBotAI = tempLethalBotAI;
                    player = tempPlayer;
                    playerTransform = tempPlayer.transform;
                    break;
                }
            }

            // If we failed to find a bot or player, exit out early!
            if (lethalBotAI == null || player == null)
            {
                return false;
            }

            Plugin.LogDebug($"Stop special animation on {__instance.gameObject.name}, by {playerTransform}; {player}");
            if (__instance.isPlayingSpecialAnimation && __instance.stopAnimationManually && playerTransform != null)
            {
                Plugin.LogDebug($"Calling stop animation function StopUsing server rpc for playerController: {player.playerClientId}");
                StopUsingServerRpc_ReversePatch(__instance, (int)player.playerClientId);
            }
            if (player != null)
            {
                __instance.onCancelAnimation.Invoke(player);
                if (__instance.hidePlayerItem && player.currentlyHeldObjectServer != null)
                {
                    player.currentlyHeldObjectServer.EnableItemMeshes(enable: true);
                }
                __instance.isPlayingSpecialAnimation = false;
                player.inSpecialInteractAnimation = false;
                if (player.clampLooking)
                {
                    player.gameplayCamera.transform.localEulerAngles = Vector3.zero;
                }
                player.clampLooking = false;
                player.inVehicleAnimation = false;
                if ((bool)__instance.overridePlayerParent && player.overridePhysicsParent == __instance.overridePlayerParent)
                {
                    player.overridePhysicsParent = null;
                }
                player.currentTriggerInAnimationWith = null;
                if (player.isClimbingLadder)
                {
                    CancelLadderAnimation(__instance, player, ref ___lockedPlayer);
                    player.isClimbingLadder = false;
                }
                Plugin.LogDebug("Stop special animation F");
                if (__instance.stopAnimationManually)
                {
                    player.playerBodyAnimator.SetTrigger(__instance.stopAnimationString);
                }
                player.UpdateSpecialAnimationValue(specialAnimation: false, 0);
                ___lockedPlayer = null!;
                __instance.currentCooldownValue = __instance.cooldownTime;
                __instance.onInteract.Invoke(null);
                Plugin.LogDebug("Stop special animation G");
                lethalBotAI.StopCoroutine(lethalBotAI.useInteractTriggerCoroutine);
                lethalBotAI.useInteractTriggerCoroutine = null;
                /*if (player.isHoldingObject && player.currentlyHeldObjectServer != null)
                {
                    player.currentlyHeldObjectServer.SetControlTipsForItem();
                }*/
            }
            return false;
        }
    }
}
