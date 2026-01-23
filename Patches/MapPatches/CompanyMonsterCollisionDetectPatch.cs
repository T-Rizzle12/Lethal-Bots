using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    /// <summary>
    /// Patch for <c>CompanyMonsterCollisionDetect</c>
    /// </summary>
    [HarmonyPatch(typeof(CompanyMonsterCollisionDetect))]
    public class CompanyMonsterCollisionDetectPatch
    {
        [HarmonyPatch("OnTriggerEnter")]
        [HarmonyPrefix]
        static bool OnTriggerEnter_Prefix(CompanyMonsterCollisionDetect __instance, Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                return true;
            }

            // Check if this is a bot
            PlayerControllerB component = other.gameObject.GetComponent<PlayerControllerB>();
            if (component != null && !component.isPlayerDead && component.IsOwner && LethalBotManager.Instance.IsPlayerLethalBot(component))
            {
                DepositItemsDesk? companyDesk = LethalBotManager.CompanyDesk;
                if (companyDesk != null)
                {
                    // HACKHACK: We CANNOT call the base method, as its set to kill the local player!
                    // We recreate the logic here
                    if (companyDesk.attacking && !companyDesk.monsterAnimations[__instance.monsterAnimationID].animatorCollidedOnClient)
                    {
                        companyDesk.monsterAnimations[__instance.monsterAnimationID].animatorCollidedOnClient = true;
                        if (companyDesk.IsServer)
                        {
                            companyDesk.ConfirmAnimationGrabPlayerClientRpc(__instance.monsterAnimationID, (int)component.playerClientId);
                        }
                        else
                        {
                            companyDesk.CheckAnimationGrabPlayerServerRpc(__instance.monsterAnimationID, (int)component.playerClientId);
                        }
                        switch (companyDesk.currentMood.manifestation)
                        {
                            case CompanyMonster.Tentacles:
                                Plugin.LogInfo("Tentacle collision");
                                break;
                            case CompanyMonster.Tongue:
                                Plugin.LogInfo("Tongue collision");
                                break;
                            case CompanyMonster.GiantHand:
                                Plugin.LogInfo("Hand collision");
                                break;
                        }
                    }
                }
                return false;
            }
            return true;
        }
    }
}
