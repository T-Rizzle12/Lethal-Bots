using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.ModPatches.LethalPhones;
using LethalBots.Patches.NpcPatches;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using LethalLib.Modules;
using Scoops.gameobjects;
using Scoops.misc;
using Scoops.service;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;
using static Unity.Audio.Handle;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot uses the ship terminal to monitor the rest of the crew.
    /// The bots will open doors, disable traps, and teleport players if needed.
    /// FIXME: This state is currently unfinished, there may be some issues or inconsistencies!
    /// </summary>
    public class MissionControlState : AIState
    {
        private bool overrideCrouch;
        private bool skipTerminalThink; // Used so the bot can accept calls on the switchboard
        private bool canCollectPurchasedItems; // Used to keep the bot from heading over to collect what is purchased when we are in the middle of ordering stuff.
        private bool grabbedLoadout; // Used to make the bot grab its loadout before heading on the terminal
        private bool botClosedShipDoors; // Used so the bot doesn't mess with the doors when the player touches them
        private bool playerRequestLeave; // This is used when a human player requests the bot to pull the ship lever!
        private bool playerRequestedTerminal; // This is used when a human player requests to use the terminal!
        private float waitForTerminalTime; // This is used to wait for the terminal to be free
        private bool targetPlayerUpdated; // This tells the signal translator coroutine the targeted player has updated!
        private WalkieTalkie? walkieTalkie; // This is the walkie-talkie we want to have in our inventory
        private GrabbableObject? weapon; // This is the weapon we want to have in our inventory
        private GrabbableObject? bodyToCollect; // This is the dead body the bot wants to pickup so it gets properly registered as on the ship!
        private PlayerControllerB? targetedPlayer; // This is the current player on the monitor based on last vision update
        private PlayerControllerB? monitoredPlayer; // This is the player we want to be monitoring
        private Queue<PlayerControllerB> playersRequstedTeleport = new Queue<PlayerControllerB>();
        private Coroutine? monitorCrew;
        private Coroutine? useSignalTranslator;
        private Coroutine? restockShip;
        private float leavePlanetTimer;
        private static Dictionary<Turret, TerminalAccessibleObject> turrets = new Dictionary<Turret, TerminalAccessibleObject>();
        private static Dictionary<Landmine, TerminalAccessibleObject> landmines = new Dictionary<Landmine, TerminalAccessibleObject>();
        private static Dictionary<SpikeRoofTrap, TerminalAccessibleObject> spikeRoofTraps = new Dictionary<SpikeRoofTrap, TerminalAccessibleObject>();
        private Dictionary<string, float> calledOutEnemies = new Dictionary<string, float>(); // Should this be an enemy name rather than the AI itself?
        private PriorityMessageQueue messageQueue = new PriorityMessageQueue();
        private static readonly AccessTools.FieldRef<TerminalAccessibleObject, bool> isDoorOpen = AccessTools.FieldRefAccess<bool>(typeof(TerminalAccessibleObject), "isDoorOpen");
        private static readonly AccessTools.FieldRef<TerminalAccessibleObject, bool> inCooldown = AccessTools.FieldRefAccess<bool>(typeof(TerminalAccessibleObject), "inCooldown");
        private static ShipTeleporter? ShipTeleporter
        {
            get
            {
                if (field == null)
                {
                    field = LethalBotAI.FindTeleporter();
                }
                return field;
            }
        }
        internal static SignalTranslator? SignalTranslator
        {
            get
            {
                if (field == null)
                {
                    field = UnityEngine.Object.FindObjectOfType<SignalTranslator>();
                }
                return field;
            }
        }
        private static ShipAlarmCord? ShipHorn
        {
            get
            {
                if (field == null)
                {
                    field = UnityEngine.Object.FindObjectOfType<ShipAlarmCord>();
                }
                return field;
            }
        }

        public MissionControlState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.MissionControl;
        }

        public MissionControlState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.MissionControl;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                PlayerControllerB? missionController = LethalBotManager.Instance.MissionControlPlayer;
                if (missionController == null || !missionController.isPlayerControlled || missionController.isPlayerDead)
                {
                    LethalBotManager.Instance.MissionControlPlayer = npcController.Npc;
                }
                // Might not need this as we moved this to a synced version up in bot manager
                /*TimeOfDay timeOfDay = TimeOfDay.Instance;
                DayMode dayMode = timeOfDay.GetDayPhase(timeOfDay.currentDayTime / timeOfDay.totalTime);
                if (LethalBotManager.lastReportedTimeOfDay != dayMode)
                {
                    LethalBotManager.lastReportedTimeOfDay = dayMode;
                    LethalBotManager.Instance.SetLastReportedTimeOfDayAndSync(dayMode);
                }*/
                SetupTerminalAccessibleObjects();
                FindWalkieTalkie();
                FindWeapon();
                grabbedLoadout = false;
            }

            // If we are the mission controller, we cannot be in a group
            if (LethalBotManager.Instance.MissionControlPlayer == npcController.Npc 
                && GroupManager.Instance.IsPlayerInGroup(npcController.Npc))
            {
                GroupManager.Instance.RemoveFromCurrentGroupAndSync(npcController.Npc);
            }
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            // If we are no longer the mission controller, stop being the switchboard operator!
            if (Plugin.IsModLethalPhonesLoaded && LethalBotManager.Instance.MissionControlPlayer != npcController.Npc)
            {
                ai.StopBeingSwitchboardOperator();
            }
            base.OnExitState(newState);
        }

        public override void DoAI()
        {
            // If we are not the mission controller or the ship is leaving, we should not be in this state
            if (LethalBotManager.Instance.MissionControlPlayer != npcController.Npc 
                || StartOfRound.Instance.shipIsLeaving)
            {
                GetOffTerminal();
                if (StartOfRound.Instance.shipIsLeaving)
                {
                    LethalBotManager.Instance.MissionControlPlayer = null;
                }
                ai.State = new ChillAtShipState(this);
                return;
            }

            // We are assigned to man the ship, make sure we are not on the loot transfer list
            if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
            {
                LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
            }

            // If we are the mission controller, we cannot be in a group
            if (GroupManager.Instance.IsPlayerInGroup(npcController.Npc))
            {
                GroupManager.Instance.RemoveFromCurrentGroupAndSync(npcController.Npc);
            }

            // Its kinda hard to be the mission controller if we are not on the ship!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // Make sure to grab our loadout before we get on the terminal
            if (!grabbedLoadout)
            {
                grabbedLoadout = true;
                ai.State = new GrabLoadoutState(this);
                return;
            }

            // A human player requested to use the terminal,
            // we should get off and let them use it!
            if (playerRequestedTerminal)
            {
                if (GetOffTerminal())
                {
                    return;
                }
                // We have finished allowing the player to use the terminal,
                // we should reset the timer and allow the bot to use it again.
                // If the human player is still on the terminal,
                // the rest of the code will handle it.
                else if (waitForTerminalTime > Const.LETHAL_BOT_TIMER_WAIT_FOR_TERMINAL)
                {
                    playerRequestedTerminal = false;
                    waitForTerminalTime = 0f;
                }
                else
                {
                    waitForTerminalTime += ai.AIIntervalTime;
                }
                return;
            }
            else
            {
                waitForTerminalTime = 0f;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive(true, true);
            if (playerController != null)
            {
                if (GetOffTerminal())
                {
                    return;
                }
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // Check to see if we can heal someone!
            playerController = ai.LookingForPlayerToHeal(true, true);
            if (playerController != null)
            {
                if (GetOffTerminal())
                {
                    return;
                }
                ai.State = new HealPlayerState(this, playerController);
                return;
            }

            // Bot drop item
            if (!ai.AreHandsFree() 
                && FindObject(ai.HeldItem))
            {
                if (GetOffTerminal())
                {
                    return;
                }
                ai.DropItem();
                return;
            }
            // If we still have stuff in our inventory,
            // we should swap to it and drop it!
            else if (ai.HasGrabbableObjectInInventory(FindObject, out int objectSlot))
            {
                if (GetOffTerminal())
                {
                    return;
                }
                ai.SwitchItemSlotsAndSync(objectSlot);
                return;
            }

            // Check if we have a dead body to collect
            GrabbableObject? body = bodyToCollect;
            if (body != null)
            {
                if (GetOffTerminal())
                {
                    return;
                }
                bodyToCollect = null;
                LethalBotAI.DictJustDroppedItems.Remove(body); // HACKHACK: Skip the dropped item cooldown so bot can grab the body immediately
                ai.State = new FetchingObjectState(this, body);
                return;
            }

            // Do we have purchased items to collect?
            if (canCollectPurchasedItems && CollectPurchasedItemsState.IsPossible())
            {
                GetOffTerminal();
                ai.State = new CollectPurchasedItemsState(this);
                return;
            }

            // If we are to return to the ship, we should pull the ship lever if needed!
            if (LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                if (ai.LookingForObjectsToSell(true) != null || LethalBotManager.AreThereItemsOnDesk())
                {
                    if (GetOffTerminal())
                    {
                        return;
                    }
                    ai.State = new CollectScrapToSellState(this);
                    return;
                }
                else if ((playerRequestLeave || LethalBotManager.Instance.AreAllHumanPlayersDead())
                && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                {
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                    {
                        if (GetOffTerminal())
                        {
                            return;
                        }
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
                    return;
                }
                else
                {
                    leavePlanetTimer = 0f;
                }
            }
            else
            {
                bool isShipCompromised = LethalBotManager.IsShipCompromised(ai);
                if (playerRequestLeave
                    || (ShouldReturnToShip()
                    && LethalBotManager.Instance.AreAllPlayersOnTheShip(true)
                    && (LethalBotManager.Instance.AreAllHumanPlayersDead(true)
                        || isShipCompromised)))
                {
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET
                        || isShipCompromised)
                    {
                        if (GetOffTerminal())
                        {
                            return;
                        }
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
                    return;
                }
                else
                {
                    leavePlanetTimer = 0f;
                }

                // If we have a weapon, we should fight any enemies that invaded the ship
                if (weapon != null)
                {
                    // If we don't have our weapon, we should pick it up!
                    if (!ai.HasGrabbableObjectInInventory(weapon, out _))
                    {
                        int openSlot = ai.FirstEmptyItemSlot(weapon);
                        if (openSlot != Const.INVALID_ITEM_SLOT)
                        {
                            if (!weapon.isInShipRoom || weapon.isHeld)
                            {
                                FindWeapon();
                                return;
                            }
                            LethalBotAI.DictJustDroppedItems.Remove(weapon);
                            ai.State = new FetchingObjectState(this, weapon);
                            return;
                        }
                        else
                        {
                            // Give up on that weapon, we have no room!
                            weapon = null;
                        }
                    }
                    // If our weapon uses batteries and its low on battery, we should charge it!
                    else if (!LethalBotAI.IsItemPowered(weapon))
                    {
                        // We should charge our weapon if we can!
                        ai.State = new ChargeHeldItemState(this, weapon);
                        return;
                    }
                    else
                    {
                        // Check if one of them are killable!
                        EnemyAI? newEnemyAI = CheckForInvadingEnemy();
                        if (newEnemyAI != null)
                        {
                            // ATTACK!
                            ai.State = new FightEnemyState(this, newEnemyAI);
                            return;
                        }
                    }
                }

                // Ship door logic
                /* TODO: Implement this code instead of this hack!
                    DoorPanel = GameObject.Find("Environment/HangarShip/AnimatedShipDoor/HangarDoorButtonPanel").transform;
			        Transform DoorStartButton = DoorPanel.Find("StartButton").Find("Cube (2)");
			        Transform DoorStopButton = DoorPanel.Find("StopButton").Find("Cube (3)");
			        if (DoorPanel == null || DoorStartButton == null || DoorStopButton == null) {
				        Console.LogError($"StartOfRound.GetDoorPanel() could not find HangarDoorButtonPanel references");
				        return;
			        }
			        DoorStartButtonTrigger = DoorStartButton.GetComponent<InteractTrigger>();
			        DoorStopButtonTrigger = DoorStopButton.GetComponent<InteractTrigger>();
                */
                // FIXME: Too buggy to use right now!
                //EnemyAI? enemyAI = CheckForInvadingEnemy(false, true);
                //if (enemyAI != null && !StartOfRound.Instance.hangarDoorsClosed)
                //{
                //    LethalBotManager.Instance.SetHangarShipDoorStateServerRpc(true);
                //    botClosedShipDoors = true;
                //}
                //else if (enemyAI == null && botClosedShipDoors && StartOfRound.Instance.hangarDoorsClosed)
                //{
                //    LethalBotManager.Instance.SetHangarShipDoorStateServerRpc(false);
                //    botClosedShipDoors = false;
                //}
            }

            // Terminal is invalid for some reason, just wait for now!
            Terminal ourTerminal = TerminalManager.Instance.GetTerminal();
            if (ourTerminal == null)
            {
                return;
            }

            // If we have a walkie set, we manage it here!
            if (walkieTalkie != null)
            {
                // We don't have the walkie-talkie, so we should pick it up!
                if (!ai.HasGrabbableObjectInInventory(walkieTalkie, out int walkieSlot))
                {
                    int openSlot = ai.FirstEmptyItemSlot(walkieTalkie);
                    if (openSlot != Const.INVALID_ITEM_SLOT)
                    {
                        if (!walkieTalkie.isInShipRoom || walkieTalkie.isHeld)
                        {
                            FindWalkieTalkie();
                            return;
                        }
                        LethalBotAI.DictJustDroppedItems.Remove(walkieTalkie); // HACKHACK: Since the walkie-talkie is on the ship, we clear the just dropped item timer!
                        ai.State = new FetchingObjectState(this, walkieTalkie);
                        return;
                    }
                    else
                    {
                        // Give up on that walkie, we have no room!
                        walkieTalkie = null;
                    }
                }
                // If our walkie-talkie is low on battery, we should charge it!
                else if (walkieTalkie.insertedBattery.empty
                    || walkieTalkie.insertedBattery.charge < 0.1f)
                {
                    // We should charge the walkie-talkie if we can!
                    ai.State = new ChargeHeldItemState(this, walkieTalkie);
                    return;
                }
                // Check if we are holding the walkie-talkie, if not we should switch to it!
                else if (walkieTalkie != null && ai.HeldItem != walkieTalkie)
                {
                    // We should switch to the walkie-talkie if we can!
                    ai.SwitchItemSlotsAndSync(walkieSlot);
                    return;
                }
            }

            // If we are not at the ship or terminal, we should move there now!
            InteractTrigger terminalTrigger = ourTerminal.gameObject.GetComponent<InteractTrigger>();
            float sqrDistFromTerminal = (terminalTrigger.playerPositionNode.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistFromTerminal > Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                ai.SetDestinationToPositionLethalBotAI(terminalTrigger.playerPositionNode.position);

                // Allow dynamic crouching
                overrideCrouch = false;

                // Manage our stamina usage!
                if (!npcController.WaitForFullStamina && sqrDistFromTerminal > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else if (npcController.WaitForFullStamina || sqrDistFromTerminal < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
                {
                    npcController.OrderToStopSprint();
                }

                // Move, now!
                ai.OrderMoveToDestination();
            }
            else
            {
                // We don't need to move now!
                ai.StopMoving();

                // We are manning the ship, we handle the calls to the switchboard as well!
                if (Plugin.IsModLethalPhonesLoaded)
                {
                    ai.BecomeSwitchboardOperator();
                }

                // We were told not to get on the terminal this think.
                // Clear the flag and skip the rest of this logic!
                if (skipTerminalThink)
                {
                    skipTerminalThink = false;
                    return;
                }

                // Can't do anything without the terminal!
                if (!npcController.Npc.inTerminalMenu)
                {
                    // Wait if someone else is on the terminal!
                    if (ourTerminal.terminalInUse 
                        || ourTerminal.placeableObject.inUse)
                    {
                        return;
                    }

                    // Make sure we stand up!
                    overrideCrouch = true;

                    // Wait until we are standing!
                    if (npcController.Npc.isCrouching)
                    {
                        return;
                    }

                    // Make sure our walkie-talkie is on!
                    if (walkieTalkie != null 
                        && !walkieTalkie.isBeingUsed 
                        && LethalBotAI.IsItemPowered(walkieTalkie))
                    {
                        walkieTalkie.ItemInteractLeftRightOnClient(false);
                        return;
                    }

                    // Hop on the terminal!
                    ai.EnterTerminal();
                }
                else
                {
                    // At the company building, we have different logic!
                    if (LethalBotManager.AreWeAtTheCompanyBuilding())
                    {
                        // FIXME: This blocks out some of the chat commands.
                        // It may be a good idea to have some kind of flag to allow the default stuff when needed.
                        if (restockShip == null)
                        {
                            restockShip = ai.StartCoroutine(RestockTheShip());
                        }
                        return;
                    }

                    // TODO: Implement AI for monitoring players, opening and closing blast doors, teleporting players,
                    // using the signal translator, buying a walkie-talkie to distract eyeless dogs, using the ship horn,
                    // and more I can't think of at the time.....
                    if (monitorCrew == null)
                    {
                        monitorCrew = ai.StartCoroutine(MissionSurveillanceRoutine());
                    }
                    if (useSignalTranslator == null && SignalTranslator != null)
                    {
                        useSignalTranslator = ai.StartCoroutine(UseSignalTranslator());
                    }
                }
            }
        }

        public override bool? ShouldBotCrouch()
        {
            // Stop crouching if we want to use the terminal
            if (overrideCrouch || npcController.Npc.inTerminalMenu)
            {
                return false;
            }
            return base.ShouldBotCrouch();
        }

        // Stops all coroutines!
        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopMonitoringCrew();
            StopUsingSignalTranslator();
            StopRestockingTheShip();
        }

        private IEnumerator MissionSurveillanceRoutine()
        {
            yield return null;
            while (ai.State != null 
                && ai.State == this
                && npcController.Npc.inTerminalMenu)
            {
                // Give the map a chance to update!
                float startTime = Time.timeSinceLevelLoad;
                yield return new WaitUntil(() => targetedPlayer != StartOfRound.Instance.mapScreen.targetedPlayer || (Time.timeSinceLevelLoad - startTime) > 1f);
                targetPlayerUpdated = true;

                // Get the next queued message!
                if (HasMessageToSend() 
                    && SignalTranslator != null 
                    && Time.realtimeSinceStartup - SignalTranslator.timeLastUsingSignalTranslator >= 8f)
                {
                    // Make sure the message is vaild
                    string messageToSend = GetNextMessageToSend();
                    if (!string.IsNullOrWhiteSpace(messageToSend))
                    {
                        yield return SendCommandToTerminal(string.Format(TerminalConst.STRING_TRANSMIT_COMMAND, messageToSend));
                    }
                }

                // Update our "vision" to the targeted player on the monitor
                targetedPlayer = StartOfRound.Instance.mapScreen.targetedPlayer;
                if (playersRequstedTeleport.TryDequeue(out PlayerControllerB playerControllerB))
                {
                    // If someone requested we teleport them we need to do it first!
                    if (playerControllerB == null)
                    {
                        continue;
                    }

                    // Check if we need to switch targets!
                    if (playerControllerB != targetedPlayer)
                    {
                        // Switch to the requested player first
                        yield return SwitchRadarTarget(playerControllerB);

                        // Wait until the teleport target is updated
                        startTime = Time.timeSinceLevelLoad; // Reuse start time variable, just in case we fail to update the target somehow.
                        yield return new WaitUntil(() => StartOfRound.Instance.mapScreen.targetedPlayer == playerControllerB || (Time.timeSinceLevelLoad - startTime) > 3f);
                    }

                    // Beam them up Scotty!
                    yield return TryTeleportPlayer(skipPostCheck: true);
                    continue;
                }

                // Someone requested we watch them, don't do the normal loop!
                if (IsValidRadarTarget(monitoredPlayer))
                {
                    if (targetedPlayer != monitoredPlayer)
                    {
                        yield return SwitchRadarTarget(monitoredPlayer);
                    }
                    else
                    {
                        yield return HandlePlayerMonitorLogic(monitoredPlayer);
                    }
                    continue;
                }

                // Make sure we are monitoring a player!
                // NOTE: We will work on using radar boosters later!
                if (IsValidRadarTarget(targetedPlayer))
                {
                    yield return HandlePlayerMonitorLogic(targetedPlayer);
                }

                yield return SwitchRadarTarget();
            }

            // Clear the monitor crew coroutine!
            StopMonitoringCrew();
        }

        /// <summary>
        /// Switches the ship monitor's targeted player to the player given
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator SwitchRadarTarget(PlayerControllerB? player = null)
        {
            if (player != null)
            {
                string playerUsername = player.playerUsername.ToLower();
                yield return SendCommandToTerminal($"switch {playerUsername}");
            }
            else
            {
                yield return SendCommandToTerminal("switch");
            }
        }

        /// <summary>
        /// Makes the bot disable traps nearby the given player!
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator UseTerminalAccessibleObjects(PlayerControllerB player)
        {
            TerminalAccessibleObject[] objectsToUse = FindTerminalAccessibleObjectsToUse(player);
            foreach (TerminalAccessibleObject terminalAccessible in objectsToUse)
            {
                yield return SendCommandToTerminal(terminalAccessible.objectCode);
            }
        }

        /// <summary>
        /// Makes the bot teleport the currently targeted player
        /// </summary>
        /// <param name="isDeadBody"></param>
        /// <returns></returns>
        private IEnumerator TryTeleportPlayer(bool isDeadBody = false, bool skipPostCheck = false)
        {
            if (ShipTeleporter != null && (!isDeadBody || ShipTeleporter.buttonTrigger.interactable))
            {
                // Make sure we lift the glass first
                if (ShipTeleporter.buttonAnimator.GetBool("GlassOpen") == false)
                {
                    ShipTeleporter.buttonAnimator.SetBool("GlassOpen", value: true);
                    yield return new WaitForSeconds(0.5f); // Wait for the glass to open
                }
                // HACKHACK: Fake pressing the button!
                yield return new WaitUntil(() => ShipTeleporter.buttonTrigger.interactable);
                yield return null; // Just in case the WaitUntil was already true;

                // Make sure that in the period we were waiting to teleport the player or body
                // that they still need to be teleported!
                // NOTE: This also helps the bot not teleport the wrong player if someone changes who is being monitored.
                PlayerControllerB playerOnMonitor = StartOfRound.Instance.mapScreen.targetedPlayer;
                if (playerOnMonitor != null && !skipPostCheck)
                {
                    if (isDeadBody)
                    {
                        if (!ShouldTeleportDeadBody(playerOnMonitor))
                            yield break;
                    }
                    else if (!IsPlayerInGraveDanger(playerOnMonitor))
                    {
                        yield break;
                    }
                }

                // FIXME: We need to hop off the terminal in order to push the teleport button!
                ShipTeleporter.PressTeleportButtonOnLocalClient();
                //ShipTeleporter.buttonTrigger.Interact(npcController.Npc.thisPlayerBody);
            }
        }

        /// <summary>
        /// The main logic for monitroing the entered player
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator HandlePlayerMonitorLogic(PlayerControllerB player)
        {
            // Watch over them for a second or two.
            float startTime = Time.timeSinceLevelLoad;
            const float cycleToNextPlayerTime = 1f;
            while ((Time.timeSinceLevelLoad - startTime) < cycleToNextPlayerTime)
            {
                // Ok, now we check some things!
                // Check for objects that need to be disabled nearby the player
                yield return UseTerminalAccessibleObjects(player);

                if (player.isPlayerDead)
                {
                    // The bot teleports the dead body back to the ship!
                    if (ShouldTeleportDeadBody(player))
                    {
                        yield return TryTeleportPlayer(true);
                    }
                    // Pickup and collect the dead body!
                    else if (player.deadBody != null)
                    {
                        DeadBodyInfo deadBodyInfo = player.deadBody;
                        if (!deadBodyInfo.isInShip
                            && !deadBodyInfo.grabBodyObject.isInShipRoom
                            && StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(deadBodyInfo.transform.position)
                            && ai.IsGrabbableObjectGrabbable(deadBodyInfo.grabBodyObject))
                        {
                            bodyToCollect = deadBodyInfo.grabBodyObject;
                        }
                    }

                    // Move onto the next player!
                    break;
                }
                else if (!player.isInElevator && !player.isInHangarShipRoom)
                {
                    // So the player is alive and controlled, time to do some logic!
                    // TODO: Add more logic!
                    if (IsPlayerInGraveDanger(player))
                    {
                        yield return TryTeleportPlayer();
                    }
                }
                else
                {
                    break; // Player is in ship, on to the next player!
                }
                yield return new WaitForSeconds(0.2f); // Chill out for a little bit
            }
        }

        /// <summary>
        /// Helper function to make the bot have to type out a message!
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        private IEnumerator SendCommandToTerminal(string command)
        {
            command = command.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                yield break;
            }

            // Use the terminal to play keyboard sound effects!
            Terminal ourTerminal = TerminalManager.Instance.GetTerminal();
            for (int i = 0; i < command.Length + 1; i++) // +1 for pressing the enter key!
            {
                if (ourTerminal != null)
                {
                    RoundManager.PlayRandomClip(ourTerminal.terminalAudio, ourTerminal.keyboardClips);
                }

                // Wait for a random time between 0.05 and 0.3 seconds before typing the next character
                yield return new WaitForSeconds(Random.Range(0.05f, 0.3f));
            }

            // Update the terminal using the given command!
            TerminalManager.Instance.OnSubmit(command, ourTerminal);

            // Update the terminal if possible
            // FIXME: We going to need some patches to do this!
            /*if (ourTerminal != null)
            {
                ourTerminal.TextChanged(message);
                ourTerminal.OnSubmit();
            }*/

        }

        /// <summary>
        /// The mission controller checks a certain criteria and determines if the bot should send a message!
        /// </summary>
        /// <remarks>
        /// The actual sending of the message is handled in <see cref="MissionSurveillanceRoutine"/>!
        /// </remarks>
        /// <returns></returns>
        private IEnumerator UseSignalTranslator()
        {
            yield return null;
            while (ai.State != null
                && ai.State == this
                && npcController.Npc.inTerminalMenu 
                && SignalTranslator != null)
            {
                // NOTE: Unlike MonitorCrew we don't update the targetedPlayer variable!
                float startTime = Time.timeSinceLevelLoad;
                yield return new WaitUntil(() => targetPlayerUpdated || (Time.timeSinceLevelLoad - startTime) > 1f);
                targetPlayerUpdated = false;

                // Not monitoring a player, do nothing!
                if (targetedPlayer == null)
                {
                    continue;
                }

                // Warn of threats!
                RoundManager instanceRM = RoundManager.Instance;
                Vector3 playerPos = targetedPlayer.transform.position;
                if (targetedPlayer.redirectToEnemy != null)
                {
                    playerPos = targetedPlayer.redirectToEnemy.transform.position;
                }
                else if (targetedPlayer.deadBody != null)
                {
                    playerPos = targetedPlayer.deadBody.transform.position;
                }
                for (int i = 0; i < instanceRM.SpawnedEnemies.Count; i++)
                {
                    EnemyAI spawnedEnemy = instanceRM.SpawnedEnemies[i];
                    string enemyName = GetEnemyName(spawnedEnemy);
                    if (!spawnedEnemy.isEnemyDead && (!calledOutEnemies.TryGetValue(enemyName, out var lastCalledTime) || Time.timeSinceLevelLoad - lastCalledTime > Const.TIMER_NEXT_ENEMY_CALL))
                    {
                        float? fearRange = ai.GetFearRangeForEnemies(spawnedEnemy); // NOTE: This is what the bot perceives as dangerous!
                        if ((fearRange.HasValue || IsEnemy(spawnedEnemy)) && (spawnedEnemy.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                        {
                            calledOutEnemies[enemyName] = Time.timeSinceLevelLoad;
                            MessagePriority messagePriority = spawnedEnemy is JesterAI ? MessagePriority.Critical : MessagePriority.Low; // If we see a jester, that is an immediate callout!
                            SendMessageUsingSignalTranslator(enemyName, messagePriority);
                        }
                    }
                    yield return null;
                }

                // Report the current time of day!
                TimeOfDay timeOfDay = TimeOfDay.Instance;
                if (timeOfDay != null)
                {
                    DayMode newDayMode = timeOfDay.GetDayPhase(timeOfDay.currentDayTime / timeOfDay.totalTime);
                    if (LethalBotManager.lastReportedTimeOfDay != newDayMode)
                    {
                        LethalBotManager.Instance.SetLastReportedTimeOfDayAndSync(newDayMode);
                        SendMessageUsingSignalTranslator(GetCurrentTime(timeOfDay.normalizedTimeOfDay, timeOfDay.numberOfHours, createNewLine: false), MessagePriority.Normal);
                    }
                }
                yield return null;
            }

            // Clear the use signal translator coroutine!
            StopUsingSignalTranslator();
        }

        /// <summary>
        /// This queues a message to be sent by the bot using the signal translator!
        /// </summary>
        /// <param name="message"></param>
        public void SendMessageUsingSignalTranslator(string message, MessagePriority priority = MessagePriority.Low)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                messageQueue.Enqueue(message, priority);
            }
        }

        /// <summary>
        /// Grabs the next message to send
        /// </summary>
        /// <returns>The next message to send!</returns>
        private string GetNextMessageToSend()
        {
            // Sanity check, make sure everything is valid!
            if (!messageQueue.TryDequeue(out var message))
            {
                return string.Empty;
            }
            return message;
        }

        /// <summary>
        /// Checks if there is a message to send!
        /// </summary>
        /// <returns>true: we have a message to send, false: we don't have any messages to send</returns>
        private bool HasMessageToSend()
        {
            // Sanity check, make sure everything is valid!
            if (messageQueue.TryPeek(out var message) && !string.IsNullOrEmpty(message))
            {
                return true;
            }
            return messageQueue.Count > 0;
        }

        /// <summary>
        /// The mission controller checks a certain criteria and determines if the bot should purchase an item!
        /// </summary>
        /// <returns></returns>
        private IEnumerator RestockTheShip()
        {
            yield return null;
            while (ai.State != null
                && ai.State == this
                && npcController.Npc.inTerminalMenu
                && LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                canCollectPurchasedItems = true; // Allow during the cooldown period
                yield return new WaitForSeconds(1f); // One second cooldown on this!
                canCollectPurchasedItems = false; // We are using the terminal, don't do it now!

                // Now lets check what we need to stock!
                Terminal ourTerminal = TerminalManager.Instance.GetTerminal();

                // To the store page!
                //if (ourTerminal.currentNode != ourTerminal.terminalNodes.specialNodes[TerminalConst.INDEX_DEFAULT_TERMINALNODE])
                //{
                //    // Ok, so we are not on the start page. Lets head over there now!
                //    if (ourTerminal.currentNode.isConfirmationNode)
                //    {
                //        yield return SendCommandToTerminal(TerminalConst.STRING_CANCEL_COMMAND); // Just decline whatever is presented!
                //    }
                //    yield return SendCommandToTerminal(TerminalConst.STRING_HELP);
                //}

                //// Alright, to the store page
                //yield return SendCommandToTerminal(TerminalConst.STRING_STORE);

                // Lets see what we need
                foreach (LethalBotStockRequirement? stockRequirement in RestockManager.Instance.LethalBotStockRequirements)
                {
                    yield return null; // Wait a frame!

                    // Sanity check
                    if (stockRequirement == null 
                        || stockRequirement.Item == null)
                    {
                        continue;
                    }

                    // Alright, now how much do we need.
                    Item item = stockRequirement.Item;
                    int requiredStock = stockRequirement.RequiredStock;
                    if (requiredStock <= 0)
                    {
                        continue;
                    }

                    // Check how much is already ordered.
                    int totalOwned = GetPendingOrderCount(item, ourTerminal, CollectPurchasedItemsState.ItemDropship) + GetNumberOfItemAlreadyOwned(item);

                    // Make the purchase as needed.
                    int numToPurchase = requiredStock - totalOwned;
                    if (numToPurchase > 0)
                    {
                        // Check, do we have the required item?
                        if (string.IsNullOrWhiteSpace(stockRequirement.RequiredItemName) 
                            || FindItemWithName(stockRequirement.RequiredItemName))
                        {
                            yield return PurchaseItem(item, numToPurchase); // Make the purchase
                        }
                    }
                }
            }
            StopRestockingTheShip();
        }

        /// <summary>
        /// Makes the bot purchase the given number of <paramref name="numToPurchase"/> for the following <paramref name="item"/>
        /// on the terminal
        /// </summary>
        /// <param name="item"></param>
        /// <param name="numToPurchase"></param>
        /// <returns></returns>
        private IEnumerator PurchaseItem(Item item, int numToPurchase = 1)
        {
            // Should never happen, but you never know.....
            Terminal terminal = TerminalManager.Instance.GetTerminal();
            if (terminal == null || numToPurchase <= 0)
            {
                yield break;
            }

            // Make sure we can actually purchase said item
            int itemIndex = Array.FindIndex(
                terminal.buyableItemsList, 
                x => x != null && x.itemName == item.itemName
            );

            // Item cannot be purchased
            if (itemIndex < 0)
            {
                yield break;
            }

            // Lets check if there is space on the drop ship......
            const int dropshipMaxmimumSpace = 12;
            int spaceLeft = dropshipMaxmimumSpace - terminal.numberOfItemsInDropship;
            if (spaceLeft <= 0)
            {
                yield break; // Yeah, its out of space.....
            }

            // Only buy to max capacity
            numToPurchase = Mathf.Min(numToPurchase, spaceLeft);

            // Make sure we have the money
            // Check if we have the money to make the purchase
            int unitCost = (int)((float)terminal.buyableItemsList[itemIndex].creditsWorth * ((float)terminal.itemSalesPercentages[itemIndex] / 100f));
            int itemCost = unitCost * numToPurchase;
            if (itemCost > 0)
            {
                int groupCredits = terminal.groupCredits;
                int availableCredits = groupCredits - Plugin.Config.RestockEcoLimit.Value;
                if (availableCredits <= 0)
                {
                    yield break; // We don't have the money!
                }

                // Max we can afford
                int maxAffordable = availableCredits / unitCost;

                // Clamp purchase amount
                numToPurchase = Math.Min(numToPurchase, maxAffordable);

                if (numToPurchase <= 0)
                {
                    yield break; // We don't have the money!
                }
            }

            // Alright, lets make a purchase
            if (terminal.currentNode != terminal.terminalNodes.specialNodes[TerminalConst.INDEX_DEFAULT_TERMINALNODE])
            {
                // Ok, so we are not on the start page. Lets head over there now!
                if (terminal.currentNode.isConfirmationNode)
                {
                    yield return SendCommandToTerminal(TerminalConst.STRING_CANCEL_COMMAND); // Just decline whatever is presented!
                }
                yield return SendCommandToTerminal(TerminalConst.STRING_HELP);
            }

            // Alright, to the store page
            yield return SendCommandToTerminal(TerminalConst.STRING_STORE);

            // And now we make the purchase
            yield return SendCommandToTerminal(string.Format(TerminalConst.STRING_BUY_COMMAND, item.itemName, numToPurchase));

            // Accept at the confirmation page
            if (!terminal.currentNode.isConfirmationNode)
            {
                Plugin.LogWarning($"Bot {npcController.Npc.playerUsername} attempted to buy {item.itemName}, but it failed?");
                yield break; // Huh, how did this happen?!
            }
            yield return SendCommandToTerminal(TerminalConst.STRING_CONFIRM_COMMAND);
        }

        /// <summary>
        /// Checks how many of the given <paramref name="item"/> was already ordered
        /// </summary>
        /// <param name="item"></param>
        /// <param name="ourTerminal"></param>
        /// <param name="itemDropship"></param>
        /// <returns></returns>
        private int GetPendingOrderCount(Item item, Terminal? ourTerminal = null, ItemDropship? itemDropship = null)
        {
            ourTerminal ??= TerminalManager.Instance.GetTerminal();
            if (ourTerminal == null)
            {
                return 0;
            }

            // Check if the item was ordered.
            int numOrdered = 0;
            int itemIndex = Array.FindIndex(
                ourTerminal.buyableItemsList,
                i => i != null && i.itemName == item.itemName
            );

            // Item is not purchasable!
            if (itemIndex < 0)
            {
                return int.MaxValue;
            }

            // Count how many times it appears in ordered list
            foreach (int index in ourTerminal.orderedItemsFromTerminal)
            {
                if (index == itemIndex)
                    numOrdered++;
            }

            // Consider what is currently in the dropship as well
            if (itemDropship != null)
            {
                foreach (int index in PatchesUtil.itemsToDeliverField.Invoke(itemDropship))
                {
                    if (index == itemIndex)
                        numOrdered++;
                }
            }

            return numOrdered;
        }

        /// <summary>
        /// Checks how many of the given <paramref name="item"/> we already own!
        /// </summary>
        /// <param name="item"></param>
        /// <param name="shipOnly"></param>
        /// <returns></returns>
        private int GetNumberOfItemAlreadyOwned(Item item, bool shipOnly = false)
        {
            string targetName = item.itemName;
            int numOwned = 0;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? itemObject = gameObject.GetComponent<GrabbableObject>();
                if (itemObject != null
                    && itemObject.itemProperties.itemName == targetName 
                    && (!shipOnly || itemObject.isInShipRoom))
                {
                    numOwned++;
                }
            }
            return numOwned;
        }

        /// <summary>
        /// Helper function that finds the given <paramref name="name"/> 
        /// on the ship, if one exists on the ship.
        /// </summary>
        /// <param name="name">The <see cref="GrabbableObject.itemProperties"/>'s <see cref="Item.itemName"/> to search for!</param>
        /// <returns>We found an object that had the same <see cref="Item.itemName"/> as <paramref name="name"/></returns>
        private bool FindItemWithName(string name)
        {
            // Do we at least have one instance of the given item?
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? item = gameObject.GetComponent<GrabbableObject>();
                if (item != null && item.itemProperties.itemName == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if the entered player is valid for monitoring!
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidRadarTarget([NotNullWhen(true)] PlayerControllerB? player)
        {
            return player != null && (player.isPlayerControlled || player.isPlayerDead);
        }

        /// <summary>
        /// Different from <see cref="LethalBotAI.GetFearRangeForEnemies(EnemyAI, PlayerControllerB?)"/>
        /// as most enemies are called out early!
        /// </summary>
        /// <param name="enemy"></param>
        /// <returns></returns>
        private static bool IsEnemy(EnemyAI enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            switch(enemy.enemyType.enemyName)
            {
                case "Masked":
                case "Jester":
                case "Crawler":
                case "Bunker Spider":
                case "ForestGiant":
                case "Butler Bees":
                case "Earth Leviathan":
                case "Nutcracker":
                case "Red Locust Bees":
                case "Blob":
                case "ImmortalSnail":
                case "Clay Surgeon":
                case "Flowerman":
                case "Bush Wolf":
                case "T-rex":
                case "MouthDog":
                case "Centipede":
                case "Spring":
                case "Butler":
                    return true;

                case "Hoarding bug":
                    if (enemy.currentBehaviourStateIndex == 2)
                    {
                        // Mad
                        return true;
                    }
                    else
                    {
                        return false;
                    }

                case "RadMech":
                    return true;

                case "Baboon hawk":
                    return false;


                case "Maneater":
                    if (enemy.currentBehaviourStateIndex > 0)
                    {
                        // Mad
                        return true;
                    }
                    else
                    {
                        return false;
                    }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the name of an enemy, used so modders can define custom names
        /// for the bots to send!
        /// </summary>
        /// <param name="enemy"></param>
        /// <returns>The overriden name for the given <see cref="EnemyAI"/> or returns the name found in <see cref="EnemyAI.enemyType"/></returns>
        private static string GetEnemyName(EnemyAI enemy)
        {
            string defaultName = enemy.enemyType.enemyName;
            switch(defaultName)
            {
                case "Clay Surgeon":
                    return "Barber";
                case "Red Locust Bees":
                    return "BEEES";
                case "Centipede":
                    return "Snare Flea";
                case "Flowerman":
                    return "Braken";
                case "Crawler":
                    return "Thumper";
                case "Spring":
                    return "Coil Head";
                case "MouthDog":
                    return "Dog";
                case "RadMech":
                    return "Old Bird";
                case "Bunker Spider":
                    return "Spider";
                case "ForestGiant":
                    return "Giant";
                default:
                    return defaultName;
            }
        }

        private void StopMonitoringCrew()
        {
            if (monitorCrew != null)
            {
                ai.StopCoroutine(monitorCrew);
                monitorCrew = null;
            }
        }

        private void StopUsingSignalTranslator()
        {
            if (useSignalTranslator != null)
            {
                ai.StopCoroutine(useSignalTranslator);
                useSignalTranslator = null;
            }
        }

        private void StopRestockingTheShip()
        {
            canCollectPurchasedItems = true;
            if (restockShip != null)
            {
                ai.StopCoroutine(restockShip);
                restockShip = null;
            }
        }

        /// <summary>
        /// Checks if we should teleport the dead body of the player!
        /// </summary>
        /// <param name="player">Player to check</param>
        /// <returns></returns>
        private bool ShouldTeleportDeadBody(PlayerControllerB player)
        {
            DeadBodyInfo? deadBodyInfo = player.deadBody;
            if (deadBodyInfo != null
                && !deadBodyInfo.isInShip
                && !deadBodyInfo.grabBodyObject.isHeld
                && !RescueAndReviveState.CanRevivePlayer(ai, player, true)
                && (!RescueAndReviveState.IsAnyReviveModInstalled() || !LethalBotAI.NearOtherPlayers(player, 17f))
                && !StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(deadBodyInfo.transform.position)
                && !ai.CheckProximityForEyelessDogs())
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the entered player is in grave danger and is in need of a teleport!
        /// </summary>
        /// <param name="player"></param>
        /// <returns>true: the player needs rescue, false: the player is fine</returns>
        private bool IsPlayerInGraveDanger(PlayerControllerB? player)
        {
            // Invalid player?
            if (player == null)
            {
                return false;
            }

            // In an animation with an enemy, SAVE THEM!
            // Usually when a player is in an animation they will die when it ends!
            if (player.inAnimationWithEnemy != null)
            {
                return true;
            }

            // This host has disabled rescuing players if they are in danger.
            if (!Plugin.Config.AllowMissionControlTeleport.Value)
            {
                return false;
            }

            // NEEDTOVALIDATE: Should I make it where the bot waits for the player to spin
            // or shake their camera instead?
            if (!player.isInElevator && !player.isInHangarShipRoom)
            {
                RoundManager instanceRM = RoundManager.Instance;
                Vector3 playerPos = player.transform.position;
                foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
                {
                    if (spawnedEnemy != null && !spawnedEnemy.isEnemyDead)
                    {
                        float? fearRange = ai.GetFearRangeForEnemies(spawnedEnemy, player); // NOTE: This is what the bot perceves as dangerous!
                        if (fearRange.HasValue && (spawnedEnemy.transform.position - playerPos).sqrMagnitude < fearRange * fearRange)
                        {
                            // There is an enemy nearby the player and they are criticaly injured!
                            // Get them out of there!
                            if (player.criticallyInjured)
                            {
                                return true;
                            }

                            // They are probably fighting an enemy, leave them alone!
                            LethalBotAI? isPlayerBot = LethalBotManager.Instance.GetLethalBotAI(player);
                            bool hasRangedWeapon = isPlayerBot?.HasRangedWeapon() ?? false; // NOTE: hasRangedWeapon has no effect for human players in CanEnemyBeKilled
                            if (LethalBotAI.CanEnemyBeKilled(spawnedEnemy, hasRangedWeapon, isPlayerBot == null) && DoesPlayerHaveWeaponInInventory(player))
                            {
                                return false;
                            }

                            // They are the one being targeted!
                            if (spawnedEnemy is RadMechAI oldBird)
                            {
                                // Is the old bird targeting them?
                                Transform? targetTransform = oldBird.targetedThreat?.threatScript?.GetThreatTransform();
                                if (player.transform == targetTransform)
                                {
                                    return true;
                                }
                            }
                            else if (spawnedEnemy is BaboonBirdAI baboonHawk)
                            {
                                // If the babooon hawk targeting them
                                Transform? targetTransform = baboonHawk.focusedThreat?.threatScript?.GetThreatTransform();
                                if (player.transform == targetTransform)
                                {
                                    return true;
                                }
                            }
                            // JESTER!!! GET THEM OUT NOW!!!!
                            else if (spawnedEnemy is JesterAI)
                            {
                                return true;
                            }
                            else if (spawnedEnemy.targetPlayer == player)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Helper function that checks if the given player has a weapon in their inventory!
        /// </summary>
        /// <param name="player">The player to check</param>
        /// <returns><see langword="true"/> if <paramref name="player"/> has a weapon; otherwise <see langword="false"/></returns>
        private static bool DoesPlayerHaveWeaponInInventory(PlayerControllerB? player)
        {
            if (player == null)
            {
                return false; 
            }

            // Check if the player has a weapon in their item only slot
            bool isPlayerBot = LethalBotManager.Instance.IsPlayerLethalBot(player);
            GrabbableObject? itemOnlySlot = player.ItemOnlySlot;
            if (LethalBotAI.IsItemWeapon(itemOnlySlot) 
                || (!isPlayerBot && itemOnlySlot != null && itemOnlySlot.itemProperties.isDefensiveWeapon))
            {
                return true;
            }

            // Check if the player has a weapon in their inventory
            foreach (var weapon in player.ItemSlots)
            {
                if (LethalBotAI.IsItemWeapon(weapon) 
                    || (!isPlayerBot && weapon != null && weapon.itemProperties.isDefensiveWeapon))
                {
                    return true; 
                }
            }

            return false;
        }

        /// <summary>
        /// Finds traps and big doors the bot wants to open or shutoff
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private static TerminalAccessibleObject[] FindTerminalAccessibleObjectsToUse(PlayerControllerB? player)
        {
            // Invalid player?
            if (player == null)
            {
                return new TerminalAccessibleObject[0];
            }

            // Now we need to go through each type of hazard and disable them if possible or needed
            List<TerminalAccessibleObject> objectsToUse = new List<TerminalAccessibleObject>();
            Vector3 playerPos = player.transform.position;
            if (player.redirectToEnemy != null)
            {
                playerPos = player.redirectToEnemy.transform.position;
            }
            else if (player.deadBody != null)
            {
                playerPos = player.deadBody.transform.position;
            }
            foreach (var turretInfo in turrets)
            {
                Turret turret = turretInfo.Key;
                if (turret != null)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if (turret.targetPlayerWithRotation == player 
                        || (turret.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = turretInfo.Value;
                        if (accessibleObject != null && !inCooldown.Invoke(accessibleObject))
                        { 
                            objectsToUse.Add(accessibleObject); 
                        }
                    }
                }
            }

            // Landmines
            foreach (var landmineInfo in landmines)
            {
                Landmine landmine = landmineInfo.Key;
                if (landmine != null && !landmine.hasExploded)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if ((landmine.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = landmineInfo.Value;
                        if (accessibleObject != null && !inCooldown.Invoke(accessibleObject))
                        {
                            objectsToUse.Add(accessibleObject);
                        }
                    }
                }
            }

            // Spike Roof Traps
            foreach (var spikeRoofTrapInfo in spikeRoofTraps)
            {
                SpikeRoofTrap spikeRoofTrap = spikeRoofTrapInfo.Key;
                if (spikeRoofTrap != null)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if ((spikeRoofTrap.spikeTrapAudio.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = spikeRoofTrapInfo.Value;
                        if (accessibleObject != null && !inCooldown.Invoke(accessibleObject))
                        {
                            objectsToUse.Add(accessibleObject);
                        }
                    }
                }
            }

            // Check for a big door to open!
            // TODO: Get the bot to close big doors when there is danger!
            Ray ray = new Ray(player.transform.position + Vector3.up * 2.3f, player.transform.forward);
            float maxDistance = player.grabDistance * 2f;
            LayerMask layerMask = 1342179585;// walkableSurfacesNoPlayersMask: 1342179585
            RaycastHit[] raycastHits = Physics.RaycastAll(ray, maxDistance, layerMask);
            foreach (RaycastHit raycastHit in raycastHits)
            {
                TerminalAccessibleObject accessibleObject = raycastHit.collider.gameObject.GetComponent<TerminalAccessibleObject>();
                if (accessibleObject != null 
                    && accessibleObject.isBigDoor 
                    && !objectsToUse.Contains(accessibleObject)
                    && !inCooldown.Invoke(accessibleObject)
                    && !isDoorOpen.Invoke(accessibleObject))
                {
                    objectsToUse.Add(accessibleObject);
                }
            }
            return objectsToUse.ToArray();
        }

        /// <summary>
        /// Bascially a carbon copy of <see cref="HUDManager.SetClock(float, float, bool)"/>, 
        /// but used by the bots to send the time!
        /// </summary>
        /// <param name="timeNormalized"></param>
        /// <param name="numberOfHours"></param>
        /// <param name="createNewLine"></param>
        /// <returns></returns>
        private static string GetCurrentTime(float timeNormalized, float numberOfHours, bool createNewLine = true)
        {
            int num = (int)(timeNormalized * (60f * numberOfHours)) + 360;
            int num2 = (int)Mathf.Floor(num / 60);
            string newLine;
            if (!createNewLine)
            {
                newLine = " ";
            }
            else
            {
                newLine = "\n";
            }

            string amPM = newLine + "AM";
            if (num2 >= 24)
            {
                return "12:00" + newLine + "AM";
            }

            if (num2 < 12)
            {
                amPM = newLine + "AM";
            }
            else
            {
                amPM = newLine + "PM";
            }

            if (num2 > 12)
            {
                num2 %= 12;
            }

            int num3 = num % 60;
            string text = $"{num2:00}:{num3:00}".TrimStart('0') + amPM;
            return text;
        }

        /// <summary>
        /// Helper function to find the walkie-talkie in our inventory or on the ship!
        /// </summary>
        private void FindWalkieTalkie()
        {
            // First, we need to check if we have a walkie-talkie in our inventory
            walkieTalkie = null;
            if (ai.HasGrabbableObjectInInventory(FindWalkieHelper, out int walkieSlot))
            {
                // Check for the reserved equipment slot
                // TODO: Add helper function to get grabbable object from inventory slot index, since this is used in multiple places now!
                if (walkieSlot == Const.RESERVED_EQUIPMENT_SLOT)
                {
                    this.walkieTalkie = npcController.Npc.ItemOnlySlot as WalkieTalkie;
                }
                else
                {
                    this.walkieTalkie = npcController.Npc.ItemSlots[walkieSlot] as WalkieTalkie;
                }

                // Make sure its valid!
                if (walkieTalkie != null)
                {
                    return;
                }
            }

            // So, we don't have a walkie-talkie in our inventory, lets check the ship!
            float closestWalkieSqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? walkieTalkie = gameObject.GetComponent<GrabbableObject>();
                if (walkieTalkie != null
                    && walkieTalkie is WalkieTalkie walkieTalkieObj 
                    && walkieTalkieObj.isInShipRoom)
                {
                    float walkieSqr = (walkieTalkieObj.transform.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (walkieSqr < closestWalkieSqr 
                        && ai.IsGrabbableObjectGrabbable(walkieTalkieObj)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestWalkieSqr = walkieSqr;
                        this.walkieTalkie = walkieTalkieObj;
                    }
                }
            }
        }

        /// <summary>
        /// Helper function to find a walkie-talkie in the bot's inventory.
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private bool FindWalkieHelper(GrabbableObject item)
        {
            return item != null && item is WalkieTalkie;
        }

        /// <summary>
        /// Helper function to find a weapon in our inventory or on the ship!
        /// </summary>
        private void FindWeapon()
        {
            // First, we need to check if we have a weapon in our inventory
            weapon = null;
            if (ai.HasGrabbableObjectInInventory(FindWeaponHelper, out int weaponSlot))
            {
                // Check for the reserved equipment slot
                // TODO: Add helper function to get grabbable object from inventory slot index, since this is used in multiple places now!
                if (weaponSlot == Const.RESERVED_EQUIPMENT_SLOT)
                {
                    this.weapon = npcController.Npc.ItemOnlySlot;
                }
                else
                {
                    this.weapon = npcController.Npc.ItemSlots[weaponSlot];
                }

                // Make sure its valid!
                if (weapon != null)
                {
                    return;
                }
            }

            // So, we don't have a weapon in our inventory, lets check the ship!
            float closestWeaponSqr = float.MaxValue;
            for (int i = 0; i < LethalBotManager.grabbableObjectsInMap.Count; i++)
            {
                GameObject gameObject = LethalBotManager.grabbableObjectsInMap[i];
                if (gameObject == null)
                {
                    LethalBotManager.grabbableObjectsInMap.TrimExcess();
                    continue;
                }

                GrabbableObject? weapon = gameObject.GetComponent<GrabbableObject>();
                if (ai.HasAmmoForWeapon(weapon)
                    && weapon.isInShipRoom)
                {
                    float weaponSqr = (weapon.transform.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (weaponSqr < closestWeaponSqr
                        && ai.IsGrabbableObjectGrabbable(weapon)) // NOTE: IsGrabbableObjectGrabbable has a pathfinding check, so we run it last since it can be expensive!
                    {
                        closestWeaponSqr = weaponSqr;
                        this.weapon = weapon;
                    }
                }
            }
        }

        /// <summary>
        /// Helper function to find a weapon in the bot's inventory.
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private bool FindWeaponHelper(GrabbableObject item)
        {
            return item != null && ai.HasAmmoForWeapon(item);
        }

        private void SetupTerminalAccessibleObjects()
        {
            // Remove all previous entries
            MissionControlState.turrets.Clear();
            MissionControlState.landmines.Clear();
            MissionControlState.spikeRoofTraps.Clear();

            // Fill dictionaries with new information
            Turret[] turrets = UnityEngine.Object.FindObjectsOfType<Turret>();
            Landmine[] landmines = UnityEngine.Object.FindObjectsOfType<Landmine>();
            SpikeRoofTrap[] spikeRoofTraps = UnityEngine.Object.FindObjectsOfType<SpikeRoofTrap>();
            foreach (var turret in turrets)
            {
                if (turret == null) continue;

                TerminalAccessibleObject terminalAccessibleObject = turret.GetComponent<TerminalAccessibleObject>();
                if (terminalAccessibleObject == null)
                {
                    Plugin.LogWarning($"Turret object {turret}, had no TerminalAccessableObject!? This should not happen!");
                    continue;
                }

                if (!MissionControlState.turrets.TryAdd(turret, terminalAccessibleObject))
                {
                    Plugin.LogWarning($"Turret object {turret} was already added to turrets table, skipping!");
                }
            }

            foreach (var landmine in landmines)
            {
                if (landmine == null) continue;

                TerminalAccessibleObject terminalAccessibleObject = landmine.GetComponent<TerminalAccessibleObject>();
                if (terminalAccessibleObject == null)
                {
                    Plugin.LogWarning($"Landmine object {landmine}, had no TerminalAccessableObject!? This should not happen!");
                    continue;
                }

                if (!MissionControlState.landmines.TryAdd(landmine, terminalAccessibleObject))
                {
                    Plugin.LogWarning($"Landmine object {landmine} was already added to landmines table, skipping!");
                }
            }

            foreach (var spikeRoofTrap in spikeRoofTraps)
            {
                if (spikeRoofTrap == null) continue;

                // This works, but is a lot slower!
                //Component[] components = spikeRoofTrap.gameObject.GetComponentsInParent<Component>();
                //foreach (Component component in components)
                //{
                //    if (component == null) continue;
                //    // Based on my research, GetComponentInChildren also calls GetComponent internally!
                //    //Plugin.LogDebug($"Checking if {component} has TerminalAccessableObject");
                //    //TerminalAccessibleObject terminalAccessible = component.GetComponent<TerminalAccessibleObject>();
                //    //Plugin.LogDebug($"{component} {(terminalAccessible != null ? "did" : "did not")} have a terminal accessable object!\n");

                //    // Aliright, second look at the log file shows the TerminalAccessableObject as a child component
                //    // Time to find it!
                //    terminalAccessibleObject = component.GetComponentInChildren<TerminalAccessibleObject>();
                //    if (terminalAccessibleObject != null)
                //    {
                //        break;
                //    }

                //}

                // Much more efficent method!
                TerminalAccessibleObject? terminalAccessibleObject = spikeRoofTrap.transform?.root?.GetComponentInChildren<TerminalAccessibleObject>();
                if (terminalAccessibleObject == null)
                {
                    Plugin.LogWarning($"Spike Roof Trap object {spikeRoofTrap}, had no TerminalAccessableObject!? This should not happen!");
                    continue;
                }

                if (!MissionControlState.spikeRoofTraps.TryAdd(spikeRoofTrap, terminalAccessibleObject))
                {
                    Plugin.LogWarning($"Spike Roof Trap object {spikeRoofTrap} was already added to Spike Roof Traps table, skipping!");
                }
            }
        }

        /// <summary>
        /// Helper function that checks if an enemy is invading the ship!
        /// </summary>
        /// <returns></returns>
        private EnemyAI? CheckForInvadingEnemy(bool onlyKillable = true, bool checkOutsideShip = false)
        {
            RoundManager instanceRM = RoundManager.Instance;
            Transform thisLethalBotCamera = this.npcController.Npc.gameplayCamera.transform;
            Bounds shipBounds = checkOutsideShip ? StartOfRound.Instance.shipBounds.bounds : StartOfRound.Instance.shipInnerRoomBounds.bounds;
            EnemyAI? closestEnemy = null;
            float closestEnemyDistSqr = float.MaxValue;
            foreach (EnemyAI spawnedEnemy in instanceRM.SpawnedEnemies)
            {
                // Only check for alive and invading enemies!
                if (spawnedEnemy.isEnemyDead 
                    || (onlyKillable && !ai.CanEnemyBeKilled(spawnedEnemy)))
                {
                    continue;
                }

                // HACKHACK: isInsidePlayerShip can be unreliable, YAY, so we have to check the shipInnerRoomBounds as well.....
                if (!spawnedEnemy.isInsidePlayerShip 
                    && !shipBounds.Contains(spawnedEnemy.transform.position))
                {
                    continue;
                }

                // Fear range
                float? fearRange = ai.GetFearRangeForEnemies(spawnedEnemy);
                if (!fearRange.HasValue)
                {
                    continue;
                }

                // Alright, mark masked players since we are now aware of their EVIL presence!
                if (spawnedEnemy is MaskedPlayerEnemy masked)
                {
                    ai.DictKnownMasked[masked] = true;
                }

                Vector3 positionEnemy = spawnedEnemy.transform.position;
                Vector3 directionEnemyFromCamera = positionEnemy - thisLethalBotCamera.position;
                float sqrDistanceToEnemy = directionEnemyFromCamera.sqrMagnitude;
                if (sqrDistanceToEnemy < closestEnemyDistSqr)
                {
                    closestEnemyDistSqr = sqrDistanceToEnemy;
                    closestEnemy = spawnedEnemy;
                }
            }

            return closestEnemy;
        }

        public override bool CheckAllowsTerminalUse() => true;

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // TODO: Add a way for the bot to declare messages to the players!
            // This is a placeholder for now!
            // This is done so the bot talks on the radio to keep other players in-game sanity up!
            // NOTE: Players can use walkie-talkies while they are using the terminal!
            if (walkieTalkie != null || Plugin.IsModLethalPhonesLoaded)
            {
                // Default states, wait for cooldown and if no one is talking close
                ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                {
                    VoiceState = EnumVoicesState.Chilling,
                    CanTalkIfOtherLethalBotTalk = true,
                    WaitForCooldown = true,
                    CutCurrentVoiceStateToTalk = false,
                    CanRepeatVoiceState = true,

                    ShouldSync = true,
                    IsLethalBotInside = npcController.Npc.isInsideFactory,
                    AllowSwearing = Plugin.Config.AllowSwearing.Value
                });
            }
        }

        /// <inheritdoc cref="AIState.RegisterChatCommands"/>
        public static new void RegisterChatCommands()
        {
            // We don't care about the default commands as the Mission Controller!
            ChatCommandsManager.RegisterIgnoreDefaultForState<MissionControlState>();

            // Someone requested that we start the ship!
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.START_THE_SHIP_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                // Make sure the sender is valid!
                if (playerWhoSentMessage == null)
                {
                    return true;
                }

                // Now we need to do some safety checks first. Only the host can tell the bot to pull the lever.
                // Unless they are dead!
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                PlayerControllerB? hostPlayer = LethalBotManager.HostPlayerScript;
                if (hostPlayer == null
                    || hostPlayer == playerWhoSentMessage
                    || hostPlayer.isPlayerDead
                    || !Plugin.Config.StartShipChatCommandProtection.Value)
                {
                    if (LethalBotManager.AreWeAtTheCompanyBuilding())
                    {
                        lethalBotAI.SendChatMessage($"Affirmative, I will start the ship in {Const.LETHAL_BOT_TIMER_LEAVE_PLANET} seconds and once everyone is onboard.");
                    }
                    else
                    {
                        lethalBotAI.SendChatMessage($"Affirmative, I will start the ship in {Const.LETHAL_BOT_TIMER_LEAVE_PLANET} seconds.");
                    }
                    missionControlState.playerRequestLeave = true;
                }
                else
                {
                    lethalBotAI.SendChatMessage($"Sorry {playerWhoSentMessage.playerUsername}, but only the captain can tell me to start the ship!");
                }
                return true;
            }));

            // A player is requesting we monitor them
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.REQUEST_MONITORING_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                lethalBotAI.SendChatMessage($"Roger, I will only monitor you, {playerWhoSentMessage.playerUsername}.");
                missionControlState.monitoredPlayer = playerWhoSentMessage;
                return true;
            }));

            // The player wants to stop being monitored
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.CLEAR_MONITORING_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                if (missionControlState.monitoredPlayer != playerWhoSentMessage)
                {
                    return true; // Ignore request cancellations from other players!
                }
                lethalBotAI.SendChatMessage("Understood, I will resume monitoring all crew members.");
                missionControlState.monitoredPlayer = null;
                return true;
            }));

            // This player wants to be teleported back to the ship
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.REQUEST_TELEPORT_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                // Make sure we have a teleporter
                if (ShipTeleporter == null)
                {
                    // Remind the player on their poor decision to not buy a teleporter.......
                    lethalBotAI.SendChatMessage("What do you mean, \"TELEPORT ME\"! We don't own a teleporter!");
                    return true;
                }

                // Only add new requests!
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                if (!missionControlState.playersRequstedTeleport.Contains(playerWhoSentMessage))
                {
                    lethalBotAI.SendChatMessage("Hold on, I will teleport you back to the ship as soon as possible.");
                    missionControlState.playersRequstedTeleport.Enqueue(playerWhoSentMessage);
                }
                return true;
            }));

            // A player is asking us to get off the terminal,
            // probably so they can use it.
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.HOP_OFF_THE_TERMINAL_COMMAND, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                if (!missionControlState.playerRequestedTerminal)
                {
                    lethalBotAI.SendChatMessage("Understood, I am leaving the terminal now.");
                }
                missionControlState.playerRequestedTerminal = true;
                missionControlState.waitForTerminalTime = 0f;
                return true;
            }));

            // A player is asking us to transmit a message
            ChatCommandsManager.RegisterCommandForState<MissionControlState>(new ChatCommand(Const.TRANSMIT_KEYWORD, (state, lethalBotAI, playerWhoSentMessage, message, isVoice) =>
            {
                // First we need to extract the message!
                // FIXME: There has to be a better way to do this!
                MissionControlState missionControlState = (MissionControlState)state; // We have bigger problems if this cast fails!
                int transmitIndex = message.IndexOf(Const.TRANSMIT_KEYWORD) + Const.TRANSMIT_KEYWORD_LENGTH;
                string messageToTransmit = message.Substring(transmitIndex).Trim();
                lethalBotAI.SendChatMessage($"Alright, I will relay, {messageToTransmit} to the rest of the crew.");

                // Queue the message to be sent!
                missionControlState.SendMessageUsingSignalTranslator(messageToTransmit, MessagePriority.High);
                return true;
            }));
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are the ship operator, these messages mean nothing to us!
            // After all, we already know what we just sent!
            SignalTranslatorCommandsManager.RegisterIgnoreDefaultForState<MissionControlState>();
        }

        public override void UseLethalPhones()
        {
            SwitchboardPhone? switchboardPhone = PhoneNetworkHandler.Instance?.switchboard;
            if (switchboardPhone != null)
            {
                // Check to see if we are the switchboardOperator
                PlayerControllerB switchboardOperator = switchboardPhone.switchboardOperator;
                if (switchboardOperator != null && switchboardOperator == npcController.Npc)
                {
                    // First things first, we don't accept calls sent to our phone!
                    PlayerPhone? ourPhone = ai.GetOurPlayerPhone();
                    if (ourPhone != null && (ourPhone.IsBusy() || ai.IsPhoneEquipped()))
                    {
                        ai.HangupPhone();
                        return;
                    }

                    // Alright, switchboard logic go!
                    // Check if we are getting a call!
                    NetworkVariable<short> incomingCall = (NetworkVariable<short>)PhoneBehaviorPatch.incomingCall.GetValue(switchboardPhone);
                    if (incomingCall.Value != Const.LETHAL_PHONES_NO_CALLER_ID)
                    {
                        // Found the trigger name in the source code, if we can, let the bot actually press the button.
                        InteractTrigger? acceptCallButton = switchboardPhone.transform?.Find("GreenButtonCube")?.GetComponent<InteractTrigger>();
                        if (acceptCallButton != null)
                        {
                            if (GetOffTerminal())
                            {
                                skipTerminalThink = true; // Make sure we don't get back on next think!
                                return;
                            }

                            try
                            {
                                acceptCallButton.Interact(npcController.Npc.thisPlayerBody);
                            }
                            catch (Exception e)
                            {
                                Plugin.LogError($"Error occurred when bot {npcController.Npc.playerUsername} attempted to press accept call button on the SwitchBoard!");
                                Plugin.LogError($"Exception: {e.Message}");
                                switchboardPhone.CallButtonPressedServerRpc();  // If we fail, just call it directly!
                            }
                        }
                        else
                        {
                            switchboardPhone.CallButtonPressedServerRpc();  // If we fail, just call it directly!
                        }
                        return;
                    }

                    // TODO: Add chat commands that allow players to request
                    // call transfers and other player's phone numbers!
                    return;
                }
            }

            // Just do the base stuff, we may not have the switchboard upgrade!
            base.UseLethalPhones();
        }

        /// <summary>
        /// Helper function that makes the bot get off the terminal!
        /// </summary>
        /// <returns>true: the bot got off the terminal; or false: the bot wasn't on the terminal</returns>
        private bool GetOffTerminal()
        {
            if (npcController.Npc.inTerminalMenu)
            {
                StopAllCoroutines();
                ai.LeaveTerminal();
                return true;
            }
            return false;
        }

        /// <summary>
        /// This makes the mission controller bot drop all items they don't need!
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected override bool FindObject(GrabbableObject item)
        {
            // Don't drop our walkieTalkie or weapon!
            if (item == walkieTalkie || item == weapon)
            {
                return false;
            }
            return base.FindObject(item); // Everything else can go!
        }

        /// <summary>
        /// This is basicially <see cref="Queue{T}"/>, but its designed to allow me to set the priority of messages!
        /// </summary>
        private sealed class PriorityMessageQueue
        {
            private readonly Dictionary<MessagePriority, Queue<string>> _queues;
            public PriorityMessageQueue()
            {
                _queues = new Dictionary<MessagePriority, Queue<string>>()
                {
                    { MessagePriority.Critical, new Queue<string>() },
                    { MessagePriority.High, new Queue<string>() },
                    { MessagePriority.Normal, new Queue<string>() },
                    { MessagePriority.Low, new Queue<string>() }
                };
            }

            /// <inheritdoc cref="Queue{T}.Enqueue(T)"/>
            public void Enqueue(string message, MessagePriority priority = MessagePriority.Low)
            {
                _queues[priority].Enqueue(message);
            }

            /// <inheritdoc cref="Queue{T}.TryDequeue(out T)"/>
            public bool TryDequeue(out string message)
            {
                for (var priority = MessagePriority.Critical; priority <= MessagePriority.Low; priority++)
                {
                    var q = _queues[priority];
                    if (q.TryDequeue(out message))
                    {
                        return true;
                    }
                }

                message = string.Empty;
                return false;
            }

            /// <inheritdoc cref="Queue{T}.TryPeek(out T)"/>
            public bool TryPeek(out string message)
            {
                for (var priority = MessagePriority.Critical; priority <= MessagePriority.Low; priority++)
                {
                    var q = _queues[priority];
                    if (q.TryPeek(out message))
                    {
                        return true;
                    }
                }

                message = string.Empty;
                return false;
            }

            /// <inheritdoc cref="Queue{T}.Count"/>
            public int Count
            {
                get
                {
                    int total = 0;
                    foreach (var q in _queues.Values)
                    {
                        total += q.Count;
                    }
                    return total;
                }
            }
        }
    }
}
