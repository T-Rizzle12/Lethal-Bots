using HarmonyLib;
using HolsteredItemPositions.Monobehaviours;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UsualScrap.Behaviors;

namespace LethalBots.Patches.ModPatches.UsualScrap
{
    [HarmonyPatch(typeof(MedicalKitScript))]
    public class MedicalKitScriptPatch
    {
        [HarmonyPatch(nameof(MedicalKitScript.ItemActivate))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ItemActivate_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Log what we are doing
            Plugin.LogDebug("MedicalKitScriptPatch.ItemActivate_Transpiler: Patching ItemActivate to allow bots to use the Medkit again.");

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Target property: playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), nameof(GrabbableObject.playerHeldBy));

            // Unity Object Inequality Method
            MethodInfo opInequalityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                //Plugin.LogWarning(codes[i].ToString());
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(playerHeldByField)
                    && codes[i + 2].Calls(getGameNetworkManagerInstance)
                    && codes[i + 3].LoadsField(localPlayerControllerField)
                    && codes[i + 4].Calls(opInequalityMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Save any labels that were on the original instruction
                var originalLabel = codes[startIndex].ExtractLabels(); // The GameNetworkManager.Instance instruction, for some reason the label is here

                // Remove the five instructions: GameNetworkManager.Instance, localPlayerController, ldarg.0, targetPlayer, and op_Inequality
                codes.RemoveRange(startIndex, 5);

                // Insert the new instruction to call the replacement method.
                List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0).WithLabels(originalLabel), // Load `this`
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod), // Call our replacement method
                    new CodeInstruction(OpCodes.Ldc_I4_0), // Push 0 (false)
                    new CodeInstruction(OpCodes.Ceq) // Compare equal: (result == false) --> equivalent to !
                };
                codes.InsertRange(startIndex, codesToReplace); // Call our replacement methods

                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.UsualScrap: Could not replace local player check!");
            }

            return codes.AsEnumerable();
        }
    }
}
