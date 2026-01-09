using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using ModelReplacement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
            //Plugin.LogInfo($"SetPlayerModelReplacement_Prefix called for {player.playerUsername}! IsBot: {LethalBotManager.Instance.IsPlayerLethalBot(player)}");
            if (LethalBotManager.Instance.IsPlayerLethalBot(player))
            {
                if (!type.IsSubclassOf(typeof(BodyReplacementBase)))
                {
                    Plugin.LogError("Cannot set body replacement of type " + type.Name + ", must inherit from BodyReplacementBase");
                }
                else
                {
                    BodyReplacementBase component;
                    bool flag = player.gameObject.TryGetComponent<BodyReplacementBase>(out component);
                    int currentSuitID = player.currentSuitID;
                    string unlockableName = StartOfRound.Instance.unlockablesList.unlockables[currentSuitID].unlockableName;
                    if (flag)
                    {
                        if (component.GetType() == type && component.suitName == unlockableName)
                        {
                            return false;
                        }
                        Plugin.LogInfo($"Model Replacement Change detected {component.GetType()} => {type}, changing model.");
                        component.IsActive = false;
                        UnityEngine.Object.Destroy(component);
                    }
                    Plugin.LogInfo($"Suit Change detected {component?.suitName} => {unlockableName}, Replacing {type}.");
                    #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                    BodyReplacementBase bodyReplacementBase = player.gameObject.AddComponent(type) as BodyReplacementBase;
                    #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                    bodyReplacementBase!.suitName = unlockableName;
                }
                return false;
            }
            return true;
        }

        //[HarmonyPatch("SetPlayerModelReplacement")]
        //[HarmonyPostfix]
        //static void SetPlayerModelReplacement_Postfix(PlayerControllerB player)
        //{
        //    Plugin.LogInfo($"SetPlayerModelReplacement_Postfix called for {player.playerUsername}! IsBot: {LethalBotManager.Instance.IsPlayerLethalBot(player)}");
        //    return;
        //}

        //[HarmonyPatch("SetPlayerModelReplacement")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> SetPlayerModelReplacement_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    for (var i = 0; i < codes.Count - 1; i++)
        //    {
        //        if (codes[i].ToString() == "call virtual void ModelReplacement.Monobehaviors.ManagerBase::ReportBodyReplacementRemoval()"
        //            && codes[i + 1].ToString() == "call NULL")
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex + 1].opcode = OpCodes.Call;
        //        codes[startIndex + 1].operand = SymbolExtensions.GetMethodInfo(() => new ViewStateManager().UpdateModelReplacement());
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogInfo($"LethalBot.Patches.ModPatches.ModelRplcmntAPI.ModelReplacementAPIPatch.FixOpenBodyCamTranspilerRemovePlayerModelReplacement_Transpiler, could not find call null line, ignoring fix.");
        //    }

        //    return codes.AsEnumerable();
        //}

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

        //[HarmonyPatch("RemovePlayerModelReplacement")]
        //[HarmonyPrefix]
        //static bool RemovePlayerModelReplacement_Prefix(PlayerControllerB player)
        //{
        //    Plugin.LogInfo($"RemovePlayerModelReplacement_Prefix called for {player.playerUsername}! IsBot: {LethalBotManager.Instance.IsPlayerLethalBot(player)}");
        //    if (LethalBotManager.Instance.IsPlayerLethalBot(player))
        //    {
        //        return false;
        //    }
        //    return true;
        //}

        //[HarmonyPatch("RemovePlayerModelReplacement")]
        //[HarmonyPostfix]
        //static void RemovePlayerModelReplacement_Postfix(PlayerControllerB player)
        //{
        //    Plugin.LogInfo($"RemovePlayerModelReplacement_Postfix called for {player.playerUsername}! IsBot: {LethalBotManager.Instance.IsPlayerLethalBot(player)}");
        //    return;
        //}
    }
}