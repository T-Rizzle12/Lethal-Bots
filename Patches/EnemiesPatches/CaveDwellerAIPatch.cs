using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using static Unity.Netcode.NetworkBehaviour;

namespace LethalBots.Patches.EnemiesPatches
{
    [HarmonyPatch(typeof(CaveDwellerAI))]
    public class CaveDwellerAIPatch
    {
        [HarmonyPatch("OnCollideWithPlayer")]
        [HarmonyPrefix]
        static void OnCollideWithPlayer_PreFix(ref bool ___startingKillAnimationLocalClient)
        {
            // startingKillAnimationLocalClient mysteriously set back to true after killing bot... force it to false here
            // Maybe bugs will occurs we'll see
            ___startingKillAnimationLocalClient = false;
        }


        [HarmonyPatch("ScareBaby")]
        [HarmonyPostfix]
        static void ScareBaby_PostFix(CaveDwellerAI __instance)
        {
            if (!__instance.IsServer)
            {
                return;
            }

            if (__instance.sittingDown && !__instance.holdingBaby)
            {
                return;
            }

            PlayerControllerB playerHeldBy = __instance.propScript.playerHeldBy;
            if (playerHeldBy == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerHeldBy);
            if (lethalBotAI == null)
            {
                return;
            }

            Plugin.LogDebug("ScareBaby_PostFix");
            playerHeldBy.DiscardHeldObject();
        }

        [HarmonyPatch("ScareBabyClientRpc")]
        [HarmonyPostfix]
        static void ScareBabyClientRpc_PostFix(CaveDwellerAI __instance)
        {
            if (__instance.IsServer)
            {
                return;
            }

            PlayerControllerB playerHeldBy = __instance.propScript.playerHeldBy;
            if (playerHeldBy == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerHeldBy);
            if (lethalBotAI == null)
            {
                return;
            }

            Plugin.LogDebug("ScareBabyClientRpc_PostFix");
            playerHeldBy.DiscardHeldObject();
        }

        [HarmonyPatch("CancelKillAnimationClientRpc")]
        [HarmonyPostfix]
        static void CancelKillAnimationClientRpc_PostFix(CaveDwellerAI __instance, 
                                                         int playerObjectId,
                                                         ref bool ___startingKillAnimationLocalClient)
        {
            Plugin.LogDebug($"CancelKillAnimationClientRpc_PostFix playerObjectId {playerObjectId}");
            if (LethalBotManager.Instance.IsIdPlayerLethalBotOwnerLocal(playerObjectId))
            {
                Plugin.LogDebug("CancelKillAnimationClientRpc_PostFix");
                ___startingKillAnimationLocalClient = false;
            }
        }

        [HarmonyPatch("KillPlayerAnimationClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> KillPlayerAnimationClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 3; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_0
                    && codes[i + 1].Calls(getGameNetworkManagerInstance)
                    && codes[i + 2].LoadsField(localPlayerControllerField)
                    && codes[i + 3].Calls(opEqualityMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Nop;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Call;
                codes[startIndex + 3].operand = PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.CaveDwellerAIPatch.KillPlayerAnimationClientRpc_Transpiler could not check if local player or bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("StartTransformationAnim")]
        [HarmonyPostfix]
        static void StartTransformationAnim_PostFix(CaveDwellerAI __instance)
        {
            PlayerControllerB playerHeldBy = __instance.propScript.playerHeldBy;
            if (playerHeldBy == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerHeldBy);
            if (lethalBotAI == null)
            {
                return;
            }

            playerHeldBy.DiscardHeldObject();
        }
    }
}
