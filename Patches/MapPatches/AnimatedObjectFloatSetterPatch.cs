using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    /// <summary>
    /// Patch for <c>AnimatedObjectFloatSetter</c>
    /// </summary>
    [HarmonyPatch(typeof(AnimatedObjectFloatSetter))]
    public class AnimatedObjectFloatSetterPatch
    {
        /// <summary>
        /// Made with the sole purpose of letting <see cref="AnimatedObjectFloatSetter.KillPlayerAtPoint"/> affect bots
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("KillPlayerAtPoint")]
        [HarmonyPostfix]
        static void KillPlayerAtPoint_Postfix(AnimatedObjectFloatSetter __instance)
        {
            // Same as the base game
            if (__instance.killPlayerPoint == null)
            {
                return;
            }

            // Make this affect bots as well!
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (var lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController != null && !lethalBotController.isPlayerDead)
                {
                    Vector3 position = __instance.killPlayerPoint.position;
                    if (__instance.ignoreVerticalDistance)
                    {
                        position.y = lethalBotController.transform.position.y;
                    }
                    if ((lethalBotController.transform.position - position).sqrMagnitude < __instance.killRange * __instance.killRange)
                    {
                        lethalBotController.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Crushing);
                    }
                }
            }
        }
    }
}
