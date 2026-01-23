using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Patches.GameEnginePatches;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    /// <summary>
    /// Patch for <c>OutOfBoundsTrigger</c>
    /// </summary>
    [HarmonyPatch(typeof(OutOfBoundsTrigger))]
    public class OutOfBoundsTriggerPatch
    {
        /// <summary>
        /// Made with the sole purpose of letting <see cref="OutOfBoundsTrigger.OnTriggerEnter"/> affect bots
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("OnTriggerEnter")]
        [HarmonyPostfix]
        static void OnTriggerEnter_Postfix(OutOfBoundsTrigger __instance, Collider other, StartOfRound ___playersManager)
        {
            // Same as base game
            if (__instance.disableWhenRoundStarts && !___playersManager.inShipPhase)
            {
                return;
            }

            // Left in to be consistent with base game!
            if (other.tag.StartsWith("PlayerRagdoll"))
            {
                return;
            }
            else
            {
                if (!(other.tag == "Player"))
                {
                    return;
                }

                // Make sure we only do this for bots that are active!
                PlayerControllerB component = other.GetComponent<PlayerControllerB>();
                if (!LethalBotManager.Instance.IsPlayerLethalBotControlledAndOwner(component))
                {
                    return;
                }
                component.ResetFallGravity();
                if (!(component != null))
                {
                    return;
                }
                if (!___playersManager.shipDoorsEnabled)
                {
                    // Copied from LethalBotManager.CountAliveAndDisableLethalBots!
                    component.isInElevator = true;
                    component.isInHangarShipRoom = true;
                    Vector3 shipPos = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(StartOfRound.Instance, (int)component.playerClientId, false);
                    component.thisController.enabled = false;
                    component.TeleportPlayer(shipPos);
                    component.serverPlayerPosition = shipPos;
                    component.transform.localPosition = shipPos;
                    component.transform.position = shipPos;
                    component.thisController.enabled = true;

                    // Make sure we move our inventory to the ship as well!
                    foreach (var item in component.ItemSlots)
                    {
                        Transform? parentObject = item?.parentObject;
                        if (item != null && parentObject != null)
                        {
                            item.transform.rotation = parentObject.rotation;
                            item.transform.Rotate(item.itemProperties.rotationOffset);
                            item.transform.position = parentObject.position;
                            Vector3 positionOffset = item.itemProperties.positionOffset;
                            positionOffset = parentObject.rotation * positionOffset;
                            item.transform.position += positionOffset;
                        }
                    }
                }
                else if (component.isInsideFactory)
                {
                    if (!StartOfRound.Instance.isChallengeFile)
                    {
                        component.KillPlayer(Vector3.zero, spawnBody: false);
                    }
                    else
                    {
                        component.TeleportPlayer(RoundManager.FindMainEntrancePosition(getTeleportPosition: true));
                    }
                }
                else if (component.isInHangarShipRoom)
                {
                    component.TeleportPlayer(___playersManager.playerSpawnPositions[0].position);
                }
                else
                {
                    component.TeleportPlayer(___playersManager.outsideShipSpawnPosition.position);
                }
            }
        }
    }
}
