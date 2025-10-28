using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputForUI;
using UnityEngine.UIElements.UIR;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patch for <c>RadMechAI</c>
    /// </summary>
    [HarmonyPatch(typeof(RadMechAI))]
    public class RadMechAIPatch
    {
        static MethodInfo IsLocalPlayerOrLethalBotCloseToPosMethod = SymbolExtensions.GetMethodInfo(() => RadMechAIPatch.IsLocalPlayerOrLethalBotCloseToPos(new Vector3(), new Vector3()));

        [HarmonyPatch("Stomp")]
        [HarmonyPostfix]
        private static void Stomp_Postfix(RadMechAI __instance, Transform stompTransform, ParticleSystem particle, ParticleSystem particle2, float radius)
        {
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach(LethalBotAI lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    float num = Vector3.Distance(lethalBotController.transform.position, stompTransform.position);
                    Vector3 vector = Vector3.Normalize(lethalBotController.gameplayCamera.transform.position - stompTransform.position) * 15f / Vector3.Distance(lethalBotController.gameplayCamera.transform.position, stompTransform.position);
                    if (num < radius)
                    {
                        if ((double)num < (double)radius * 0.175)
                        {
                            lethalBotController.DamagePlayer(70, hasDamageSFX: true, callRPC: true, CauseOfDeath.Crushing, 0, fallDamage: false, Vector3.down * 40f);
                        }
                        else if (num < radius * 0.5f)
                        {
                            lethalBotController.DamagePlayer(30, hasDamageSFX: true, callRPC: true, CauseOfDeath.Crushing, 0, fallDamage: false, Vector3.down * 40f);
                        }

                        lethalBotController.externalForceAutoFade += vector;
                    }
                }
            }
        }

        // Grandpa? Why is this a prefix and not a postfix?
        // Well you see Timmy, the orignal function only works for the local player and since the bots are not the local player
        // we have to recreate them here. This is because the original function also changes some variables that we need to check before they are changed!
        [HarmonyPatch("DoFootstepCycle")]
        [HarmonyPrefix]
        private static void DoFootstepCycle_Prefix(RadMechAI __instance, bool __runOriginal, ref bool ___disableWalking, ref float ___beginChargingTimer, ref bool ___startedChargeEffect)
        {
            if (!__instance.IsServer || !__runOriginal)
            {
                return;
            }

            if (__instance.chargingForward && !___disableWalking && !__instance.aimingGun)
            {
                if (___beginChargingTimer < 0.4f)
                {
                    return;
                }

                if (___startedChargeEffect)
                {
                    return;
                }

                LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
                foreach (LethalBotAI lethalBotAI in lethalBotAIs)
                {
                    PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                    if (lethalBotController != null)
                    {
                        float num = Vector3.Distance(__instance.transform.position - __instance.transform.forward * 5f, lethalBotController.transform.position);
                        if (num < 12f)
                        {
                            Landmine.SpawnExplosion(__instance.transform.position + Vector3.up * 0.2f, spawnExplosionEffect: false, 2f, 5f, 30, 45f);
                            break; // Only do this once!
                        }
                    }
                }
            }
        }

        [HarmonyPatch("SetExplosion")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SetExplosion_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch RadMechAI.SetExplosion!");

            /*Plugin.LogDebug("Before patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 11; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) // 69
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 11].opcode == OpCodes.Bge_Un)
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the local player check with a check for local player or lethal bot
                // if (Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, explosionPosition - forwardRotation * 0.1f) < 8f)
                List<Label> jumpToElseLabel = codes[startIndex + 11].labels;
                Label branchTarget = (Label)codes[startIndex + 11].operand;
                codes.RemoveRange(startIndex, 12);

                codes.InsertRange(startIndex, new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_1), // Load local variable explosionPosition
                    new CodeInstruction(OpCodes.Ldarg_2), // Load local variable forwardRotation
                    new CodeInstruction(OpCodes.Call, IsLocalPlayerOrLethalBotCloseToPosMethod), // Call the method to check for local player or lethal bot
                    new CodeInstruction(OpCodes.Brfalse, branchTarget).WithLabels(jumpToElseLabel) // Branch to the else block if false
                });

                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.RadMechAI.SetExplosion_Transpiler could not change local explostion distance checks!");
            }

            Plugin.LogDebug("Finished patching RadMechAI.SetExplosion!");

            /*Plugin.LogDebug("After patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            return codes.AsEnumerable();
        }

        private static bool IsLocalPlayerOrLethalBotCloseToPos(Vector3 explosionPosition, Vector3 forwardRotation)
        {
            Vector3 endPosition = explosionPosition - forwardRotation * 0.1f;
            if ((GameNetworkManager.Instance.localPlayerController.transform.position - endPosition).sqrMagnitude < 8f * 8f)
            {
                return true;
            }

            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI? lethalBotAI in lethalBotAIs)
            {
                if (lethalBotAI != null)
                {
                    PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
                    if (lethalBotController != null 
                        && !lethalBotController.isPlayerDead)
                    {
                        if ((lethalBotController.transform.position - endPosition).sqrMagnitude < 8f * 8f)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
