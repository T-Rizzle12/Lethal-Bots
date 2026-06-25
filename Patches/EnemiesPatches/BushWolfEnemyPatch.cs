using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Label = System.Reflection.Emit.Label;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patch for <c>BushWolfEnemy</c>
    /// </summary>
    [HarmonyPatch(typeof(BushWolfEnemy))]
    public class BushWolfEnemyPatch
    {
        /// <summary>
        /// Patch for making the bush wolf be able to kill an bot
        /// </summary>
        [HarmonyPatch("OnCollideWithPlayer")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> OnCollideWithPlayer_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(PatchesUtil.FieldInfoTargetPlayer)
                    && codes[i + 2].Calls(getGameNetworkManagerInstance)
                    && codes[i + 3].LoadsField(localPlayerControllerField)
                    && codes[i + 4].Calls(opEqualityMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old this.targetPlayer == GameNetworkManager.Instance.localPlayerController check
                // with our IsPlayerLocalOrLethalBotOwnerLocalMethod
                // In order to preserve labels, we just replace the instructions instead of removing and inserting new ones
                codes[startIndex + 2].opcode = OpCodes.Nop;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Nop;
                codes[startIndex + 3].operand = null;
                codes[startIndex + 4].opcode = OpCodes.Call;
                codes[startIndex + 4].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.OnCollideWithPlayer_Transpiler could not check if bot or local player!");
            }

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) 
                    && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old GameNetworkManager.Instance.localPlayerController call,
                // and replace it with our targetPlayer field.
                codes[startIndex].opcode = OpCodes.Ldarg_0;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldfld;
                codes[startIndex + 1].operand = PatchesUtil.FieldInfoTargetPlayer;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.OnCollideWithPlayer_Transpiler could not replace GameNetworkManager.Instance.localPlayerController with targetPlayer field!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
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

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // Player Controller JumpToFearLevel Method
            MethodInfo jumpToFearLevelMethod = AccessTools.Method(typeof(PlayerControllerB), "JumpToFearLevel");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1588
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1589
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1591
                    && codes[i + 4].Calls(opEqualityMethod)) // 1593
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 1");
            }

            // -----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1603
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1604
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].LoadsField(PatchesUtil.FieldInfoDraggingPlayer) // 1606
                    && codes[i + 4].Calls(opEqualityMethod)) // 1608
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
                codes[startIndex + 3].operand = PatchesUtil.FieldInfoDraggingPlayer;
                codes[startIndex + 4].opcode = OpCodes.Call;
                codes[startIndex + 4].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 2");
            }

            // -----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1588
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1589
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1591
                    && codes[i + 4].Calls(opEqualityMethod)) // 1593
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 3");
            }

            // -----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1686
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1687
                    && codes[i + 2].opcode == OpCodes.Ldc_R4 && codes[i + 2].operand is float f && f == 1f
                    && codes[i + 3].opcode == OpCodes.Ldc_I4_1
                    && codes[i + 4].Calls(jumpToFearLevelMethod)) // 1690
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Ldarg_0;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldfld;
                codes[startIndex + 1].operand = PatchesUtil.FieldInfoTargetPlayer;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not use target player for JumpToFearLevel method");
            }

            // ------------------------------------------------ (upperSpineLocalPoint 1)
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1588
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1589
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1591
                    && codes[i + 4].Calls(opEqualityMethod)) // 2040
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 4");
            }

            // ------------------------------------------------ (upperSpineLocalPoint 2)
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 1588
                    && codes[i + 1].LoadsField(localPlayerControllerField) // 1589
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 1591
                    && codes[i + 4].Calls(opEqualityMethod)) // 2040
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 5");
            }

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(PatchesUtil.FieldInfoTargetPlayer) // 2118
                    && codes[i + 2].Calls(getGameNetworkManagerInstance) // 2119
                    && codes[i + 3].LoadsField(localPlayerControllerField) // 2120
                    && codes[i + 4].Calls(opEqualityMethod)) // 2122
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
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not check if bot or local player 6");
            }

            // -----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 18; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 2126
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 17].Calls(getGameNetworkManagerInstance) // 2143
                    && codes[i + 18].LoadsField(localPlayerControllerField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Ldarg_0;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Ldfld;
                codes[startIndex + 1].operand = PatchesUtil.FieldInfoTargetPlayer;

                codes[startIndex + 17].opcode = OpCodes.Ldarg_0;
                codes[startIndex + 17].operand = null;
                codes[startIndex + 18].opcode = OpCodes.Ldfld;
                codes[startIndex + 18].operand = PatchesUtil.FieldInfoTargetPlayer;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.BushWolfEnemyPatch.Update_Transpiler could not use target player for check if HitByEnemyServerRpc method");
            }

            return codes.AsEnumerable();
        }
    }
}