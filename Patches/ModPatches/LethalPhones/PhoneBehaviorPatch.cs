using HarmonyLib;
using LethalBots.Utils;
using Scoops.misc;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine.Animations.Rigging;

namespace LethalBots.Patches.ModPatches.LethalPhones
{
    [HarmonyPatch(typeof(PhoneBehavior))]
    public class PhoneBehaviorPatch
    {
        // Sigh, these are set to protected and there doesn't appear to be any public method to change or retrieve them.
        // We use reflection to access them. It sucks, but it is what it is.
        #region Reflection Fields

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<short>> incomingCall = null!;

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<short>> outgoingCall = null!;

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<short>> activeCall = null!;

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<ulong>> incomingCaller = null!;

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<ulong>> outgoingCaller = null!;

        public static AccessTools.FieldRef<PhoneBehavior, NetworkVariable<ulong>> activeCaller = null!;

        public static AccessTools.FieldRef<PlayerPhone, ChainIKConstraint> serverLeftArmRig = null!;

        internal static void SetupReflectionFields()
        {
            // PhoneBehavior
            incomingCall = AccessTools.FieldRefAccess<NetworkVariable<short>>(typeof(PhoneBehavior), "incomingCall");
            outgoingCall = AccessTools.FieldRefAccess<NetworkVariable<short>>(typeof(PhoneBehavior), "outgoingCall");
            activeCall = AccessTools.FieldRefAccess<NetworkVariable<short>>(typeof(PhoneBehavior), "activeCall");
            incomingCaller = AccessTools.FieldRefAccess<NetworkVariable<ulong>>(typeof(PhoneBehavior), "incomingCaller");
            outgoingCaller = AccessTools.FieldRefAccess<NetworkVariable<ulong>>(typeof(PhoneBehavior), "outgoingCaller");
            activeCaller = AccessTools.FieldRefAccess<NetworkVariable<ulong>>(typeof(PhoneBehavior), "activeCaller");

            // PlayerPhone
            serverLeftArmRig = AccessTools.FieldRefAccess<ChainIKConstraint>(typeof(PlayerPhone), "serverLeftArmRig");
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
