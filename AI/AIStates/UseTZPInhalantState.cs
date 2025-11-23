using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
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
        //private static readonly FieldInfo tetraChemicalItemFuelField = AccessTools.Field(typeof(TetraChemicalItem), "fuel");
        // Now you may ask, why use a delegate function here, I wanted to try something new!
        //public static readonly Func<TetraChemicalItem, float>? getFuelDelegate = CreateFuelGetter();
        
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

        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // If we reach the targeted drunkness level, our work here is done!
            GrabbableObject? heldItem = ai.HeldItem;
            TetraChemicalItem? tzpItem = heldItem as TetraChemicalItem;
            if (desiredDrunknessAmount <= npcController.Npc.drunkness)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Make sure we actually have the TZP in our inventory!
            int tzpSlot = tzpItem != null && !tzpItem.itemUsedUp ? npcController.Npc.currentItemSlot : -1;
            if (tzpSlot == -1)
            {
                // We don't have any TZP in our inventory!
                if (!ai.HasGrabbableObjectInInventory(IsUsableTZPItem, out tzpSlot))
                {
                    ChangeBackToPreviousState();
                    return;
                }
            }

            // We have no need to move
            ai.StopMoving();

            // Use the TZP until we reach the desired level of drunkness or its empty!
            if (!npcController.Npc.inAnimationWithEnemy)
            {
                // Make sure we are actually holding the TZP!
                if (heldItem == null || tzpItem == null)
                {
                    // We need to drop our two handed item first!
                    if (heldItem != null && heldItem.itemProperties.twoHanded)
                    {
                        droppedHeldItem = heldItem;
                        ai.DropItem();
                        return;
                    }
                    ai.SwitchItemSlotsAndSync(tzpSlot);
                    return;
                }

                // Use it!
                if (!npcController.Npc.activatingItem)
                { 
                    tzpItem.UseItemOnClient(true); 
                }
            }
        }

        protected override void ChangeBackToPreviousState()
        {
            // Make sure we release the held item button when finished!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem != null)
            {
                // Wait until the cooldown is over!
                if (heldItem.RequireCooldown())
                {
                    return;
                }
                heldItem.UseItemOnClient(false);
            }
            if (droppedHeldItem != null)
            {
                LethalBotAI.DictJustDroppedItems.Remove(droppedHeldItem); //HACKHACK: Since DropItem sets the just dropped item timer, we clear it here!
            }
            base.ChangeBackToPreviousState();
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
        /// <remarks>
        /// This was designed for use in <see cref="LethalBotAI.HasGrabbableObjectInInventory(Func{GrabbableObject?, bool}, out int)"/> calls.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool IsUsableTZPItem(GrabbableObject? item)
        {
            if (item != null
                && item is TetraChemicalItem tempTZP
                && !tempTZP.itemUsedUp)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Helper function to check if the given <paramref name="tzpItem"/> has fuel remaining!
        /// </summary>
        /// <param name="tzpItem"></param>
        /// <returns>true: <paramref name="tzpItem"/> has fuel. false: <paramref name="tzpItem"/> is out of fuel.</returns>
        /*public static bool DoesTZPHaveFuelRemaining(TetraChemicalItem tzpItem)
        {
            if (getFuelDelegate == null)
            {
                Plugin.LogError("Fuel getter delegate was not initialized.");
                return false;
            }
            float fuelRemaining = getFuelDelegate(tzpItem);
            return fuelRemaining > 0;
        }*/

        /// <summary>
        /// Helper function to create a delegate to grab the fuel remaining on <see cref="TetraChemicalItem"/>s!
        /// </summary>
        /// <returns>A function to grab the private fuel field or null if we failed!</returns>
        /*private static Func<TetraChemicalItem, float>? CreateFuelGetter()
        {
            FieldInfo? fuelField = AccessTools.Field(typeof(TetraChemicalItem), "fuel");
            if (fuelField == null)
            {
                Plugin.LogError("Could not find 'fuel' field on TetraChemicalItem.");
                return null;
            }

            // Create a dynamic method delegate to avoid boxing
            var dm = new DynamicMethod(
                "GetFuel",
                typeof(float),
                new[] { typeof(TetraChemicalItem) },
                typeof(TetraChemicalItem), // owner
                true // skip visibility
            );

            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); // Load first argument (TetraChemicalItem)
            il.Emit(OpCodes.Ldfld, fuelField); // Load the 'fuel' field
            il.Emit(OpCodes.Ret); // Return

            return (Func<TetraChemicalItem, float>)dm.CreateDelegate(typeof(Func<TetraChemicalItem, float>));
        }*/
    }
}
