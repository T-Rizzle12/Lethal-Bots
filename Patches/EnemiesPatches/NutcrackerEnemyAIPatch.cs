using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patch for <c>NutcrackerEnemyAI</c>
    /// </summary>
    [HarmonyPatch(typeof(NutcrackerEnemyAI))]
    public class NutcrackerEnemyAIPatch
    {
        // Conditional Weak Table since when the NutcrackerEnemyAI is removed, the table automatically cleans itself!
        // TODO: I should probably move this along with the UpdateLimiter into its own file, an change the key to EnemyAI, so
        // I don't have to keep recreating this code.....
        private static ConditionalWeakTable<NutcrackerEnemyAI, UpdateLimiter> nextUpdateList = new ConditionalWeakTable<NutcrackerEnemyAI, UpdateLimiter>();

        /// <summary>
        /// Helper function that retrieves the <see cref="UpdateLimiter"/>
        /// for the given <see cref="NutcrackerEnemyAI"/>
        /// </summary>
        /// <param name="ai"></param>
        /// <returns>The <see cref="UpdateLimiter"/> associated with the given <see cref="NutcrackerEnemyAI"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static UpdateLimiter GetOrCreateMonitor(NutcrackerEnemyAI ai)
        {
            return nextUpdateList.GetValue(ai, key => new UpdateLimiter(0.5f)); // TODO: Find out how long I should make my patch wait between calls! 
        }

        /// <summary>
        /// Patch for making the nutcrackers see moving bots!
        /// </summary>
        /// <remarks>
        /// I had to recreate some parts of the AI since it was made to only work with the local player.
        /// </remarks>
        /// <param name="__instance"></param>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Postfix(NutcrackerEnemyAI __instance, 
            ref bool ___isInspecting, 
            ref float ___timeSinceSeeingTarget,
            ref float ___timeSinceFiringGun,
            ref bool ___reloadingGun,
            ref bool ___aimingGun,
            ref float ___timeSinceHittingPlayer,
            ref bool ___lostPlayerInChase,
            ref Vector3 ___lastSeenPlayerPos)
        {
            if (__instance.isEnemyDead || __instance.stunNormalizedTimer >= 0f)
            {
                return;
            }

            UpdateLimiter updateLimiter = GetOrCreateMonitor(__instance);
            if (!updateLimiter.CanUpdate())
            {
                updateLimiter.Update(Time.deltaTime);
                return;
            }

            updateLimiter.Invalidate();
            switch (__instance.currentBehaviourStateIndex)
            {
                case 1:
                {
                    if (___isInspecting)
                    {
                        // Minor optimization, only create array if we are actually going to use it!
                        LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
                        foreach (var lethalBotAI in lethalBotAIs) 
                        {
                            PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                            if (IsPlayerMoving(lethalBotController) && CheckLineOfSightForPlayer(__instance, lethalBotController, 70f, 60, 1))
                            {
                                ___isInspecting = false;
                                __instance.SeeMovingThreatServerRpc((int)lethalBotController.playerClientId);
                                break;
                            }
                        }
                    }
                    break;
                }
                case 2:
                {
                    // Mimic base game logic!
                    if (__instance.lastPlayerSeenMoving == -1)
                    {
                        break;
                    }

                    // TODO: I should use a transpiler to fix the nutcracker firing part of the code, the current method is really hacky!
                    PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[__instance.lastPlayerSeenMoving];
                    if (LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(targetPlayer) && __instance.CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position, 70f, 60, 1f) && ___timeSinceSeeingTarget < 8f)
                    {
                        if (___timeSinceFiringGun > 0.75f && !___reloadingGun && !___aimingGun && ___timeSinceHittingPlayer > 1f && Vector3.Angle(__instance.gun.shotgunRayPoint.forward, targetPlayer.gameplayCamera.transform.position - __instance.gun.shotgunRayPoint.position) < 30f)
                        {
                            ___timeSinceFiringGun = 0f;
                            __instance.agent.speed = 0f;
                            __instance.AimGunServerRpc(__instance.transform.position);
                        }
                        if (___lostPlayerInChase)
                        {
                            ___lostPlayerInChase = false;
                            __instance.SetLostPlayerInChaseServerRpc(lostPlayer: false);
                        }
                        ___timeSinceSeeingTarget = 0f;
                        ___lastSeenPlayerPos = targetPlayer.transform.position;
                    }

                    // Got to make sure bots are considered as valid targets as well!
                    LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
                    foreach (var lethalBotAI in lethalBotAIs) 
                    {
                        PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                        if (IsPlayerMoving(lethalBotController) && CheckLineOfSightForPlayer(__instance, lethalBotController, 70f, 25, 1))
                        {
                            bool flag = (int)lethalBotController.playerClientId == __instance.lastPlayerSeenMoving;
                            if (flag)
                            {
                                ___timeSinceSeeingTarget = 0f;
                            }

                            float sqrDistLast = (__instance.transform.position - StartOfRound.Instance.allPlayerScripts[__instance.lastPlayerSeenMoving].transform.position).sqrMagnitude;
                            float sqrDistBot = (__instance.transform.position - lethalBotController.transform.position).sqrMagnitude;
                            if ((sqrDistLast - sqrDistBot) > 3f * 3f || (___timeSinceSeeingTarget > 3f && !flag))
                            {
                                __instance.lastPlayerSeenMoving = (int)lethalBotController.playerClientId;
                                __instance.SeeMovingThreatServerRpc((int)lethalBotController.playerClientId);
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// A carbon copy of <see cref="NutcrackerEnemyAI.CheckLineOfSightForLocalPlayer(float, int, int)"/>, but made to consider bots!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="playerControllerB"></param>
        /// <param name="width"></param>
        /// <param name="range"></param>
        /// <param name="proximityAwareness"></param>
        /// <returns></returns>
        private static bool CheckLineOfSightForPlayer(NutcrackerEnemyAI __instance, PlayerControllerB playerControllerB, float width = 45f, int range = 60, int proximityAwareness = -1)
        {
            Vector3 position = playerControllerB.gameplayCamera.transform.position;
            if (Vector3.Distance(position, __instance.eye.position) < (float)range && !Physics.Linecast(__instance.eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                Vector3 to = position - __instance.eye.position;
                if (Vector3.Angle(__instance.eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(__instance.eye.position, position) < (float)proximityAwareness))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Basically a carbon copy of <see cref="NutcrackerEnemyAI.IsLocalPlayerMoving"/>, but made to support other players!
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public static bool IsPlayerMoving([NotNullWhen(true)]PlayerControllerB? player)
        {
            // TODO: Implement code that lets me check the bots current turn distance.......
            if (player == null)
            {
                return false;
            }
            if (player.performingEmote)
            {
                return true;
            }
            if (player.timeSincePlayerMoving < 0.05f)
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Helper class that allows me to limit how often a patch is run!
    /// </summary>
    /// <remarks>
    /// I should probably move this into its own file
    /// </remarks>
    internal class UpdateLimiter
    {
        private float nextUpdateCheck;
        private float updateInterval;

        internal UpdateLimiter(float updateInterval = 0.5f)
        {
            this.updateInterval = updateInterval;
            this.Invalidate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanUpdate()
        {
            return nextUpdateCheck >= updateInterval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invalidate()
        {
            nextUpdateCheck = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            nextUpdateCheck += deltaTime;
        }
    }
}
