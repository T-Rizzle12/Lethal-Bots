using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using ModelReplacement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Object = UnityEngine.Object;

namespace LethalBots.Patches.ModPatches.ModelRplcmntAPI
{
    [HarmonyPatch(typeof(ModelReplacementAPI))]
    public class ModelReplacementAPIPatch
    {
        [HarmonyPatch("SetPlayerModelReplacement")]
        [HarmonyPrefix]
        static bool SetPlayerModelReplacement_Prefix(PlayerControllerB player, Type type)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)player.playerClientId);
            if (lethalBotAI == null)
            {
                return true;
            }

            if (!type.IsSubclassOf(typeof(BodyReplacementBase)))
            {
                return true;
            }

            int currentSuitID = player.currentSuitID;
            string unlockableName = StartOfRound.Instance.unlockablesList.unlockables[currentSuitID].unlockableName;

            string suitNameToReplace = string.Empty;
            bool shouldAddNewBodyReplacement = true;
            IBodyReplacementBase[] bodiesReplacementBase = lethalBotAI.ListModelReplacement.ToArray();
            //Plugin.LogDebug($"{player.playerUsername} SetPlayerModelReplacement bodiesReplacementBase.Length {bodiesReplacementBase.Length}");
            foreach (IBodyReplacementBase bodyReplacementBase in bodiesReplacementBase)
            {
                if (LethalBotManager.Instance.ListBodyReplacementOnDeadBodies.Contains(bodyReplacementBase))
                {
                    continue;
                }

                if (bodyReplacementBase.TypeReplacement == type
                    && bodyReplacementBase.SuitName == unlockableName)
                {
                    shouldAddNewBodyReplacement = false;
                }
                else
                {
                    Plugin.LogInfo($"Patch LethalBot, bot {player.playerUsername}, Model Replacement change detected {bodyReplacementBase.GetType()} => {type}, changing model.");
                    suitNameToReplace = bodyReplacementBase.SuitName;
                    lethalBotAI.ListModelReplacement.Remove(bodyReplacementBase);
                    bodyReplacementBase.IsActive = false;
                    UnityEngine.Object.Destroy((Object)bodyReplacementBase.BodyReplacementBase);
                    shouldAddNewBodyReplacement = true;
                }
            }

            if (shouldAddNewBodyReplacement && 
                !lethalBotAI.NpcController.Npc.isPlayerDead && 
                lethalBotAI.NpcController.Npc.isPlayerControlled)
            {
                Plugin.LogInfo($"Patch LethalBot, bot {player.playerUsername}, Suit Change detected {suitNameToReplace} => {currentSuitID} {unlockableName}, Replacing {type}.");
                BodyReplacementAdapter bodyReplacementBaseToAdd = new BodyReplacementAdapter(player.gameObject.AddComponent(type));
                bodyReplacementBaseToAdd.SuitName = unlockableName;
                lethalBotAI.ListModelReplacement.Add(bodyReplacementBaseToAdd);
            }

            return false;
        }

        [HarmonyPatch("RemovePlayerModelReplacement")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> FixOpenBodyCamTranspilerRemovePlayerModelReplacement_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].ToString() == "call virtual void ModelReplacement.Monobehaviors.ManagerBase::ReportBodyReplacementRemoval()"
                    && codes[i + 1].ToString() == "call NULL")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = SymbolExtensions.GetMethodInfo(() => new ViewStateManager().UpdateModelReplacement());
                startIndex = -1;
            }
            else
            {
                Plugin.LogInfo($"LethalBot.Patches.ModPatches.ModelRplcmntAPI.ModelReplacementAPIPatch.FixOpenBodyCamTranspilerRemovePlayerModelReplacement_Transpiler, could not find call null line, ignoring fix.");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("RemovePlayerModelReplacement")]
        [HarmonyPrefix]
        static bool RemovePlayerModelReplacement_Prefix(PlayerControllerB player)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(player);
            if (lethalBotAI == null)
            {
                return true;
            }

            IBodyReplacementBase[] bodiesReplacementBase = lethalBotAI.ListModelReplacement.ToArray();
            //Plugin.LogDebug($"RemovePlayerModelReplacement bodiesReplacementBase.Length {bodiesReplacementBase.Length}");
            foreach (IBodyReplacementBase bodyReplacementBase in bodiesReplacementBase)
            {
                if (LethalBotManager.Instance.ListBodyReplacementOnDeadBodies.Contains(bodyReplacementBase))
                {
                    continue;
                }

                lethalBotAI.ListModelReplacement.Remove(bodyReplacementBase);
                bodyReplacementBase.IsActive = false;
                UnityEngine.Object.Destroy((Object)bodyReplacementBase.BodyReplacementBase);
            }

            return false;
        }
    }
}
