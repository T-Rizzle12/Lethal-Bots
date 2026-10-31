using Dawn.Internal;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.DawnLib
{
    [HarmonyPatch(typeof(DawnDungeonNetworker))]
    public class DawnDungeonNetworkerPatch
    {
        private static Coroutine? markBotsReadyCoroutine = null;

        [HarmonyPatch("QueueDungeonBundleLoading")]
        [HarmonyPostfix]
        static void QueueDungeonBundleLoading_Postfix(DawnDungeonNetworker __instance)
        {
            // Only the host should update the bots!
            if (__instance.IsServer || __instance.IsHost)
            {
                if (markBotsReadyCoroutine != null)
                {
                    __instance.StopCoroutine(markBotsReadyCoroutine);
                }
                markBotsReadyCoroutine = __instance.StartCoroutine(MarkBotsAsReadyCoroutine(__instance));
            }
        }

        /// <summary>
        /// Helper function that marks the bots as ready for DawnLib
        /// </summary>
        public static void MarkBotsAsReadyDawnLib(NetworkBehaviour dawnDungeonNetworker)
        {
            // So you may ask, why not just directly call the function.
            // I need to make sure I don't spam PlayerSetBundleStateRpc calls, since that could break stuff.
            DawnDungeonNetworker dawnDungeonInstance = (DawnDungeonNetworker)dawnDungeonNetworker;
            Dictionary<PlayerControllerB, DawnMoonNetworker.BundleState> playerStates = dawnDungeonInstance._playerStates;

            // Tell DawnLib that our bots are READY TO RUMBLE!
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotAIs();
            foreach (LethalBotAI lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                if (lethalBotController != null
                    && (lethalBotController.isPlayerControlled
                    || lethalBotController.isPlayerDead)
                    && playerStates.ContainsKey(lethalBotController) // Only do this if the bot is in the dictionary
                    && playerStates[lethalBotController] != DawnMoonNetworker.BundleState.Done) // Since rpc postfixes can be called multiple times, make sure we only send an rpc as needed
                {
                    // This will network to all players that the bot is "ready"
                    dawnDungeonInstance.PlayerSetBundleStateRpc(lethalBotController, DawnMoonNetworker.BundleState.Done);
                }
            }
        }

        /// <summary>
        /// A helper coroutine to make sure we only run MarkBotsAsReadyDawnLib once!
        /// </summary>
        /// <param name="dawnDungeonNetworker"></param>
        /// <returns></returns>
        private static IEnumerator MarkBotsAsReadyCoroutine(NetworkBehaviour dawnDungeonNetworker)
        {
            // TODO: Something to think about, it would be cool to give bots a fake loading delay!
            yield return null;
            //yield return new WaitForSeconds(5f);
            MarkBotsAsReadyDawnLib(dawnDungeonNetworker);
            markBotsReadyCoroutine = null;
        }
    }
}
