using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    public class GrabLoadoutState : AIState
    {
        List<string> itemsToGrab = new List<string>();

        public GrabLoadoutState(AIState oldState, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.GrabLoadout;
        }

        public GrabLoadoutState(LethalBotAI ai, AIState? changeToOnEnd = null) : base(ai, changeToOnEnd)
        {
            CurrentState = EnumAIStates.GrabLoadout;
        }

        public override void OnEnterState()
        {
            // First things first, find out what we need to grab!
            itemsToGrab.Clear();
            foreach (var item in ai.LethalBotIdentity.Loadout.Items)
            {
                if (item != null
                    && !itemsToGrab.Contains(item.itemName))
                {
                    itemsToGrab.Add(item.itemName);
                }
            }
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // We got everything we needed.
            // Lets do this!
            if (itemsToGrab.Count <= 0 || !ai.HasSpaceInInventory())
            {
                ChangeBackToPreviousState();
                return;
            }

            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // We are not at the ship, go back to the previous state!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
            {
                ChangeBackToPreviousState();
                return;
            }

            string itemToGrab = itemsToGrab[0]; // Lets grab our gear!
            itemsToGrab.RemoveAt(0); // Remove from list, even if we don't find it.
            GrabbableObject? grabbableObject = FindItemWithName(itemToGrab);
            if (grabbableObject != null)
            {
                ai.State = new FetchingObjectState(this, grabbableObject);
                return;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Nothing to say here.......for now!
            return;
        }

        /// <summary>
        /// Helper function that finds the given <paramref name="name"/> 
        /// on the ship, if one exists on the ship.
        /// </summary>
        /// <param name="name">The <see cref="GrabbableObject.itemProperties"/>'s <see cref="Item.itemName"/> to search for!</param>
        /// <returns>The grabbable object that had the same <see cref="Item.itemName"/> as <paramref name="name"/></returns>
        private GrabbableObject? FindItemWithName(string name)
        {
            // First, we need to check if we have the item in our inventory already!
            if (ai.HasGrabbableObjectInInventory(item => item != null && item.itemProperties.itemName == name, out _))
            {
                return null; // Don't return the item, we will just have the bot skip this think!
            }

            // So, we don't have the item in our inventory, lets check the ship!
            GrabbableObject? closestItem = null;
            float closestItemSqr = float.MaxValue;
            LevelWeatherType levelWeatherType = TimeOfDay.Instance.currentLevelWeather;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? item = gameObject.GetComponent<GrabbableObject>();
                if (item != null
                    && item.isInShipRoom
                    && item.itemProperties.itemName == name 
                    && (levelWeatherType != LevelWeatherType.Stormy || !item.itemProperties.isConductiveMetal)) // Don't pickup conductive items during storms!
                {
                    float itemSqr = (item.transform.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (itemSqr < closestItemSqr
                        && ai.IsGrabbableObjectGrabbable(item)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestItemSqr = itemSqr;
                        closestItem = item;
                    }
                }
            }

            return closestItem;
        }
    }
}
