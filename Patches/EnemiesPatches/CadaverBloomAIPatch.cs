using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    [HarmonyPatch(typeof(CadaverBloomAI))]
    public class CadaverBloomAIPatch
    {
        [HarmonyPatch("OnCollideWithPlayer")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OnCollideWithPlayer_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Target function
            MethodInfo jumpToFearLevelMethod = AccessTools.Method(typeof(PlayerControllerB), "JumpToFearLevel");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 2].opcode == OpCodes.Ldc_R4 && codes[i + 2].operand is float f && f == 1f
                    && codes[i + 3].opcode == OpCodes.Ldc_I4_1
                    && codes[i + 4].Calls(jumpToFearLevelMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace with the local variable that holds the player controller,
                // for some reason Zeekerss decided to call GameNetworkManager.Instance for this one function call.
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldloc_0;
                codes[startIndex + 1].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.OnOnCollideWithPlayer_Transpiler could not replace localPlayerController with locally cached player");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Simple postfix to get the fear level change to work for bots as well.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="___lostPlayerInChase"></param>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(CadaverBloomAI __instance, 
            ref bool ___lostPlayerInChase, 
            ref float ___seePlayerMeter,
            ref float ___timeAtLastHeardNoise,
            ref float ___timeAtLastHitStun)
        {
            // Just like the base game, if the cadaver bloom is in a special animation, don't do anything.
            if (__instance.isEnemyDead || __instance.inSpecialAnimation)
            {
                return;
            }

            // Limit the frequency of this code since it can be quite expensive, especially if there are multiple cadaver
            UpdateLimiter updateLimiter = UpdateLimiter.GetOrCreateMonitor(__instance);
            if (!updateLimiter.CanUpdate())
            {
                return;
            }

            // Reset the limiter
            updateLimiter.Invalidate();

            // In the base game, this part of the code returns early.
            bool flag = Time.realtimeSinceStartup - ___timeAtLastHeardNoise < 1.5f;
		    bool flag2 = Time.realtimeSinceStartup - ___timeAtLastHitStun < 0.75f;
		    if ((flag && (___lostPlayerInChase || ___seePlayerMeter < 0.07f)) || flag2 || __instance.stunNormalizedTimer > 0f)
            {
                return;
            }

            // Just like the base game, limit the range.
            int range = 18;
            if (TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = 8;
            }

            // Loop through all the lethal bots and check if they have line of sight to the cadaver bloom, if they do, increase their fear level.
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (var lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null && lethalBotController.HasLineOfSightToPosition(__instance.transform.position + Vector3.up * 0.25f, 80f, range, 5f))
                {
                    if (__instance.currentBehaviourStateIndex == 1 && !___lostPlayerInChase)
                    {
                        lethalBotController.IncreaseFearLevelOverTime(0.8f, 0.7f);
                    }
                    else
                    {
                        lethalBotController.IncreaseFearLevelOverTime(0.8f, 0.3f);
                    }
                }
            }
        }

        [HarmonyPatch("BurstForth")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> BurstForth_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 2].Calls(opEqualityMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Modify call to check both the local player and bots owned by the local player.
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
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BurstForth_Transpiler could not change kill local player comparison to consider bots as well");
            }

            return codes.AsEnumerable();
        }
    }
}
