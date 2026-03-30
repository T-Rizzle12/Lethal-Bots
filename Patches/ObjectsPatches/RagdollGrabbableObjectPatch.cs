using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Managers;

namespace LethalBots.Patches.ObjectsPatches
{
    [HarmonyPatch(typeof(RagdollGrabbableObject))]
    public class RagdollGrabbableObjectPatch
    {
        /// <summary>
        /// Used so we can manually register new items that spawn!
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_Postfix(RagdollGrabbableObject __instance)
        {
            LethalBotManager.Instance.GrabbableObjectSpawned(__instance);
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        static bool Update_PreFix(RagdollGrabbableObject __instance, ref bool ___foundRagdollObject)
        {
            int bodyID = __instance.bodyID;
            if (bodyID == Const.INIT_RAGDOLL_ID)
            {
                if (__instance.ragdoll == null)
                {
                    return false;
                }
                ___foundRagdollObject = true;
                __instance.grabbableToEnemies = false;
                return true;
            }

            // BodyId is a networkVariable
            // It is 0 until the client receive the message that it is not
            // but the init of RagdollGrabbableObject can be done with that value not yet updated
            // So we make sure that the deadbody id still match the bodyID network variable in case it has been updated
            if (___foundRagdollObject)
            {
                if (StartOfRound.Instance.allPlayerScripts[bodyID].deadBody != null
                    && __instance.ragdoll != null
                    && bodyID != (int)__instance.ragdoll.playerScript.playerClientId)
                {
                    ___foundRagdollObject = false;
                }
            }

            return true;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Update_PostFix(RagdollGrabbableObject __instance)
        {
            if (__instance.bodyID == Const.INIT_RAGDOLL_ID)
            {
                __instance.grabbableToEnemies = false;
            }
        }
    }
}
