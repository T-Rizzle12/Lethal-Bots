using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils;
using ReservedItemSlotCore.Data;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    public class CollectPurchasedItemsState : AIState
    {
        private float waitNoItemTimer = 0f;
        private static ItemDropship? _itemDropship;
        internal static ItemDropship? ItemDropship
        {
            get
            {
                if (_itemDropship == null)
                {
                    _itemDropship = Object.FindObjectOfType<ItemDropship>();
                }
                return _itemDropship;
            }
        }

        public static bool CollectDeliveredItems { internal set; get; }

        public CollectPurchasedItemsState(AIState oldState, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.CollectPurchasedItems;
        }

        public CollectPurchasedItemsState(LethalBotAI ai, AIState? changeToOnEnd = null) : base(ai, changeToOnEnd)
        {
            CurrentState = EnumAIStates.CollectPurchasedItems;
        }

        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // We couldn't find the item dropship?
            if (ItemDropship == null || !IsPossible())
            {
                ChangeBackToPreviousState();
                return;
            }

            // Say hello to the ugliest thing in the world.
            // I got this from the mod Problematic Pilotry.
            Vector3 ourPos = npcController.Npc.transform.position;
            Vector3 dropshipLandingPos = ItemDropship.transform.parent.gameObject.transform.position;
            float sqrDistToLandingSpot = (dropshipLandingPos - ourPos).sqrMagnitude;
            float sqrDistToInteractTrigger = (ItemDropship.transform.position - ourPos).sqrMagnitude;

            // Move to the landing spot if we are not nearby it!
            float grabDistance = npcController.Npc.grabDistance; // grabDistance determines our interact trigger distance!
            if (Mathf.Min(sqrDistToInteractTrigger, sqrDistToLandingSpot) > grabDistance * grabDistance)
            {
                if (!npcController.WaitForFullStamina && sqrDistToLandingSpot > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else if (npcController.WaitForFullStamina || sqrDistToLandingSpot < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
                {
                    npcController.OrderToStopSprint();
                }

                // If need to wait nearby the landing spot, we should do so!
                if ((IsDropShipLanding() || IsPurchasedItemsInbound()) && !ItemDropship.shipLanded && sqrDistToLandingSpot <= Const.DISTANCE_TO_WAIT_FOR_DROPSHIP * Const.DISTANCE_TO_WAIT_FOR_DROPSHIP)
                {
                    ai.StopMoving();

                    // Don't get too close until it has landed!
                    // We are WAY TOO CLOSE, FALLBACK!
                    if (sqrDistToLandingSpot < Const.DISTANCE_FALLBACK_FROM_DROPSHIP * Const.DISTANCE_FALLBACK_FROM_DROPSHIP)
                    {
                        Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - dropshipLandingPos + Vector3.up * 0.2f);
                        ray.direction = new Vector3(ray.direction.x, 0f, ray.direction.z);
                        Vector3 pos = (!Physics.Raycast(ray, out RaycastHit hit, Const.DISTANCE_FALLBACK_FROM_DROPSHIP, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) ? ray.GetPoint(Const.DISTANCE_FALLBACK_FROM_DROPSHIP) : hit.point;

                        // GO GO GO!
                        ai.SetDestinationToPositionLethalBotAI(RoundManager.Instance.GetNavMeshPosition(pos, default, 2.7f));
                        ai.OrderMoveToDestination();
                    }
                }
                else
                {
                    ai.SetDestinationToPositionLethalBotAI(dropshipLandingPos);
                    ai.OrderMoveToDestination();
                }
            }
            else
            {
                // Stop moving!
                ai.StopMoving();

                // Don't get too close until it has landed!
                if (IsDropShipLanding() || IsPurchasedItemsInbound() && !ItemDropship.shipLanded)
                {
                    // We are WAY TOO CLOSE, FALLBACK!
                    if (sqrDistToLandingSpot < Const.DISTANCE_FALLBACK_FROM_DROPSHIP * Const.DISTANCE_FALLBACK_FROM_DROPSHIP)
                    {
                        Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - dropshipLandingPos + Vector3.up * 0.2f);
                        ray.direction = new Vector3(ray.direction.x, 0f, ray.direction.z);
                        Vector3 pos = (!Physics.Raycast(ray, out RaycastHit hit, Const.DISTANCE_FALLBACK_FROM_DROPSHIP, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) ? ray.GetPoint(Const.DISTANCE_FALLBACK_FROM_DROPSHIP) : hit.point;
                    
                        // GO GO GO!
                        ai.SetDestinationToPositionLethalBotAI(RoundManager.Instance.GetNavMeshPosition(pos, default, 2.7f));
                        ai.OrderMoveToDestination();
                    }
                    return;
                }

                // Collect the items!
                if (!ItemDropship.shipDoorsOpened && ItemDropship.shipLanded)
                {
                    ItemDropship.triggerScript.Interact(npcController.Npc.thisPlayerBody);
                    CollectDeliveredItems = true;
                }
                else
                {
                    GrabbableObject? objectToCollect = GrabDeliveredObjects();
                    if (objectToCollect == null)
                    {
                        const float maxWaitTime = 3f;
                        if (waitNoItemTimer < maxWaitTime)
                        {
                            waitNoItemTimer += ai.AIIntervalTime;
                            return;
                        }
                        CollectDeliveredItems = false;
                        ai.State = new ReturnToShipState(this);
                        return;
                    }
                    else if (!ai.HasSpaceInInventory(objectToCollect))
                    {
                        waitNoItemTimer = 0f;
                        CollectDeliveredItems = true;
                        ai.State = new ReturnToShipState(this);
                        return;
                    }
                    else
                    {
                        waitNoItemTimer = 0f;
                        CollectDeliveredItems = true;
                        ai.State = new FetchingObjectState(this, objectToCollect);
                        return;
                    }
                }
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Nothing for now........
            return;
        }

        /// <summary>
        /// Do we have the right conditions to start this state.
        /// </summary>
        /// <returns></returns>
        public static bool IsPossible()
        {
            // TODO: Hook into ItemDropship to set this when the doors are opened,
            // should fix some edge cases......
            if (CollectDeliveredItems)
            {
                return true;
            }

            // FIXME: We can't tell the difference between a legit order or one to distact eyeless dogs........
            // For now, only help collect orders while at the company building!
            if (!LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                return false; 
            }

            if (ItemDropship != null 
                && !ItemDropship.shipDoorsOpened
                && (ItemDropship.shipLanded || PatchesUtil.itemsToDeliverField.Invoke(ItemDropship).Count > 0))
            {
                return true; 
            }

            if (IsPurchasedItemsInbound())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Is the <see cref="ItemDropship"/> landing?
        /// </summary>
        /// <returns></returns>
        public static bool IsDropShipLanding()
        {
            if (ItemDropship != null
                && !ItemDropship.shipLanded
                && (ItemDropship.deliveringOrder 
                    || ItemDropship.deliveringVehicle 
                    || PatchesUtil.itemsToDeliverField.Invoke(ItemDropship).Count > 0))
            {
                return true;
            }
            return false; 
        }

        /// <summary>
        /// Do we have items that were recently purchased on the terminal inbound?
        /// </summary>
        /// <returns></returns>
        public static bool IsPurchasedItemsInbound()
        {
            Terminal? terminal = TerminalManager.Instance.GetTerminal();
            if (terminal != null && terminal.orderedItemsFromTerminal.Count > 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds a delivered <see cref="GrabbableObject"/> for the bot to collect!
        /// </summary>
        /// <returns></returns>
        private GrabbableObject? GrabDeliveredObjects()
        {
            // No dropship?
            if (ItemDropship == null)
            {
                return null;
            }

            Vector3 dropshipLandingPos = ItemDropship.transform.parent.gameObject.transform.position;
            Vector3 ourPos = npcController.Npc.transform.position;
            GrabbableObject? bestItem = null;
            float closestItemDistSqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? item = gameObject.GetComponent<GrabbableObject>();
                if (item != null)
                {
                    // Only grab stuff near the dropship!
                    float itemDistSqr = (item.transform.position - dropshipLandingPos).sqrMagnitude;
                    if (itemDistSqr > Const.DISTANCE_ITEM_TO_COLLECT * Const.DISTANCE_ITEM_TO_COLLECT) // Cheap way to limit range!
                    {
                        continue;
                    }

                    // Check distance to us!
                    itemDistSqr = (item.transform.position - ourPos).sqrMagnitude;
                    if (itemDistSqr < closestItemDistSqr 
                        && ai.IsGrabbableObjectGrabbable(item))
                    {
                        bestItem = item;
                        closestItemDistSqr = itemDistSqr;
                    }
                }
            }
            return bestItem;
        }
    }
}
