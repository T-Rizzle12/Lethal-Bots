using HarmonyLib;
using HolsteredItemPositions.Monobehaviours;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;

namespace LethalBots.Patches.ModPatches.HolsteredItemPositions
{
    [HarmonyPatch(typeof(HolsterItemVisual))]
    public class HolsterItemVisualPatch
    {
        [HarmonyPatch(nameof(HolsterItemVisual.Restore))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Restore_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Log what we are doing
            Plugin.LogDebug("HolsterItemVisualPatch.Restore_Transpiler: Patching HolsterItemVisual.Restore to correctly restore the item parent after the bot re-equips an item.");

            // Target property: Player.IsOwner
            FieldInfo playerField = AccessTools.Field(typeof(HolsterItemVisual), nameof(HolsterItemVisual.Player));
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), nameof(NetworkBehaviour.IsOwner));

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                //Plugin.LogWarning(codes[i].ToString());
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(playerField)
                    && codes[i + 2].Calls(isOwnerGetter))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the IsOwner check with IsPlayerLocal call
                codes[startIndex + 2].opcode = OpCodes.Call;
                codes[startIndex + 2].operand = PatchesUtil.IsPlayerLocalMethod;

                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBots.Patches.ModPatches.HolsteredItemPositions: Could not replace IsOwner check!");
            }

            return codes.AsEnumerable();
        }
    }
}
