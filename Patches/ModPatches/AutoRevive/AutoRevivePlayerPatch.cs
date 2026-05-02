using GameNetcodeStuff;
using HarmonyLib;
using LCAutoRevive.Utils;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.GameEnginePatches;
using LethalLib.Modules;
using OPJosMod.ReviveCompany;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.AutoRevive
{
    [HarmonyPatch(typeof(RevivePlayer))]
    public class AutoRevivePlayerPatch
    {
        [HarmonyPatch("ReiveDeadPlayer")]
        [HarmonyPrefix]
        static bool ReiveDeadPlayer_Prefix(int playerId)
        {
            // If the player being revived is not a bot, do nothing and let the mod handle it.
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(playerId);
            if (lethalBotAI == null)
            {
                return true;
            }

            // Identity and body are not sync, need to find the identity to revive not the body
            PlayerControllerB? playerToRevive = StartOfRound.Instance.allPlayerScripts[playerId];
            if (playerToRevive == null)
            {
                Plugin.LogError($"Auto Revive with LethalBot: error when trying to revive bot, could not find player controller.");
                return false;
            }

            LethalBotIdentity? lethalBotIdentity = lethalBotAI.LethalBotIdentity;
            if (lethalBotIdentity == null)
            {
                return true;
            }

            // Get the same logic as the mod at the beginning
            if (lethalBotIdentity.Alive)
            {
                Plugin.LogError($"Auto Revive with LethalBot: error when trying to revive bot \"{lethalBotIdentity.Name}\", bot is already alive! do nothing more");
                return false;
            }

            // This is called by an rpc for all players,
            // only run this on the host!
            if (!NetworkManager.Singleton.IsServer)
            {
                return false;
            }

            // Respawn bot
            int playerClientId = (int)playerToRevive.playerClientId;
            Plugin.LogDebug($"Reviving bot {lethalBotIdentity.Name}");
            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(StartOfRound.Instance, playerClientId, simpleTeleport: false),
                                                                YRot = 0,
                                                                IsOutside = true,
                                                                IndexNextPlayerObject = playerClientId
                                                            });
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is respawned
            StartOfRound.Instance.livingPlayers++;
            return false;
        }
    }
}
