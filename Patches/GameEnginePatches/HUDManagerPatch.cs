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

        /*[HarmonyPatch("FillEndGameStats")]
        [HarmonyPrefix]
        static void FillEndGameStats_PreFix(HUDManager __instance)
        {
            // Why hudmanager statsUIElements gets to length 0 on client ?
            if (LethalBotManager.Instance.AllEntitiesCount == 0
                || __instance.statsUIElements.playerNamesText.Length < LethalBotManager.Instance.AllEntitiesCount)
            {
                ResizeStatsUIElements(__instance);
            }
        }*/

        /// <summary>
        /// Patch for making the hud only show end games stats for irl players, not bots
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        /*[HarmonyPatch("FillEndGameStats")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> FillEndGameStats_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 3; i++)
            {
                if (codes[i].ToString().StartsWith("ldarg.0 NULL") //170
                    && codes[i + 1].ToString() == "ldfld StartOfRound HUDManager::playersManager"
                    && codes[i + 2].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 3].ToString() == "ldlen NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Nop;
                codes[startIndex + 2].operand = null;
                codes[startIndex + 3].opcode = OpCodes.Call;
                codes[startIndex + 3].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.FillEndGameStats_Transpiler could not use irl number of player in list.");
            }

            return codes.AsEnumerable();
        }*/

        /*[HarmonyPatch("SyncAllPlayerLevelsServerRpc", new Type[] { })]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SyncAllPlayerLevelsServerRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static StartOfRound StartOfRound::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 2].ToString() == "ldlen NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.SyncAllPlayerLevelsServerRpc_Transpiler 1 could not use irl number of player in list.");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static StartOfRound StartOfRound::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 2].ToString() == "ldlen NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.SyncAllPlayerLevelsServerRpc_Transpiler 2 could not use irl number of player in list.");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("SyncAllPlayerLevelsClientRpc", new Type[] { typeof(int[]), typeof(bool[]) })]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SyncAllPlayerLevelsClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static StartOfRound StartOfRound::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 2].ToString() == "ldlen NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.SyncAllPlayerLevelsClientRpc_Transpiler could not use irl number of player in list.");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("UpdateBoxesSpectateUI")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> UpdateBoxesSpectateUI_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].ToString() == "call static StartOfRound StartOfRound::get_Instance()"
                    && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
                    && codes[i + 2].ToString() == "ldlen NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Nop;
                codes[startIndex + 1].operand = null;
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.UpdateBoxesSpectateUI_Transpiler could not use irl number of player for iteration.");
            }

            return codes.AsEnumerable();
        }*/

        /*[HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void Start_Postfix(HUDManager __instance)
        {
            ResizeStatsUIElements(__instance);
        }

        private static void ResizeStatsUIElements(HUDManager instance)
        {
            EndOfGameStatUIElements statsUIElements = instance.statsUIElements;
            GameObject gameObjectParent = statsUIElements.playerNamesText[0].gameObject.transform.parent.gameObject;

            int allEntitiesCount = LethalBotManager.Instance.AllEntitiesCount == 0 ? Plugin.PluginIrlPlayersCount : LethalBotManager.Instance.AllEntitiesCount;
            Array.Resize(ref statsUIElements.playerNamesText, allEntitiesCount);
            Array.Resize(ref statsUIElements.playerStates, allEntitiesCount);
            Array.Resize(ref statsUIElements.playerNotesText, allEntitiesCount);

            for (int i = LethalBotManager.Instance.IndexBeginOfInterns; i < allEntitiesCount; i++)
            {
                GameObject newGameObjectParent = Object.Instantiate<GameObject>(gameObjectParent);
                GameObject gameObjectPlayerName = newGameObjectParent.transform.Find("PlayerName1").gameObject;
                GameObject gameObjectNotes = newGameObjectParent.transform.Find("Notes").gameObject;
                GameObject gameObjectSymbol = newGameObjectParent.transform.Find("Symbol").gameObject;

                statsUIElements.playerNamesText[i] = gameObjectPlayerName.GetComponent<TextMeshProUGUI>();
                statsUIElements.playerNotesText[i] = gameObjectNotes.GetComponent<TextMeshProUGUI>();
                statsUIElements.playerStates[i] = gameObjectSymbol.GetComponent<Image>();
            }

            Plugin.LogDebug($"ResizeStatsUIElements {instance.statsUIElements.playerNamesText.Length}");
        }*/

        /*[HarmonyPatch("GetTextureFromImage")]
        [HarmonyPrefix]
        public static bool GetTextureFromImage_Prefix(HUDManager __instance, ref Steamworks.Data.Image? image) //ref Texture2D __result
        {
            // Check if the nullable image is null before the original method runs
            // We do this since the bots don't have steam accounts which can cause errors if we don't
            if (!image.HasValue)
            {
                Plugin.LogWarning("HUDManagerPatch found a null image for spectator HUD, aboring GetTextureFromImage to prevent errors!");
                return false;
            }

            // Return true to continue with the orignial method if image has value
            return true;
        }

        [HarmonyPatch("GetTextureFromImage")]
        [HarmonyPostfix]
        public static void GetTextureFromImage_Postfix(HUDManager __instance, ref Steamworks.Data.Image? image, ref Texture2D __result)
        {
            if (__result == null)
            {
                Plugin.LogWarning("The returned texture is null, applying fallback texture from HUD.");
                GameObject tempObject = UnityEngine.Object.Instantiate(__instance.spectatingPlayerBoxPrefab);
                tempObject.SetActive(value: true); // Turn it on real quick

                try
                {
                    RawImage defaultImage = tempObject.GetComponent<RawImage>();
                    if (defaultImage != null && defaultImage.texture != null)
                    {
                        // Check if the texture is a Texture2D 
                        // NEEDTOVALIDATE: Should I cache an object for this instead?
                        if (defaultImage.texture is Texture2D texture2D)
                        {
                            Texture2D fallbackTexture = new Texture2D(texture2D.width, texture2D.height);
                            UnityEngine.Color[] pixels = texture2D.GetPixels();
                            fallbackTexture.SetPixels(pixels);
                            fallbackTexture.Apply();

                            __result = fallbackTexture;
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.Destroy(tempObject);
                }
            }
        }*/

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
