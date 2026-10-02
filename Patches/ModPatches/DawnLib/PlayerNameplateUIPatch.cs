using Dawn;
using Dawn.Internal;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Patches.ModPatches.DawnLib
{
    [HarmonyPatch(typeof(PlayerNameplateUI))]
    public class PlayerNameplateUIPatch
    {
        [HarmonyPatch("Setup")]
        [HarmonyPrefix]
        public static bool Setup_Prefix(PlayerNameplateUI __instance, PlayerControllerB player)
        {
            // Just in case this somehow fails, we don't break everything
            try
            {
                // Grab the bot controller
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(player);
                if (lethalBotAI != null)
                {
                    // Grab our steam id
                    SteamId steamId = lethalBotAI.BotSteamID;
                    if (steamId.IsValid)
                    {
                        // Call HUDManager since our patched version should run
                        HUDManager.FillImageWithSteamProfile(__instance._image, steamId, true);
                        __instance._usernameText.text = player.playerUsername;
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogDebug($"Hiding error for `PlayerNameplateUI.Setup` because it's like a false positive.");
                Plugin.LogDebug($"Failed to set up player nameplate UI for player {player.playerUsername}: {e.Message}");
            }

            // Let the original run
            return true;
        }
    }
}
