using HarmonyLib;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    [HarmonyPatch(typeof(Terminal))]
    public class TerminalPatch
    {
        /// <summary>
        /// Simple patch that fixes the terminal update not updating the money counter
        /// when a bot is using it.
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch(nameof(Terminal.Update))]
        [HarmonyPostfix]
        static void Update_Postfix(Terminal __instance)
        {
            if (__instance.terminalInUse)
            {
                return; // Human player is using the terminal, no need to update the money counter
            }

            if (IsBotUsingTerminal(__instance))
            {
                __instance.topRightText.text = $"${__instance.groupCredits}";
                __instance.screenText.caretPosition = __instance.screenText.text.Length;
                if (__instance.scrollBarVertical.value < 0.95f)
                {
                    __instance.scrollBarCanvasGroup.alpha = Mathf.Lerp(__instance.scrollBarCanvasGroup.alpha, 1f, 10f * Time.deltaTime);
                }
                else
                {
                    __instance.scrollBarCanvasGroup.alpha = 0f;
                }
            }
        }

        [HarmonyPatch(nameof(Terminal.OnSubmit))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> OnSubmit_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Log what we are doing
            Plugin.LogDebug("TerminalPatch.OnSubmit_Transpiler: Patching Terminal.OnSubmit to allow bots to use the terminal without taking over the human player's cursor.");

            // Target property: terminalInUse
            FieldInfo terminalInUseField = AccessTools.Field(typeof(Terminal), "terminalInUse");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                //Plugin.LogWarning(codes[i].ToString());
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(terminalInUseField))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the field access with a call to our method
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = AccessTools.Method(typeof(TerminalPatch), nameof(IsHumanPlayerOrBotUsingTerminal));

                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.TerminalPatch.OnSubmit_Transpiler: Could not find the target field access!");
            }

            // Purpose: The original code takes over the player cursor when enter is pressed, since bots
            // run on the same client as a human player, we need to skip that part of the code if the bot is the one on the terminal
            // NOTE: This is a bit risky, but i'm just going to check for the screenText.ActivateInputField(); followed by the screenText.Select(); calls,
            // since those are the ones that take over the player cursor.

            // Target method: screenText.ActivateInputField(); followed by screenText.Select();
            MethodInfo activateInputFieldMethod = AccessTools.Method(typeof(TMPro.TMP_InputField), nameof(TMPro.TMP_InputField.ActivateInputField));
            MethodInfo selectMethod = AccessTools.Method(typeof(UnityEngine.UI.Selectable), nameof(UnityEngine.UI.Selectable.Select));
            FieldInfo screenTextField = AccessTools.Field(typeof(Terminal), "screenText");

            // ----------------------------------------------------------------------
            bool patched = false;
            int timesPatched = 0;
            for (var i = 0; i < codes.Count - 5; i++)
            {
                //Plugin.LogWarning(codes[i].ToString());
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(screenTextField)
                    && codes[i + 2].Calls(activateInputFieldMethod)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].LoadsField(screenTextField)
                    && codes[i + 5].Calls(selectMethod))
                {
                    Plugin.LogDebug($"Adding new branch at index {i}...");

                    // Extract the labels from the original instructions to preserve them
                    List<Label> oldLabels = codes[i].ExtractLabels();

                    // Create our label's destination....
                    Label skipSnapshot = generator.DefineLabel();
                    var nop = new CodeInstruction(OpCodes.Nop);
                    nop.labels.Add(skipSnapshot);
                    codes.Insert(i + 6, nop); // Set label to the instruction **after** the calls

                    List<CodeInstruction> codesToInsert = new List<CodeInstruction>() 
                    { 
                        new CodeInstruction(OpCodes.Ldarg_0).WithLabels(oldLabels), // Load the Terminal instance onto the stack; also preserve the original labels
                        new CodeInstruction(OpCodes.Ldfld, terminalInUseField), // Load the terminalInUse field onto the stack
                        new CodeInstruction(OpCodes.Brfalse, skipSnapshot) // If the human player is not using the terminal, skip the next instructions (which take over the player cursor)
                    };

                    // Insert our new instructions before the ActivateInputField and Select calls
                    codes.InsertRange(i, codesToInsert);

                    // Move the index forward to skip over the newly inserted instructions and the original calls
                    i += codesToInsert.Count;
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.TerminalPatch.OnSubmit_Transpiler could not bypass the player cursor takeover!");
            }
            else
            {
                Plugin.LogDebug($"Added new branches {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        private static bool IsHumanPlayerOrBotUsingTerminal(Terminal terminal)
        {
            return terminal.terminalInUse || IsBotUsingTerminal(terminal);
        }

        private static bool IsBotUsingTerminal(Terminal terminal)
        {
            InteractTrigger interactTrigger = terminal.terminalTrigger;
            return interactTrigger != null && LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(interactTrigger.playerScriptInSpecialAnimation);
        }
    }
}
