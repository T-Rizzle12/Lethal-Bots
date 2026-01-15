using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot just saw a door to unlock (see: <see cref="LethalBotAI.UnlockDoorIfNeeded(float, bool, float)"><c>LethalBotAI.UnlockDoorIfNeeded</c></see>).
    /// The bot will try to open the door with a key if they have one.
    /// FIXME: The bot should choose the closest side of the door!
    /// </summary>
    public class UseKeyOnLockedDoorState : AIState
    {
        private DoorLock? targetDoor = null;
        private Vector3? doorPos = null;
        private float attemptToUnlockTimer;
        private GrabbableObject? droppedHeldItem;
        private bool placedLockpicker = false;
        public UseKeyOnLockedDoorState(AIState oldState, DoorLock? doorLock) : base(oldState)
        {
            CurrentState = EnumAIStates.UseKeyOnLockedDoor;

            // We need to find the position to approach the door from!
            targetDoor = doorLock;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (targetDoor != null)
                {
                    // Get potential door positions
                    Vector3? doorPos1 = LethalBotAI.GetOffsetLockPickerPosition(targetDoor);
                    Vector3? doorPos2 = LethalBotAI.GetOffsetLockPickerPosition(targetDoor, true);

                    // Check path validity and distance for both positions
                    float? doorDistance1 = null;
                    float? doorDistance2 = null;

                    if (doorPos1.HasValue && ai.IsValidPathToTarget(doorPos1.Value, true))
                    {
                        doorDistance1 = ai.pathDistance;
                    }

                    if (doorPos2.HasValue && ai.IsValidPathToTarget(doorPos2.Value, true))
                    {
                        doorDistance2 = ai.pathDistance;
                    }

                    // Select the closest valid door position
                    if (doorDistance1.HasValue && doorDistance2.HasValue)
                    {
                        // Both positions are valid, choose the closest
                        this.doorPos = (doorDistance1.Value <= doorDistance2.Value) ? doorPos1 : doorPos2;
                    }
                    else if (doorDistance1.HasValue)
                    {
                        // Only doorPos1 is valid
                        this.doorPos = doorPos1;
                    }
                    else if (doorDistance2.HasValue)
                    {
                        // Only doorPos2 is valid
                        this.doorPos = doorPos2;
                    }
                    else
                    {
                        // No valid positions found, set targetDoor to null
                        this.targetDoor = null;
                    }
                }
            }
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            if (droppedHeldItem != null)
            {
                LethalBotAI.DictJustDroppedItems.Remove(droppedHeldItem); //HACKHACK: Since DropItem sets the just dropped item timer, we clear it here!
            }
            base.OnExitState(newState);
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

            // No door to unlock!
            if (targetDoor == null || !targetDoor.isLocked || targetDoor.isPickingLock)
            {
                // Wait for the lockpicker to be finished!
                if (placedLockpicker && targetDoor != null && targetDoor.isPickingLock)
                {
                    StartLookingAroundCoroutine();
                    return;
                }
                ChangeBackToPreviousState();
                return;
            }
            else if (!doorPos.HasValue || !ai.IsValidPathToTarget(doorPos.Value, false))
            {
                // Get potential door positions
                Vector3? doorPos1 = LethalBotAI.GetOffsetLockPickerPosition(targetDoor);
                Vector3? doorPos2 = LethalBotAI.GetOffsetLockPickerPosition(targetDoor, true);

                // Check path validity and distance for both positions
                float? doorDistance1 = null;
                float? doorDistance2 = null;

                if (doorPos1.HasValue && ai.IsValidPathToTarget(doorPos1.Value, true))
                {
                    doorDistance1 = ai.pathDistance;
                }

                if (doorPos2.HasValue && ai.IsValidPathToTarget(doorPos2.Value, true))
                {
                    doorDistance2 = ai.pathDistance;
                }

                // Select the closest valid door position
                if (doorDistance1.HasValue && doorDistance2.HasValue)
                {
                    // Both positions are valid, choose the closest
                    this.doorPos = (doorDistance1.Value <= doorDistance2.Value) ? doorPos1 : doorPos2;
                }
                else if (doorDistance1.HasValue)
                {
                    // Only doorPos1 is valid
                    this.doorPos = doorPos1;
                }
                else if (doorDistance2.HasValue)
                {
                    // Only doorPos2 is valid
                    this.doorPos = doorPos2;
                }
                else
                {
                    // No valid positions found, set targetDoor to null
                    this.targetDoor = null;
                    return;
                }
            }

            // Make sure we actually have a key!
            int keySlot = ai.IsHoldingKey() ? npcController.Npc.currentItemSlot : -1;
            if (keySlot == -1)
            {
                if (!ai.TryFindItemInInventory(FindObject, FindBetterObject, out keySlot))
                {
                    // We don't have a key, exit early!
                    // If we were trying to grab a item past a locked door,
                    // we set it as recently dropped so we don't go after it for a bit.
                    if (this.TargetItem != null)
                    {
                        LethalBotAI.DictJustDroppedItems[this.TargetItem] = Time.realtimeSinceStartup;
                        this.TargetItem = null;
                    }
                    ChangeBackToPreviousState();
                    return;
                }
            }

            // Look at door or not if hidden by stuff
            // NOTE: 2816 is the layer keys and lockpickers use in their raycast checks.
            Vector3 lockerPickerPos = GetClosestSideToDoor();
            if (attemptToUnlockTimer > 0.0f // If we are trying to unlock, always look at the door
                || !Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, lockerPickerPos, out RaycastHit hitInfo, 2816) // Can we see the door?
                || hitInfo.transform.GetComponent<DoorLock>() == this.targetDoor  // Did we hit the door?
                || hitInfo.transform.GetComponent<TriggerPointToDoor>()?.pointToDoor == this.targetDoor) // Did we hit the door trigger?
            {
                npcController.OrderToLookAtPosition(lockerPickerPos, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime); // Look at the door so we can use the key or lockpicker
            }
            else
            {
                npcController.OrderToLookForward(); // Look where we were going
            }

            // Close enough to use item, attempt to use
            float sqrMagDistanceDoor = (doorPos.Value - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrMagDistanceDoor < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                if (!npcController.Npc.inAnimationWithEnemy
                    && !npcController.Npc.activatingItem)
                {
                    ai.StopMoving();
                    GrabbableObject? heldItem = ai.HeldItem;
                    if (heldItem != null && !FindObject(heldItem) && heldItem.itemProperties.twoHanded)
                    {
                        droppedHeldItem = heldItem;
                        ai.DropItem();
                        return;
                    }
                    else if (heldItem == null || !FindObject(heldItem))
                    {
                        ai.SwitchItemSlotsAndSync(keySlot);
                        return;
                    }

                    // We attempt to open the door the correct way, but if it takes too long just give up!
                    if (attemptToUnlockTimer > Const.TIMER_USE_KEY_UNSTUCK)
                    {
                        if (heldItem is KeyItem)
                        {
                            this.targetDoor.UnlockDoorSyncWithServer();
                            npcController.Npc.DespawnHeldObject();
                        }
                        else if (heldItem is LockPicker lockPicker)
                        {
                            MethodInfo getLockPickerDoorPositionMethod = AccessTools.Method(typeof(LockPicker), "GetLockPickerDoorPosition");
                            bool lockPicker1 = (bool)getLockPickerDoorPositionMethod.Invoke(lockPicker, new object[] { this.targetDoor });
                            lockPicker.PlaceLockPickerServerRpc(this.targetDoor.NetworkObject, lockPicker1);
                            lockPicker.PlaceOnDoor(this.targetDoor, lockPicker1);
                            placedLockpicker = true;
                        }
                        else
                        {
                            this.targetDoor.UnlockDoorSyncWithServer();
                        }
                        return;
                    }
                    attemptToUnlockTimer += ai.AIIntervalTime;
                    heldItem.UseItemOnClient(true); // Should I call ItemActivate instead?
                    if (heldItem is LockPicker)
                    {
                        placedLockpicker = true;
                    }
                    return;
                }
            }

            // Else get close to door
            ai.SetDestinationToPositionLethalBotAI(doorPos.Value);

            // Sprint if far enough from the door
            if (sqrMagDistanceDoor > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
            {
                npcController.OrderToSprint();
            }
            else
            {
                npcController.OrderToStopSprint();
            }

            ai.OrderMoveToDestination();
        }

        /// <summary>
        /// Helper function that returns the closes lockpicker transform of the door
        /// </summary>
        /// <returns></returns>
        private Vector3 GetClosestSideToDoor()
        {
            if (this.targetDoor == null)
            {
                return Vector3.zero;
            }

            float sqrDistToLockPickerPostion1 = (this.targetDoor.lockPickerPosition.position - npcController.Npc.transform.position).sqrMagnitude;
            float sqrDistToLockPickerPostion2 = (this.targetDoor.lockPickerPosition2.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToLockPickerPostion1 < sqrDistToLockPickerPostion2)
            {
                return this.targetDoor.lockPickerPosition.position; // Was local position, but was causing issues.
            }
            return this.targetDoor.lockPickerPosition2.position; // Was local position, but was causing issues.
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        /// <summary>
        /// Helper function to check if the given <paramref name="item"/> is a key or lockpicker!
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected override bool FindObject(GrabbableObject item)
        {
            return item is KeyItem || item is LockPicker;
        }

        /// <summary>
        /// Helper function to check if the <paramref name="canidate"/> key is better than our <paramref name="currentBest"/>!
        /// </summary>
        /// <inheritdoc cref="AIState.FindBetterObject(GrabbableObject, GrabbableObject)"/>
        protected override bool FindBetterObject(GrabbableObject currentBest, GrabbableObject canidate)
        {
            // We prefer the key over the lockpick if possible!
            if (currentBest is not KeyItem && canidate is KeyItem)
            {
                return true;
            }

            return false;
        }

        // We are unlocking a door, these messages should be queued!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            // Return to the ship when we finish!
            if (message == "return")
            {
                previousAIState = new ReturnToShipState(this);
                return;
            }
            base.OnSignalTranslatorMessageReceived(message);
        }
    }
}
