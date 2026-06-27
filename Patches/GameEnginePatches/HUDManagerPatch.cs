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
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Unity.Netcode.NetworkBehaviour;
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
        [HarmonyPriority(Priority.Last)]
        [Obsolete]
        public static void AddPlayerChatMessageClientRpc_Postfix(HUDManager __instance, string chatMessage, int playerId)
        {
            // Grandpa, why don't we use AddTextToChatOnServer or AddChatMessage?
            // Well you see Timmy, AddTextToChatOnServer is too early and is called for all types of messages
            // and AddChatMessage would only let us hear messages if the local player could hear them!
            if (Plugin.Config.UseOldChatRecevier)
            {
                LethalBotManager.Instance.LethalBotsRespondToChatMessage(chatMessage, playerId);
            }
        }

        [HarmonyPatch("AddPlayerChatMessageClientRpc")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> AddPlayerChatMessageClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            FieldInfo allPlayerScriptsField = AccessTools.Field(typeof(StartOfRound), "allPlayerScripts");
            FieldInfo playersManagerField = AccessTools.Field(typeof(HUDManager), "playersManager");
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");
            FieldInfo isPlayerDeadField = AccessTools.Field(typeof(PlayerControllerB), "isPlayerDead");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 8; i++)
            {
                // if (playersManager.allPlayerScripts[playerId].isPlayerDead == GameNetworkManager.Instance.localPlayerController.isPlayerDead)
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(playersManagerField)
                    && codes[i + 2].LoadsField(allPlayerScriptsField)
                    && codes[i + 3].IsLdarg(2)
                    && codes[i + 4].opcode == OpCodes.Ldelem_Ref
                    && codes[i + 5].LoadsField(isPlayerDeadField)
                    && codes[i + 6].Calls(getGameNetworkManagerInstance)
                    && codes[i + 7].LoadsField(localPlayerControllerField)
                    && codes[i + 8].LoadsField(isPlayerDeadField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Add in our chat command handler to the mix
                // We make sure to have it run before the base game runs its code
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HUDManagerPatch), nameof(ChatMessageHandler)))
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBots.Patches.GameEnginePatches.AddPlayerChatMessageClientRpc_Transpiler failed to introduce chat command handler!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Helper function for use in <see cref="AddPlayerChatMessageClientRpc_Transpiler(IEnumerable{CodeInstruction}, ILGenerator)"/>
        /// </summary>
        /// <param name="chatMessage"></param>
        /// <param name="playerId"></param>
        private static void ChatMessageHandler(string chatMessage, int playerId)
        {
            if (Plugin.Config.UseOldChatRecevier) return;

            // Try-catch here, just in case this errors out so the base game logic still gets to run!
            try
            {
                // Grandpa, why don't we use AddTextToChatOnServer or AddChatMessage?
                // Well you see Timmy, AddTextToChatOnServer is too early and is called for all types of messages
                // and AddChatMessage would only let us hear messages if the local player could hear them!
                //Plugin.LogInfo($"Sending Chat Message {chatMessage} send by player with ID {playerId} to all bots!");
                LethalBotManager.Instance.LethalBotsRespondToChatMessage(chatMessage, playerId);
            }
            catch (Exception e)
            {
                Plugin.LogError($"An Error Occurred when trying to relay chat message, {chatMessage}, to bots. Error: {e}");
            }
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
