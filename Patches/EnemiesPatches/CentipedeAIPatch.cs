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
                    for (int i = 0; i < lethalBotAIs.Length; i++)
                    {
                        LethalBotAI? lethalBotAI = lethalBotAIs[i];
                        PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                        if (lethalBotController != null && lethalBotController.HasLineOfSightToPosition(__instance.transform.position, 60f, 12))
                        {
                            lethalBotController.IncreaseFearLevelOverTime(0.6f, 0.9f);
                        }
                    }
                    break;
            }
        }
    }
}
