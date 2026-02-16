using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using Scoops.misc;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.LethalPhones
{
    [HarmonyPatch(typeof(PlayerPhone))]
    public class PlayerPhonePatch
    {
        #region Prefixes

        /// <summary>
        /// Prefix to stop the phone from managing inputs for lethal bots, 
        /// as they will be managed by the AI instead.
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("ManageInputs")]
        [HarmonyPrefix]
        static bool ManageInputs_Prefix(PlayerPhone __instance)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance.player);
            if (lethalBotAI != null)
            {
                return false;
            }
            return true;
        }

        #endregion

        #region Transpilers

        [HarmonyPatch("SetNewPhoneNumberClientRpc")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SetNewPhoneNumberClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(isOwnerGetter))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                //codes.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod).WithLabels(codes[startIndex].labels));
                codes[startIndex] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerPhonePatch), nameof(IsPhoneLocal))).WithLabels(codes[startIndex].labels);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.PlayerPhonePatch.SetNewPhoneNumberClientRpc_Transpiler could not find IsOwner check to bypass!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("LateUpdate")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> LateUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(isOwnerGetter))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                //codes.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod).WithLabels(codes[startIndex].labels));
                codes[startIndex] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerPhonePatch), nameof(IsPhoneLocal))).WithLabels(codes[startIndex].labels);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.PlayerPhonePatch.LateUpdate_Transpiler could not find IsOwner check to bypass!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("ToggleServerPhoneModelClientRpc")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ToggleServerPhoneModelClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(isOwnerGetter))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                //codes.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod).WithLabels(codes[startIndex].labels));
                codes[startIndex] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerPhonePatch), nameof(IsPhoneLocal))).WithLabels(codes[startIndex].labels);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.PlayerPhonePatch.ToggleServerPhoneModelClientRpc_Transpiler could not find IsOwner check to bypass!");
            }

            return codes.AsEnumerable();
        }

        private static bool IsPhoneLocal(PlayerPhone playerPhone)
        {
            if (playerPhone.player == null)
            {
                return playerPhone.IsOwner; // Let the original method handle this case.
            }

            // Check if this is the local player's phone.
            return LethalBotManager.IsPlayerLocal(playerPhone.player);
        }

        #endregion
    }

    [HarmonyPatch(typeof(Scoops.patch.PlayerPhonePatch))]
    public class PlayerPhonePatchPatch
    {
        #region Postfixes

        [HarmonyPatch("PlayerDamaged")]
        [HarmonyPrefix]
        static bool PlayerDamaged_Prefix(PlayerControllerB __0, int __1)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(__0);
            if (lethalBotAI != null && __0.isPlayerControlled)
            {
                float changeAmount = Mathf.Clamp01(__1 / 75f);
                PlayerPhone? phone = __0.transform.Find("PhonePrefab(Clone)")?.GetComponent<PlayerPhone>();
                phone?.ApplyTemporaryInterference(changeAmount);
                return false;
            }
            return true;
        }

        #endregion
    }
}
