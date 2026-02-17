using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalBots.Patches.NpcPatches
{
    /// <summary>
    /// Patch for the lethalBotAI
    /// </summary>
    [HarmonyPatch(typeof(EnemyAI))]
    public class EnemyAIPatch
    {
        /// <summary>
        /// Patch for intercepting when ownership of an enemy changes.<br/>
        /// Only change ownership to a irl player, if new owner is lethalBot then new owner is the owner (real player) of the lethalBot
        /// </summary>
        /// <param name="newOwnerClientId"></param>
        /// <returns></returns>
        [HarmonyPatch("ChangeOwnershipOfEnemy")]
        [HarmonyPrefix]
        static bool ChangeOwnershipOfEnemy_PreFix(ref ulong newOwnerClientId)
        {
            Plugin.LogDebug($"[PREFIX]: Try ChangeOwnershipOfEnemy newOwnerClientId : {(int)newOwnerClientId}");
            if (newOwnerClientId > Const.LETHAL_BOT_ACTUAL_ID_OFFSET)
            {
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)(newOwnerClientId - Const.LETHAL_BOT_ACTUAL_ID_OFFSET));
                if (lethalBotAI == null)
                {
                    Plugin.LogDebug($"Could not find lethalBot with id : {(int)(newOwnerClientId - Const.LETHAL_BOT_ACTUAL_ID_OFFSET)}, aborting ChangeOwnershipOfEnemy.");
                    return false;
                }

                Plugin.LogDebug($"ChangeOwnershipOfEnemy not on lethalBot but on lethalBot owner : {lethalBotAI.OwnerClientId}");
                newOwnerClientId = lethalBotAI.OwnerClientId;
            }

            return true;
        }

        /// <summary>
        /// Patch for intercepting when ownership of an bot changes.<br/>
        /// This allows us to change the items' ownership to the lethalBot owner instead of the lethalBot itself
        /// </summary>
        /// <param name="newOwnerClientId"></param>
        /// <returns></returns>
        [HarmonyPatch("ChangeOwnershipOfEnemy")]
        [HarmonyPostfix]
        static void ChangeOwnershipOfEnemy_PostFix(EnemyAI __instance, ulong newOwnerClientId)
        {
            LethalBotAI? lethalBotAI = __instance as LethalBotAI;
            if (lethalBotAI != null)
            {
                Plugin.LogDebug($"[POSTFIX]: Try ChangeOwnershipOfEnemy for lethalBot newOwnerClientId : {(int)newOwnerClientId}");
                lethalBotAI.ChangeOwnershipOfBotInventoryServerRpc(newOwnerClientId);
                lethalBotAI.ChangeNpcOwnershipOfBotServerRpc(newOwnerClientId);
                if (Plugin.IsModLethalPhonesLoaded)
                {
                    lethalBotAI.ChangeOwnershipOfLethalPhoneServerRpc(newOwnerClientId);
                }
            }
        }

        [HarmonyPatch("HitEnemyOnLocalClient")]
        [HarmonyPrefix]
        static bool HitEnemyOnLocalClient(EnemyAI __instance)
        {
            if (__instance is LethalBotAI)
            {
                return false;
            }

            return true;
        }

        #region Transpilers

        /// <summary>
        /// Patch for making the enemy able to detect an lethalBot when colliding
        /// </summary>
        /// <param name="instructions"></param>
        /// <returns></returns>
        [HarmonyPatch("MeetsStandardPlayerCollisionConditions")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> MeetsStandardPlayerCollisionConditions_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // bypass "component != GameNetworkManager.Instance.localPlayerController" if player is an lethalBot
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            for (var i = 0; i < codes.Count - 8; i++)
            {
                if (codes[i].opcode == OpCodes.Brtrue
                    && codes[i + 1].opcode == OpCodes.Ldloc_0
                    && codes[i + 2].opcode == OpCodes.Call
                    && codes[i + 3].opcode == OpCodes.Ldfld
                    && codes[i + 4].opcode == OpCodes.Call
                    && codes[i + 8].opcode == OpCodes.Ldarg_0)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex > -1)
            {
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsColliderFromLocalOrLethalBotOwnerLocalMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, codes[startIndex + 8].labels.First()/*IL_0051*/)
                };
                codes.InsertRange(startIndex + 1, codesToAdd);
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.NpcPatches.EnemyAIPatch.MeetsStandardPlayerCollisionConditions_Transpiler could not insert instruction if is lethalBot for \"component != GameNetworkManager.Instance.localPlayerController\".");
            }

            return codes.AsEnumerable();
        }

        #endregion

        #region Post Fixes

        /// <summary>
        /// Patch for making the enemy check lethalBot too when calling <c>CheckLineOfSightForPlayer</c>
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <param name="width"></param>
        /// <param name="range"></param>
        /// <param name="proximityAwareness"></param>
        //[HarmonyPatch("CheckLineOfSightForPlayer")]
        //[HarmonyPostfix]
        //static void CheckLineOfSightForPlayer_PostFix(EnemyAI __instance, ref PlayerControllerB __result, float width, ref int range, int proximityAwareness)
        //{
        //    PlayerControllerB lethalBotControllerFound = null!;

        //    if (__instance.isOutside && !__instance.enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
        //    {
        //        range = Mathf.Clamp(range, 0, 30);
        //    }

        //    // FIXME: Is this still needed, this code only checks for bots, but still checks all players.....
        //    for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        //    {
        //        PlayerControllerB lethalBotController = StartOfRound.Instance.allPlayerScripts[i];
        //        if (!__instance.PlayerIsTargetable(lethalBotController) 
        //            || !LethalBotManager.Instance.IsPlayerLethalBot(lethalBotController))
        //        {
        //            continue;
        //        }

        //        Vector3 position = lethalBotController.gameplayCamera.transform.position;
        //        if (Vector3.Distance(position, __instance.eye.position) < (float)range && !Physics.Linecast(__instance.eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        //        {
        //            Vector3 to = position - __instance.eye.position;
        //            if (Vector3.Angle(__instance.eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(__instance.eye.position, position) < (float)proximityAwareness))
        //            {
        //                lethalBotControllerFound = lethalBotController;
        //            }
        //        }
        //    }

        //    if (__result == null && lethalBotControllerFound == null)
        //    {
        //        return;
        //    }
        //    else if (__result == null && lethalBotControllerFound != null)
        //    {
        //        Plugin.LogDebug("bot found, no player found");
        //        __result = lethalBotControllerFound;
        //        return;
        //    }
        //    else if (__result != null && lethalBotControllerFound == null)
        //    {
        //        Plugin.LogDebug("bot not found, player found");
        //        return;
        //    }
        //    else
        //    {
        //        if (__result == null || lethalBotControllerFound == null) return;
        //        Vector3 playerPosition = __result.gameplayCamera.transform.position;
        //        Vector3 lethalBotPosition = lethalBotControllerFound.gameplayCamera.transform.position;
        //        Vector3 aiEnemyPosition = __instance.eye == null ? __instance.transform.position : __instance.eye.position;
        //        if ((lethalBotPosition - aiEnemyPosition).sqrMagnitude < (playerPosition - aiEnemyPosition).sqrMagnitude)
        //        {
        //            Plugin.LogDebug("lethalBot closer");
        //            __result = lethalBotControllerFound;
        //        }
        //        else { Plugin.LogDebug("player closer"); }
        //    }
        //}

        /// <summary>
        /// Patch for making the enemy check lethalBot too when calling <c>GetClosestPlayer</c>
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <param name="requireLineOfSight"></param>
        /// <param name="cannotBeInShip"></param>
        /// <param name="cannotBeNearShip"></param>
        /// NOTE: This isn't needed since the bots only use player slots!
        /*[HarmonyPatch("GetClosestPlayer")]
        [HarmonyPostfix]
        static void GetClosestPlayer_PostFix(EnemyAI __instance, ref PlayerControllerB __result, bool requireLineOfSight, bool cannotBeInShip, bool cannotBeNearShip)
        {
            PlayerControllerB lethalBotControllerFound = null!;

            // FIXME: Is this still needed, this code only checks for bots, but still checks all players.....
            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB lethalBotController = StartOfRound.Instance.allPlayerScripts[i];

                if (!__instance.PlayerIsTargetable(lethalBotController, cannotBeInShip, false) 
                    || !LethalBotManager.Instance.IsPlayerLethalBot(lethalBotController))
                {
                    continue;
                }

                if (cannotBeNearShip)
                {
                    if (lethalBotController.isInElevator)
                    {
                        continue;
                    }
                    bool flag = false;
                    for (int j = 0; j < RoundManager.Instance.spawnDenialPoints.Length; j++)
                    {
                        if (Vector3.Distance(RoundManager.Instance.spawnDenialPoints[j].transform.position, lethalBotController.transform.position) < 10f)
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        continue;
                    }
                }
                if (!requireLineOfSight || !Physics.Linecast(__instance.transform.position, lethalBotController.transform.position, 256))
                {
                    __instance.tempDist = Vector3.Distance(__instance.transform.position, lethalBotController.transform.position);
                    if (__instance.tempDist < __instance.mostOptimalDistance)
                    {
                        __instance.mostOptimalDistance = __instance.tempDist;
                        lethalBotControllerFound = lethalBotController;
                    }
                }
            }

            if (__result == null && lethalBotControllerFound == null)
            {
                return;
            }
            else if (__result == null && lethalBotControllerFound != null)
            {
                __result = lethalBotControllerFound;
                return;
            }
            else if (__result != null && lethalBotControllerFound == null)
            {
                return;
            }
            else
            {
                if (__result == null || lethalBotControllerFound == null) return;
                Vector3 playerPosition = __result.gameplayCamera.transform.position;
                Vector3 lethalBotPosition = lethalBotControllerFound.gameplayCamera.transform.position;
                Vector3 aiEnemyPosition = __instance.eye == null ? __instance.transform.position : __instance.eye.position;
                if ((lethalBotPosition - aiEnemyPosition).sqrMagnitude < (playerPosition - aiEnemyPosition).sqrMagnitude)
                {
                    __result = lethalBotControllerFound;
                }
            }
        }*/

        /// <summary>
        /// Patch for making the enemy check lethalBot too when calling <c>TargetClosestPlayer</c>
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        /// <param name="bufferDistance"></param>
        /// <param name="requireLineOfSight"></param>
        /// <param name="viewWidth"></param>
        /// NEEDTOVAILIDATE: This addon changes the number of players "connected" so I do wonder if this is fixed?
        //[HarmonyPatch("TargetClosestPlayer")]
        //[HarmonyPostfix]
        //static void TargetClosestPlayer_PostFix(EnemyAI __instance, ref bool __result, float bufferDistance, bool requireLineOfSight, float viewWidth)
        //{
        //    PlayerControllerB playerTargetted = __instance.targetPlayer;

        //    // FIXME: Is this still needed, this code only checks for bots, but still checks all player indexes.....
        //    for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        //    {
        //        PlayerControllerB lethalBot = StartOfRound.Instance.allPlayerScripts[i];
        //        if (!LethalBotManager.Instance.IsPlayerLethalBot(lethalBot) 
        //            || playerTargetted == lethalBot)
        //        {
        //            continue;
        //        }

        //        if (__instance.PlayerIsTargetable(lethalBot, false, false)
        //            && !__instance.PathIsIntersectedByLineOfSight(lethalBot.transform.position, false, false)
        //            && (!requireLineOfSight || __instance.CheckLineOfSightForPosition(lethalBot.gameplayCamera.transform.position, viewWidth, 40, -1f, null)))
        //        {
        //            __instance.tempDist = Vector3.Distance(__instance.transform.position, lethalBot.transform.position);
        //            if (__instance.tempDist < __instance.mostOptimalDistance)
        //            {
        //                __instance.mostOptimalDistance = __instance.tempDist;
        //                __instance.targetPlayer = lethalBot;
        //            }
        //        }
        //    }
        //    if (__instance.targetPlayer != null && bufferDistance > 0f && playerTargetted != null
        //        && Mathf.Abs(__instance.mostOptimalDistance - Vector3.Distance(__instance.transform.position, playerTargetted.transform.position)) < bufferDistance)
        //    {
        //        __instance.targetPlayer = playerTargetted;
        //    }
        //    __result = __instance.targetPlayer != null;
        //}

        #endregion
    }
}
