using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LethalBots.Patches.ObjectsPatches
{
    [HarmonyPatch(typeof(GrabbableObject))]
    public class GrabbableObjectPatch
    {
        /// <summary>
        /// Used so we can manually register new items that spawn!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_Postfix(GrabbableObject __instance)
        {
            LethalBotManager.Instance.GrabbableObjectSpawned(__instance);
        }

        [HarmonyPatch("SetControlTipsForItem")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool SetControlTipsForItem_PreFix(GrabbableObject __instance)
        {
            return !LethalBotManager.Instance.IsAnLethalBotAiOwnerOfObject(__instance);
        }

        [HarmonyPatch("DiscardItemOnClient")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DiscardItemOnClient_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Delcare local variable
            var playerLocal = generator.DeclareLocal(typeof(PlayerControllerB));

            // Grab methods and fields we need!
            MethodInfo discardItemMethod = AccessTools.Method(typeof(GrabbableObject), "DiscardItem");
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            MethodInfo clearControlTipsMethod = AccessTools.Method(typeof(HUDManager), "ClearControlTips");
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(discardItemMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert new field call so we can store the heldItem variable so we can check if a bot is dropping the item
                // We do this since DiscardItem clears the playerHeldBy attribute making it impossible to check who is dropping the object at this point
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0), // Load this
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load this.playerHeldBy
                    new CodeInstruction(OpCodes.Stloc, playerLocal) // Store in our local variable
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.DiscardItemOnClient_Transpiler could not remove check if holding player is bot");
                return codes.AsEnumerable();
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].Calls(clearControlTipsMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 2, nop); // Set label to the instruction **after** the ClearControlTips call

                // Insert new method call to skip ClearControlTips if a bot is dropping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc, playerLocal),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.DiscardItemOnClient_Transpiler could not remove check if holding player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("DiscardItem")]
        [HarmonyPrefix]
        static void DiscardItem_PreFix(GrabbableObject __instance)
        {
            PlayerControllerB? lethalBotController = __instance.playerHeldBy;
            if (lethalBotController == null
                || __instance.IsOwner
                || !LethalBotManager.Instance.IsPlayerLethalBot(lethalBotController))
            {
                return;
            }

            // This is needed incase the bot changes ownership to another player!
            __instance.playerHeldBy.IsInspectingItem = false;
            __instance.playerHeldBy.activatingItem = false;
        }

        [HarmonyPatch("DiscardItem")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DiscardItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager methods
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            MethodInfo clearControlTipsMethod = AccessTools.Method(typeof(HUDManager), "ClearControlTips");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].Calls(clearControlTipsMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 2, nop); // Set label to the instruction **after** the ClearControlTips call

                // Insert new method call to skip ClearControlTips if a bot is dropping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.DiscardItem_Transpiler could not remove check if holding player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("GrabItemOnClient")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> GrabItemOnClient_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // GrabbableObject.SetControlTipsForItem
            MethodInfo setControlTipsForItemMethod = AccessTools.Method(typeof(GrabbableObject), "SetControlTipsForItem");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].Calls(setControlTipsForItemMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 2, nop); // Set label to the instruction **after** the SetControlTipsForItem call

                // Insert new method call to skip SetControlTipsForItem if a bot is grabbing the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.GrabItemOnClient_Transpiler could not remove check if holding player is bot");
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch("EquipItem")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> EquipItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // HUDManager methods
            MethodInfo getHUDManagerInstance = AccessTools.PropertyGetter(typeof(HUDManager), "Instance");
            MethodInfo clearControlTipsMethod = AccessTools.Method(typeof(HUDManager), "ClearControlTips");
            MethodInfo setControlTipsForItemMethod = AccessTools.Method(typeof(GrabbableObject), "SetControlTipsForItem");

            // Replacement field: this.playerHeldBy
            FieldInfo playerHeldByField = AccessTools.Field(typeof(GrabbableObject), "playerHeldBy");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 3; i++)
            {
                if (codes[i].Calls(getHUDManagerInstance)
                    && codes[i + 1].Calls(clearControlTipsMethod)
                    && codes[i + 2].IsLdarg(0)
                    && codes[i + 3].Calls(setControlTipsForItemMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Create our label's destination....
                Label skipSnapshot = generator.DefineLabel();
                var nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.Add(skipSnapshot);
                codes.Insert(startIndex + 4, nop); // Set label to the instruction **after** the ClearControlTips call

                // Insert new method call to skip ClearControlTips if a bot is equiping the item
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, playerHeldByField), // Load `playerHeldBy`
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLethalBotMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, skipSnapshot)
                };
                codes.InsertRange(startIndex, codesToAdd);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.ObjectsPatches.EquipItem_Transpiler could not remove check if holding player is bot");
            }

            return codes.AsEnumerable();
        }
    }
}
