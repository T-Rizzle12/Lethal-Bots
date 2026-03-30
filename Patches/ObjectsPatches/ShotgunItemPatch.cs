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
using Vector3 = UnityEngine.Vector3;

namespace LethalBots.Patches.ObjectsPatches
{
    /// <summary>
    /// Patches for <c>ShotgunItem</c>
    /// </summary>
    [HarmonyPatch(typeof(ShotgunItem))]
    public class ShotgunItemPatch
    {
        [HarmonyPatch("ShootGunAndSync")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ShootGunAndSync_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Replacement field: this.previousPlayerHeldBy
            FieldInfo previousPlayerHeldByField = AccessTools.Field(typeof(ShotgunItem), "previousPlayerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch ShotgunItem.ShootGunAndSync!");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    // Replace with "this.previousPlayerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    codes[i].opcode = OpCodes.Ldarg_0;
                    codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Ldfld;
                    codes[i + 1].operand = previousPlayerHeldByField;
                    patched = true;
                    timesPatched++;
                }
            }
            
            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.ShotgunItem.ShootGunAndSync_Transpiler could not check if player local or bot local 1");
            }
            else
            {
                Plugin.LogDebug($"Patched out localPlayerController {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("ShootGun")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ShootGun_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            /*Plugin.LogDebug("Before patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // Replacement field: this.previousPlayerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // Lets us know that the patching has begun!
            Plugin.LogDebug("Beginning to patch ShotgunItem.ShootGun!");

            // ---------- Step 1: Replace 'playerHeldBy == GameNetworkManager.Instance.localPlayerController' ----------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Look for occurrences of "GameNetworkManager.Instance.localPlayerController"
                if (codes[i].Calls(getGameNetworkManagerInstance) 
                    && codes[i + 1].LoadsField(localPlayerControllerField) 
                    && codes[i + 2].Calls(opEqualityMethod))
                {
                    // Replace with "this.previousPlayerHeldBy"
                    Plugin.LogDebug($"Patching localPlayerController comparison at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]}, 2: {codes[i + 1]}, 3: {codes[i + 2]}");
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                // Remove the three instructions: GameNetworkManager.Instance, localPlayerController, and op_Equality
                codes.RemoveRange(startIndex, 3);

                // Insert the new instruction to call the replacement method.
                codes.Insert(startIndex, new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod)); // Call our replacement method
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.ShotgunItem.ShootGun_Transpiler could not find the localPlayerController reference!");
            }

            // ---------- Step 2: Modify 'flag = true;' logic ----------
            // We search for the sequence where flag is set to true:
            // The IL we're targeting is something like:
            //    ldc.i4.1
            //    stloc.s   X
            // We want to replace that with a conditional assignment:
            //    ldarg.0
            //    ldfld playerHeldByField
            //    call getGameNetworkManagerInstance
            //    ldsfld localPlayerControllerField
            //    ceq
            //    stloc.s   X
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // Look for flag assignment (flag = true;)
                if (codes[i].opcode == OpCodes.Ldc_I4_1 
                    && codes[i + 1].opcode == OpCodes.Stloc_0)
                {
                    // Replace with "this.previousPlayerHeldBy"
                    Plugin.LogDebug($"Patching flag assignment at index {i}...");
                    Plugin.LogDebug($"Original Codes: 1: {codes[i]} and 2: {codes[i + 1]}");
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                // Modify flag logic to be: flag = (playerHeldBy == GameNetworkManager.Instance.localPlayerController);
                var flagStoreInstruction = codes[startIndex + 1]; // Save the `stloc.s` instruction

                // Replace `ldc.i4.1` with new instructions
                List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // Load `this`
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod), // Compare playerHeldBy == localPlayerController
                    flagStoreInstruction // Store the result (this replaces the old `stloc.s`)
                };

                // Replace the existing two instructions (`ldc.i4.1` and `stloc.s`) with our new ones
                codes.RemoveRange(startIndex, 2);
                codes.InsertRange(startIndex, codesToReplace);
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectPatches.ShotgunItem.ShootGun_Transpiler could not change the flag!");
            }

            Plugin.LogDebug("Finished patching ShotgunItem.ShootGun!");

            /*Plugin.LogDebug("After patching IL instructions:");
            foreach (var instruction in codes)
            {
                Plugin.LogDebug(instruction.ToString());
            }*/

            return codes.AsEnumerable();
        }

        [HarmonyPatch("ItemInteractLeftRight")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ItemInteractLeftRight_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // SetSafetyControlTip method
            MethodInfo setSafetyControlTipMethod = AccessTools.Method(typeof(ShotgunItem), "SetSafetyControlTip");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(setSafetyControlTipMethod))
                {
                    // Create our label's destination....
                    Label skipSnapshot = generator.DefineLabel();
                    var nop = new CodeInstruction(OpCodes.Nop);
                    nop.labels.Add(skipSnapshot);
                    codes.Insert(i + 2, nop); // Set label to the instruction **after** the ClearControlTips call

                    // Insert new method call to skip SetSafetyControlTip if a bot is holding the item
                    List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                        new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                        new CodeInstruction(OpCodes.Brtrue_S, skipSnapshot)
                    };

                    // Insert the new instruction to call the replacement method.
                    codes.InsertRange(i, codesToAdd);
                    i += codesToAdd.Count; // Move our index past the newly added codes!
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.ShotgunItem.ItemInteractLeftRight_Transpiler could not check if player local for Crouch");
            }
            else
            {
                Plugin.LogDebug($"Patched out SetSafetyControlTip calls for bots in Shotgun code {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("SetControlTipsForItem")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool SetControlTipsForItem_PreFix(ShotgunItem __instance)
        {
            return !LethalBotManager.Instance.IsAnLethalBotAiOwnerOfObject(__instance);
        }

        [HarmonyPatch("SetSafetyControlTip")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool SetSafetyControlTip_PreFix(ShotgunItem __instance)
        {
            return !LethalBotManager.Instance.IsAnLethalBotAiOwnerOfObject(__instance);
        }

        /// <summary>
        /// Patch to make the shotgun able to damage/kill bots, held by players or enemies
        /// </summary>
        [HarmonyPatch("ShootGun")]
        [HarmonyPostfix]
        static void ShootGun_PostFix(ShotgunItem __instance,
                                     Vector3 shotgunPosition,
                                     Vector3 shotgunForward)
        {
            // NOTE: This is needed since the shotgun only checks for the local player!
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI? lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController == null || lethalBotController.isPlayerDead || !lethalBotController.isPlayerControlled)
                {
                    continue;
                }

                if (__instance.playerHeldBy == lethalBotController)
                {
                    continue;
                }

                int damage = 0;
                Vector3 lethalBotPos = lethalBotController.transform.position + new Vector3(0, 1f, 0);
                float distanceTarget = Vector3.Distance(lethalBotPos, __instance.shotgunRayPoint.transform.position);
                Vector3 contactPointTarget = lethalBotPos;
                if (Vector3.Angle(shotgunForward, contactPointTarget - shotgunPosition) < 30f
                    && !Physics.Linecast(shotgunPosition, contactPointTarget, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    if (distanceTarget < 5f)
                    {
                        damage = 100;
                    }
                    if (distanceTarget < 15f)
                    {
                        damage = 100;
                    }
                    else if (distanceTarget < 23f)
                    {
                        damage = 40;
                    }
                    else if (distanceTarget < 30f)
                    {
                        damage = 20;
                    }

                    lethalBotController.DamagePlayer(damage, hasDamageSFX: true, callRPC: true, CauseOfDeath.Gunshots, 0, false, __instance.shotgunRayPoint.forward * 30f);
                }
            }
        }
    }
}
