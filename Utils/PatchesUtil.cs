using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalBots.Utils
{
    internal static class PatchesUtil
    {
        public static readonly FieldInfo FieldInfoWasUnderwaterLastFrame = AccessTools.Field(typeof(PlayerControllerB), "wasUnderwaterLastFrame");
        public static readonly FieldInfo FieldInfoPlayerClientId = AccessTools.Field(typeof(PlayerControllerB), "playerClientId");
        public static readonly FieldInfo FieldInfoPreviousAnimationStateHash = AccessTools.Field(typeof(PlayerControllerB), "previousAnimationStateHash");
        public static readonly FieldInfo FieldInfoCurrentAnimationStateHash = AccessTools.Field(typeof(PlayerControllerB), "currentAnimationStateHash");
        public static readonly FieldInfo FieldInfoTargetPlayer = AccessTools.Field(typeof(BushWolfEnemy), "targetPlayer");
        public static readonly FieldInfo FieldInfoDraggingPlayer = AccessTools.Field(typeof(BushWolfEnemy), "draggingPlayer");

        #region AccessTools FieldRefs

        public static readonly AccessTools.FieldRef<Terminal, bool> usedTerminalThisSessionField = AccessTools.FieldRefAccess<bool>(typeof(Terminal), "usedTerminalThisSession");
        public static readonly AccessTools.FieldRef<Terminal, bool> syncedTerminalValuesField = AccessTools.FieldRefAccess<bool>(typeof(Terminal), "syncedTerminalValues");
        public static readonly AccessTools.FieldRef<Terminal, int> totalCostOfItemsField = AccessTools.FieldRefAccess<int>(typeof(Terminal), "totalCostOfItems");
        public static readonly AccessTools.FieldRef<Terminal, bool> broadcastedCodeThisFrameField = AccessTools.FieldRefAccess<bool>(typeof(Terminal), "broadcastedCodeThisFrame");
        public static readonly AccessTools.FieldRef<Terminal, bool> hasGottenNounField = AccessTools.FieldRefAccess<bool>(typeof(Terminal), "hasGottenNoun");
        public static readonly AccessTools.FieldRef<Terminal, bool> hasGottenVerbField = AccessTools.FieldRefAccess<bool>(typeof(Terminal), "hasGottenVerb");

        #endregion

        public static readonly MethodInfo AllEntitiesCountMethod = SymbolExtensions.GetMethodInfo(() => AllEntitiesCount());
        public static readonly MethodInfo AllRealPlayersCountMethod = SymbolExtensions.GetMethodInfo(() => AllRealPlayersCount());
        public static readonly MethodInfo IsPlayerLocalOrLethalBotOwnerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLocalOrLethalBotOwnerLocal(new PlayerControllerB()));
        public static readonly MethodInfo IsPlayerLocalOrLethalBotMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLocalOrLethalBot(new PlayerControllerB()));
        public static readonly MethodInfo IsColliderFromLocalOrLethalBotOwnerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsColliderFromLocalOrLethalBotOwnerLocal(new Collider()));
        public static readonly MethodInfo IsPlayerLethalBotMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLethalBot(new PlayerControllerB()));
        public static readonly MethodInfo IsPlayerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLocal(new PlayerControllerB()));
        public static readonly MethodInfo IsIdPlayerLethalBotMethod = SymbolExtensions.GetMethodInfo(() => IsIdPlayerLethalBot(new int()));
        public static readonly MethodInfo IsRagdollPlayerIdLethalBotMethod = SymbolExtensions.GetMethodInfo(() => IsRagdollPlayerIdLethalBot(new RagdollGrabbableObject()));
        public static readonly MethodInfo IsPlayerLethalBotOwnerLocalMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLethalBotOwnerLocal(new PlayerControllerB()));
        public static readonly MethodInfo IsAnLethalBotAiOwnerOfObjectMethod = SymbolExtensions.GetMethodInfo(() => IsAnLethalBotAiOwnerOfObject((GrabbableObject)new object()));
        public static readonly MethodInfo DisableOriginalGameDebugLogsMethod = SymbolExtensions.GetMethodInfo(() => DisableOriginalGameDebugLogs());
        public static readonly MethodInfo IsPlayerLethalBotControlledAndOwnerMethod = SymbolExtensions.GetMethodInfo(() => IsPlayerLethalBotControlledAndOwner(new PlayerControllerB()));
        public static readonly MethodInfo GetDamageFromSlimeIfLethalBotMethod = SymbolExtensions.GetMethodInfo(() => GetDamageFromSlimeIfLethalBot(new PlayerControllerB()));

        public static readonly MethodInfo SyncJumpMethod = SymbolExtensions.GetMethodInfo(() => SyncJump(new ulong()));
        public static readonly MethodInfo SyncLandFromJumpMethod = SymbolExtensions.GetMethodInfo(() => SyncLandFromJump(new ulong(), new bool()));


        public static List<CodeInstruction> InsertIsPlayerLethalBotInstructions(List<CodeInstruction> codes,
                                                                             ILGenerator generator,
                                                                             int startIndex,
                                                                             int indexToJumpTo)
        {
            Label labelToJumpTo;
            List<Label> labelsOfStartCode = codes[startIndex].labels;
            List<Label> labelsOfCodeToJumpTo = codes[startIndex + indexToJumpTo].labels;
            List<CodeInstruction> codesToAdd;

            // Define label for the jump
            labelToJumpTo = generator.DefineLabel();
            labelsOfCodeToJumpTo.Add(labelToJumpTo);

            // Rearrange label if start is a destination label for a previous code
            if (labelsOfStartCode.Count > 0)
            {
                codes.Insert(startIndex + 1, new CodeInstruction(codes[startIndex].opcode, codes[startIndex].operand));
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                startIndex++;
            }

            codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, IsPlayerLethalBotMethod),
                new CodeInstruction(OpCodes.Brtrue_S, labelToJumpTo)
            };
            codes.InsertRange(startIndex, codesToAdd);
            return codes;
        }

        public static List<CodeInstruction> InsertLogOfFieldOfThis(string logWithZeroParameter, FieldInfo fieldInfo, Type fieldType)
        {
            return new List<CodeInstruction>()
                {
                    new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Plugin), "Logger")),
                    new CodeInstruction(OpCodes.Ldstr, logWithZeroParameter),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, fieldInfo),
                    new CodeInstruction(OpCodes.Box, fieldType),
                    new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => String.Format(new string(new char[]{ }), new object()))),
                    new CodeInstruction(OpCodes.Callvirt, SymbolExtensions.GetMethodInfo(() => Plugin.LogDebug(new string(new char[]{ })))),
                };

            //codes.InsertRange(0, PatchesUtil.InsertLogOfFieldOfThis("isPlayerControlled {0}", AccessTools.Field(typeof(PlayerControllerB), "isPlayerControlled"), typeof(bool)));
        }

        public static List<CodeInstruction> InsertLogWithoutParameters(string log)
        {
            return new List<CodeInstruction>()
                {
                    new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Plugin), "Logger")),
                    new CodeInstruction(OpCodes.Ldstr, log),
                    new CodeInstruction(OpCodes.Callvirt, SymbolExtensions.GetMethodInfo(() => Plugin.LogDebug(new string(new char[]{ })))),
                };
        }

        public static List<CodeInstruction> InsertIsBypass(List<CodeInstruction> codes,
                                                           ILGenerator generator,
                                                           int startIndex,
                                                           int indexToJumpTo)
        {
            Label labelToJumpTo;
            List<Label> labelsOfStartCode = codes[startIndex].labels;
            List<Label> labelsOfCodeToJumpTo = codes[startIndex + indexToJumpTo].labels;
            List<CodeInstruction> codesToAdd;

            // Define label for the jump
            labelToJumpTo = generator.DefineLabel();
            labelsOfCodeToJumpTo.Add(labelToJumpTo);

            // Rearrange label if start is a destination label for a previous code
            if (labelsOfStartCode.Count > 0)
            {
                codes.Insert(startIndex + 1, new CodeInstruction(codes[startIndex].opcode, codes[startIndex].operand));
                codes[startIndex].opcode = OpCodes.Nop;
                codes[startIndex].operand = null;
                startIndex++;
            }

            codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Call, DisableOriginalGameDebugLogsMethod),
                new CodeInstruction(OpCodes.Brtrue_S, labelToJumpTo)
            };
            codes.InsertRange(startIndex, codesToAdd);
            return codes;
        }

        public static bool IsGrabbableObjectEqualsToNull(GrabbableObject grabbableObject)
        {
            return grabbableObject == null;
        }

        private static bool DisableOriginalGameDebugLogs()
        {
            return Const.DISABLE_ORIGINAL_GAME_DEBUG_LOGS;
        }

        private static bool IsPlayerLocal(PlayerControllerB player)
        {
            return LethalBotManager.IsPlayerLocal(player);
        }

        private static bool IsPlayerLocalOrLethalBot(PlayerControllerB player)
        {
            return LethalBotManager.Instance.IsPlayerLocalOrLethalBot(player);
        }

        private static bool IsPlayerLocalOrLethalBotOwnerLocal(PlayerControllerB player)
        {
            return LethalBotManager.Instance.IsPlayerLocalOrLethalBotOwnerLocal(player);
        }
        private static int AllEntitiesCount()
        {
            return LethalBotManager.Instance.AllEntitiesCount;
        }
        private static int AllRealPlayersCount()
        {
            // We subtract by one to mimic StartOfRound.Instance.connectedPlayersAmount
            return Math.Clamp(LethalBotManager.Instance.AllRealPlayersCount - 1, 0, AllEntitiesCount());
        }
        private static bool IsColliderFromLocalOrLethalBotOwnerLocal(Collider collider)
        {
            return LethalBotManager.Instance.IsColliderFromLocalOrLethalBotOwnerLocal(collider);
        }
        private static bool IsPlayerLethalBot(PlayerControllerB player)
        {
            return LethalBotManager.Instance.IsPlayerLethalBot(player);
        }
        private static bool IsIdPlayerLethalBot(int id)
        {
            return LethalBotManager.Instance.IsPlayerLethalBot(id);
        }
        private static bool IsRagdollPlayerIdLethalBot(RagdollGrabbableObject ragdollGrabbableObject)
        {
            return LethalBotManager.Instance.IsPlayerLethalBot((int)ragdollGrabbableObject.ragdoll.playerScript.playerClientId);
        }
        private static bool IsPlayerLethalBotOwnerLocal(PlayerControllerB player)
        {
            return LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(player);
        }
        private static bool IsPlayerLethalBotControlledAndOwner(PlayerControllerB player)
        {
            return LethalBotManager.Instance.IsPlayerLethalBotControlledAndOwner(player);
        }
        private static int GetDamageFromSlimeIfLethalBot(PlayerControllerB player)
        {
            return LethalBotManager.Instance.GetDamageFromSlimeIfLethalBot(player);
        }

        private static bool IsAnLethalBotAiOwnerOfObject(GrabbableObject grabbableObject)
        {
            return LethalBotManager.Instance.IsAnLethalBotAiOwnerOfObject(grabbableObject);
        }

        private static void SyncJump(ulong playerClientId)
        {
            LethalBotManager.Instance.GetLethalBotAI((int)playerClientId)?.SyncJump();
        }

        private static void SyncLandFromJump(ulong playerClientId, bool fallHard)
        {
            LethalBotManager.Instance.GetLethalBotAI((int)playerClientId)?.SyncLandFromJump(fallHard);
        }
    }
}
