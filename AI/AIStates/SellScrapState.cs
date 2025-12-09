using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.HID;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where we go to sell our collected scrap to the company.
    /// We also ring the bell to get Jeb to collect it!
    /// </summary>
    public class SellScrapState : AIState
    {
        private static InteractTrigger? bellInteractTrigger;

        public SellScrapState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.SellScrap;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                // If we still don't have the company desk, we can't sell scrap!
                // We also need to be at the company building to sell scrap!
                // If either of these conditions are not met, we return to our ship!
                if (LethalBotManager.CompanyDesk == null
                    || !LethalBotManager.AreWeAtTheCompanyBuilding())
                {
                    ai.State = new ReturnToShipState(this);
                    return;
                }

                // Only find the bell interact trigger if it's not already set!
                if (bellInteractTrigger == null)
                {
                    // HACKHACK: I found this in the source code, so we can use it to find the bell,
                    // this will break if the name changes, but the code will fall back to faking the bell sound.
                    GameObject? bell = GameObject.Find("BellDinger");
                    if (bell != null)
                    {
                        bellInteractTrigger = (bell.transform.Find("Trigger") as Component)?.GetComponent<InteractTrigger>();
                        if (bellInteractTrigger == null)
                        {
                            Plugin.LogWarning("Could not find the bell interact trigger! The bell sounds will be faked!");
                        }
                        else
                        {
                            Plugin.LogInfo($"Found bell interact trigger {bellInteractTrigger}!");
                        }
                    }
                }

                #if DEBUG
                // DEBUG: Remove this before final release!
                // We use this to find the bell interact trigger!
                InteractTrigger[] interactTriggers = UnityEngine.Object.FindObjectsOfType<InteractTrigger>();
                foreach (var trigger in interactTriggers)
                {
                    if (trigger != null)
                    {
                        Plugin.LogInfo($"Found trigger {trigger} at company building!");
                        Plugin.LogInfo($"Trigger name {trigger.name}");
                        Plugin.LogInfo($"GameObject name {trigger.gameObject}");
                    }
                }
                #endif
            }
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // The company desk is invaild or we are not at the company building return!
            DepositItemsDesk? companyDesk = LethalBotManager.CompanyDesk;
            if (companyDesk == null 
                || !LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Get the closest nav area to the desk!
            Vector3 closestAreaToDesk = RoundManager.Instance.GetNavMeshPosition(companyDesk.triggerCollider.ClosestPoint(npcController.Npc.transform.position), default, 20f, NavMesh.AllAreas);
            if (!RoundManager.Instance.GotNavMeshPositionResult)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // If the company is collecting the loot we should wait nearby.
            bool waitNearbyDesk = false;
            bool canReturn = true;
            bool fulfilledProfitQuota = LethalBotManager.HaveWeFulfilledTheProfitQuota();
            if (companyDesk.inGrabbingObjectsAnimation || companyDesk.doorOpen)
            {
                waitNearbyDesk = true;
            }

            // We should wait nearby the desk if its full!
            float sqrDistToDesk = (closestAreaToDesk - npcController.Npc.transform.position).sqrMagnitude;
            if (!waitNearbyDesk 
                && (LethalBotManager.GetNumberOfItemsOnDesk() >= 150 
                    || (ai.LookingForObjectsToSell(true) == null && LethalBotManager.AreThereItemsOnDesk())))
            {
                // If the company isn't collecting the loot, we should ring the bell first!
                canReturn = false;
                if (sqrDistToDesk <= Const.DISTANCE_TO_COMPANY_DESK * Const.DISTANCE_TO_COMPANY_DESK)
                {
                    // Stop moving!
                    ai.StopMoving();

                    // Make the bot ring the bell!
                    if (bellInteractTrigger != null)
                    {
                        // Make the bot ring the bell!
                        bellInteractTrigger.Interact(npcController.Npc.thisPlayerBody);
                    }
                    else
                    {
                        // No bell interact trigger, so we need to fake the sound!
                        RoundManager.Instance.PlayAudibleNoise(npcController.Npc.transform.position);
                    }
                    return;
                }
                
            }

            // If we don't have any scrap to place, just return!
            if (!waitNearbyDesk && canReturn && (fulfilledProfitQuota || !ai.HasSellableItemInInventory()))
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Move to the desk if we are not nearby it!
            if (sqrDistToDesk > Const.DISTANCE_TO_COMPANY_DESK * Const.DISTANCE_TO_COMPANY_DESK)
            {
                ai.SetDestinationToPositionLethalBotAI(closestAreaToDesk);

                if (!npcController.WaitForFullStamina && sqrDistToDesk > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else if (npcController.WaitForFullStamina || sqrDistToDesk < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
                {
                    npcController.OrderToStopSprint();
                }

                // If need to wait nearby the desk, we should do so!
                if (waitNearbyDesk && sqrDistToDesk <= Const.DISTANCE_TO_WAIT_AT_DESK * Const.DISTANCE_TO_WAIT_AT_DESK)
                {
                    ai.StopMoving();
                }
                else
                {
                    ai.OrderMoveToDestination();
                }
            }
            else
            {
                // Stop moving!
                ai.StopMoving();

                // Don't sell until we are ready!
                if (waitNearbyDesk 
                    || fulfilledProfitQuota)
                {
                    return;
                }

                // Sell our item!
                if (!ai.AreHandsFree())
                {
                    // NEEDTOVALIDATE: Do we use a custom method for this?
                    companyDesk.PlaceItemOnCounter(npcController.Npc);
                }
                else
                {
                    // Swap to our next item!
                    if (ai.HasGrabbableObjectInInventory(FindNextSellableObject, out int itemSlot))
                    {
                        ai.SwitchItemSlotsAndSync(itemSlot);
                    }
                }
            }

        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        /// <summary>
        /// Helper function to check if the given <paramref name="item"/> is a object we can sell!
        /// </summary>
        /// <remarks>
        /// This was designed for use in <see cref="LethalBotAI.HasGrabbableObjectInInventory(Func{GrabbableObject?, bool}, out int)"/> calls.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool FindNextSellableObject(GrabbableObject? item)
        {
            if (item == null) return false;
            return ai.IsGrabbableObjectSellable(item, true, true);
        }

        /// <summary>
        /// This is the same code used internally for the DepositItemsDesk
        /// Just changed so it works with the bots!
        /// </summary>
        /*private void PlaceItemOnCounter()
        {
            // How did this happen?
            if (companyDesk == null || ai.HeldItem == null)
            {
                return;
            }
            if (companyDesk.deskObjectsContainer.GetComponentsInChildren<GrabbableObject>().Length < 12 && !companyDesk.inGrabbingObjectsAnimation && GameNetworkManager.Instance != null)
            {
                Vector3 vector = RoundManager.RandomPointInBounds(companyDesk.triggerCollider.bounds);
                vector.y = companyDesk.triggerCollider.bounds.min.y;
                if (Physics.Raycast(new Ray(vector + Vector3.up * 3f, Vector3.down), out var hitInfo, 8f, 1048640, QueryTriggerInteraction.Collide))
                {
                    vector = hitInfo.point;
                }
                vector.y += ai.HeldItem.itemProperties.verticalOffset;
                vector = companyDesk.deskObjectsContainer.transform.InverseTransformPoint(vector);
                companyDesk.AddObjectToDeskServerRpc(ai.HeldItem.gameObject.GetComponent<NetworkObject>());
                AddObjectToDeskServerRpc(ai.HeldItem.gameObject.GetComponent<NetworkObject>());
                npcController.Npc.DiscardHeldObject(placeObject: true, companyDesk.deskObjectsContainer, vector, matchRotationOfParent: false);
                Plugin.LogDebug("Discard held object called from Lethal Bot deposit items desk method.");
            }
        }

        // NEEDTOVALIDATE: Do we need a seperate version? 
        [ServerRpc(RequireOwnership = false)]
        private void AddObjectToDeskServerRpc(NetworkObjectReference grabbableObjectNetObject)
        {
            if (companyDesk == null)
            {
                return;
            }
            if (grabbableObjectNetObject.TryGet(out NetworkObject lastObjectAddedToDesk))
            {
                if (!companyDesk.itemsOnCounter.Contains(lastObjectAddedToDesk.GetComponentInChildren<GrabbableObject>()))
                {
                    companyDesk.itemsOnCounterNetworkObjects.Add(lastObjectAddedToDesk);
                    companyDesk.itemsOnCounter.Add(lastObjectAddedToDesk.GetComponentInChildren<GrabbableObject>());
                    AddObjectToDeskClientRpc(grabbableObjectNetObject);
                    FieldInfo privateDesk = AccessTools.Field(typeof(DepositItemsDesk), "grabObjectsTimer");
                    float grabObjectsTimer = (float)privateDesk.GetValue(companyDesk);
                    privateDesk.SetValue(companyDesk, Mathf.Clamp(grabObjectsTimer + 6f, 0f, 10f));
                    if (!companyDesk.doorOpen && (!companyDesk.currentMood.mustBeWokenUp || companyDesk.timesHearingNoise >= 5f))
                    {
                        companyDesk.OpenShutDoorClientRpc();
                    }
                }
            }
            else
            {
                Plugin.LogError("ServerRpc: Could not find networkobject in the object that was placed on desk.");
            }
        }

        [ClientRpc]
        private void AddObjectToDeskClientRpc(NetworkObjectReference grabbableObjectNetObject)
        {
            if (grabbableObjectNetObject.TryGet(out NetworkObject lastObjectAddedToDesk))
            {
                lastObjectAddedToDesk.gameObject.GetComponentInChildren<GrabbableObject>().EnablePhysics(enable: false);
            }
            else
            {
                Plugin.LogError("ClientRpc: Could not find networkobject in the object that was placed on desk.");
            }
        }*/
    }
}
