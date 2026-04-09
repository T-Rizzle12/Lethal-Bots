using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Random = System.Random;

namespace LethalBots.Patches.MapPatches
{
    [HarmonyPatch(typeof(ShipTeleporter))]
    public class ShipTeleporterPatch
    {
        [HarmonyPatch("SetPlayerTeleporterId")]
        [HarmonyPrefix]
        static void SetPlayerTeleporterId_PreFix(PlayerControllerB playerScript,
                                                 int teleporterId)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerScript);
            if (lethalBotAI == null)
            {
                return;
            }

            if (playerScript.shipTeleporterId == 1
                && teleporterId == -1)
            {
                // The bot is being teleported to the ship
                playerScript.ResetFallGravity(); // Found out the hard way gravity wasn't reset on the bots......poor Felix....
                lethalBotAI.InitStateToSearchingNoTarget();
                AudioReverbPresets audioReverbPresets = UnityEngine.Object.FindObjectOfType<AudioReverbPresets>();
                if ((bool)audioReverbPresets)
                {
                    audioReverbPresets.audioPresets[3].ChangeAudioReverbForPlayer(playerScript);
                }
            }
        }

        [HarmonyPatch("beamOutPlayer")]
        [HarmonyPostfix]
        static void beamOutPlayer_PostFix(ShipTeleporter __instance,
                                          Random ___shipTeleporterSeed)
        {
            LethalBotManager.Instance.TeleportOutLethalBots(__instance, ___shipTeleporterSeed);
        }

        [HarmonyPatch("TeleportPlayerOutWithInverseTeleporter")]
        [HarmonyPostfix]
        static void TeleportPlayerOutWithInverseTeleporter_PostFix(ShipTeleporter __instance, int playerObj)
        {
            // Bots that were inverse teleported should swap to the Searching for Scrap state.
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(playerObj);
            if (lethalBotAI != null)
            {
                lethalBotAI.InitStateToSearchingNoTarget(true);
            }
        }

        // I just recently learned that we CAN patch coroutines, you just need to add the MethodType.Enumerator to do so!!!!! :D
        [HarmonyPatch("beamUpPlayer", MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> beamUpPlayer_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
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
                // NOTE: We cannot access the fields of the coroutine class, we must manually find them instead!
                if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo fi && fi.Name.Contains("playerToBeamUp")
                    && codes[i + 1].Calls(getGameNetworkManagerInstance) // 1588
                    && codes[i + 2].LoadsField(localPlayerControllerField) // 1589
                    && codes[i + 3].Calls(opEqualityMethod)) // 1593
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the old GameNetworkManager.Instance.localPlayerController == this.playerToBeamUp check,
                // and replace it with our IsPlayerLocalOrLethalBotOwnerLocalMethod
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
                Plugin.LogError($"LethalBot.Patches.MapPatches.ShipTeleporterPatch.beamUpPlayer_Transpiler could not make bots drop their held items when teleported");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("SetPlayerTeleporterId")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void SetPlayerTeleporterId_ReversePatch(object instance, PlayerControllerB playerScript, int teleporterId) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.ShipTeleporterPatch.SetPlayerTeleporterId_ReversePatch");

        [HarmonyPatch("TeleportPlayerOutWithInverseTeleporter")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void TeleportPlayerOutWithInverseTeleporter_ReversePatch(object instance, int playerObj, Vector3 teleportPos) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.ShipTeleporterPatch.TeleportPlayerOutWithInverseTeleporter_ReversePatch");

        [HarmonyPatch("GetInverseTelePosition")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static Vector3 GetInverseTelePosition_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.MapPatches.ShipTeleporterPatch.GetInverseTelePosition_ReversePatch");
    }
}
