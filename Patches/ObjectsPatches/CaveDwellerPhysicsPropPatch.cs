using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static Unity.Netcode.NetworkBehaviour;

namespace LethalBots.Patches.ObjectsPatches
{
    /// <summary>
    /// Patches for <see cref="CaveDwellerPhysicsProp"/>
    /// </summary>
    [HarmonyPatch(typeof(CaveDwellerPhysicsProp))]
    public class CaveDwellerPhysicsPropPatch
    {
        /// <summary>
        /// Used so we can manually register new items that spawn!
        /// </summary>
        /// <remarks>
        /// Grandpa, don't we already have this logic in the <see cref="GrabbableObjectPatch"/>?
        /// Well you see Timmy, the <see cref="CaveDwellerPhysicsProp"/> uses a custom start method
        /// that doesn't call the base class Start. So, we need to manually patch it here! 
        /// </remarks>
        /// <param name="__instance"></param>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_Postfix(CaveDwellerPhysicsProp __instance)
        {
            LethalBotManager.Instance.GrabbableObjectSpawned(__instance);
        }

        /// <summary>
        /// Makes the rocking animation respect the fear level for bots!
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        static bool Update_Prefix(CaveDwellerPhysicsProp __instance)
        {
            PlayerControllerB playerHeldBy = __instance.playerHeldBy;
            CaveDwellerAI caveDwellerScript = __instance.caveDwellerScript;
            if (caveDwellerScript == null || playerHeldBy == null)
            {
                return true; // No need to continue if the script or player is not set
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(playerHeldBy);
            if (__instance.isHeld && lethalBotAI != null && caveDwellerScript.rockingBaby > 0)
            {
                float fearLevel = lethalBotAI.FearLevel.Value;
                if (caveDwellerScript.rockingBaby < 2 && fearLevel > 0.75f)
                {
                    caveDwellerScript.rockingBaby = 2;
                    playerHeldBy.playerBodyAnimator.SetInteger("RockBaby", 2);
                    __instance.SetRockingBabyServerRpc(rockHard: true);
                }
                else if (fearLevel < 0.6f && caveDwellerScript.rockingBaby > 2)
                {
                    caveDwellerScript.rockingBaby = 1;
                    playerHeldBy.playerBodyAnimator.SetInteger("RockBaby", 1);
                    __instance.SetRockingBabyServerRpc(rockHard: false);
                }
            }

            return true;
        }

        /// <summary>
        /// Prefix to stop the orignial method from running if the bot that dropped it 
        /// was owned by the local client!
        /// </summary>
        /// <remarks>
        /// This is needed due to the transpiler <see cref="DiscardItem_Transpiler(IEnumerable{CodeInstruction}, ILGenerator)"/>
        /// editing the default value <see cref="GameNetworkManager.localPlayerController"/> to <see cref="GrabbableObject.playerHeldBy"/> holding the object
        /// so it could work properly with bots!
        /// </remarks>
        /// <param name="__instance"></param>
        /// <param name="playerId"></param>
        /// <returns></returns>
        [HarmonyPatch("DropBabyClientRpc")]
        [HarmonyPrefix]
        static bool DropBabyClientRpc_Prefix(CaveDwellerAI __instance, int playerId)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(playerId);
            if (lethalBotAI != null)
            {
                return false;
            }
            return true;
        }

        
        /// <summary>
        /// Patch to modify the <c>DiscardItem</c> method to use the <c>playerHeldBy</c> field instead of <c>GameNetworkManager.Instance.localPlayerController</c>
        /// </summary>
        /// <remarks>
        /// The original method uses <c>GameNetworkManager.Instance.localPlayerController</c> to get the player controller, 
        /// but this its not compatible with bots.
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("DiscardItem")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DiscardItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.previousPlayerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");
            FieldInfo bodyAnimatorField = AccessTools.Field(typeof(PlayerControllerB), "playerBodyAnimator");
            FieldInfo playerClientIdField = AccessTools.Field(typeof(PlayerControllerB), "playerClientId");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch CaveDwellerPhysicsProp.DiscardItem!");

            // ---------- Step 1: Replace 'GameNetworkManager.Instance.localPlayerController' for animations! ----------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 2].LoadsField(bodyAnimatorField))
                {
                    // Replace localPlayerController usage with playerHeldBy and playerBodyAnimator
                    Plugin.LogDebug($"Patching localPlayerController usage at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]}, 2: {codes[i + 1]}, 3: {codes[i + 2]}");
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                // Remove the three instructions: GameNetworkManager.Instance, localPlayerController, and playerBodyAnimator
                codes.RemoveRange(startIndex, 3);

                // Insert the new instructions to call the replacement method.
                List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // Load `this`
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Ldfld, bodyAnimatorField), // Load `playerBodyAnimator`
                };

                codes.InsertRange(startIndex, codesToReplace); // Call our replacement method
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.CaveDwellerPhysicsProp.DiscardItem_Transpiler could not find the localPlayerController reference for animations!");
            }

            // ---------- Step 2: Replace 'GameNetworkManager.Instance.localPlayerController' for RPC method ----------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 2].LoadsField(playerClientIdField))
                {
                    // Replace localPlayerController usage with playerHeldBy and playerBodyAnimator
                    Plugin.LogDebug($"Patching localPlayerController usage at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]}, 2: {codes[i + 1]}, 3: {codes[i + 2]}");
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                // Remove the three instructions: GameNetworkManager.Instance, localPlayerController, and playerClientId
                codes.RemoveRange(startIndex, 3);

                // Insert the new instructions to call the replacement method.
                List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // Load `this`
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Ldfld, playerClientIdField), // Load `playerClientIdField`
                };

                codes.InsertRange(startIndex, codesToReplace); // Call our replacement method
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.CaveDwellerPhysicsProp.DiscardItem_Transpiler could not find the localPlayerController reference for RPC!");
            }

            Plugin.LogDebug("Finished patching CaveDwellerPhysicsProp.DiscardItem!");

            return codes.AsEnumerable();
        }
    }
}
