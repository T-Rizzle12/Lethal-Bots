using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;
using LethalBots.Utils;
using Scoops.patch;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace LethalBots.Patches.ModPatches.LethalPhones
{
    [HarmonyPatch(typeof(PlayerPhonePatch))]
    public class PlayerPhonePatchPatch
    {
        //[HarmonyPatch("PlayerModelDisabled")]
        //[HarmonyPrefix]
        //static bool PlayerModelDisabled_Prefix(PlayerControllerB __0)
        //{
        //    if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
        //    {
        //        return false;
        //    }
        //    return true;
        //}

        [HarmonyPatch("PlayerSpawnBody")]
        [HarmonyPrefix]
        static bool PlayerSpawnBody_Prefix(PlayerControllerB __0)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch("CreatePhoneAssets")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> CreatePhoneAssets_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = 0;
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            Label labelToJumpTo = generator.DefineLabel();
            codes[startIndex].labels.Add(labelToJumpTo);

            List<CodeInstruction> codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldind_Ref), // ref arg 0 !
                new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                new CodeInstruction(OpCodes.Brfalse, labelToJumpTo),
                new CodeInstruction(OpCodes.Ret, null)
            };
            codes.InsertRange(startIndex, codesToAdd);

            return codes.AsEnumerable();
        }
    }
}
