using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using Unity.Netcode;

namespace LethalBots.Patches.GameEnginePatches
{
    /// <summary>
    /// Patch for <c>GameNetworkManager</c>
    /// </summary>
    [HarmonyPatch(typeof(GameNetworkManager))]
    public class GameNetworkManagerPatch
    {
        /// <summary>
        /// Patch to intercept when saving base game, save our also plugin 
        /// </summary>
        [HarmonyPatch("SaveGame")]
        [HarmonyPostfix]
        public static void SaveGame_Postfix(GameNetworkManager __instance)
        {
            SaveManager.Instance.SavePluginInfos();

            // DISABLE-ANTI-SAVESCUM CODE!
            // This is for TESTING purposes only. I have to save and reload files A TON.
            //if (!StartOfRound.Instance.inShipPhase)
            //{
            //    if (StartOfRound.Instance.connectedPlayersAmount <= 0)
            //    {
            //        return;
            //    }
            //    StartOfRound startOfRound = UnityEngine.Object.FindObjectOfType<StartOfRound>();
            //    UnityEngine.Object.FindObjectOfType<MoldSpreadManager>();
            //    int num = ES3.Load($"Level{startOfRound.currentLevel.levelID}TimesSavescumming", __instance.currentSaveFileName, 0);
            //    ES3.Save($"Level{StartOfRound.Instance.currentLevel.levelID}TimesSavescumming", 0, __instance.currentSaveFileName); // SET THE DAMN THING BACK TO 0!
            //    if (startOfRound != null && num >= 4 && StartOfRound.Instance.currentLevel.canSpawnMold)
            //    {
            //        if (startOfRound.currentLevel.moldSpreadIterations > 0 && startOfRound.currentLevel.moldSpreadIterations <= 5)
            //        {
            //            startOfRound.currentLevel.moldSpreadIterations -= 5;
            //        }
            //        else
            //        {
            //            startOfRound.currentLevel.moldSpreadIterations--;
            //        }
            //        ES3.Save($"Level{startOfRound.currentLevel.levelID}Mold", startOfRound.currentLevel.moldSpreadIterations, __instance.currentSaveFileName);
            //        ES3.Save($"Level{startOfRound.currentLevel.levelID}MoldOrigin", startOfRound.currentLevel.moldStartPosition, __instance.currentSaveFileName);
            //    }
            //}
        }

        /// <summary>
        /// If a player leaves the game, have the bots swap their ownership back to the host!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="clientId"></param>
        [HarmonyPatch("Singleton_OnClientDisconnectCallback")]
        [HarmonyPrefix]
        static void Singleton_OnClientDisconnectCallback_Prefix(GameNetworkManager __instance, ulong clientId)
        {
            // Don't do this if the host disconnects
            if (NetworkManager.Singleton == null 
                || clientId == NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            // Only do this on the server
            if (NetworkManager.Singleton.IsServer)
            {
                // Update the player counts!
                LethalBotManager.Instance.sendPlayerCountUpdate.Value = true;

                // Change the bot's ownership to the host
                int playerClientId = -1;
                if (StartOfRound.Instance.ClientPlayerList.TryGetValue(clientId, out var value))
                {
                    playerClientId = (int)StartOfRound.Instance.allPlayerScripts[value].playerClientId;
                }

                Terminal terminal = TerminalManager.Instance.GetTerminal();
                InteractTrigger terminalInteractTrigger = PatchesUtil.terminalTriggerField.Invoke(terminal);
                foreach (LethalBotAI lethalBotAI in LethalBotManager.Instance.GetLethalBotAIs())
                {
                    // Check to see if the bot is owned by the disconnecting client
                    if (lethalBotAI != null 
                        && (lethalBotAI.OwnerClientId == clientId 
                            || lethalBotAI.currentOwnershipOnThisClient == playerClientId))
                    {
                        // Change the ownership
                        lethalBotAI.ChangeOwnershipOfEnemy(NetworkManager.ServerClientId);

                        // To prevent desyncs, make the bot leave the terminal
                        if (lethalBotAI.NpcController.Npc.currentTriggerInAnimationWith == terminalInteractTrigger)
                        {
                            lethalBotAI.LeaveTerminal(syncTerminalInUse: true, forceEndUse: true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Patch to mark the lobby as full if there are no open <see cref="PlayerControllerB"/>s
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        [HarmonyPatch("ConnectionApproval")]
        [HarmonyPrefix]
        static bool ConnectionApproval_Prefix(GameNetworkManager __instance, 
                                            ref NetworkManager.ConnectionApprovalRequest request, 
                                            ref NetworkManager.ConnectionApprovalResponse response)
        {
            if (request.ClientNetworkId == NetworkManager.Singleton.LocalClientId)
            {
                return true;
            }
            if (__instance.disallowConnection)
            {
                return true;
            }

            int openPlayerSlot = LethalBotManager.Instance?.GetNextAvailablePlayerObject() ?? 0;
            if (openPlayerSlot < 0)
            {
                response.Reason = "Lobby is full!";
                response.CreatePlayerObject = false;
                response.Approved = false;
                response.Pending = false;
                return false;
            }

            return true;
        }
    }
}
