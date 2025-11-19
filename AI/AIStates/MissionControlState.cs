using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalLib.Modules;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using static UnityEngine.GraphicsBuffer;

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
        private bool playerRequestLeave; // This is used when a human player requests the bot to pull the ship lever!
        private bool playerRequestedTerminal; // This is used when a human player requests to use the terminal!
        private float waitForTerminalTime; // This is used to wait for the terminal to be free
        private bool targetPlayerUpdated; // This tells the signal translator coroutine the targeted player has updated!
        private WalkieTalkie? walkieTalkie; // This is the walkie-talkie we want to have in our inventory
        private PlayerControllerB? targetedPlayer; // This is the current player on the monitor based on last vision update
        private PlayerControllerB? monitoredPlayer; // This is the player we want to be monitoring
        private Queue<PlayerControllerB> playersRequstedTeleport = new Queue<PlayerControllerB>();
        private Coroutine? monitorCrew;
        private Coroutine? useSignalTranslator;
        private float leavePlanetTimer;
        private static Turret[] turrets = null!;
        private static Landmine[] landmines = null!;
        private static SpikeRoofTrap[] spikeRoofTraps = null!;
        private Dictionary<string, float> calledOutEnemies = new Dictionary<string, float>(); // Should this be an enemy name rather than the AI itself?
        private Queue<string> messageQueue = new Queue<string>();
        private static readonly FieldInfo isDoorOpen = AccessTools.Field(typeof(TerminalAccessibleObject), "isDoorOpen");
        private static readonly FieldInfo inCooldown = AccessTools.Field(typeof(TerminalAccessibleObject), "inCooldown");
        private static ShipTeleporter? _shipTeleporter;
        private static ShipTeleporter? ShipTeleporter
        {
            get
            {
                if (_shipTeleporter == null)
                {
                    _shipTeleporter = FindTeleporter();
                }
                return _shipTeleporter;
            }
        }
        private static SignalTranslator? _signalTranslator;
        private static SignalTranslator? SignalTranslator
        {
            get
            {
                if (_signalTranslator == null)
                {
                    _signalTranslator = UnityEngine.Object.FindObjectOfType<SignalTranslator>();
                }
                return _signalTranslator;
            }
        }
        private static ShipAlarmCord? _shipHorn;
        private static ShipAlarmCord? ShipHorn
        {
            get
            {
                if (_shipHorn == null)
                {
                    _shipHorn = UnityEngine.Object.FindObjectOfType<ShipAlarmCord>();
                }
                return _shipHorn;
            }
        }

        public MissionControlState(AIState oldState) : base(oldState)
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
                turrets = UnityEngine.Object.FindObjectsOfType<Turret>();
                landmines = UnityEngine.Object.FindObjectsOfType<Landmine>();
                spikeRoofTraps = UnityEngine.Object.FindObjectsOfType<SpikeRoofTrap>();
                FindWalkieTalkie();
            }
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // If we are not the mission controller or the ship is leaving, we should not be in this state
            if (LethalBotManager.Instance.MissionControlPlayer != npcController.Npc 
                || StartOfRound.Instance.shipIsLeaving)
            {
                if (npcController.Npc.inTerminalMenu)
                {
                    ai.LeaveTerminal();
                }
                if (StartOfRound.Instance.shipIsLeaving)
                {
                    LethalBotManager.Instance.MissionControlPlayer = null;
                }
                ai.State = new ChillAtShipState(this);
                return;
            }

            // Its kinda hard to be the mission controller if we are not on the ship!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
            {
                ai.State = new ReturnToShipState(this);
                return;
            }

            // A human player requested to use the terminal,
            // we should get off and let them use it!
            if (playerRequestedTerminal)
            {
                if (npcController.Npc.inTerminalMenu)
                {
                    StopAllCoroutines();
                    ai.LeaveTerminal();
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
                waitForTerminalTime += ai.AIIntervalTime;
                return;
            }
            else
            {
                waitForTerminalTime = 0f;
            }

            // If we are to return to the ship, we should pull the ship lever if needed!
            if (LethalBotManager.AreWeAtTheCompanyBuilding())
            {
                if (ai.LookingForObjectsToSell(true) != null || LethalBotManager.AreThereItemsOnDesk())
                {
                    if (npcController.Npc.inTerminalMenu)
                    {
                        StopAllCoroutines();
                        ai.LeaveTerminal();
                        return;
                    }
                    ai.State = new CollectScrapToSellState(this);
                    return;
                }
                else if ((playerRequestLeave || LethalBotManager.Instance.AreAllHumanPlayersDead())
                && LethalBotManager.Instance.AreAllPlayersOnTheShip())
                {
                    // HACKHACK: We fake pulling the ship lever to leave early, we will make the bot actually
                    // use the ship lever once I fix the interact trigger object code later
                    if (leavePlanetTimer > Const.LETHAL_BOT_TIMER_LEAVE_PLANET)
                    {
                        if (npcController.Npc.inTerminalMenu)
                        {
                            StopAllCoroutines();
                            ai.LeaveTerminal();
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
                        if (npcController.Npc.inTerminalMenu)
                        {
                            StopAllCoroutines();
                            ai.LeaveTerminal();
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
                    LethalBotAI.DictJustDroppedItems.Remove(walkieTalkie); // HACKHACK: Since the walkie-talkie is on the ship, we clear the just dropped item timer!
                    ai.State = new FetchingObjectState(this, walkieTalkie);
                    return;
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
                        && !walkieTalkie.insertedBattery.empty)
                    {
                        walkieTalkie.ItemInteractLeftRightOnClient(false);
                        return;
                    }

                    // Hop on the terminal!
                    ai.EnterTerminal();
                }
                else
                {
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
        }

        private static ShipTeleporter? FindTeleporter()
        {
            ShipTeleporter[] shipTeleporters = Object.FindObjectsOfType<ShipTeleporter>(includeInactive: false);
            foreach (var teleporter in shipTeleporters)
            {
                if (teleporter == null)
                {
                    continue;
                }

                if (!teleporter.isInverseTeleporter)
                {
                    return teleporter;
                }
            }
            return null;
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
                if (SignalTranslator != null 
                    && HasMessageToSend() 
                    && Time.realtimeSinceStartup - SignalTranslator.timeLastUsingSignalTranslator >= 8f)
                {
                    // Make sure the message is vaild
                    string messageToSend = GetNextMessageToSend();
                    if (!string.IsNullOrWhiteSpace(messageToSend))
                    {
                        yield return SendCommandToTerminal($"transmit {messageToSend}");
                        //SignalTranslator.timeLastUsingSignalTranslator = Time.realtimeSinceStartup;
                        HUDManager.Instance.UseSignalTranslatorServerRpc(messageToSend.Substring(0, Mathf.Min(messageToSend.Length, 10)));
                    }
                }

                // Update out "vision" to the targeted player on the monitor
                targetedPlayer = StartOfRound.Instance.mapScreen.targetedPlayer;
                if (playersRequstedTeleport.TryPeek(out PlayerControllerB playerControllerB))
                {
                    // If someone requested we teleport them we need to do it first!
                    if (playerControllerB == null)
                    {
                        playersRequstedTeleport.Dequeue();
                    }
                    else if (playerControllerB != targetedPlayer)
                    {
                        yield return SwitchRadarTargetToPlayer(playerControllerB);
                    }
                    else
                    {
                        yield return TryTeleportPlayer();
                        playersRequstedTeleport.Dequeue();
                    }
                    continue;
                }

                // Someone requested we watch them, don't do the normal loop!
                if (IsValidRadarTarget(monitoredPlayer))
                {
                    if (targetedPlayer != monitoredPlayer)
                    {
                        yield return SwitchRadarTargetToPlayer(monitoredPlayer);
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

                yield return SwitchToNextRadarTarget();
            }

            // Clear the monitor crew coroutine!
            StopMonitoringCrew();
        }

        /// <summary>
        /// Switches the ship monitor's targeted player up one index
        /// </summary>
        /// <returns></returns>
        private IEnumerator SwitchToNextRadarTarget()
        {
            yield return SendCommandToTerminal("switch");
            StartOfRound.Instance.mapScreen.SwitchRadarTargetForward(true);
        }

        /// <summary>
        /// Switches the ship monitor's targeted player to the player given
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator SwitchRadarTargetToPlayer(PlayerControllerB player)
        {
            yield return SendCommandToTerminal($"switch {player.playerUsername}");
            StartOfRound.Instance.mapScreen.SwitchRadarTargetAndSync((int)player.playerClientId);
        }

        /// <summary>
        /// Makes the bot disable traps nearby the given player!
        /// </summary>
        /// <remarks>
        /// FIXME: This is a bit different compared to how the base game does it,
        /// we should be doing every object with the same code rather than only the object we want to use.
        /// </remarks>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator UseTerminalAccessibleObjects(PlayerControllerB player)
        {
            TerminalAccessibleObject[] objectsToUse = FindTerminalAccessibleObjectsToUse(player);
            Terminal ourTerminal = TerminalManager.Instance.GetTerminal();
            foreach (TerminalAccessibleObject terminalAccessible in objectsToUse)
            {
                yield return SendCommandToTerminal(terminalAccessible.objectCode);
                if (ourTerminal != null)
                {
                    ourTerminal.codeBroadcastAnimator.SetTrigger("display");
                    ourTerminal.terminalAudio.PlayOneShot(ourTerminal.codeBroadcastSFX, 1f);
                }
                terminalAccessible.CallFunctionFromTerminal();
                yield return null;
            }
        }

        /// <summary>
        /// Makes the bot teleport the currently targeted player
        /// </summary>
        /// <param name="isDeadBody"></param>
        /// <returns></returns>
        private IEnumerator TryTeleportPlayer(bool isDeadBody = false)
        {
            if (ShipTeleporter != null && (!isDeadBody || ShipTeleporter.buttonTrigger.interactable))
            {
                // HACKHACK: Fake pressing the button!
                yield return new WaitUntil(() => ShipTeleporter.buttonTrigger.interactable);
                ShipTeleporter.PressTeleportButtonOnLocalClient();
            }
        }

        /// <summary>
        /// The main logic for monitroing the entered player
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private IEnumerator HandlePlayerMonitorLogic(PlayerControllerB player)
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
                // HACKHACK: Make the bot "pick-up" the dead body so they get marked as collected!
                else if (player.deadBody != null)
                {
                    DeadBodyInfo deadBodyInfo = player.deadBody;
                    if (!deadBodyInfo.isInShip
                        && StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(deadBodyInfo.transform.position))
                    {
                        npcController.Npc.SetItemInElevator(true, true, deadBodyInfo.grabBodyObject);
                    }
                }
            }
            else if (!player.isInElevator && !player.isInHangarShipRoom)
            {
                // So the player is alive and controlled, time to do some logic!
                // TODO: Add more logic!
                yield return new WaitForSeconds(1f);
                if (IsPlayerInGraveDanger(player))
                {
                    yield return TryTeleportPlayer();
                    //yield return new WaitForSeconds(3f); // Should we wait?
                }
            }
        }

        /// <summary>
        /// Helper function to make the bot have to type out a message!
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private IEnumerator SendCommandToTerminal(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                yield break;
            }

            // Use the terminal to play keyboard sound effects!
            Terminal ourTerminal = TerminalManager.Instance.GetTerminal();
            for (int i = 0; i < message.Length; i++)
            {
                if (ourTerminal != null)
                {
                    RoundManager.PlayRandomClip(ourTerminal.terminalAudio, ourTerminal.keyboardClips);
                }

                // Wait for a random time between 0.05 and 0.3 seconds before typing the next character
                yield return new WaitForSeconds(Random.Range(0.05f, 0.3f));
            }

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
                        float? fearRange = ai.GetFearRangeForEnemies(spawnedEnemy); // NOTE: This is what the bot perceves as dangerous!
                        if ((fearRange.HasValue || IsEnemy(spawnedEnemy)) && (spawnedEnemy.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                        {
                            calledOutEnemies[enemyName] = Time.timeSinceLevelLoad;
                            SendMessageUsingSignalTranslator(enemyName);
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
                        SendMessageUsingSignalTranslator(GetCurrentTime(timeOfDay.normalizedTimeOfDay, timeOfDay.numberOfHours, createNewLine: false));
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
        private void SendMessageUsingSignalTranslator(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                messageQueue.Enqueue(message);
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

            // TODO: Put this behind a config option incase players don't want this!
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
            foreach (var turret in turrets)
            {
                if (turret != null)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if (turret.targetPlayerWithRotation == player 
                        || (turret.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = turret.GetComponent<TerminalAccessibleObject>();
                        if (accessibleObject != null && !(bool)inCooldown.GetValue(accessibleObject))
                        { 
                            objectsToUse.Add(accessibleObject); 
                        }
                    }
                }
            }

            // Landmines
            foreach (var landmine in landmines)
            {
                if (landmine != null && !landmine.hasExploded)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if ((landmine.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = landmine.GetComponent<TerminalAccessibleObject>();
                        if (accessibleObject != null && !(bool)inCooldown.GetValue(accessibleObject))
                        {
                            objectsToUse.Add(accessibleObject);
                        }
                    }
                }
            }

            // Spike Roof Traps
            foreach (var spikeRoofTrap in spikeRoofTraps)
            {
                if (spikeRoofTrap != null)
                {
                    // Only use objects in terminal view range!
                    // NEEDTOVALIDATE: Is this too high or too low?
                    if ((spikeRoofTrap.spikeTrapAudio.transform.position - playerPos).sqrMagnitude < 40f * 40f)
                    {
                        TerminalAccessibleObject accessibleObject = spikeRoofTrap.gameObject.GetComponentInChildren<TerminalAccessibleObject>() ?? spikeRoofTrap.gameObject.GetComponentInParent<TerminalAccessibleObject>();
                        if (accessibleObject != null && !(bool)inCooldown.GetValue(accessibleObject))
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
                    && !(bool)inCooldown.GetValue(accessibleObject)
                    && !(bool)isDoorOpen.GetValue(accessibleObject))
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
        private string GetCurrentTime(float timeNormalized, float numberOfHours, bool createNewLine = true)
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
            foreach (var walkieTalkie in npcController.Npc.ItemSlots)
            {
                if (walkieTalkie != null && walkieTalkie is WalkieTalkie walkieTalkieObj)
                {
                    this.walkieTalkie = walkieTalkieObj;
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

        public override bool CheckAllowsTerminalUse() => true;

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // TODO: Add a way for the bot to declare messages to the players!
            // This is a placeholder for now!
            // This is done so the bot talks on the radio to keep other players in-game sanity up!
            // NOTE: Players can use walkie-talkies while they are using the terminal!
            if (walkieTalkie != null)
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

        // Allow players to request specific monitoring from the bot
        public override void OnPlayerChatMessageReceived(string message, PlayerControllerB playerWhoSentMessage)
        {
            if (playerWhoSentMessage != null)
            {
                if (message.Contains("start the ship"))
                {
                    // Now we need to do some safety checks first. Only the host can tell the bot to pull the lever.
                    // Unless they are dead!
                    PlayerControllerB? hostPlayer = LethalBotManager.HostPlayerScript;
                    if (hostPlayer == null 
                        || hostPlayer == playerWhoSentMessage 
                        || hostPlayer.isPlayerDead)
                    { 
                        playerRequestLeave = true; 
                    }
                }
                else if (message.Contains("request monitoring"))
                {
                    monitoredPlayer = playerWhoSentMessage;
                }
                else if (monitoredPlayer == playerWhoSentMessage && message.Contains("clear monitoring"))
                {
                    monitoredPlayer = null;
                }
                else if (message.Contains("request teleport"))
                {
                    playersRequstedTeleport.Enqueue(playerWhoSentMessage);
                }
                else if (message.Contains("hop off the terminal"))
                {
                    playerRequestedTerminal = true;
                    waitForTerminalTime = 0f;
                }
            }
        }

        // We are the ship operator, these messages mean nothing to us!
        // After all, we already know what we just sent!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            return;
        }
    }
}
