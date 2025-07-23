using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using LethalBots.Patches.GameEnginePatches;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace LethalBots.Patches.EnemiesPatches
{
    [HarmonyPatch(typeof(GiantKiwiAI))]
    public class GiantKiwiAIPatch
    {
        // Conditional Weak Table since when the GiantKiwiAI is removed, the table automatically cleans itself!
        private static ConditionalWeakTable<GiantKiwiAI, GiantKiwiPlayerMonitor> lethalBotGiantKiwiMonitor = new ConditionalWeakTable<GiantKiwiAI, GiantKiwiPlayerMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="GiantKiwiPlayerMonitor"/>
        /// for the given <see cref="GiantKiwiAI"/>
        /// </summary>
        /// <param name="ai"></param>
        /// <returns>The <see cref="GiantKiwiPlayerMonitor"/> associated with the given <see cref="GiantKiwiAI"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static GiantKiwiPlayerMonitor GetOrCreateMonitor(GiantKiwiAI ai)
        {
            return lethalBotGiantKiwiMonitor.GetOrCreateValue(ai);
        }

        /// <summary>
        /// Override the default collide with player for bots since if we don't, 
        /// the local player will take damage and be flung instead!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        /// <param name="___attacking"></param>
        /// <returns></returns>
        [HarmonyPatch("OnCollideWithPlayer")]
        [HarmonyPrefix]
        public static bool OnCollideWithPlayer_Prefix(GiantKiwiAI __instance, Collider other, ref bool ___attacking)
        {
            PlayerControllerB? playerControllerB = __instance.MeetsStandardPlayerCollisionConditions(other, __instance.inKillAnimation);
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(playerControllerB);
            if (lethalBotAI == null)
            {
                return true;
            }
            if (!playerControllerB.isInHangarShipRoom || __instance.isInsidePlayerShip || !(playerControllerB.transform.position.y - __instance.transform.position.y > 1.6f) || !Physics.Linecast(__instance.transform.position + Vector3.up * 0.45f, playerControllerB.transform.position + Vector3.up * 0.45f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                GiantKiwiPlayerMonitor giantKiwiPlayerMonitor = GetOrCreateMonitor(__instance);
                giantKiwiPlayerMonitor.UpdateTimeSinceHittingBot(lethalBotAI);
                if (___attacking)
                {
                    playerControllerB.JumpToFearLevel(1f);
                }
            }
            return false;
        }

        /// <summary>
        /// Since the base game only checks for the local player,
        /// we need to update animation event to damage the bots here!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="___attacking"></param>
        [HarmonyPatch("AnimationEventB")]
        [HarmonyPostfix]
        public static void AnimationEventB_Postfix(GiantKiwiAI __instance, ref bool ___attacking)
        {
            if (___attacking && !__instance.isEnemyDead)
            {
                GiantKiwiPlayerMonitor giantKiwiPlayerMonitor = GetOrCreateMonitor(__instance);

                // Make a copy of the keys to safely iterate
                List<LethalBotAI> keys = new List<LethalBotAI>(giantKiwiPlayerMonitor.lethalBotAIs.Keys);
                foreach (var lethalBotAI in keys)
                {
                    if (lethalBotAI != null && giantKiwiPlayerMonitor.ShouldApplyDamageTo(lethalBotAI))
                    {
                        PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
                        _ = lethalBotController.transform.position;
                        Vector3 vector = lethalBotController.transform.position + Vector3.up * 3f - __instance.transform.position;
                        lethalBotController.externalForceAutoFade += vector * __instance.hitVelocityForce;
                        lethalBotController.DamagePlayer(10, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing, 9, fallDamage: false, vector * __instance.hitVelocityForce * 0.4f);
                        giantKiwiPlayerMonitor.UpdateTimeSinceHittingBot(lethalBotAI);
                    }
                }
            }
        }

        private class GiantKiwiPlayerMonitor
        {
            public Dictionary<LethalBotAI, float> lethalBotAIs = new Dictionary<LethalBotAI, float>();

            public void UpdateTimeSinceHittingBot(LethalBotAI bot)
            {
                lethalBotAIs[bot] = Time.timeSinceLevelLoad;
            }

            public bool ShouldApplyDamageTo(LethalBotAI bot)
            {
                float lastDamageTime = lethalBotAIs.GetValueOrDefault(bot);
                return (Time.timeSinceLevelLoad - lastDamageTime) < 0.1f;
            }
        }
    }
}
