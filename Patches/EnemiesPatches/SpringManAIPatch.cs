using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patch for <c>SpringManAI</c>
    /// </summary>
    [HarmonyPatch(typeof(SpringManAI))]
    public class SpringManAIPatch
    {
        /// <summary>
        /// Make the sping man use all array of player + bots to target
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
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].ToString() == "ldc.i4.4 NULL" || codes[i].ToString() == "ldsfld int MoreCompany.MainClass::newPlayerCount")//110
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Call;
                codes[startIndex].operand = PatchesUtil.AllEntitiesCountMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.SpringManAIPatch.Update_Transpiler could not change size of player array to look up.");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DoSpringAnimation")]
        [HarmonyPostfix]
        public static void DoSpringAnimation_Postfix(SpringManAI __instance, bool springPopUp = false)
        {
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI? lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    if (lethalBotController.HasLineOfSightToPosition(__instance.transform.position + Vector3.up * 0.6f, 70f, 25))
                    {
                        float num = Vector3.Distance(__instance.transform.position, lethalBotController.transform.position);
                        if (num < 4f)
                        {
                            lethalBotController.JumpToFearLevel(0.9f);
                        }
                        else if (num < 9f)
                        {
                            lethalBotController.JumpToFearLevel(0.4f);
                        }
                    }
                }
            }
        }
    }
}
