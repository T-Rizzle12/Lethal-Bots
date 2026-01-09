using HarmonyLib;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Label = System.Reflection.Emit.Label;

namespace LethalBots.Patches.ModPatches.ReviveCompany
{
    public class ReviveCompanyPlayerControllerBPatchPatch
    {
        private static readonly MethodInfo IsGrabbableObjectEqualsToNullMethod = SymbolExtensions.GetMethodInfo(() => PatchesUtil.IsGrabbableObjectEqualsToNull((GrabbableObject)new object()));

        public static IEnumerable<CodeInstruction> SetHoverTipAndCurrentInteractTriggerPatch_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            CodeInstruction loadClosestDeadBody = new CodeInstruction(codes[74]); // ldloc.s 5 (RagdollGrabbableObject)

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString().StartsWith("stsfld bool OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch::StartedRevive") // 72
                    && codes[i + 1].ToString().StartsWith("ldc.i4.1 NULL")
                    && codes[i + 2].ToString().StartsWith("ldloc.s 5 (RagdollGrabbableObject)"))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                Label label = generator.DefineLabel();
                codes[startIndex + 18].labels.Add(label);

                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    loadClosestDeadBody,
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsRagdollPlayerIdLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, label)
                };
                //-----------------------------
                codes.InsertRange(startIndex + 1, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.ReviveCompany.ReviveCompanyPlayerControllerBPatchPatch.SetHoverTipAndCurrentInteractTriggerPatch_Transpiler could not check if bot to not send rpc 1");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].ToString().StartsWith("call static void OPJosMod.ReviveCompany.GeneralUtil::RevivePlayer(int playerId)") // 96
                    && codes[i + 1].ToString().StartsWith("nop NULL"))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                Label label = generator.DefineLabel();
                codes[startIndex + 16].labels.Add(label);

                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    loadClosestDeadBody,
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsRagdollPlayerIdLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue, label)
                };
                //-----------------------------
                codes.InsertRange(startIndex + 2, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.ReviveCompany.ReviveCompanyPlayerControllerBPatchPatch.SetHoverTipAndCurrentInteractTriggerPatch_Transpiler could not check if bot to not send rpc 2");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString().StartsWith("call static RagdollGrabbableObject OPJosMod.ReviveCompany.GeneralUtil::GetClosestDeadBody") // 148
                    && codes[i + 1].ToString().StartsWith("stloc.s")
                    && codes[i + 2].ToString().StartsWith("ldsfld int OPJosMod.ReviveCompany.ConfigVariables::TimeUnitlCantBeRevived"))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                Label label = generator.DefineLabel();
                codes[startIndex + 2].labels.Add(label);

                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_S, 10),
                    new CodeInstruction(OpCodes.Call, IsGrabbableObjectEqualsToNullMethod),
                    new CodeInstruction(OpCodes.Brfalse, label),
                    new CodeInstruction(OpCodes.Ret)
                };
                //-----------------------------
                codes.InsertRange(startIndex + 2, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.ReviveCompany.ReviveCompanyPlayerControllerBPatchPatch.SetHoverTipAndCurrentInteractTriggerPatch_Transpiler could not check for closest dead body null");
            }

            return codes.AsEnumerable();
        }
    }
}
