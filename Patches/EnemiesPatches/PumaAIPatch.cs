using HarmonyLib;
using LethalBots.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace LethalBots.Patches.EnemiesPatches
{
    [HarmonyPatch(typeof(PumaAI))]
    public class PumaAIPatch
    {
        [HarmonyPatch("DoAIInterval")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DoAIInterval_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Inequality Method
            MethodInfo opInequalityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].IsLdarg(0) // 1588
                    && codes[i + 1].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1589
                    && codes[i + 2].Calls(getGameNetworkManagerInstance)
                    && codes[i + 3].LoadsField(localPlayerControllerField) // 1591
                    && codes[i + 4].Calls(opInequalityMethod)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)) // 1593
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old GameNetworkManager.Instance.localPlayerController == this.targetPlayer check,
                // and replace it with our IsPlayerLocalOrLethalBotOwnerLocalMethod
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;

                // If is localPlayerController or bot owned by localPlayerController
                // In order to preserve labels, we just replace the instructions instead of removing and inserting new ones
                codes[startIndex + 2].opcode = OpCodes.Ldarg_0;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Ldfld;
                codes[startIndex + 3].operand = PatchesUtil.FieldInfoTargetPlayer;
                codes[startIndex + 4].opcode = OpCodes.Call;
                codes[startIndex + 4].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                codes[startIndex + 5].opcode = codes[startIndex + 5].opcode == OpCodes.Brfalse ? OpCodes.Brtrue : OpCodes.Brtrue_S;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.PumaAIPatch.DoAIInterval_Transpiler could not check if bot or local player 1");
            }

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].IsLdarg(0) // 1588
                    && codes[i + 1].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1589
                    && codes[i + 2].Calls(getGameNetworkManagerInstance)
                    && codes[i + 3].LoadsField(localPlayerControllerField) // 1591
                    && codes[i + 4].Calls(opInequalityMethod)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)) // 1593
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old GameNetworkManager.Instance.localPlayerController == this.targetPlayer check,
                // and replace it with our IsPlayerLocalOrLethalBotOwnerLocalMethod
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;

                // If is localPlayerController or bot owned by localPlayerController
                // In order to preserve labels, we just replace the instructions instead of removing and inserting new ones
                codes[startIndex + 2].opcode = OpCodes.Ldarg_0;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Ldfld;
                codes[startIndex + 3].operand = PatchesUtil.FieldInfoTargetPlayer;
                codes[startIndex + 4].opcode = OpCodes.Call;
                codes[startIndex + 4].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                codes[startIndex + 5].opcode = codes[startIndex + 5].opcode == OpCodes.Brfalse ? OpCodes.Brtrue : OpCodes.Brtrue_S;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.PumaAIPatch.DoAIInterval_Transpiler could not check if bot or local player 2");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Inequality Method
            MethodInfo opInequalityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].IsLdarg(0) // 1588
                    && codes[i + 1].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1589
                    && codes[i + 2].Calls(getGameNetworkManagerInstance)
                    && codes[i + 3].LoadsField(localPlayerControllerField) // 1591
                    && codes[i + 4].Calls(opInequalityMethod)
                    && (codes[i + 5].opcode == OpCodes.Brfalse_S || codes[i + 5].opcode == OpCodes.Brfalse)) // 1593
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old GameNetworkManager.Instance.localPlayerController == this.targetPlayer check,
                // and replace it with our IsPlayerLocalOrLethalBotOwnerLocalMethod
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;

                // If is localPlayerController or bot owned by localPlayerController
                // In order to preserve labels, we just replace the instructions instead of removing and inserting new ones
                codes[startIndex + 2].opcode = OpCodes.Ldarg_0;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Ldfld;
                codes[startIndex + 3].operand = PatchesUtil.FieldInfoTargetPlayer;
                codes[startIndex + 4].opcode = OpCodes.Call;
                codes[startIndex + 4].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                codes[startIndex + 5].opcode = codes[startIndex + 5].opcode == OpCodes.Brfalse ? OpCodes.Brtrue : OpCodes.Brtrue_S;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.PumaAIPatch.Update_Transpiler could not check if bot or local player 1");
            }

            return codes.AsEnumerable();
        }
    }
}
