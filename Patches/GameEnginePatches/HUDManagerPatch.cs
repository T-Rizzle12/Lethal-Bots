using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LethalBots.Patches.GameEnginePatches
{
    /// <summary>
    /// Patch for the <c>HUDManager</c>
    /// </summary>
    [HarmonyPatch(typeof(HUDManager))]
    [HarmonyAfter(Const.BETTER_EXP_GUID)]
    public class HUDManagerPatch
    {
        #region Reverse patches

        [HarmonyPatch("DisplayGlobalNotification")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void DisplayGlobalNotification_ReversePatch(object instance, string displayText) => throw new NotImplementedException("Stub LethalBot.Patches.GameEnginePatches.HUDManagerPatch.DisplayGlobalNotification_ReversePatch");

        [HarmonyPatch("AddPlayerChatMessageServerRpc")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void AddPlayerChatMessageServerRpc_ReversePatch(object instance, string chatMessage, int playerId) => throw new NotImplementedException("Stub LethalBot.Patches.GameEnginePatches.HUDManagerPatch.AddPlayerChatMessageServerRpc_ReversePatch");

        #endregion

        /// <summary>
        /// A postfix made to update the speaker icons for bots!
        /// </summary>
        [HarmonyPatch("UpdateSpectateBoxSpeakerIcons")]
        [HarmonyPostfix]
        public static void UpdateSpectateBoxSpeakerIcons_Postfix(HUDManager __instance, ref Dictionary<Animator, PlayerControllerB> ___spectatingPlayerBoxes)
        {
            for (int i = 0; i < ___spectatingPlayerBoxes.Count; i++)
            {
                // Base game logic.
                var playerBox = ___spectatingPlayerBoxes.ElementAt(i);
                PlayerControllerB value = playerBox.Value;
                if (!value.isPlayerControlled && !value.isPlayerDead)
                {
                    continue;
                }

                // Only do this for bots, the base game handles human players.
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(value);
                if (lethalBotAI == null)
                {
                    continue;
                }

                // Sanity check, make sure our voice is valid.
                LethalBotVoice? voice = lethalBotAI.LethalBotIdentity?.Voice;
                if (voice == null)
                {
                    continue;
                }

                // Just like the base game, check if the bot is speaking loud enough.
                bool isSpeaking = false;
                const float minimumVoiceAmplitude = 0.005f;
                if (voice.IsTalking() && voice.GetVoiceAmplitude() > minimumVoiceAmplitude)
                {
                    isSpeaking = true;
                }

                playerBox.Key.SetBool("speaking", isSpeaking);
            }
        }

        [HarmonyPatch("UseSignalTranslatorClientRpc")]
        [HarmonyPostfix]
        public static void UseSignalTranslatorClientRpc_Postfix(HUDManager __instance, string signalMessage)
        {
            LethalBotManager.Instance.LethalBotsRespondToSignalTranslator(signalMessage);
        }

        [HarmonyPatch("AddPlayerChatMessageClientRpc")]
        [HarmonyPostfix]
        public static void AddPlayerChatMessageClientRpc_Postfix(HUDManager __instance, string chatMessage, int playerId)
        {
            // Grandpa, why don't we use AddTextToChatOnServer or AddChatMessage?
            // Well you see Timmy, AddTextToChatOnServer is too early and is called for all types of messages
            // and AddChatMessage would only let us hear messages if the local player could hear them!
            LethalBotManager.Instance.LethalBotsRespondToChatMessage(chatMessage, playerId);
        }

        /// <summary>
        /// A prefix made to fix errors caused when <see cref="HUDManager.FillImageWithSteamProfile"/> is called for bots.
        /// </summary>
        /// <remarks>
        /// This could be modified later down the line to accept custom Profile Pictures for bots!
        /// </remarks>
        /// <param name="__instance"></param>
        /// <param name="image"></param>
        /// <param name="steamId"></param>
        /// <param name="large"></param>
        /// <returns></returns>
        [HarmonyPatch("FillImageWithSteamProfile")]
        [HarmonyPrefix]
        public static bool FillImageWithSteamProfile_Prefix(HUDManager __instance, ref RawImage image, ref SteamId steamId, ref bool large)
        {
            if (!steamId.IsValid)
            {
                Plugin.LogWarning($"FillImageWithSteamProfile: Invaild steam id {steamId} or steam id is a bot. Aboring FillImageWithSteamProfile to prevent errors.");
                return false;
            }
            return true;
        }

        [HarmonyPatch("ChangeControlTipMultiple")]
        [HarmonyPostfix]
        public static void ChangeControlTipMultiple_Postfix(HUDManager __instance)
        {
            InputManager.Instance.AddLethalBotsControlTip(__instance);
        }

        [HarmonyPatch("ClearControlTips")]
        [HarmonyPostfix]
        public static void ClearControlTips_Postfix(HUDManager __instance)
        {
            InputManager.Instance.AddLethalBotsControlTip(__instance);
        }

        [HarmonyPatch("ChangeControlTip")]
        [HarmonyPostfix]
        public static void ChangeControlTip_Postfix(HUDManager __instance)
        {
            InputManager.Instance.AddLethalBotsControlTip(__instance);
        }
    }
}
