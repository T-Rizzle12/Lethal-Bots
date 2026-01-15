using LethalBots.Constants;
using LethalBots.Enums;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bots try to get close and grab a item
    /// </summary>
    public class FetchingObjectState : AIState
    {
        private EnumGrabbableObjectCall enumGrabbable;
        private int grabAttempts;

        /// <summary>
        /// <inheritdoc cref="AIState(AIState)"/>
        /// </summary>
        public FetchingObjectState(AIState state, GrabbableObject? targetItem, EnumGrabbableObjectCall enumGrabbable = EnumGrabbableObjectCall.Default, AIState? changeToOnEnd = null) : base(state, changeToOnEnd)
        {
            CurrentState = EnumAIStates.FetchingObject;
            this.TargetItem = targetItem;
            this.enumGrabbable = enumGrabbable;
        }

        /// <summary>
        /// <inheritdoc cref="AIState.DoAI"/>
        /// </summary>
        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                grabAttempts++;
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Target item invalid to grab
            if (!ai.HasSpaceInInventory()
                || this.TargetItem == null 
                || !IsObjectGrabbable() 
                || grabAttempts > Const.MAX_GRAB_OBJECT_ATTEMPTS)
            {
                if (this.TargetItem != null)
                {
                    LethalBotAI.DictJustDroppedItems[this.TargetItem] = Time.realtimeSinceStartup;
                }
                this.TargetItem = null;
                ChangeBackToPreviousState();
                return;
            }

            // Close enough to item for grabbing, attempt to grab
            float sqrMagDistanceItem = (this.TargetItem.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrMagDistanceItem < npcController.Npc.grabDistance * npcController.Npc.grabDistance)
            {
                if (!npcController.Npc.inAnimationWithEnemy 
                    && !npcController.Npc.activatingItem)
                {
                    GrabbableObject? heldItem = ai.HeldItem;
                    if (heldItem != null && heldItem.itemProperties.twoHanded)
                    {
                        ai.DropItem();
                    }
                    ai.StopMoving();
                    ai.GrabItemServerRpc(this.TargetItem.NetworkObject, itemGiven: false);
                    if (heldItem != null)
                    {
                        LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                        this.TargetItem = heldItem;
                        return;
                    }
                    this.TargetItem = null;
                    ChangeBackToPreviousState();
                    return;
                }
            }

            // Else get close to item
            Vector3 targetItemPos = RoundManager.Instance.GetNavMeshPosition(this.TargetItem.transform.position, default, npcController.Npc.grabDistance);
            ai.SetDestinationToPositionLethalBotAI(targetItemPos);

            // If we can't path to the object, there might be a locked door in our way!
            // The chances of this are VERY low, but it still may happen!
            if (!ai.IsValidPathToTarget(targetItemPos, false))
            {
                DoorLock? lockedDoor = ai.UnlockDoorIfNeeded(200f, false);
                if (lockedDoor != null)
                {
                    ai.State = new UseKeyOnLockedDoorState(this, lockedDoor);
                    return;
                }
            }

            // Look at item or not if hidden by stuff
            if (!Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, this.TargetItem.transform.position + Vector3.up * 0.05f, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault) 
                || hitInfo.transform.GetComponent<GrabbableObject>() == this.TargetItem)
            {
                npcController.OrderToLookAtPosition(this.TargetItem.transform.position, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime);
            }
            else
            {
                npcController.OrderToLookForward();
            }

            // Sprint if far enough from the item
            if (sqrMagDistanceItem > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING 
                || this.TargetItem is CaveDwellerPhysicsProp)
            {
                npcController.OrderToSprint();
            }
            else
            {
                npcController.OrderToStopSprint();
            }

            ai.OrderMoveToDestination();
        }

        // Checks if our target object is grabbable!
        private bool IsObjectGrabbable()
        {
            if (this.TargetItem == null)
            {  
                return false; 
            }

            if (enumGrabbable == EnumGrabbableObjectCall.Selling)
            {
                return ai.IsGrabbableObjectSellable(TargetItem);
            }
            return ai.IsGrabbableObjectGrabbable(TargetItem, enumGrabbable);
        }

        public override void OnBotStuck()
        {
            base.OnBotStuck();
            grabAttempts++;
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Talk if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.FoundLoot,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = false,
                CutCurrentVoiceStateToTalk = true,
                CanRepeatVoiceState = false,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        public override string GetBillboardStateIndicator()
        {
            return "!!";
        }
    }
}
