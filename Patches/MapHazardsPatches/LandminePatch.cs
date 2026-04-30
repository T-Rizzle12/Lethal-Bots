using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.MapHazardsPatches
{
    /// <summary>
    /// Patch for the <c>Landmine</c>
    /// </summary>
    [HarmonyPatch(typeof(Landmine))]
    public class LandminePatch
    {
        /// <summary>
        /// Patch for making the bot able to trigger the mine by stepping on it
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        /// <param name="___localPlayerOnMine"></param>
        /// <param name="___pressMineDebounceTimer"></param>
        [HarmonyPatch("OnTriggerEnter")]
        [HarmonyPrefix]
        static bool OnTriggerEnter_PreFix(ref Landmine __instance,
                                           Collider other,
                                           ref bool ___localPlayerOnMine,
                                           ref float ___pressMineDebounceTimer
                                           )
        {
            if (__instance.hasExploded)
            {
                return true;
            }
            if (___pressMineDebounceTimer > 0f)
            {
                return true;
            }

            PlayerControllerB lethalBotController = other.gameObject.GetComponent<PlayerControllerB>();
            if (lethalBotController != null 
                && !lethalBotController.isPlayerDead)
            {
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(lethalBotController);
                if (lethalBotAI != null)
                {
                    ___localPlayerOnMine = true;
                    ___pressMineDebounceTimer = 0.5f;
                    __instance.PressMineServerRpc();

                    // Audio
                    lethalBotAI.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                    {
                        VoiceState = EnumVoicesState.SteppedOnTrap,
                        CanTalkIfOtherLethalBotTalk = true,
                        WaitForCooldown = false,
                        CutCurrentVoiceStateToTalk = true,
                        CanRepeatVoiceState = false,

                        ShouldSync = false,
                        IsLethalBotInside = lethalBotController.isInsideFactory,
                        AllowSwearing = Plugin.Config.AllowSwearing.Value
                    });
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Patch for making the bot able to trigger the mine by stepping on it
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        /// <param name="___localPlayerOnMine"></param>
        /// <param name="___mineActivated"></param>
        [HarmonyPatch("OnTriggerExit")]
        [HarmonyPrefix]
        static bool OnTriggerExit_PreFix(ref Landmine __instance,
                                          Collider other,
                                          ref bool ___localPlayerOnMine,
                                          bool ___mineActivated)
        {
            if (__instance.hasExploded)
            {
                return true;
            }
            if (!___mineActivated)
            {
                return true;
            }

            PlayerControllerB lethalBotController = other.gameObject.GetComponent<PlayerControllerB>();
            if (lethalBotController != null
                && !lethalBotController.isPlayerDead)
            {
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(lethalBotController);
                if (lethalBotAI != null)
                {
                    ___localPlayerOnMine = false;

                    // Audio
                    lethalBotAI.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                    {
                        VoiceState = EnumVoicesState.SteppedOnTrap,
                        CanTalkIfOtherLethalBotTalk = true,
                        WaitForCooldown = false,
                        CutCurrentVoiceStateToTalk = true,
                        CanRepeatVoiceState = true,

                        ShouldSync = false,
                        IsLethalBotInside = lethalBotController.isInsideFactory,
                        AllowSwearing = Plugin.Config.AllowSwearing.Value
                    });

                    // Boom
                    TriggerMineOnLocalClientByExiting_ReversePatch(__instance);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reverse patch for calling <c>TriggerMineOnLocalClientByExiting</c>z.
        /// Set the mine to explode.
        /// </summary>
        /// <param name="instance"></param>
        /// <exception cref="NotImplementedException">Ignore (see harmony)</exception>
        [HarmonyPatch("TriggerMineOnLocalClientByExiting")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void TriggerMineOnLocalClientByExiting_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.EnemiesPatches.TriggerMineOnLocalClientByExiting");

        /// <summary>
        /// Patch for making an explosion check for bots, calls for an explosion by landmine or lightning.
        /// </summary>
        /// <remarks>
        /// All of the player's body parts can be hit by <c>Physics.OverlapSphere</c>,<br/>
        /// so we need to check a hit collider's parent to make sure we only hit a bot once.
        /// </remarks>
        /// <param name="explosionPosition"></param>
        /// <param name="killRange"></param>
        /// <param name="damageRange"></param>
        /// <param name="nonLethalDamage"></param>
        [HarmonyPatch("SpawnExplosion")]
        [HarmonyPostfix]
        static void SpawnExplosion_PostFix(Vector3 explosionPosition, 
                                           float killRange, 
                                           float damageRange, 
                                           int nonLethalDamage,
                                           float physicsForce,
                                           bool goThroughCar)
        {
            Collider[] array = Physics.OverlapSphere(explosionPosition, damageRange, 8, QueryTriggerInteraction.Collide);
            PlayerControllerB? lethalBotController;
            LethalBotAI? lethalBotAI;
            HashSet<ulong> lethalBotsAlreadyExploded = new HashSet<ulong>();
            for (int i = 0; i < array.Length; i++)
            {
                var hitCollider = array[i];
                Plugin.LogDebug($"SpawnExplosion OverlapSphere array {i} {hitCollider.name}");
                float distanceFromExplosion = Vector3.Distance(explosionPosition, hitCollider.transform.position);
                lethalBotController = hitCollider.gameObject.GetComponent<PlayerControllerB>();
                if (lethalBotController == null 
                    || lethalBotsAlreadyExploded.Contains(lethalBotController.playerClientId))
                {
                    continue;
                }

                lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(lethalBotController);
                if (lethalBotAI == null)
                {
                    continue;
                }

                if (Physics.Linecast(explosionPosition, hitCollider.transform.position + Vector3.up * 0.3f, out RaycastHit hitInfo, 1073742080, QueryTriggerInteraction.Ignore) 
                    && ((!goThroughCar && hitInfo.collider.gameObject.layer == 30) 
                        || distanceFromExplosion > 4f))
                {
                    continue;
                }

                if (hitCollider.gameObject.layer == 3)
                {
                    if (distanceFromExplosion < killRange)
                    {
                        Vector3 vector = Vector3.Normalize(lethalBotController.gameplayCamera.transform.position - explosionPosition) * 80f / Vector3.Distance(lethalBotController.gameplayCamera.transform.position, explosionPosition);
                        Plugin.LogDebug($"SyncKillLethalBot from explosion for LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                        lethalBotController.KillPlayer(vector, spawnBody: true, CauseOfDeath.Blast, 0, default);
                    }
                    else if (distanceFromExplosion < damageRange)
                    {
                        Vector3 vector = Vector3.Normalize(lethalBotController.gameplayCamera.transform.position - explosionPosition) * 80f / Vector3.Distance(lethalBotController.gameplayCamera.transform.position, explosionPosition);
                        lethalBotController.DamagePlayer(nonLethalDamage, hasDamageSFX: true, callRPC: true, CauseOfDeath.Blast, 0, false, vector * 0.6f);
                    }
                }

                if (physicsForce > 0f && distanceFromExplosion < 35f && !Physics.Linecast(explosionPosition, hitCollider.transform.position + Vector3.up * 0.3f, out _, 256, QueryTriggerInteraction.Ignore))
                {
                    float num3 = distanceFromExplosion;
                    Vector3 vector = Vector3.Normalize(lethalBotController.transform.position + Vector3.up * num3 - explosionPosition) / (num3 * 0.35f) * physicsForce;
                    Plugin.LogDebug($"Physics Force is {physicsForce}. Calculated Force is {vector.magnitude}!");
                    if (vector.sqrMagnitude > 2f * 2f)
                    {
                        if (vector.sqrMagnitude > 10f * 10f)
                        {
                            lethalBotController.CancelSpecialTriggerAnimations();
                        }
                        if (!lethalBotController.inVehicleAnimation || (lethalBotController.externalForceAutoFade + vector).sqrMagnitude > 50f * 50f)
                        {
                            lethalBotController.externalForceAutoFade += vector;
                        }
                    }
                }

                lethalBotsAlreadyExploded.Add(lethalBotController.playerClientId);
            }
        }

        /// <summary>
        /// This fixes a rare bug where bots could die in place of the local player..........
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("SpawnExplosion")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SpawnExplosion_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            /*Plugin.LogDebug("Before patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            // Target property: IsOwner
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch Landmine.SpawnExplosion!");

            // ---------- Step 1: Replace 'playerControllerB.IsOwner' ----------
            for (var i = 0; i < codes.Count; i++)
            {
                // Look for occurrences of "IsOwner"
                if (codes[i].Calls(isOwnerGetter))
                {
                    // Replace with IsPlayerLocal
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                // Replace the old instruction to call the replacement method.
                codes[startIndex].opcode = OpCodes.Call;
                codes[startIndex].operand = PatchesUtil.IsPlayerLocalMethod; // Call our replacement method
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapHazardsPatches.Landmine.SpawnExplosion_Transpiler could not find the IsOwner call!");
            }

            // Let the user know that we finished
            Plugin.LogDebug("Finished patching Landmine.SpawnExplosion!");

            /*Plugin.LogDebug("After patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            return codes.AsEnumerable();
        }
    }
}
