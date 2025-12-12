using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // TODO: Change this into a transpiler so we can make it more comptable with other mods!
        [HarmonyPatch("DiscardItemOnClient")]
        [HarmonyPrefix]
        static bool DiscardItemOnClient_PreFix(GrabbableObject __instance)
        {
            if (!__instance.IsOwner)
            {
                return true;
            }
            PlayerControllerB? lethalBotController = __instance.playerHeldBy;
            if (lethalBotController != null)
            {
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(lethalBotController);
                if (lethalBotAI != null)
                {
                    __instance.DiscardItem();
                    lethalBotAI.SyncBatteryLethalBotServerRpc(__instance.NetworkObject, (int)(__instance.insertedBattery.charge * 100f));
                    if (__instance.itemProperties.syncDiscardFunction)
                    {
                        __instance.isSendingItemRPC++;
                        lethalBotAI.DiscardItemServerRpc(__instance.NetworkObject);
                    }
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch("DiscardItem")]
        [HarmonyPrefix]
        static bool DiscardItem_PreFix(GrabbableObject __instance)
        {
            PlayerControllerB? lethalBotController = __instance.playerHeldBy;
            if (lethalBotController == null
                || !LethalBotManager.Instance.IsPlayerLethalBot(lethalBotController))
            {
                return true;
            }

            __instance.playerHeldBy.IsInspectingItem = false;
            __instance.playerHeldBy.activatingItem = false;
            __instance.playerHeldBy = null;
            return false;
        }

        /// <summary>
        /// ScanNodeProperties can be null, so perfix patch to cover cases with ragdoll bodies only
        /// </summary>
        /// <returns></returns>
        [HarmonyPatch("SetScrapValue")]
        [HarmonyPrefix]
        static bool SetScrapValue_PreFix(GrabbableObject __instance, int setValueTo)
        {
            RagdollGrabbableObject? ragdollGrabbableObject = __instance as RagdollGrabbableObject;
            if (ragdollGrabbableObject == null)
            {
                // Other scrap = do base game logic
                return true;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(ragdollGrabbableObject.bodyID.Value);
            if (lethalBotAI == null)
            {
                if (ragdollGrabbableObject.gameObject.GetComponentInChildren<ScanNodeProperties>() == null)
                {
                    // ragdoll of irl player with ScanNodeProperties null, we do the base game logic without the error
                    __instance.scrapValue = setValueTo;
                    return false;
                }

                return true;
            }

            if (lethalBotAI.NpcController.Npc.isPlayerDead
                && ragdollGrabbableObject.gameObject.GetComponentInChildren<ScanNodeProperties>() == null)
            {
                if (ragdollGrabbableObject.gameObject.GetComponentInChildren<ScanNodeProperties>() == null)
                {
                    // ragdoll of bot with ScanNodeProperties null, we do the base game logic without the error
                    __instance.scrapValue = setValueTo;
                    return false;
                }

                return true;
            }

            // Grabbable ragdoll body, not sellable, bot not dead
            return false;
        }

        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(nameof(GrabbableObject.Update))]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void GrabbableObject_Update_ReversePatch(RagdollGrabbableObject instance) => throw new NotImplementedException("Stub LethalBot.Patches.NpcPatches.GrabbableObjectPatch.GrabbableObject_Update_ReversePatch");

        [HarmonyPatch("EquipItem")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> EquipItem_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 3; i++)
            {
                if (codes[i].ToString() == "call static HUDManager HUDManager::get_Instance()"//3
                    && codes[i + 1].ToString() == "callvirt void HUDManager::ClearControlTips()"
                    && codes[i + 3].ToString() == "callvirt virtual void GrabbableObject::SetControlTipsForItem()")
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsAnLethalBotAiOwnerOfObjectMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, codes[startIndex + 4].labels[0])
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
