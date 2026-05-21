using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patches for <c>CentipedeAI</c>
    /// </summary>
    [HarmonyPatch(typeof(CentipedeAI))]
    public class CentipedeAIPatch
    {
        /// <summary>
        /// Patch for making the centipede hurt the bot
        /// </summary>
        /// <remarks>
        /// TODO: Use a transpiler here instead!
        /// </remarks>
        /// <param name="__instance"></param>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Update_PostFix(ref CentipedeAI __instance)
        {
            if (__instance.isEnemyDead)
            {
                return;
            }

            switch (__instance.currentBehaviourStateIndex)
            {
                case 3:
                    if (__instance.clingingToPlayer == null)
                    {
                        break;
                    }

                    if (LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(__instance.clingingToPlayer))
                    {
                        __instance.DamagePlayerOnIntervals();
                    }

                    // Limit the frequency of this code since it can be quite expensive, especially if there are multiple snare fleas
                    UpdateLimiter updateLimiter = UpdateLimiter.GetOrCreateMonitor(__instance);
                    if (!updateLimiter.CanUpdate())
                    {
                        return;
                    }

                    // Reset the limiter
                    updateLimiter.Invalidate();

                    // Loop through all the lethal bots and check if they have line of sight to the cadaver bloom, if they do, increase their fear level.
                    LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
                    foreach (var lethalBotAI in lethalBotAIs)
                    {
                        PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                        if (lethalBotController != null && lethalBotController.HasLineOfSightToPosition(__instance.transform.position, 60f, 12))
                        {
                            lethalBotController.IncreaseFearLevelOverTime(0.6f, 0.9f);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Patch to clean up <see cref="UpdateLimiter"/>'s that are no longer needed.
        /// </summary>
        /// <remarks>
        /// Although <see cref="ConditionalWeakTable{TKey, TValue}"/> can clean this for us,
        /// it will only clean the table if nothing refrences the key anymore.
        /// </remarks>
        /// <param name="__instance"></param>
        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix(CentipedeAI __instance)
        {
            UpdateLimiter.RemoveMonitor(__instance);
        }
    }
}
