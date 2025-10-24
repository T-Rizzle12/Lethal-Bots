using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using ModelReplacement;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.ModelRplcmntAPI
{
    [HarmonyPatch(typeof(BodyReplacementBase))]
    public class BodyReplacementBasePatch
    {

        [HarmonyPatch("LateUpdate")]
        [HarmonyPrefix]
        static bool LateUpdate_Prefix(BodyReplacementBase __instance, ref GameObject ___replacementDeadBody)
        {
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(__instance.controller);
            if (lethalBotAI == null)
            {
                return true;
            }

            Component instanceComponent = (Component)__instance; // Dodge the BodyReplacementBase compiler link with ListBodyReplacementOnDeadBodies
            if (__instance.controller.deadBody != null
                && !LethalBotManager.Instance.ListBodyReplacementOnDeadBodies.Any(x => x.BodyReplacementBase == instanceComponent))
            {
                LethalBotManager.Instance.ListBodyReplacementOnDeadBodies.Add(new BodyReplacementAdapter(instanceComponent));
                __instance.viewState.ReportBodyReplacementRemoval();
                __instance.cosmeticAvatar = __instance.ragdollAvatar;
                CreateAndParentRagdoll_ReversePatch(__instance, __instance.controller.deadBody);
                lethalBotAI.LethalBotIdentity.BodyReplacementBase = __instance;
            }

            if (LethalBotManager.Instance.ListBodyReplacementOnDeadBodies.Any(x => x.BodyReplacementBase == instanceComponent))
            {
                //Plugin.LogDebug($"{internAI.NpcController.Npc.playerUsername} {__instance.GetInstanceID()} only ragdoll update, {__instance.controller.deadBody}");
                __instance.ragdollAvatar.Update();
                return false;
            }

            //Plugin.LogDebug($"{internAI.NpcController.Npc.playerUsername} {__instance.GetInstanceID()} all update");
            return true;
        }


        [HarmonyPatch("CreateAndParentRagdoll")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static void CreateAndParentRagdoll_ReversePatch(object instance, DeadBodyInfo bodyinfo) => throw new NotImplementedException("Stub LethalBot.Patches.ModPatches.ModelRplcmntAPI.BodyReplacementBasePatch.CreateAndParentRagdoll_ReversePatch");

        [HarmonyPatch("GetBounds")]
        [HarmonyPrefix]
        public static bool GetBounds_Prefix(BodyReplacementBase __instance, GameObject model, ref Bounds __result)
        {
            LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(__instance.controller);
            if (lethalBot == null)
            {
                return true;
            }

            __result = lethalBot.NpcController.GetBoundsTimedCheck.GetBoundsModel(model);
            return false;
        }
    }
}
