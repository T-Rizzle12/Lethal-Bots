using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot chooses to use the <see cref="TetraChemicalItem"/> aka TZPInhalant
    /// to get a speed and stamina boost!
    /// </summary>
    public class UseTZPInhalantState : AIState
    {
        private float desiredDrunknessAmount;
        private GrabbableObject? droppedHeldItem;
        
        public UseTZPInhalantState(AIState oldState, float desiredDrunknessAmount) : base(oldState)
        {
            CurrentState = EnumAIStates.UseTZPInhalant;
            this.desiredDrunknessAmount = Mathf.Clamp01(desiredDrunknessAmount);
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                // If our desired drunkness amount is less than or equal to 0, we should not use the TZP inhalant!
                if (this.desiredDrunknessAmount <= 0)
                {
                    Plugin.LogError($"A negative number or zero was given for desired amount of drunkness. Got {this.desiredDrunknessAmount}");
                    ChangeBackToPreviousState();
                    return;
                }
            }
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            // Make sure we release the held item button when finished!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem != null)
            {
                // Wait until the cooldown is over!
                // NOTE: If we fail somehow, the default UseHeldItem will
                // make us release the use key as well.
                heldItem.UseItemOnClient(false);
            }
            if (droppedHeldItem != null)
            {
                LethalBotAI.DictJustDroppedItems.Remove(droppedHeldItem); //HACKHACK: Since DropItem sets the just dropped item timer, we clear it here!
            }
            base.OnExitState(newState);
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

            // If we reach the targeted drunkness level, our work here is done!
            GrabbableObject? heldItem = ai.HeldItem;
            TetraChemicalItem? tzpItem = heldItem as TetraChemicalItem;
            if (desiredDrunknessAmount <= lethalBotController.drunkness)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Make sure we actually have the TZP in our inventory!
            int tzpSlot = tzpItem != null && !tzpItem.itemUsedUp ? lethalBotController.currentItemSlot : -1;
            if (tzpSlot == -1)
            {
                // We don't have any TZP in our inventory!
                tzpItem = null;
                if (!ai.HasGrabbableObjectInInventory(FindObject, out tzpSlot))
                {
                    ChangeBackToPreviousState();
                    return;
                }
            }

            // We have no need to move
            ai.StopMoving();

            // Use the TZP until we reach the desired level of drunkness or its empty!
            if (!lethalBotController.inAnimationWithEnemy)
            {
                // Make sure we are actually holding the TZP!
                if (heldItem == null || tzpItem == null)
                {
                    // We need to drop our two handed item first!
                    if (heldItem != null && heldItem.itemProperties.twoHanded)
                    {
                        droppedHeldItem = heldItem;
                        lethalBotController.DiscardHeldObject();
                        return;
                    }
                    if (lethalBotController.activatingItem)
                    {
                        heldItem?.UseItemOnClient(false);
                        return;
                    }
                    ai.SwitchItemSlotsAndSync(tzpSlot);
                    return;
                }

                // Use it!
                if (!lethalBotController.activatingItem && ai.CanUseHeldItem())
                { 
                    tzpItem.UseItemOnClient(true); 
                }
            }
        }

        public override void UseHeldItem()
        {
            // Override the default logic for the TZP, since we will be managing it here!
            if (ai.HeldItem is TetraChemicalItem)
            {
                return;
            }
            base.UseHeldItem();
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        /// <summary>
        /// Helper function to check if the given <paramref name="item"/> is a usable <see cref="TetraChemicalItem"/>!
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected override bool FindObject(GrabbableObject item)
        {
            if (item is TetraChemicalItem tempTZP
                && !tempTZP.itemUsedUp)
            {
                return true;
            }
            return false;
        }
    }
}
