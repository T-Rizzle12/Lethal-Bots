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

        /// <summary>
        /// Used so we can manually register new enemies that spawn!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_Postfix(EnemyAI __instance)
        {
            if (__instance.IsServer && __instance is not LethalBotAI && !RoundManager.Instance.SpawnedEnemies.Contains(__instance))
                RoundManager.Instance.SpawnedEnemies.Add(__instance);
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
    }
}
