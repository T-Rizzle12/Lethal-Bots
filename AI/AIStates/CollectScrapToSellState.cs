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
            if (!ai.HasSpaceInInventory())
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
    }
}
