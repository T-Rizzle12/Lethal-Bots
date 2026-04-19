using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.ObjectsPatches
{
    /// <summary>
    /// Small patch to replace all instances of "GameNetworkManager.Instance.localPlayerController" in SprayPaintItem with 
    /// "this.playerHeldBy" so that bots can use the weed killer and spray paint item!
    /// </summary>
    [HarmonyPatch(typeof(SprayPaintItem))]
    public class SprayPaintItemPatch
    {
        [HarmonyPatch("TrySprayingWeedKillerOnLocalPlayer")]
        [HarmonyPostfix]
        static void TrySprayingWeedKillerOnLocalPlayer_Postfix(SprayPaintItem __instance, ref CadaverGrowthAI ___cadaverGrowthAI)
        {
            // Just like the base game, we have to check if the cadaverGrowthAI is null.
            if (___cadaverGrowthAI == null)
            {
                return;
            }

            // Mimic logic for bots as well.
            PlayerControllerB playerHeldBy = __instance.playerHeldBy;
            if (playerHeldBy == null)
            {
                return; // If we are not being held by anyone, then we don't need to do anything!
            }

            // Cache some values to reduce indexing calls in the loop.
            Vector3 itemPos = __instance.transform.position;
            Vector3 itemForward = __instance.transform.forward;
            Vector3 heldPos = playerHeldBy.transform.position;

            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (var lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null && playerHeldBy != lethalBotController)
                {
                    PlayerInfection playerInfection = ___cadaverGrowthAI.playerInfections[lethalBotController.playerClientId];
                    if (playerInfection.infected)
                    {
                        const float effectiveRange = 5f;
                        Vector3 position = lethalBotController.transform.position;
                        position.y = itemPos.y; // Ignore height difference for angle calculation, just like the base game does.
                        if ((heldPos - lethalBotController.transform.position).sqrMagnitude < effectiveRange * effectiveRange && Vector3.Angle(itemForward, position - itemPos) < 40f)
                        {
                            HealPlayerInfection(lethalBotController, lethalBotAI!, playerInfection, ___cadaverGrowthAI);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Basically a carbon copy of HealPlayerInfection, but made to work with bots as well.
        /// </summary>
        /// <param name="infection"></param>
        private static void HealPlayerInfection(PlayerControllerB lethalBotController, LethalBotAI lethalBotAI, PlayerInfection infection, CadaverGrowthAI cadaverGrowthAI)
        {
            LethalBotInfection lethalBotInfection = lethalBotAI.BotInfectionData.Value;
            if (lethalBotInfection.sprayOnPlayerMeter > 0.33f)
            {
                lethalBotInfection.sprayOnPlayerMeter = 0f;
                if (infection.burstMeter > 0f)
                {
                    cadaverGrowthAI.BurstFromPlayer(lethalBotController, lethalBotController.transform.position, lethalBotController.transform.eulerAngles);
                    cadaverGrowthAI.SyncBurstFromPlayerRpc((int)lethalBotController.playerClientId, lethalBotController.transform.position, lethalBotController.transform.eulerAngles);
                    return;
                }
                lethalBotController.DamagePlayer(8, hasDamageSFX: true, callRPC: true, CauseOfDeath.Suffocation);
                cadaverGrowthAI.HealInfection((int)lethalBotController.playerClientId, 0.1f);
                if (lethalBotController.criticallyInjured)
                {
                    lethalBotInfection.sprayOnPlayerMeter = -0.2f;
                }
            }
            else
            {
                lethalBotInfection.sprayOnPlayerMeter += Time.deltaTime;
            }
        }

        [HarmonyPatch("TrySprayingWeedKillerBottle")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> TrySprayingWeedKillerBottle_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch SprayPaintItemPatch.TrySprayingWeedKillerBottle!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    // Replace with "this.playerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    codes[i].opcode = OpCodes.Ldarg_0;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldfld;
                    codes[i + 1].operand = playerHeldByField;
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.SprayPaintItemPatch.TrySprayingWeedKillerBottle_Transpiler could not check if player local or bot local 1");
            }
            else
            {
                Plugin.LogDebug($"Patched out localPlayerController {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("CheckForCadaverPlantsInSprayPath")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> CheckForCadaverPlantsInSprayPath_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch SprayPaintItemPatch.CheckForCadaverPlantsInSprayPath!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    // Replace with "this.playerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    codes[i].opcode = OpCodes.Ldarg_0;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldfld;
                    codes[i + 1].operand = playerHeldByField;
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.SprayPaintItemPatch.CheckForCadaverPlantsInSprayPath_Transpiler could not check if player local or bot local 1");
            }
            else
            {
                Plugin.LogDebug($"Patched out localPlayerController {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("CheckForWeedsInSprayPath")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> CheckForWeedsInSprayPath_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch SprayPaintItemPatch.CheckForWeedsInSprayPath!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    // Replace with "this.playerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    codes[i].opcode = OpCodes.Ldarg_0;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldfld;
                    codes[i + 1].operand = playerHeldByField;
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.SprayPaintItemPatch.CheckForWeedsInSprayPath_Transpiler could not check if player local or bot local 1");
            }
            else
            {
                Plugin.LogDebug($"Patched out localPlayerController {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("TrySpraying")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> TrySpraying_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch SprayPaintItemPatch.TrySpraying!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    // Replace with "this.playerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    codes[i].opcode = OpCodes.Ldarg_0;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldfld;
                    codes[i + 1].operand = playerHeldByField;
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.SprayPaintItemPatch.TrySpraying_Transpiler could not check if player local or bot local 1");
            }
            else
            {
                Plugin.LogDebug($"Patched out localPlayerController {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }
    }
}
