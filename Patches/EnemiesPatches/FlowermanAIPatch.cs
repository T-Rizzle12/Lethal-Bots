using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    [HarmonyPatch(typeof(FlowermanAI))]
    public class FlowermanAIPatch
    {
        /// <summary>
        /// Fixes bug where bots are unable to fend off the Braken since the checks are only for the local client!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(FlowermanAI __instance)
        {
            if (__instance.isEnemyDead || __instance.inKillAnimation || GameNetworkManager.Instance == null)
            {
                return;
            }

            UpdateLimiter updateLimiter = UpdateLimiter.GetOrCreateMonitor(__instance);
            if (!updateLimiter.CanUpdate())
            {
                return;
            }

            updateLimiter.Invalidate();
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI lethalBotAI = lethalBotAIs[i];
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    if (lethalBotController.HasLineOfSightToPosition(__instance.transform.position + Vector3.up * 0.5f, 30f))
                    {
                        if (__instance.currentBehaviourStateIndex == 0)
                        {
                            __instance.SwitchToBehaviourState(1);
                            if (!__instance.thisNetworkObject.IsOwner)
                            {
                                __instance.ChangeOwnershipOfEnemy(lethalBotController.actualClientId);
                            }
                            if ((__instance.transform.position - lethalBotController.transform.position).sqrMagnitude < 5f * 5f)
                            {
                                lethalBotController.JumpToFearLevel(0.6f);
                            }
                            else
                            {
                                lethalBotController.JumpToFearLevel(0.3f);
                            }
                            __instance.agent.speed = 0f;
                            __instance.evadeStealthTimer = 0f;
                        }
                        else if (__instance.evadeStealthTimer > 0.5f)
                        {
                            int playerObj = (int)lethalBotController.playerClientId;
                            __instance.LookAtFlowermanTrigger(playerObj);
                            __instance.ResetFlowermanStealthTimerServerRpc(playerObj);
                        }
                    }
                }
            }
        }

        [HarmonyPatch("KillPlayerAnimationClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> KillPlayerAnimationClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static GameNetworkManager GameNetworkManager::get_Instance()" //
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB GameNetworkManager::localPlayerController"
                    && codes[i + 2].ToString() == "call static bool UnityEngine.Object::op_Equality(UnityEngine.Object x, UnityEngine.Object y)") //
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
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.FlowermanAIPatch.KillPlayerAnimationClientRpc_Transpiler could not check if player local or bot local 1");
            }

            return codes.AsEnumerable();
        }
    }
}
