using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LethalBots.Patches.ObjectsPatches
{
    /// <summary>
    /// Patches for <c>DeadBodyInfo</c>
    /// </summary>
    [HarmonyPatch(typeof(DeadBodyInfo))]
    public class DeadBodyInfoPatch
    {
        // Conditional Weak Table since when the DeadBodyInfo is removed, the table automatically cleans itself!
        private static ConditionalWeakTable<DeadBodyInfo, DeadBodyInfoMonitor> lethalBotDeadBodyInfoMonitor = new ConditionalWeakTable<DeadBodyInfo, DeadBodyInfoMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="DeadBodyInfoMonitor"/>
        /// for the given <see cref="DeadBodyInfo"/>
        /// </summary>
        /// <param name="body"></param>
        /// <returns>The <see cref="DeadBodyInfoMonitor"/> associated with the given <see cref="DeadBodyInfo"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DeadBodyInfoMonitor GetOrCreateMonitor(DeadBodyInfo body)
        {
            return lethalBotDeadBodyInfoMonitor.GetOrCreateValue(body);
        }

        /// <summary>
        /// Postfix with the sole purpose of making the bots gain fear when seeing a dead body for the first time.
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("DetectIfSeenByLocalPlayer")]
        [HarmonyPostfix]
        static void DetectIfSeenByLocalPlayer_PostFix(DeadBodyInfo __instance)
        {
            DeadBodyInfoMonitor deadBodyInfoMonitor = GetOrCreateMonitor(__instance);
            if (!deadBodyInfoMonitor.CanUpdate())
            {
                deadBodyInfoMonitor.Update(Time.deltaTime);
                return;
            }

            deadBodyInfoMonitor.Invalidate();
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (LethalBotAI lethalBotAI in lethalBotAIs)
            {
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController.Npc;
                if (lethalBotController != null 
                    && !deadBodyInfoMonitor.HasBotSeenBody(lethalBotAI))
                {
                    Rigidbody? rigidbody = null;
                    float num = Vector3.Distance(lethalBotController.gameplayCamera.transform.position, __instance.transform.position);
                    foreach (Rigidbody tempRigidBody in __instance.bodyParts)
                    {
                        if (rigidbody == tempRigidBody)
                        {
                            continue;
                        }
                        rigidbody = tempRigidBody;
                        if (lethalBotController.HasLineOfSightToPosition(rigidbody.transform.position, 30f / (num / 5f)))
                        {
                            if (num < 10f)
                            {
                                lethalBotController.JumpToFearLevel(0.9f);
                            }
                            else
                            {
                                lethalBotController.JumpToFearLevel(0.55f);
                            }
                            deadBodyInfoMonitor.SetBotSeenBody(lethalBotAI);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Patch for assigning right tag to a dead body for not getting debug logs of errors
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("Start")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Start_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 8; i++)
            {
                if (codes[i].ToString().StartsWith("ldarg.0 NULL") //65
                    && codes[i + 3].ToString().StartsWith("ldarg.0 NULL")//68
                    && codes[i + 8].ToString() == "ldstr \"PlayerRagdoll\"")//73
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                List<Label> labelsOfCodeToJumpTo = codes[startIndex + 3].labels;

                // Define label for the jump
                Label labelToJumpTo = generator.DefineLabel();
                labelsOfCodeToJumpTo.Add(labelToJumpTo);

                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                                                        {
                                                            new CodeInstruction(OpCodes.Ldarg_0, null),
                                                            new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(DeadBodyInfo), "playerObjectId")),
                                                            new CodeInstruction(OpCodes.Call, PatchesUtil.IsIdPlayerLethalBotMethod),
                                                            new CodeInstruction(OpCodes.Brtrue_S, labelToJumpTo)
                                                        };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.DeadBodyInfoPatch.Start_Transpiler remplace with correct tag if bot.");
            }

            // ----------------------------------------------------------------------
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch to clean up <see cref="DeadBodyInfoMonitor"/>'s that are no longer needed.
        /// </summary>
        /// <remarks>
        /// Although <see cref="ConditionalWeakTable{TKey, TValue}"/> can clean this for us,
        /// it will only clean the table if nothing refrences the key anymore.
        /// </remarks>
        /// <param name="__instance"></param>
        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix(DeadBodyInfo __instance)
        {
            if (lethalBotDeadBodyInfoMonitor.TryGetValue(__instance, out _))
            {
                lethalBotDeadBodyInfoMonitor.Remove(__instance);
            }
        }

        // Might as well make it inherit the UpdateLimiter class.
        // Less objects to keep refrence of in memory!
        private class DeadBodyInfoMonitor : UpdateLimiter
        {
            public Dictionary<LethalBotAI, bool> lethalBotAIs = new Dictionary<LethalBotAI, bool>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBotSeenBody(LethalBotAI bot)
            {
                lethalBotAIs[bot] = true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool HasBotSeenBody(LethalBotAI bot)
            {
                return lethalBotAIs.GetValueOrDefault(bot, false);
            }
        }
    }
}
