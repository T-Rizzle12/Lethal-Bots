using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LethalBots.Patches.MapHazardsPatches
{
    /// <summary>
    /// Patch for the <c>QuicksandTrigger</c>
    /// </summary>
    [HarmonyPatch(typeof(QuicksandTrigger))]
    public class QuicksandTriggerPatch
    {
        // Static variables
        // Conditional Weak Table since when the QuicksandTrigger is removed, the table automatically cleans itself!
        public static readonly ConditionalWeakTable<QuicksandTrigger, QuicksandTriggerMonitor> quicksandTriggerMonitorList = new ConditionalWeakTable<QuicksandTrigger, QuicksandTriggerMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="QuicksandTriggerMonitor"/>
        /// for the given <see cref="QuicksandTrigger"/>
        /// </summary>
        /// <param name="quicksand"></param>
        /// <returns>The <see cref="QuicksandTriggerMonitor"/> associated with the given <see cref="QuicksandTrigger"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static QuicksandTriggerMonitor GetOrCreateMonitor(QuicksandTrigger quicksand)
        {
            return quicksandTriggerMonitorList.GetValue(quicksand, key => new QuicksandTriggerMonitor(key));
        }

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
            if (lethalBotAI == null)
            {
                return;
            }

            QuicksandTriggerMonitor quicksandTriggerMonitor = GetOrCreateMonitor(__instance);
            if ((__instance.isWater && lethalBotController.isInsideFactory != __instance.isInsideWater) || lethalBotController.isInElevator)
            {
                if (quicksandTriggerMonitor.IsSinkingLethalBot(lethalBotAI))
                {
                    __instance.StopSinkingLocalPlayer(lethalBotController);
                }
                return;
            }
            if (__instance.isWater && !lethalBotController.isUnderwater)
            {
                lethalBotController.underwaterCollider = __instance.gameObject.GetComponent<Collider>();
                if (lethalBotController.IsOwner)
                {
                    lethalBotController.isUnderwater = true;
                    if (!__instance.isInsideWater && (lethalBotController.isFallingFromJump || lethalBotController.isFallingNoJump) && lethalBotController.fallValue < -4f)
                    {
                        TimeOfDay.Instance.WaterSplashEffect(lethalBotController.transform.position, lethalBotController.fallValue > -17f, syncToServer: true);
                    }
                }
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
                        IsLethalBotInside = lethalBotController.isInsideFactory,
                        AllowSwearing = Plugin.Config.AllowSwearing.Value
                    });
                }
                return;
            }
            if (quicksandTriggerMonitor.IsSinkingLethalBot(lethalBotAI))
            {
                if (!lethalBotAI.NpcController.CheckConditionsForSinkingInQuicksandLethalBot())
                {
                    __instance.StopSinkingLocalPlayer(lethalBotController);
                }
            }
            else if (lethalBotAI.NpcController.CheckConditionsForSinkingInQuicksandLethalBot())
            {
                quicksandTriggerMonitor.SetBotSinkingInQuicksand(lethalBotAI, setSinking: true);
                lethalBotController.sourcesCausingSinking++;
                lethalBotController.isMovementHindered++;
                Plugin.LogDebug($"playerScript {lethalBotController.playerClientId} ++isMovementHindered {lethalBotController.isMovementHindered}");
                lethalBotController.hinderedMultiplier *= __instance.movementHinderance;
                if (__instance.isWater)
                {
                    lethalBotController.sinkingSpeedMultiplier = 0f;
                }
                else
                {
                    lethalBotController.sinkingSpeedMultiplier = __instance.sinkingSpeedMultiplier;
                }
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

            QuicksandTriggerMonitor quicksandTriggerMonitor = GetOrCreateMonitor(__instance);
            if (!quicksandTriggerMonitor.IsSinkingLethalBot(lethalBotAI))
            {
                if (__instance.isWater)
                {
                    lethalBotController.isUnderwater = false;
                }
            }
            else
            {
                __instance.StopSinkingLocalPlayer(lethalBotController);
            }
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
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerScript);
            if (lethalBotAI == null)
            {
                return true;
            }

            QuicksandTriggerMonitor quicksandTriggerMonitor = GetOrCreateMonitor(__instance);
            if (quicksandTriggerMonitor.IsSinkingLethalBot(lethalBotAI))
            {
                quicksandTriggerMonitor.SetBotSinkingInQuicksand(lethalBotAI, setSinking: false);
                playerScript.sourcesCausingSinking = Mathf.Clamp(playerScript.sourcesCausingSinking - 1, 0, 100);
                playerScript.isMovementHindered = Mathf.Clamp(playerScript.isMovementHindered - 1, 0, 100);
                playerScript.hinderedMultiplier = Mathf.Clamp(playerScript.hinderedMultiplier / __instance.movementHinderance, 1f, 100f);
                if (playerScript.isMovementHindered == 0 && __instance.isWater)
                {
                    playerScript.isUnderwater = false;
                }
            }
            return false;
        }

        /// <summary>
        /// Helper class used to mimic <see cref="QuicksandTrigger.sinkingLocalPlayer"/> for bots!
        /// </summary>
        public sealed class QuicksandTriggerMonitor
        {
            public QuicksandTrigger quicksand { private set; get; } = null!;
            private readonly Dictionary<LethalBotAI, bool> lethalBotAIs = new Dictionary<LethalBotAI, bool>();

            internal QuicksandTriggerMonitor(QuicksandTrigger quicksand)
            {
                this.quicksand = quicksand;
            }

            /// <summary>
            /// Checks if the <see cref="quicksand"/> associated with this is sinking the given <paramref name="lethalBotAI"/>.
            /// </summary>
            /// <param name="lethalBotAI">The bot to check</param>
            /// <returns><see langword="true"/> if this <see cref="quicksand"/> is sinking the bot; otherwise, <see langword="false"/>.</returns>
            public bool IsSinkingLethalBot(LethalBotAI lethalBotAI)
            {
                return lethalBotAIs.GetValueOrDefault(lethalBotAI, false);
            }

            /// <summary>
            /// Sets if the given <paramref name="lethalBotAI"/> is sinking via <paramref name="setSinking"/>
            /// </summary>
            /// <param name="lethalBotAI">The bot to change the state for.</param>
            /// <param name="setSinking">If the bot is sinking or not.</param>
            public void SetBotSinkingInQuicksand(LethalBotAI lethalBotAI, bool setSinking)
            {
                lethalBotAIs[lethalBotAI] = setSinking;
            }
        }
    }
}
