using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
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
            List<ulong> lethalBotsAlreadyExploded = new List<ulong>();
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
    }
}
