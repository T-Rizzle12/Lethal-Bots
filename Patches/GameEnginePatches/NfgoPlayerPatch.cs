using Dissonance.Integrations.Unity_NFGO;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(NfgoPlayer))]
    public class NfgoPlayerPatch
    {
        /// <summary>
        /// A prefix made to stop VoiceChatTrackingStart from being called on Lethal Bots, preventing errors.
        /// </summary>
        /// <remarks>
        /// This has to be done since bots don't have their own client and piggyback off the host's client for voice chat.
        /// This results in a bug where the host's voice chat can be heard coming from the bot's position rather than the host's position.
        /// </remarks>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("VoiceChatTrackingStart")]
        [HarmonyPrefix]
        public static bool VoiceChatTrackingStart_Prefix(NfgoPlayer __instance)
        {
            PlayerControllerB? playerController = __instance.gameObject.GetComponent<PlayerControllerB>();
            if (LethalBotManager.Instance.IsPlayerLethalBot(playerController))
            {
                Plugin.LogWarning("[NfgoPlayerPatch] VoiceChatTrackingStart was called on a Bot. Aboring VoiceChatTrackingStart to prevent errors.");
                return false;
            }
            return true;
        }
    }
}
