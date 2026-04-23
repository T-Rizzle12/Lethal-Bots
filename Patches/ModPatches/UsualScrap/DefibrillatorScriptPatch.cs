using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalLib.Modules;
using UnityEngine;
using UsualScrap.Behaviors;

namespace LethalBots.Patches.ModPatches.UsualScrap
{
    [HarmonyPatch(typeof(DefibrillatorScript))]
    public class DefibrillatorScriptPatch
    {
        [HarmonyPatch("RevivePlayer")]
        [HarmonyPrefix]
        static bool RevivePlayer_Prefix(DefibrillatorScript __instance, 
                                        ref int PlayerID, 
                                        ref Vector3 SpawnPosition,
                                        ref bool ___UsesLimited,
                                        ref int ___useLimit,
                                        ref Renderer[] ___displayRenderers)
        {
            // Check if the player index is valid
            if (PlayerID < 0)
            {
                Plugin.LogInfo("US - No player inital id? returning..");
                return true;
            }

            // Run the default logic for human players
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(PlayerID);
            if (lethalBotAI == null)
            {
                return true;
            }

            // Check if the player is actually dead
            PlayerControllerB playerControllerB = RoundManager.Instance.playersManager.allPlayerScripts[PlayerID];
            if (!playerControllerB.isPlayerDead)
            {
                return false;
            }

            // This is called by an rpc for all players,
            // only run this on the host!
            if (!__instance.IsServer)
            {
                return false;
            }

            LethalBotIdentity lethalBotIdentity = lethalBotAI.LethalBotIdentity;
            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                Hp = 5,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = SpawnPosition,
                                                                YRot = 0,
                                                                IsOutside = SpawnPosition.y >= -80f,
                                                                IndexNextPlayerObject = PlayerID
                                                            });
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is spawned
            StartOfRound.Instance.livingPlayers++;

            // Thanks to Harmony, I can access these private variables here!
            if (___UsesLimited && ___useLimit > 0)
            {
                ___useLimit--;
                if (___useLimit <= 0)
                {
                    foreach (Renderer display in ___displayRenderers)
                    {
                        display.material.SetColor("_EmissiveColor", Color.red);
                    }
                }
            }

            // Block the orignal call for the server, we handled the revive code here!
            return false;
        }
    }
}
