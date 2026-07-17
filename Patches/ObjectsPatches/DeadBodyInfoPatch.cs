using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
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
        public static readonly ConditionalWeakTable<DeadBodyInfo, DeadBodyInfoMonitor> lethalBotDeadBodyInfoMonitor = new ConditionalWeakTable<DeadBodyInfo, DeadBodyInfoMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="DeadBodyInfoMonitor"/>
        /// for the given <see cref="DeadBodyInfo"/>
        /// </summary>
        /// <param name="body"></param>
        /// <returns>The <see cref="DeadBodyInfoMonitor"/> associated with the given <see cref="DeadBodyInfo"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeadBodyInfoMonitor GetOrCreateMonitor(DeadBodyInfo body)
        {
            return lethalBotDeadBodyInfoMonitor.GetValue(body, _ => new DeadBodyInfoMonitor());
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
                return;
            }

            deadBodyInfoMonitor.Invalidate();
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI lethalBotAI = lethalBotAIs[i];
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
            lethalBotDeadBodyInfoMonitor.Remove(__instance);
        }

        /// <summary>
        /// Monitors and manages the seen body status for bots, allowing tracking of whether each bot has observed a
        /// dead body.
        /// </summary>
        /// <remarks>
        /// This class is intended for the on see dead body fear mechanic.<br/>
        /// It limits update frequency according to the specified interval and is not thread-safe.<br/>
        /// This class also inherits from <see cref="UpdateLimiter"/>
        /// </remarks>
        public sealed class DeadBodyInfoMonitor : UpdateLimiter
        {
            private const float UPDATE_INTERVAL = 0.5f;
            private readonly Dictionary<LethalBotAI, bool> lethalBotAIs = new Dictionary<LethalBotAI, bool>();

            /// <summary>
            /// Initializes a new instance of the DeadBodyInfoMonitor class using the default update interval.
            /// </summary>
            internal DeadBodyInfoMonitor() : this(UPDATE_INTERVAL) { }

            /// <summary>
            /// Initializes a new instance of the DeadBodyInfoMonitor class with the specified update interval.
            /// </summary>
            /// <param name="updateInterval">The time interval, in seconds, between monitor updates. Must be greater than zero.</param>
            internal DeadBodyInfoMonitor(float updateInterval) : base(updateInterval) { }

            /// <summary>
            /// Sets the seen body status for the specified bot.
            /// </summary>
            /// <param name="bot">The bot whose seen body status is being updated. Cannot be null.</param>
            /// <param name="seenBody">true to mark the bot as having its body seen; otherwise, false. The default is true.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBotSeenBody(LethalBotAI bot, bool seenBody = true)
            {
                lethalBotAIs[bot] = seenBody;
            }

            /// <summary>
            /// Determines whether the specified bot has previously seen the body.
            /// </summary>
            /// <param name="bot">The bot to check for prior body visibility. Cannot be null.</param>
            /// <returns>true if the specified bot has seen the body; otherwise, false.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool HasBotSeenBody(LethalBotAI bot)
            {
                return lethalBotAIs.GetValueOrDefault(bot, false);
            }
        }
    }
}
