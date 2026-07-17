using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patch for <c>SpringManAI</c>
    /// </summary>
    [HarmonyPatch(typeof(SpringManAI))]
    public class SpringManAIPatch
    {
        // Static variables
        // Conditional Weak Table since when the SpringManAI is removed, the table automatically cleans itself!
        private static ConditionalWeakTable<SpringManAI, SpringManMonitor> springManMonitorList = new ConditionalWeakTable<SpringManAI, SpringManMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="SpringManMonitor"/>
        /// for the given <see cref="SpringManAI"/>
        /// </summary>
        /// <param name="ai"></param>
        /// <returns>The <see cref="SpringManMonitor"/> associated with the given <see cref="SpringManAI"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SpringManMonitor GetOrCreateMonitor(SpringManAI ai)
        {
            return springManMonitorList.GetValue(ai, key => new SpringManMonitor(key));
        }

        /// <summary>
        /// Used to fixed the infinite scream and stare at coilhead bug for bots
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(SpringManAI __instance)
        {
            if (__instance.isEnemyDead)
            {
                return;
            }

            UpdateLimiter updateLimiter = UpdateLimiter.GetOrCreateMonitor(__instance, 0.1f);
            if (!updateLimiter.CanUpdate())
            {
                return;
            }

            updateLimiter.Invalidate();
            if (!__instance.stoppingMovement)
            {
                SpringManMonitor springManMonitor = GetOrCreateMonitor(__instance);
                springManMonitor.HasMoved();
            }
        }

        [HarmonyPatch("DoSpringAnimation")]
        [HarmonyPostfix]
        public static void DoSpringAnimation_Postfix(SpringManAI __instance, bool springPopUp = false)
        {
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI? lethalBotAI = lethalBotAIs[i];
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    if (lethalBotController.HasLineOfSightToPosition(__instance.transform.position + Vector3.up * 0.6f, 70f, 25))
                    {
                        float num = (__instance.transform.position - lethalBotController.transform.position).sqrMagnitude;
                        if (num < 4f * 4f)
                        {
                            lethalBotController.JumpToFearLevel(0.9f);
                        }
                        else if (num < 9f * 9f)
                        {
                            lethalBotController.JumpToFearLevel(0.4f);
                        }
                    }
                }
            }
        }

        public sealed class SpringManMonitor
        {
            private SpringManAI springManAI = null!;
            private IntervalTimer lastMoveTimer = new IntervalTimer();

            internal SpringManMonitor(SpringManAI springManAI)
            {
                this.springManAI = springManAI;
            }

            /// <summary>
            /// Checks if the Coil Head has moved within the last <paramref name="timeThreshold"/> seconds.
            /// </summary>
            /// <param name="timeThreshold">How long to check for movement (in seconds)</param>
            /// <returns><see langword="true"/> if the Coil Head has moved recently; otherwise, <see langword="false"/>.</returns>
            public bool HasMovedRecently(float timeThreshold = 10f)
            {
                if (springManAI == null || springManAI.isEnemyDead)
                {
                    return false;
                }
                if (lastMoveTimer.HasStarted())
                {
                    return lastMoveTimer.IsLessThan(timeThreshold);
                }
                return false;
            }

            /// <summary>
            /// Resets the movement timer, indicating that the Coil Head has moved.
            /// </summary>
            public void HasMoved()
            {
                lastMoveTimer.Reset();
            }
        }
    }
}
