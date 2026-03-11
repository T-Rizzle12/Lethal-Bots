using HarmonyLib;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.EnemiesPatches
{
    /// <summary>
    /// Patches for the <c>DressGirlAI</c>
    /// </summary>
    /// <remarks>
    /// This was a PAIN todo, there was so much that must be changed in order to get this to be
    /// compatable with bots.
    /// </remarks>
    [HarmonyPatch(typeof(DressGirlAI))]
    public class DressGirlAIPatch
    {
        /// <summary>
        /// Simple patch for debugging purposes, lets me know when she spawns for testing purposes!
        /// </summary>
        /// <param name="__instance"></param>
        //[HarmonyPatch("Start")]
        //[HarmonyPostfix]
        //public static void Start_Postfix(DressGirlAI __instance)
        //{
        //    HUDManager.Instance.DisplayTip("Ghost Girl has spawned!", "Check logs to make sure everything is working as expected!");
        //}

        /// <summary>
        /// Patch Update to check for player and bot
        /// </summary>
        /// <remarks>
        /// The AI changes its owner to hauntingPlayer, but the default AI only checks __instance.hauntingPlayer != GameNetworkManager.Instance.localPlayerController.
        /// As a result, this causes the default AI to contantly try to change its owner if its target is a bot as the bot doesn't have a client.
        /// So this patch overrides it to check if the local player owns the bot to allow the default AI to run!
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");
            FieldInfo hauntingPlayerField = AccessTools.Field(typeof(DressGirlAI), "hauntingPlayer");
            FieldInfo heartbeatMusicField = AccessTools.Field(typeof(DressGirlAI), "heartbeatMusic");
            MethodInfo opInequalityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality");
            MethodInfo lerpMethod = AccessTools.Method(typeof(UnityEngine.Mathf), "Lerp");
            MethodInfo getSoundManagerInstance = AccessTools.PropertyGetter(typeof(SoundManager), "Instance");
            MethodInfo setDiageticMixerSnapshotMethod = AccessTools.Method(typeof(SoundManager), "SetDiageticMixerSnapshot");
            MethodInfo setVolumeMethod = AccessTools.PropertySetter(typeof(AudioSource), "volume");

            //Plugin.LogDebug("DressGirlAI listing Update IL:");
            //foreach (var instruction in codes)
            //{
            //    Plugin.LogDebug(instruction.ToString());
            //}

            // ------- Step 1: Fix the ghost girl spamming ownership changes when a bot is targeted -------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].Calls(getGameNetworkManagerInstance) &&
                    codes[i + 1].LoadsField(localPlayerControllerField) &&
                    codes[i + 2].IsLdarg(0) &&
                    codes[i + 3].LoadsField(hauntingPlayerField) &&
                    codes[i + 4].Calls(opInequalityMethod)) // Branch if not equal
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Save the branch instruction (brfalse.s)
                CodeInstruction branchInstruction = codes[startIndex + 5];
                Label targetLabel = (Label)branchInstruction.operand;

                // Save the label attached to GameNetworkManagerInstance
                List<Label> labels = codes[startIndex].ExtractLabels();

                // Remove old condition check (6 instructions)
                codes.RemoveRange(startIndex, 6);

                // Insert new method call for !IsPlayerLocalOrLethalBotOwnerLocal(hauntingPlayer)
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0).WithLabels(labels), // Load DressGirlAI instance
                    new CodeInstruction(OpCodes.Ldfld, hauntingPlayerField), // Load hauntingPlayer
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalOrLethalBotOwnerLocalMethod), // Call method
                    new CodeInstruction(OpCodes.Brtrue, targetLabel)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.DressGirlAIPatch.Update_Transpiler could not check if player local or bot local 1");
            }

            // ------- Step 2: Fix the ghost girl changing the DiageticMixer when a bot is targeted -------------------------
            for (var i = 0; i < codes.Count - 13; i++)
            {
                // Sigh, I wish IL was easier at times.......
                if (codes[i].Calls(getSoundManagerInstance) &&
                    codes[i + 1].opcode == OpCodes.Ldc_I4_1 &&
                    codes[i + 2].opcode == OpCodes.Ldc_R4 && codes[i + 2].operand is float f && f == 1f &&
                    codes[i + 3].Calls(setDiageticMixerSnapshotMethod) &&
                    codes[i + 4].IsLdarg(0) &&
                    codes[i + 5].LoadsField(heartbeatMusicField) &&
                    codes[i + 13].Calls(lerpMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            // I have been thinking about this and it may be better to insert a branch here and
            // go around the statement rather than edit the call itself.
            if (startIndex > -1)
            {
                // Insert a conditional branch (if hauntingPlayer != localPlayer skip calling SetDiageticMixerSnapshot)
                int endIndex = -1;
                for (int j = startIndex; j < codes.Count; j++)
                {
                    if (codes[j].Calls(setVolumeMethod))
                    {
                        endIndex = j;
                        break;
                    }
                }

                if (endIndex == -1)
                {
                    Plugin.LogError("Could not find heartbeat volume store!");
                    endIndex = startIndex + 14;
                }

                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(endIndex + 1, nop); // Set label to the instruction **after** the lerp call
                //codes[endIndex + 1].labels.Add(skipSnapshot); // Set label to the instruction **after** the lerp call

                // Remove old SetDiageticMixerSnapshot call
                //codes.RemoveRange(startIndex, 4);

                // Insert new method call for this.hauntingPlayer == GameNetworkManager.Instance.localPlayerController ? 1 : 0
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // DressGirlAI instance
                    new CodeInstruction(OpCodes.Ldfld, hauntingPlayerField), // DressGirlAI.hauntingPlayer
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod), // Call method
                    new CodeInstruction(OpCodes.Brfalse, skipSnapshot) // Branch if not equal (skip call)
                };

                // Insert our new instructions
                codes.InsertRange(startIndex, codesToAdd);
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.DressGirlAIPatch.Update_Transpiler could not find and replace sound blur check!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch Update to make ghost girl invisible if the player being haunted is a bot!
        /// This is basically the same code as the in the base AI, just modified to consider bots.
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(DressGirlAI __instance, ref bool ___enemyMeshEnabled)
        {
            // Don't need to run this code if we are not the owner as the base AI handles this!
            if (!__instance.IsOwner)
            {
                return;
            }

            // Only run this code if the player being haunted is not the local player!
            if (__instance.hauntingPlayer != GameNetworkManager.Instance.localPlayerController)
            {
                if (___enemyMeshEnabled == true)
                {
                    ___enemyMeshEnabled = false;
                    __instance.EnableEnemyMesh(enable: false, overrideDoNotSet: true);
                }

                // Make sure we are not playing any of our sounds
                // since we are not haunting the local player, but a bot!
                //SoundManager.Instance.SetDiageticMixerSnapshot(transitionTime: 0f); // There has to be a better way of doing this, maybe the transpiler?
                __instance.SFXVolumeLerpTo = 0f;
                __instance.creatureVoice.Stop();
                __instance.heartbeatMusic.volume = 0f;
                __instance.creatureSFX.volume = 0f;

                // HACKHACK: hauntingLocalPlayer is used to check for collisions, we need to set it to true so everything works as expected!
                if (!__instance.hauntingLocalPlayer 
                    && LethalBotManager.Instance.IsPlayerLocalOrLethalBotOwnerLocal(__instance.hauntingPlayer))
                {
                    __instance.hauntingLocalPlayer = true;
                }
                else if (__instance.hauntingLocalPlayer 
                         && !LethalBotManager.Instance.IsPlayerLocalOrLethalBotOwnerLocal(__instance.hauntingPlayer))
                {
                    __instance.hauntingLocalPlayer = false;
                }
            }
        }

        /// <summary>
        /// Patch to make the Ghost girl not play her sound effects if the player being haunted is a bot!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("SetHauntStarePosition")]
        [HarmonyPostfix]
        public static void SetHauntStarePosition_Postfix(DressGirlAI __instance, ref bool ___enemyMeshEnabled)
        {
            if (__instance.hauntingPlayer != GameNetworkManager.Instance.localPlayerController)
            {
                if (___enemyMeshEnabled == true)
                {
                    ___enemyMeshEnabled = false;
                    __instance.EnableEnemyMesh(enable: false, overrideDoNotSet: true);
                }

                __instance.SFXVolumeLerpTo = 0f;
                __instance.creatureSFX.volume = 0f;
                __instance.creatureVoice.Stop();
            }
        }

        [HarmonyPatch("SetHauntStarePosition")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SetHauntStarePosition_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            FieldInfo hauntingPlayerField = AccessTools.Field(typeof(DressGirlAI), "hauntingPlayer");
            FieldInfo SFXVolumeLerpToField = AccessTools.Field(typeof(DressGirlAI), "SFXVolumeLerpTo");
            MethodInfo playMethod = AccessTools.Method(typeof(UnityEngine.AudioSource), "Play");

            // ------- Step 1: Fix the ghost girl playing sound effects when a bot is targeted -------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].IsLdarg(0) &&
                    codes[i + 1].opcode == OpCodes.Ldc_R4 && codes[i + 1].operand is float f && f == 1f &&
                    codes[i + 2].StoresField(SFXVolumeLerpToField)) // Branch if not local player
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Save the branch instruction (brfalse.s)
                Label skipSFX = generator.DefineLabel();

                // Remove old condition check (6 instructions)
                //codes.RemoveRange(startIndex, 6);

                // Insert new method call for !IsPlayerLocalMethod(hauntingPlayer)
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // Load DressGirlAI instance
                    new CodeInstruction(OpCodes.Ldfld, hauntingPlayerField), // Load hauntingPlayer
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod), // Call method
                    new CodeInstruction(OpCodes.Brfalse, skipSFX)
                };
                codes.InsertRange(startIndex, codesToAdd);
                int playIndex = codes.FindIndex(startIndex, ci => ci.Calls(playMethod));

                // Create our label's destination....
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSFX);
                codes.Insert(playIndex + 1, nop); // Set label to the instruction **after** the play call
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.EnemiesPatches.DressGirlAIPatch.SetHauntStarePosition_Transpiler could not check if player local or bot local 1");
            }

            return codes.AsEnumerable();
        }
    }
}
