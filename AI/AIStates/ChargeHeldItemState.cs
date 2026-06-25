using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System;

namespace LethalBots.AI.AIStates
{
    public class ChargeHeldItemState : AIState
    {
        private GrabbableObject? itemToCharge;
        private bool chargeAllHeldItems;
        public ChargeHeldItemState(AIState oldState, GrabbableObject? itemToCharge, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.ChargeHeldItem;
            if (itemToCharge != null)
            {
                this.itemToCharge = itemToCharge;
            }
            else
            {
                this.itemToCharge = ai.HeldItem;
            }
        }

        public ChargeHeldItemState(AIState oldState, bool chargeAllHeldItems = false, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.ChargeHeldItem;
            this.chargeAllHeldItems = chargeAllHeldItems;
            this.itemToCharge = ai.HeldItem;
        }

        public ChargeHeldItemState(LethalBotAI ai, GrabbableObject? itemToCharge, AIState? changeToOnEnd = null) : base(ai)
        {
            CurrentState = EnumAIStates.ChargeHeldItem;
            previousAIState = changeToOnEnd ?? new ReturnToShipState(this);
            if (itemToCharge != null)
            {
                this.itemToCharge = itemToCharge;
            }
            else
            {
                this.itemToCharge = ai.HeldItem;
            }
        }

        public override void DoAI()
        {
            // Check for enemies
            PlayerControllerB lethalBotController = npcController.Npc;
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // We are not at the ship, go back to the previous state!
            if (!lethalBotController.isInElevator && !lethalBotController.isInHangarShipRoom)
            {
                ChangeBackToPreviousState();
                return;
            }

            // We are in the terminal, we should leave!
            if (lethalBotController.inTerminalMenu)
            {
                ai.LeaveTerminal();
                return;
            }

            // We are not holding the item, we should change to it!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem != this.itemToCharge)
            {
                if (ai.HasGrabbableObjectInInventory(this.itemToCharge, out int itemSlot))
                {
                    if (heldItem != null && heldItem.itemProperties.twoHanded)
                    {
                        // We are holding an two handed item, we should drop it!
                        lethalBotController.DiscardHeldObject();
                        LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                        return;
                    }
                    ai.SwitchItemSlotsAndSync(itemSlot);
                    return;
                }
                // We don't have the item in our inventory! Mark it as null and go back to the previous state!
                this.itemToCharge = null;
            }

            // If we are holding an item with a battery, we should charge it!
            if (this.itemToCharge != null
                && !ItemsManager.HasRequiredCharge(this.itemToCharge, requiredChargeLevel: 0.9f))
            {
                // We should charge the item if we can!
                if (SingletonManager.ItemCharger.TryGet(out ItemCharger? itemCharger))
                {
                    InteractTrigger itemChargerTrigger = itemCharger.triggerScript;
                    if (itemChargerTrigger != null)
                    {
                        // We should move to the item charger!
                        float sqrDistFromCharger = (itemChargerTrigger.playerPositionNode.position - lethalBotController.transform.position).sqrMagnitude;
                        if (sqrDistFromCharger > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                        {
                            ai.SetDestinationToPositionLethalBotAI(itemChargerTrigger.playerPositionNode.position);
                            if (!npcController.WaitForFullStamina && sqrDistFromCharger > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                            {
                                npcController.OrderToSprint();
                            }
                            else if (npcController.WaitForFullStamina || sqrDistFromCharger < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
                            {
                                npcController.OrderToStopSprint();
                            }
                            ai.OrderMoveToDestination();
                        }
                        else
                        {
                            // We don't need to move now!
                            ai.StopMoving();
                            if (!ai.TurnOffHeldItem())
                            {
                                ai.UseItemCharger(itemCharger);
                            }
                        }
                    }
                }
            }
            else if (!chargeAllHeldItems || !HasItemToCharge(ai, out itemToCharge))
            {
                // We don't need to charge the item, go back to the previous state!
                ChangeBackToPreviousState();
                return;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        /// <summary>
        /// Static helper function that checks if the bot has something to charge
        /// </summary>
        /// <param name="lethalBotAI">The bot of whose inventory we want to check.</param>
        /// <param name="itemToCharge">The item we found that needs to be charged.</param>
        /// <returns><see langword="true"/>: we have an item we want to charge, <see langword="false"/>: we didn't find an item to charge.</returns>
        public static bool HasItemToCharge(LethalBotAI lethalBotAI, out GrabbableObject? itemToCharge)
        {
            // Check if the lethalBot has any item in its inventory that needs to be charged.
            if (lethalBotAI.HasGrabbableObjectInInventory(item => !ItemsManager.HasRequiredCharge(item, requiredChargeLevel: 0.9f), out int objectSlot))
            {
                itemToCharge = lethalBotAI.GetItemAtSlot(objectSlot);
                return true;
            }

            itemToCharge = null;
            return false;
        }
    }
}
