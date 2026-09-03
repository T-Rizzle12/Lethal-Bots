using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;
using LethalBots.Utils;
using OomJan.BetterLethalVRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;

namespace LethalBots.Patches.ModPatches.BetterLethalVRM
{
    [HarmonyPatch(typeof(BetterLethalVRMManager))]
    public class BetterLethalVRMPatch
    {
        /// <summary>
        /// Helper transpiler that allows bots to be affected by LethalVRM.
        /// </summary>
        /// <remarks>
        /// Lethal VRM doesn't do any of its logic when a <see cref="PlayerControllerB"/> is owned by the server, (unless its the host object).<br/>
        /// This transpiler changes it so it affects bots as well regardless.
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("FindUpdatedIDs")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> FindUpdatedIDs_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo getNetworkObject = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "NetworkObject");
            MethodInfo getName = AccessTools.PropertyGetter(typeof(UnityEngine.Object), "name");
            MethodInfo getOwnerClientId = AccessTools.PropertyGetter(typeof(NetworkObject), "OwnerClientId");

            // C# String Inequality Method
            MethodInfo opInequalityMethod = AccessTools.Method(typeof(string), "op_Inequality");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 16; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_2
                    && codes[i + 1].Calls(getNetworkObject)
                    && codes[i + 2].opcode == OpCodes.Dup
                    && (codes[i + 3].opcode == OpCodes.Brtrue_S || codes[i + 3].opcode == OpCodes.Brtrue)
                    && codes[i + 4].opcode == OpCodes.Pop
                    && codes[i + 5].opcode == OpCodes.Ldc_I4_0
                    && (codes[i + 6].opcode == OpCodes.Br || codes[i + 6].opcode == OpCodes.Br_S)
                    && codes[i + 7].Calls(getOwnerClientId)
                    && codes[i + 8].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 9].opcode == OpCodes.Conv_I8
                    && codes[i + 10].opcode == OpCodes.Ceq
                    && (codes[i + 11].opcode == OpCodes.Brfalse_S || codes[i + 11].opcode == OpCodes.Brfalse)
                    && codes[i + 12].opcode == OpCodes.Ldloc_2
                    && codes[i + 13].Calls(getName)
                    && codes[i + 14].opcode == OpCodes.Ldstr
                    && (codes[i + 14].operand is string name && name.Equals("Player", StringComparison.InvariantCultureIgnoreCase))
                    && codes[i + 15].Calls(opInequalityMethod)
                    && (codes[i + 16].opcode == OpCodes.Brfalse_S || codes[i + 16].opcode == OpCodes.Brfalse))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old checks
                for (int i = 0; i < 15; i++)
                {
                    codes[startIndex + i].opcode = OpCodes.Nop;
                    codes[startIndex + i].operand = null;
                }

                // Use our new check instead
                codes[startIndex + 13].opcode = OpCodes.Ldloc_2;
                codes[startIndex + 13].operand = null;
                codes[startIndex + 14].opcode = OpCodes.Call;
                codes[startIndex + 14].operand = getNetworkObject;
                codes[startIndex + 15].opcode = OpCodes.Call;
                codes[startIndex + 15].operand = AccessTools.Method(typeof(BetterLethalVRMPatch), nameof(ShouldRemoveExistingPlayer));
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.BetterLethalVRMPatch.FindUpdatedIDs_Transpiler could allow bots to be registered for modded playermodels 1");
            }

            for (var i = 0; i < codes.Count - 16; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_2
                    && codes[i + 1].Calls(getNetworkObject)
                    && codes[i + 2].opcode == OpCodes.Dup
                    && (codes[i + 3].opcode == OpCodes.Brtrue_S || codes[i + 3].opcode == OpCodes.Brtrue)
                    && codes[i + 4].opcode == OpCodes.Pop
                    && codes[i + 5].opcode == OpCodes.Ldc_I4_0
                    && (codes[i + 6].opcode == OpCodes.Br || codes[i + 6].opcode == OpCodes.Br_S) 
                    && codes[i + 7].Calls(getOwnerClientId)
                    && codes[i + 8].opcode == OpCodes.Ldc_I4_0
                    && codes[i + 9].opcode == OpCodes.Conv_I8
                    && codes[i + 10].opcode == OpCodes.Ceq
                    && (codes[i + 11].opcode == OpCodes.Brfalse_S || codes[i + 11].opcode == OpCodes.Brfalse)
                    && codes[i + 12].opcode == OpCodes.Ldloc_2
                    && codes[i + 13].Calls(getName)
                    && codes[i + 14].opcode == OpCodes.Ldstr
                    && (codes[i + 14].operand is string name && name.Equals("Player", StringComparison.InvariantCultureIgnoreCase))
                    && codes[i + 15].Calls(opInequalityMethod)
                    && (codes[i + 16].opcode == OpCodes.Brtrue_S || codes[i + 16].opcode == OpCodes.Brtrue))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old checks
                for (int i = 0; i < 15; i++)
                {
                    codes[startIndex + i].opcode = OpCodes.Nop;
                    codes[startIndex + i].operand = null;
                }

                // Use our new check instead
                codes[startIndex + 13].opcode = OpCodes.Ldloc_2;
                codes[startIndex + 13].operand = null;
                codes[startIndex + 14].opcode = OpCodes.Call;
                codes[startIndex + 14].operand = getNetworkObject;
                codes[startIndex + 15].opcode = OpCodes.Call;
                codes[startIndex + 15].operand = AccessTools.Method(typeof(BetterLethalVRMPatch), nameof(ShouldSkipBetterLethalVRM));
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.BetterLethalVRMPatch.FindUpdatedIDs_Transpiler could allow bots to be registered for modded playermodels 2");
            }

            return codes.AsEnumerable();
        }

        private static bool ShouldRemoveExistingPlayer(NetworkObject networkObject)
        {
            if (networkObject == null)
                return false;

            var player = networkObject.GetComponent<PlayerControllerB>();

            if (player == null)
                return false;

            // Bots can have OwnerClientId == 0, so don't use OwnerClientId
            // alone to determine whether this is the host/local player.
            if (LethalBotManager.Instance.IsPlayerLocalOrLethalBot(player))
                return false;

            return networkObject.OwnerClientId == 0 && player.name != "Player";
        }

        private static bool ShouldSkipBetterLethalVRM(NetworkObject networkObject)
        {
            if (networkObject == null)
                return false;

            PlayerControllerB player = networkObject.GetComponent<PlayerControllerB>();

            if (player == null)
                return false;

            // Bots can have OwnerClientId == 0, so don't use OwnerClientId
            // alone to determine whether this is the host/local player.
            if (LethalBotManager.Instance.IsPlayerLocalOrLethalBot(player))
                return false;

            return networkObject.OwnerClientId == 0 && player.name != "Player";
        }
    }
}
