using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using Unity.Netcode;
using UnityEngine;

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

            #if DEBUG
            // DISABLE-ANTI-SAVESCUM CODE!
            // This is for TESTING purposes only. I have to save and reload files A TON.
            if (!StartOfRound.Instance.inShipPhase)
            {
                if (StartOfRound.Instance.connectedPlayersAmount <= 0)
                {
                    return;
                }
                StartOfRound startOfRound = UnityEngine.Object.FindObjectOfType<StartOfRound>();
                UnityEngine.Object.FindObjectOfType<MoldSpreadManager>();
                int num = ES3.Load($"Level{startOfRound.currentLevel.levelID}TimesSavescumming", __instance.currentSaveFileName, 0);
                ES3.Save($"Level{StartOfRound.Instance.currentLevel.levelID}TimesSavescumming", 0, __instance.currentSaveFileName); // SET THE DAMN THING BACK TO 0!
                if (startOfRound != null && num >= 4 && StartOfRound.Instance.currentLevel.canSpawnMold)
                {
                    if (startOfRound.currentLevel.moldSpreadIterations > 0 && startOfRound.currentLevel.moldSpreadIterations <= 5)
                    {
                        startOfRound.currentLevel.moldSpreadIterations -= 5;
                    }
                    else
                    {
                        startOfRound.currentLevel.moldSpreadIterations--;
                    }
                    ES3.Save($"Level{startOfRound.currentLevel.levelID}Mold", startOfRound.currentLevel.moldSpreadIterations, __instance.currentSaveFileName);
                    ES3.Save($"Level{startOfRound.currentLevel.levelID}MoldOrigin", startOfRound.currentLevel.moldStartPosition, __instance.currentSaveFileName);
                }
            }
            #endif
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
                LethalBotManager.Instance.EndHumanJoin(clientId);

                // Change the bot's ownership to the host
                int playerClientId = -1;
                if (StartOfRound.Instance.ClientPlayerList.TryGetValue(clientId, out var value))
                {
                    playerClientId = (int)StartOfRound.Instance.allPlayerScripts[value].playerClientId;
                }

                Terminal terminal = TerminalManager.Instance.GetTerminal();
                InteractTrigger terminalInteractTrigger = terminal.terminalTrigger;
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
        /// If a player joins, tell the quota manager to wait for them to fully connect before being allowed to spawn bots again.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="clientId"></param>
        [HarmonyPatch("Singleton_OnClientConnectedCallback")]
        [HarmonyPrefix]
        static void Singleton_OnClientConnectedCallback_Prefix(GameNetworkManager __instance, ulong clientId)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsServer)
            { 
                LethalBotManager.Instance.BeginHumanJoin(clientId); 
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
        [HarmonyPriority(Priority.First)]
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

            // Since multiple players joining the game at the same time can break stuff,
            // we need to block players from joining while bots are joining a the same time.
            LethalBotManager instanceLBM = LethalBotManager.Instance;
            bool areBotsJoining = instanceLBM != null && (instanceLBM.isSpawningBots.Value || instanceLBM.sendPlayerCountUpdate.Value);
            if (areBotsJoining)
            {
                response.Reason = "Bot was connecting! \n Try again in a few seconds.";
                response.CreatePlayerObject = false;
                response.Approved = false;
                response.Pending = false;
                return false;
            }

            // Since bots are not considered as a connected client,
            // we need to enforce lobby size ourself.
            int openPlayerSlot = instanceLBM != null ? instanceLBM.GetNextAvailablePlayerObject() : 0;
            //Plugin.LogInfo($"OpenPlayer Slot {openPlayerSlot}");
            if (openPlayerSlot < 0)
            {
                StartOfRound instanceSOR = StartOfRound.Instance;
                int connectedPlayersAmount = instanceSOR.connectedPlayersAmount + 1; // Include host player in this count
                int connectedBotAmount = connectedPlayersAmount - instanceLBM!.AllRealPlayersCount; // Number of bots on the server
                //Plugin.LogInfo($"Connected player amount {connectedPlayersAmount} \n Connected Bot Amount {connectedBotAmount}");
                if (connectedBotAmount > 0)
                {
                    // Go through every spawned bot
                    bool kickedBot = false;
                    LethalBotAI[] lethalBotAIs = instanceLBM.GetLethalBotAIs();
                    for (int i = 0; i < lethalBotAIs.Length; i++)
                    {
                        // Check if this slot is taken by a bot
                        LethalBotAI? lethalBotAI = lethalBotAIs[i];
                        if (lethalBotAI != null)
                        {
                            // Make sure this bot is properly spawned
                            PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                            if (lethalBotController != null)
                            {
                                // Kick the bot from the server
                                // NEEDTOVALIDATE: Should I just have the bot, "disconnect," instead?
                                Plugin.LogDebug($"[GameNetworkManager] Kicking bot {lethalBotController.playerUsername}. Making room for joining player!");
                                StartOfRound.Instance.KickPlayer((int)lethalBotController.playerClientId);
                                kickedBot = true;
                                break; // Only need to kick one
                            }
                        }
                    }

                    // Check if we kicked a bot
                    if (kickedBot)
                    {
                        instanceLBM.maintainQuotaTimer.Start(1.0f); // Force restart the timer
                        LethalBotManager.Instance.BeginHumanJoin(request.ClientNetworkId);
                    }
                    // If we somehow fail, block the connection
                    else
                    {
                        response.Reason = "Lobby is full!";
                        response.CreatePlayerObject = false;
                        response.Approved = false;
                        response.Pending = false;
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
