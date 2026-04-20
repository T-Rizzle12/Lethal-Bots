using Dawn.Internal;
using Dawn.Utils;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.DawnLib
{
    [HarmonyPatch(typeof(DawnMoonNetworker))]
    public class DawnMoonNetworkerPatch
    {
        private static Coroutine? markBotsReadyCoroutine = null;

        /// <summary>
        /// Helper patch that marks the bots as ready when we are asked to queue the moon scene!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="____playerStates"></param>
        [HarmonyPatch("QueueMoonSceneLoadingClientRpc")]
        [HarmonyPostfix]
        static void QueueMoonSceneLoadingClientRpc_Postfix(DawnMoonNetworker __instance)
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
        public static void MarkBotsAsReadyDawnLib(NetworkBehaviour dawnMoonNetworker)
        {
            // So you may ask, why not just directly call the function.
            // I need to make sure I don't spam PlayerSetBundleStateServerRpc calls, since that could break stuff.
            DawnMoonNetworker dawnMoonInstance = (DawnMoonNetworker)dawnMoonNetworker;
            MethodInfo playerSetBundleStateServerRpcMethod = AccessTools.Method(typeof(DawnMoonNetworker), "PlayerSetBundleStateServerRpc"); // This rpc is private, I have to use reflection to call it
            FieldInfo _playerStatesField = AccessTools.Field(typeof(DawnMoonNetworker), "_playerStates"); // This field is private, makes sense, wish there was a way to query information from it though.
            Dictionary<PlayerControllerB, DawnMoonNetworker.BundleState> playerStates = (Dictionary<PlayerControllerB, DawnMoonNetworker.BundleState>)_playerStatesField.GetValue(dawnMoonInstance); // Cast reflected object into dictionary

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
                    playerSetBundleStateServerRpcMethod.Invoke(dawnMoonNetworker, new object[] { (PlayerControllerReference)lethalBotController, DawnMoonNetworker.BundleState.Done });
                }
            }
        }

        /// <summary>
        /// A helper coroutine to make sure we only run MarkBotsAsReadyDawnLib once!
        /// </summary>
        /// <param name="dawnMoonNetworker"></param>
        /// <returns></returns>
        private static IEnumerator MarkBotsAsReadyCoroutine(NetworkBehaviour dawnMoonNetworker)
        {
            // TODO: Something to think about, it would be cool to give bots a fake loading delay!
            yield return null;
            //yield return new WaitForSeconds(5f);
            MarkBotsAsReadyDawnLib(dawnMoonNetworker);
            markBotsReadyCoroutine = null;
        }
    }
}
