using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patches for <c>ForestGiantAI</c>
    /// </summary>
    [HarmonyPatch(typeof(ForestGiantAI))]
    public class ForestGiantAIPatch
    {
        [HarmonyPatch("GiantSeePlayerEffect")]
        [HarmonyPostfix]
        public static void GiantSeePlayerEffect_Postfix(ForestGiantAI __instance, ref bool ___lostPlayerInChase)
        {
            LethalBotAI?[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI? lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController.Npc;
                if (lethalBotController != null 
                    && !lethalBotController.isPlayerDead
                    && !lethalBotController.isInsideFactory)
                {
                    if (__instance.currentBehaviourStateIndex == 1 
                        && __instance.chasingPlayer == lethalBotController
                        && !___lostPlayerInChase)
                    {
                        lethalBotController.IncreaseFearLevelOverTime(1.4f);
                        continue;
                    }

                    //bool flag = false;
                    if (!lethalBotController.isInHangarShipRoom && __instance.CheckLineOfSightForPosition(lethalBotController.gameplayCamera.transform.position, 45f, 70))
                    {
                        if ((__instance.transform.position - lethalBotController.transform.position).sqrMagnitude < 15f * 15f)
                        {
                            lethalBotController.JumpToFearLevel(0.7f);
                        }
                        else
                        {
                            lethalBotController.JumpToFearLevel(0.4f);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// <inheritdoc cref="ButlerBeesEnemyAIPatch.OnCollideWithPlayer_Transpiler"/>
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("OnCollideWithPlayer")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OnCollideWithPlayer_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);
            
            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()" //31
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"
                    && codes[i + 2].ToString() == "call static bool UnityEngine.Object::op_Equality(UnityEngine.Object x, UnityEngine.Object y)") //49
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
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.ForestGiantAIPatch.OnCollideWithPlayer_Transpiler could not check if player local or bot");
            }

            // ----------------------------------------------------------------------
            // Replace on all occurences localPlayerController by the player from getComponent just before, so the player is local or bot
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController")
                {
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldloc_0;
                    codes[i + 1].operand = null;
                }
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Fix to allow bots to be crushed as well!
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("AnimationEventA")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> AnimationEventA_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()" //31
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"
                    && codes[i + 2].ToString() == "call static bool UnityEngine.Object::op_Equality(UnityEngine.Object x, UnityEngine.Object y)") //49
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
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.ForestGiantAIPatch.AnimationEventA_Transpiler could not check if player local or bot");
            }

            // ----------------------------------------------------------------------
            // Replace on all occurences localPlayerController by the player from getComponent just before, so the player is local or bot
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController")
                {
                    Plugin.LogDebug($"Replaced GameNetworkManagerInstance at {i} in ForestGiantAI AnimationEventA with component!");
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldloc_0;
                    codes[i + 1].operand = null;
                }
            }

            return codes.AsEnumerable();
        }
    }
}
