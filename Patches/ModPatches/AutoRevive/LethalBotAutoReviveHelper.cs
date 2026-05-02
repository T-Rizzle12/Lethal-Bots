using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using TooManyEmotes.Compatibility;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.AutoRevive
{
    public class LethalBotAutoReviveHelper
    {
        internal static Dictionary<PlayerControllerB, AutoReviveHandler> autoReviveHandlers = new Dictionary<PlayerControllerB, AutoReviveHandler>();

        /// <summary>
        /// Helper method to get the AutoReviveHandler for a given player. If one doesn't exist, it will create a new one and store it in the dictionary.
        /// </summary>
        /// <param name="player">The player for whom to get the AutoReviveHandler.</param>
        /// <returns>The AutoReviveHandler for the specified player.</returns>
        public static AutoReviveHandler GetAutoReviveHandler(PlayerControllerB player)
        {
            if (!autoReviveHandlers.TryGetValue(player, out var handler))
            {
                handler = new AutoReviveHandler(player);
                autoReviveHandlers[player] = handler;
            }
            return handler;
        }

        /// <summary>
        /// Removes the AutoReviveHandler associated with the specified player.
        /// </summary>
        /// <param name="player">The player whose AutoReviveHandler should be removed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveAutoReviveHandler(PlayerControllerB player)
        {
            autoReviveHandlers.Remove(player);
        }

        /// <summary>
        /// Simple helper that clears the auto revive handlers
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearAutoReviveHandlers()
        {
            autoReviveHandlers.Clear();
        }

        #region Patches

        /// <summary>
        /// A prefix made to block the default logic for bots
        /// </summary>
        /// <param name="__0"></param>
        /// <returns></returns>
        public static bool KillPlayerPostfix_Prefix(PlayerControllerB __0)
        {
            if (__0.IsOwner && __0.isPlayerDead && __0.AllowPlayerDeath())
            {
                if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
                {
                    AutoReviveHandler autoReviveHandler = GetAutoReviveHandler(__0);
                    autoReviveHandler.StartPlayerReviveCountDown();
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Helper that resets the revive handlers for all bots at the end of the round.
        /// </summary>
        public static void ReviveDeadPlayersPostfix_Postfix()
        {
            // Stop all revive coroutines at the end of the round!
            foreach (var handler in autoReviveHandlers.Values)
            {
                if (handler.reviveCoroutine != null)
                {
                    handler.playerController.StopCoroutine(handler.reviveCoroutine);
                    handler.reviveCoroutine = null;
                }
            }

            // Purge the revive handlers at the end of the round!
            ClearAutoReviveHandlers();
        }

        /// <summary>
        /// Helper that calls <see cref="AutoReviveHandler.ShipLeave"/> for all bots when the ship leaves.
        /// </summary>
        public static void ShipLeavePostfix_Postfix(StartOfRound __0)
        {
            // Call the ShipLeave method for all handlers if the ship actually leaves.
            if (__0.shipIsLeaving)
            {
                foreach (var handler in autoReviveHandlers.Values)
                {
                    handler.ShipLeave();
                }
            }
        }

        /// <summary>
        /// A transpiler made to replace the player count check to include bots as well!
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        public static IEnumerable<CodeInstruction> CheckIfAllPlayersDead_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo connectedPlayersField = AccessTools.Field(typeof(GameNetworkManager), "connectedPlayers");

            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) 
                    && codes[i + 1].LoadsField(connectedPlayersField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // We want to replace the call to GameNetworkManager.Instance.connectedPlayers with StartOfRound.Instance.connectedPlayersAmount.
                // This is because GameNetworkManager.Instance.connectedPlayers only contains human players
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = AccessTools.Method(typeof(LethalBotAutoReviveHelper), nameof(GetNumberOfConnectedPlayers));
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ModPatches.AutoRevive.LethalBotAutoReviveHelper.CheckIfAllPlayersDead_Transpiler could not locate the target instructions for replacement.");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Helper function that returns the number of connected players, including bots.
        /// </summary>
        /// <returns></returns>
        private static int GetNumberOfConnectedPlayers()
        {
            return StartOfRound.Instance.connectedPlayersAmount + 1; // This doesn't include the host, so we add 1 to account for that!
        }

        #endregion

        #region Lethal Bots Auto Revive

        /// <summary>
        /// A helper class that manages the auto revive logic for bots.
        /// </summary>
        /// <remarks>
        /// This is a carbon copy of HUDHandler class from the mod itself, just adjusted for bot use.
        /// </remarks>
        public class AutoReviveHandler
        {
            public bool canRevive = false;
            public bool isRunning = false;
            public bool isPermaDead = false;
            public int reviveCount = 0;

            public PlayerControllerB playerController;
            public Coroutine? reviveCoroutine = null;

            public AutoReviveHandler(PlayerControllerB playerController)
            {
                this.playerController = playerController;
            }

            ~AutoReviveHandler()
            {
                if (playerController != null && reviveCoroutine != null)
                {
                    playerController.StopCoroutine(reviveCoroutine);
                }
            }

            public void OnPlayerDC()
            {
                if (reviveCoroutine != null)
                {
                    playerController.StopCoroutine(reviveCoroutine);
                    reviveCoroutine = null;
                }

                if (playerController.disconnectedMidGame && NetworkManager.Singleton.IsServer)
                {
                    // Sadly, the NetworkHandler class is internal, so we have to use AccessTools to call the DisconnectPermaDeadPlayer
                    try
                    {
                        var type = AccessTools.TypeByName("LCAutoRevive.Network.NetworkHandler");
                        if (type != null)
                        {
                            var method = AccessTools.PropertyGetter(type, "Instance");
                            if (method != null)
                            {
                                object instance = method.Invoke(null, null);
                                MethodInfo disconnectPermaDeadPlayer = AccessTools.Method(type, "DisconnectPermaDeadPlayer");
                                if (disconnectPermaDeadPlayer != null)
                                {
                                    disconnectPermaDeadPlayer.Invoke(instance, new object[] { (int)playerController.actualClientId });
                                    Plugin.LogDebug("Successfully invoked disconnectPermaDeadPlayer via AccessTools.");
                                }
                                else
                                {
                                    Plugin.LogWarning("Could not find disconnectPermaDeadPlayer field.");
                                }
                            }
                            else
                            {
                                Plugin.LogWarning("Could not find Instance property.");
                            }
                        }
                        else
                        {
                            Plugin.LogWarning("Could not find NetworkHandler type from Auto Revive.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogError($"Error while trying to DisconnectPermaDead bot using Auto Revive: {ex}");
                    }
                }
            }

            public void OnActionPreformed()
            {
                FieldInfo reviveLimitField = AccessTools.Field(typeof(LCAutoRevive.LCAutoRevive), "reviveLimit");
                int reviveLimit = (int)reviveLimitField.GetValue(null);
                if (StartOfRound.Instance.shipIsLeaving || StartOfRound.Instance.inShipPhase || !canRevive || (reviveLimit > 0 && reviveCount >= reviveLimit))
                {
                    return;
                }

                // Sadly, the NetworkHandler class is internal, so we have to use AccessTools to call the RevivePlayerServerRpc
                try
                {
                    var type = AccessTools.TypeByName("LCAutoRevive.Network.NetworkHandler");
                    if (type != null)
                    {
                        var method = AccessTools.PropertyGetter(type, "Instance");
                        if (method != null)
                        {
                            object instance = method.Invoke(null, null);
                            MethodInfo revivePlayerServerRpc = AccessTools.Method(type, "RevivePlayerServerRpc");
                            if (revivePlayerServerRpc != null)
                            {
                                revivePlayerServerRpc.Invoke(instance, new object[] { (int)playerController.playerClientId });
                                canRevive = false;
                                reviveCount++;
                                Plugin.LogDebug("Successfully invoked revivePlayerServerRpc via AccessTools.");
                            }
                            else
                            {
                                Plugin.LogWarning("Could not find revivePlayerServerRpc field.");
                            }
                        }
                        else
                        {
                            Plugin.LogWarning("Could not find Instance property.");
                        }
                    }
                    else
                    {
                        Plugin.LogWarning("Could not find NetworkHandler type from Auto Revive.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"Error while trying to respawn bot using Auto Revive: {ex}");
                }
            }

            public void StartPlayerReviveCountDown()
            {
                if (isRunning || isPermaDead)
                {
                    return;
                }
                canRevive = false;

                FieldInfo reviveLimitField = AccessTools.Field(typeof(LCAutoRevive.LCAutoRevive), "reviveLimit");
                int reviveLimit = (int)reviveLimitField.GetValue(null);
                if (reviveCount >= reviveLimit && reviveLimit > 0)
                {
                    // Sadly, the NetworkHandler class is internal, so we have to use AccessTools to call the PermaDeadPlayerServerRpc
                    try
                    {
                        var type = AccessTools.TypeByName("LCAutoRevive.Network.NetworkHandler");
                        if (type != null)
                        {
                            var method = AccessTools.PropertyGetter(type, "Instance");
                            if (method != null)
                            {
                                object instance = method.Invoke(null, null);
                                MethodInfo permaDeadPlayerServerRpc = AccessTools.Method(type, "PermaDeadPlayerServerRpc");
                                if (permaDeadPlayerServerRpc != null)
                                {
                                    permaDeadPlayerServerRpc.Invoke(instance, new object[] { (int)playerController.playerClientId });
                                    isPermaDead = true;
                                    Plugin.LogDebug("Successfully invoked permaDeadPlayerServerRpc via AccessTools.");
                                }
                                else
                                {
                                    Plugin.LogWarning("Could not find permaDeadPlayerServerRpc field.");
                                }
                            }
                            else
                            {
                                Plugin.LogWarning("Could not find Instance property.");
                            }
                        }
                        else
                        {
                            Plugin.LogWarning("Could not find NetworkHandler type from Auto Revive.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogError($"Error while trying to mark bot as permadead using Auto Revive: {ex}");
                    }
                }
                else
                {
                    if (reviveCoroutine != null)
                    {
                        playerController.StopCoroutine(reviveCoroutine);
                    }
                    reviveCoroutine = playerController.StartCoroutine(WaitForPlayerRevival());
                }
            }

            private IEnumerator WaitForPlayerRevival()
            {
                FieldInfo reviveDelayPenaltyField = AccessTools.Field(typeof(LCAutoRevive.LCAutoRevive), "reviveDelayPenalty");
                FieldInfo reviveDelayField = AccessTools.Field(typeof(LCAutoRevive.LCAutoRevive), "reviveDelay");
                float reviveDelayPenalty = (float)reviveDelayPenaltyField.GetValue(null);
                float reviveDelay = (float)reviveDelayField.GetValue(null);

                isRunning = true;
                float timeLeft = ((reviveDelayPenalty >= 0f) ? (reviveDelay + reviveDelayPenalty * (float)reviveCount) : reviveDelay);
                float interval = 0.1f;
                while (timeLeft >= 0f)
                {
                    yield return new WaitForEndOfFrame();
                    if (interval > 0f)
                    {
                        interval -= Time.deltaTime;
                        continue;
                    }
                    if (StartOfRound.Instance.shipIsLeaving || StartOfRound.Instance.inShipPhase)
                    {
                        isRunning = false;
                        reviveCount = 0;
                        reviveCoroutine = null;
                        yield break;
                    }
                    interval = 0.1f;
                    timeLeft -= 0.1f + (interval - 0.1f) + Time.deltaTime;
                }
                canRevive = true;
                // NOTE: We don't care about InputUtilsCompat.Enabled, bots will always choose to revive themselves if they can!
                if (!StartOfRound.Instance.shipIsLeaving && !StartOfRound.Instance.inShipPhase)
                {
                    // Sadly, the NetworkHandler class is internal, so we have to use AccessTools to call the RevivePlayerServerRpc
                    try
                    {
                        var type = AccessTools.TypeByName("LCAutoRevive.Network.NetworkHandler");
                        if (type != null)
                        {
                            var method = AccessTools.PropertyGetter(type, "Instance");
                            if (method != null)
                            {
                                object instance = method.Invoke(null, null);
                                MethodInfo revivePlayerServerRpc = AccessTools.Method(type, "RevivePlayerServerRpc");
                                if (revivePlayerServerRpc != null)
                                {
                                    revivePlayerServerRpc.Invoke(instance, new object[] { (int)playerController.playerClientId });
                                    canRevive = false;
                                    reviveCount++;
                                    Plugin.LogDebug("Successfully invoked revivePlayerServerRpc via AccessTools.");
                                }
                                else
                                {
                                    Plugin.LogWarning("Could not find revivePlayerServerRpc field.");
                                }
                            }
                            else
                            {
                                Plugin.LogWarning("Could not find Instance property.");
                            }
                        }
                        else
                        {
                            Plugin.LogWarning("Could not find NetworkHandler type from Auto Revive.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogError($"Error while trying to respawn bot using Auto Revive: {ex}");
                    }
                }
                reviveCoroutine = null;
                isRunning = false;
            }

            public void ShipLeave()
            {
                canRevive = false;
                reviveCount = 0;
            }
        }

        #endregion
    }
}
