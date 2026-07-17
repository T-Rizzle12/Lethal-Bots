using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patches for <c>CrawlerAI</c>
    /// </summary>
    [HarmonyPatch(typeof(CrawlerAI))]
    [HarmonyAfter(Const.MORECOMPANY_GUID)]
    public class CrawlerAIPatch
    {
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
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].ToString() == "ldarg.0 NULL"//36
                    && codes[i + 1].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()"//37
                    && codes[i + 2].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"
                    && codes[i + 5].ToString() == "call void CrawlerAI::HitPlayerServerRpc(int playerId)")//41
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Ldloc_0;
                codes[startIndex + 2].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.CrawlerAIPatch.OnCollideWithPlayer_Transpiler could not change use of correct player id for HitPlayerServerRpc.");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()"//42
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"
                    && codes[i + 4].ToString() == "callvirt void GameNetcodeStuff.PlayerControllerB::JumpToFearLevel(float targetFearLevel, bool onlyGoUp)")//46
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldloc_0;
                codes[startIndex + 1].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.CrawlerAIPatch.OnCollideWithPlayer_Transpiler could not change use of correct player id for JumpToFearLevel.");
            }

            // ----------------------------------------------------------------------
            //Plugin.LogDebug($"OnCollideWithPlayer ======================");
            //for (var i = 0; i < codes.Count; i++)
            //{
            //    Plugin.LogDebug($"{i} {codes[i].ToString()}");
            //}
            //Plugin.LogDebug($"OnCollideWithPlayer ======================");
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch udpate to make the enemy target bot too if possible
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 3; i++)
            {
                if (codes[i].ToString() == "ldloc.1 NULL [Label11]"//120
                    && codes[i + 2].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"//122
                    && codes[i + 3].ToString() == "call static bool UnityEngine.Object::op_Equality(UnityEngine.Object x, UnityEngine.Object y)")//123
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2] = new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod);
                codes[startIndex + 3].opcode = OpCodes.Nop; // op_Equality
                codes[startIndex + 3].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.CrawlerAIPatch.Update_Transpiler could not change condition for is only local player.");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()"//145
                    && codes[i + 4].ToString() == "call void CrawlerAI::BeginChasingPlayerServerRpc(int playerObjectId)")//149
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldloc_1;
                codes[startIndex + 1].operand = null;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.CrawlerAIPatch.Update_Transpiler could not change target of begin chasing player.");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Fixes bug where bots do not gain fear when being chased by the thumper!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(CrawlerAI __instance)
        {
            if (__instance.isEnemyDead)
            {
                return;
            }

            // Optimization, only run this every half a second!
            UpdateLimiter updateLimiter = UpdateLimiter.GetOrCreateMonitor(__instance);
            if (!updateLimiter.CanUpdate())
            {
                return;
            }

            // Reset the limiter
            updateLimiter.Invalidate();
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI lethalBotAI = lethalBotAIs[i];
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    if (lethalBotController.HasLineOfSightToPosition(__instance.transform.position + Vector3.up * 0.25f, 80f, 25, 5f))
                    {
                        if (__instance.currentBehaviourStateIndex == 1)
                        {
                            lethalBotController.IncreaseFearLevelOverTime(0.8f);
                        }
                        else
                        {
                            lethalBotController.IncreaseFearLevelOverTime(0.8f, 0.5f);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fixes bug where bots do not gain fear when being chased by the thumper!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("MakeScreech")]
        [HarmonyPostfix]
        public static void MakeScreech_Postfix(CrawlerAI __instance)
        {
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    if ((lethalBotController.transform.position - __instance.transform.position).sqrMagnitude < 15f * 15f)
                    {
                        lethalBotController.JumpToFearLevel(0.75f);
                    }
                }
            }
        }
    }
}



