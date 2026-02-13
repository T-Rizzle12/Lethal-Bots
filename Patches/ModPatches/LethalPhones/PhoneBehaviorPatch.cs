using HarmonyLib;
using LethalBots.Utils;
using Scoops.misc;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace LethalBots.Patches.ModPatches.LethalPhones
{
    [HarmonyPatch(typeof(PhoneBehavior))]
    public class PhoneBehaviorPatch
    {
        // Sigh, these are set to protected and there doesn't appear to be any public method to change or retrieve them.
        // We use reflection to access them. It sucks, but it is what it is.
        #region Reflection Fields

        public static FieldInfo incomingCall = null!;

        public static FieldInfo outgoingCall = null!;

        public static FieldInfo activeCall = null!;

        public static FieldInfo incomingCaller = null!;

        public static FieldInfo outgoingCaller = null!;

        public static FieldInfo activeCaller = null!;

        internal static void SetupReflectionFields()
        {
            incomingCall = AccessTools.Field(typeof(PhoneBehavior), "incomingCall");
            outgoingCall = AccessTools.Field(typeof(PhoneBehavior), "outgoingCall");
            activeCall = AccessTools.Field(typeof(PhoneBehavior), "activeCall");
            incomingCaller = AccessTools.Field(typeof(PhoneBehavior), "incomingCaller");
            outgoingCaller = AccessTools.Field(typeof(PhoneBehavior), "outgoingCaller");
            activeCaller = AccessTools.Field(typeof(PhoneBehavior), "activeCaller");
        }

        #endregion

        /*[HarmonyPatch("UpdatePlayerVoices")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> UpdatePlayerVoices_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static StartOfRound StartOfRound::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 2].ToString() == "ldlen NULL")
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
                codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.PhoneBehaviorPatch.UpdatePlayerVoices_Transpiler could not check only for irl players not interns.");
            }

            return codes.AsEnumerable();
        }*/
    }
}
