using BetterEmote.AssetScripts;
using HarmonyLib;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UnityEngine.Rendering;

namespace LethalBots.Patches.ModPatches.BetterEmotes
{
    /// <summary>
    /// Patch for <c>CustomAnimationObjects</c>
    /// </summary>
    [HarmonyPatch(typeof(CustomAnimationObjects))]
    public class BetterEmotesCustomAnimationObjectsPatch
    {
        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // Simple job here, all we need to do is replace the IsOwner checks with IsLocalPlayer checks!
            var patched = false;
            var timesPatched = 0;
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // Methods and Fields to find
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            FieldInfo playerInstanceField = AccessTools.Field(typeof(CustomAnimationObjects), "playerInstance");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch CustomAnimationObjects.Update!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Look for occurrences of "IsOwner"
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(playerInstanceField)
                    && codes[i + 2].Calls(isOwnerGetter))
                {
                    // Replace the "this.playerInstance.IsOwner" with IsPlayerLocal(this.playerInstance)
                    Plugin.LogDebug($"Patching playerInstance.IsOwner check at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]}, 2: {codes[i + 1]}, and 3: {codes[i + 2]}");
                    codes[i + 2] = new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod);
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.BetterEmotes.Update_Transpiler could not change code to only run for local player.");
            }
            else
            {
                Plugin.LogDebug($"Patched out playerInstance.IsOwner {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DisableEverything")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DisableEverything_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // Simple job here, all we need to do is replace the IsOwner checks with IsLocalPlayer checks!
            var patched = false;
            var timesPatched = 0;
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            // Methods and Fields to find
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            FieldInfo playerInstanceField = AccessTools.Field(typeof(CustomAnimationObjects), "playerInstance");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch CustomAnimationObjects.DisableEverything!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Look for occurrences of "IsOwner"
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(playerInstanceField)
                    && codes[i + 2].Calls(isOwnerGetter))
                {
                    // Replace the "this.playerInstance.IsOwner" with IsPlayerLocal(this.playerInstance)
                    Plugin.LogDebug($"Patching playerInstance.IsOwner check at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]}, 2: {codes[i + 1]}, and 3: {codes[i + 2]}");
                    codes[i + 2] = new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod);
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.BetterEmotes.DisableEverything_Transpiler could not change code to only run for local player.");
            }
            else
            {
                Plugin.LogDebug($"Patched out playerInstance.IsOwner {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }
    }
}
