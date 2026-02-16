using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.Patches.MapHazardsPatches
{
    /// <summary>
    /// Patch for the <c>QuicksandTrigger</c>
    /// </summary>
    [HarmonyPatch(typeof(QuicksandTrigger))]
    public class QuicksandTriggerPatch
    {
        /// <summary>
        /// Patch for making quicksand works with bot, when entering
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        [HarmonyPatch("OnTriggerStay")]
        [HarmonyPostfix]
        public static void OnTriggerStay_Postfix(ref QuicksandTrigger __instance, Collider other)
        {
            PlayerControllerB lethalBotController = other.gameObject.GetComponent<PlayerControllerB>();
            if (lethalBotController == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(lethalBotController);
            if (lethalBotAI == null || lethalBotAI.NpcController.IsControllerInCruiser)
            {
                return;
            }

            if (__instance.isWater && lethalBotController.underwaterCollider == null)
            {
                lethalBotController.underwaterCollider = __instance.gameObject.GetComponent<Collider>();
            }
            lethalBotController.statusEffectAudioIndex = __instance.audioClipIndex;
            if (lethalBotController.isSinking)
            {
                if (!__instance.isWater)
                {
                    // Audio
                    lethalBotAI.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                    {
                        VoiceState = EnumVoicesState.Sinking,
                        CanTalkIfOtherLethalBotTalk = true,
                        WaitForCooldown = false,
                        CutCurrentVoiceStateToTalk = true,
                        CanRepeatVoiceState = true,

                        ShouldSync = false,
                        IsLethalBotInside = lethalBotAI.NpcController.Npc.isInsideFactory,
                        AllowSwearing = Plugin.Config.AllowSwearing.Value
                    });
                }
                return;
            }

            if (lethalBotAI.NpcController.CheckConditionsForSinkingInQuicksandLethalBot())
            {
                // Being sinking
                lethalBotController.sourcesCausingSinking++;
                lethalBotController.isMovementHindered++;
                Plugin.LogDebug($"playerScript {lethalBotController.playerClientId} ++isMovementHindered {lethalBotController.isMovementHindered}");
                lethalBotController.hinderedMultiplier *= __instance.movementHinderance;
                if (__instance.isWater)
                {
                    lethalBotController.sinkingSpeedMultiplier = 0f;
                    return;
                }
                lethalBotController.sinkingSpeedMultiplier = __instance.sinkingSpeedMultiplier;
            }
            else
            {
                lethalBotAI.StopSinkingState();
            }
        }

        /// <summary>
        /// Patch for making quicksand works with bot, when exiting
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        [HarmonyPatch("OnExit")]
        [HarmonyPostfix]
        public static void OnExit_Postfix(ref QuicksandTrigger __instance, Collider other)
        {
            PlayerControllerB lethalBotController = other.gameObject.GetComponent<PlayerControllerB>();
            if (lethalBotController == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(lethalBotController);
            if (lethalBotAI == null || lethalBotAI.NpcController.IsControllerInCruiser)
            {
                return;
            }

            lethalBotAI.StopSinkingState();
        }

        /// <summary>
        /// Patch for updating the right fields when an bot goes out of the quicksand
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="playerScript"></param>
        /// <returns></returns>
        [HarmonyPatch("StopSinkingLocalPlayer")]
        [HarmonyPrefix]
        public static bool StopSinkingLocalPlayer_Prefix(QuicksandTrigger __instance, PlayerControllerB playerScript)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(playerScript);
            if (lethalBotAI == null)
            {
                return true;
            }

            lethalBotAI.StopSinkingState();
            return false;
        }
    }
}
