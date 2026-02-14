using System;
using System.Collections.Generic;
using System.Text;
using LethalBots.Enums;
using LethalBots.Managers;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot goes and collects scrap to sell.
    /// Bots will ignore items added to the blacklist!
    /// </summary>
    public class CollectScrapToSellState : AIState
    {
        public CollectScrapToSellState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.CollectScrapToSell;
        }

        public override void DoAI()
        {
            // If we are not at the company building return!
            if (!LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Our inventory is full, lets go!
            if (!ai.HasSpaceInInventory() || CanWeFulfillTheProfitQuota())
            {
                ai.State = new SellScrapState(this);
                return;
            }

            // Check for object to grab
            GrabbableObject? grabbableObject = ai.LookingForObjectsToSell();
            if (grabbableObject != null)
            {
                ai.State = new FetchingObjectState(this, grabbableObject, EnumGrabbableObjectCall.Selling);
                return;
            }
            // Do we have scrap, lets sell it!
            // If there are still items on the desk, we should sell them first!
            else if (ai.HasSellableItemInInventory() 
                || LethalBotManager.AreThereItemsOnDesk())
            {
                ai.State = new SellScrapState(this);
                return;
            }
            // No scrap, lets return to our ship then!
            else
            {
                ai.State = new ReturnToShipState(this);
                return;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        private bool CanWeFulfillTheProfitQuota()
        {
            // If we don't have a reference to the TimeOfDay, we can't check if we can fulfill the profit quota, so return false.
            // NOTE: This should never happen, but who knows.
            TimeOfDay timeOfDay = TimeOfDay.Instance;
            if (timeOfDay == null)
            {
                return false;
            }

            // Check if we can fulfill the profit quota with the scrap we have already sold and the scrap we have in our inventory.
            int fulfilledQuota = timeOfDay.quotaFulfilled + LethalBotManager.GetValueOfItemsOnDesk();
            int valueOfInventory = 0;
            foreach (var item in npcController.Npc.ItemSlots)
            {
                // We have to check if the item is null
                // because the bot might have empty inventory slots.
                if (item != null)
                {
                    valueOfInventory += item.scrapValue;
                }
            }

            // If the value of the scrap we have already sold and the scrap we have in our inventory is
            // greater than or equal to the profit quota, then we can fulfill the profit quota!
            if (fulfilledQuota + valueOfInventory >= timeOfDay.profitQuota)
            {
                return true;
            }

            return false;
        }
    }
}
