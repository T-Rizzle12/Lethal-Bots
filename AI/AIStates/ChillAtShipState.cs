using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalLib.Modules;
using System.Collections;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bots choose to wait at the ship
    /// They may go back in for another loot run if there is time!
    /// </summary>
    public class ChillAtShipState : AIState
    {
        private float chillAtShipTimer;
        private float leavePlanetTimer;
        public ChillAtShipState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.ChillAtShip;
        }
        public ChillAtShipState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.ChillAtShip;
        }

        public override void OnExitState()
        {
            base.OnExitState();
            npcController.StopPreformingEmote();
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

            // We are not at the ship, we should go back to it!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // If we are holding an item with a battery, we should charge it!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem != null
                && heldItem.itemProperties.requiresBattery
                && (heldItem.insertedBattery.empty
                    || heldItem.insertedBattery.charge < 0.9f))
            {
                ai.State = new ChargeHeldItemState(this, heldItem, new ReturnToShipState(this));
                return;
            }

            // If we are holding anything we should drop it
            bool canInverseTeleport = true;
            if (npcController.Npc.isInHangarShipRoom)
            {
                // Bot drop item
                PlayerControllerB? missionController = LethalBotManager.Instance.MissionControlPlayer;
                if (!ai.AreHandsFree())
                {
                    ai.DropItem();
                    canInverseTeleport = false;
                }
                // If we still have stuff in our inventory,
                // we should swap to it and drop it!
                else if (ai.HasGrabbableObjectInInventory(FindObjectToDrop, out int objectSlot))
                {
                    ai.SwitchItemSlotsAndSync(objectSlot);
                    canInverseTeleport = false;
                }
                else if (missionController == npcController.Npc)
                {
                    ai.State = new MissionControlState(this);
                    return;
                }
                else if (!StartOfRound.Instance.shipIsLeaving)
                {
                    if (missionController == null || !missionController.isPlayerControlled || missionController.isPlayerDead)
                    {
                        LethalBotManager.Instance.MissionControlPlayer = npcController.Npc;
                        canInverseTeleport = false;
                    }
                }
            }

            // Is the inverse teleporter on, we should use it!
            if (LethalBotManager.IsInverseTeleporterActive 
                && canInverseTeleport)
            {
                ai.State = new UseInverseTeleporterState(this);
                return;
            }

            bool areWeAtTheCompany = LethalBotManager.AreWeAtTheCompanyBuilding();
            float waitAtShipTime = areWeAtTheCompany ? Const.TIMER_CHILL_AT_SHIP_AT_COMPANY : Const.TIMER_CHILL_AT_SHIP;
            if (chillAtShipTimer > waitAtShipTime)
            {
                // If we are at the company building, we should sell!
                if (areWeAtTheCompany)
                {
                    if(ai.LookingForObjectsToSell(true) != null || LethalBotManager.AreThereItemsOnDesk())
                    {
                        ai.State = new CollectScrapToSellState(this);
                        return;
                    }
                    else if (LethalBotManager.Instance.AreAllHumanPlayersDead()
                    && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                    {
                        // HACKHACK: We fake pulling the ship lever to leave early, we will make the bot actually
                        // use the ship lever once I fix the interact trigger object code later
                        if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                        {
                            if (npcController.Npc.playersManager.shipHasLanded
                                && !npcController.Npc.playersManager.shipIsLeaving
                                && !npcController.Npc.playersManager.shipLeftAutomatically)
                            {
                                StartMatchLever startMatchLever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
                                if (startMatchLever != null)
                                {
                                    ai.PullShipLever(startMatchLever);
                                }
                                //npcController.Npc.playersManager.ShipLeaveAutomatically(true);
                            }
                        }
                        else
                        {
                            leavePlanetTimer += ai.AIIntervalTime;
                        }
                    }
                    else
                    {
                        leavePlanetTimer = 0f;
                    }
                    return;
                }

                // Try to find the closest player to target
                PlayerControllerB? player = ai.CheckLOSForClosestPlayer(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (player != null) // new target
                {
                    // Don't compromise the ship by being loud!
                    if (!ai.CheckProximityForEyelessDogs())
                    {
                        // Play voice
                        ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                        {
                            VoiceState = EnumVoicesState.LostAndFound,
                            CanTalkIfOtherLethalBotTalk = true,
                            WaitForCooldown = false,
                            CutCurrentVoiceStateToTalk = true,
                            CanRepeatVoiceState = false,

                            ShouldSync = true,
                            IsLethalBotInside = npcController.Npc.isInsideFactory,
                            AllowSwearing = Plugin.Config.AllowSwearing.Value
                        });
                    }

                    // Assign to new target
                    ai.SyncAssignTargetAndSetMovingTo(player);
                    if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
                    {
                        ai.ChangeSuitLethalBotServerRpc(npcController.Npc.playerClientId, player.currentSuitID);
                    }
                    return;
                }

                // If its getting late out, we should stay at the ship!
                if (!ShouldReturnToShip())
                {
                    // So, we are done chilling and didn't find a player to follow, so lets go in by ourselves
                    // A player can press their +use key on us to make us follow them!
                    if (chillAtShipTimer > Const.TIMER_CHILL_AT_SHIP + 2f)
                    {
                        // Last time we were looking for scrap there was a trapped player,
                        // we should grab a key so we can potentially free them!
                        if (LethalBotManager.IsThereATrappedPlayer 
                            && !ai.HasKeyInInventory())
                        {
                            GrabbableObject? key = FindKey() ?? FindLockpicker();
                            if (key != null)
                            {
                                ai.State = new FetchingObjectState(this, key, false, new SearchingForScrapState(this));
                                return;
                            }
                        }
                        ai.State = new SearchingForScrapState(this);
                        return;
                    }
                }
                else if (LethalBotManager.Instance.AreAllHumanPlayersDead()
                && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                {
                    // HACKHACK: We fake pulling the ship lever to leave early, we will make the bot actually
                    // use the ship lever once I fix the interact trigger object code later
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                    {
                        if (npcController.Npc.playersManager.shipHasLanded
                            && !npcController.Npc.playersManager.shipIsLeaving
                            && !npcController.Npc.playersManager.shipLeftAutomatically)
                        {
                            StartMatchLever startMatchLever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
                            if (startMatchLever != null)
                            {
                                ai.PullShipLever(startMatchLever);
                            }
                            //npcController.Npc.playersManager.ShipLeaveAutomatically(true);
                        }
                    }
                    else
                    {
                        leavePlanetTimer += ai.AIIntervalTime;
                    }
                }
                else
                {
                    leavePlanetTimer = 0f;
                }
            }

            // Chill
            ai.StopMoving();

            // Emotes
            npcController.PerformRandomEmote();

            // We wait at the ship for a bit before deciding to go out for more loot
            chillAtShipTimer += ai.AIIntervalTime;
        }

        public override bool? ShouldBotCrouch()
        {
            bool? originalResult = base.ShouldBotCrouch();
            if (originalResult.HasValue)
            {
                return originalResult;
            }
            return false;
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.Chilling,
                CanTalkIfOtherLethalBotTalk = false,
                WaitForCooldown = true,
                CutCurrentVoiceStateToTalk = false,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        public override void OnSignalTranslatorMessageReceived(string message)
        {
            // We are chilling at the ship, this message means nothing to us!
            if (message == "return")
            {
                return;
            }
            base.OnSignalTranslatorMessageReceived(message);
        }

        /// <summary>
        /// Simple function that checks if the give <paramref name="item"/> is null or not
        /// </summary>
        /// <remarks>
        /// This was designed for use in <see cref="LethalBotAI.HasGrabbableObjectInInventory(System.Func{GrabbableObject, bool}, out int)"/> calls.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        private static bool FindObjectToDrop(GrabbableObject item)
        {
            return true; // Found an item, great, we want to drop it!
        }

        /// <summary>
        /// Helper function to find a key on the ship!
        /// </summary>
        private GrabbableObject? FindKey()
        {
            // So, we don't have a key in our inventory, lets check the ship!
            GrabbableObject? closestKey = null;
            float closestKeySqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? keyItem = gameObject.GetComponent<GrabbableObject>();
                if (keyItem != null
                    && keyItem is KeyItem keyItemObj
                    && keyItemObj.isInShipRoom)
                {
                    float keySqr = (keyItemObj.transform.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (keySqr < closestKeySqr
                        && ai.IsGrabbableObjectGrabbable(keyItemObj)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestKeySqr = keySqr;
                        closestKey = keyItemObj;
                    }
                }
            }

            return closestKey;
        }

        /// <summary>
        /// Helper function to find a lockpicker on the ship!
        /// </summary>
        private GrabbableObject? FindLockpicker()
        {
            // So, we don't have a lockpicker in our inventory, lets check the ship!
            GrabbableObject? closestKey = null;
            float closestKeySqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? lockpickerItem = gameObject.GetComponent<GrabbableObject>();
                if (lockpickerItem != null
                    && lockpickerItem is LockPicker lockpickerItemObj
                    && lockpickerItemObj.isInShipRoom)
                {
                    float lockpickerSqr = (lockpickerItemObj.transform.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (lockpickerSqr < closestKeySqr
                        && ai.IsGrabbableObjectGrabbable(lockpickerItemObj)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestKeySqr = lockpickerSqr;
                        closestKey = lockpickerItemObj;
                    }
                }
            }

            return closestKey;
        }
    }
}
