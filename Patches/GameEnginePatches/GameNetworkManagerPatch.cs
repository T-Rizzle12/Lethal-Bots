using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Managers;
using Unity.Netcode;
using GameNetcodeStuff;

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
        public static void SaveGame_Postfix()
        {
            SaveManager.Instance.SavePluginInfos();
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
