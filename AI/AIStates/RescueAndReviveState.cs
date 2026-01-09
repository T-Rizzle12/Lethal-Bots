using BunkbedRevive;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.GameEnginePatches;
using LethalBots.Patches.ModPatches.BunkbedRevive;
using LethalBots.Patches.ModPatches.ReviveCompany;
using OPJosMod.ReviveCompany;
using OPJosMod.ReviveCompany.CustomRpc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem.HID;

namespace LethalBots.AI.AIStates
{
    public class RescueAndReviveState : AIState
    {
        private static Func<RagdollGrabbableObject, bool>? ReviveCompanyCanReviveDelegate = null;
        private static Action<int>? BunkbedReviveRPCDelegate = null;
        private static readonly FieldInfo isScanning = AccessTools.Field(typeof(PatcherTool), "isScanning");

        private PlayerControllerB playerToRevive;
        private ReviveMethod reviveMethod = ReviveMethod.None;
        private Coroutine? fallbackCoroutine;
        private Coroutine? reviveCoroutine;
        private Vector3? fallbackPos;
        private bool shouldPickupBody;
        private bool movedFromFallback;

        public RescueAndReviveState(AIState oldState, PlayerControllerB playerToRevive) : base(oldState)
        {
            CurrentState = EnumAIStates.RescueAndRevive;
            this.playerToRevive = playerToRevive;
        }

        public RescueAndReviveState(LethalBotAI ai, PlayerControllerB playerToRevive) : base(ai)
        {
            CurrentState = EnumAIStates.RescueAndRevive;
            this.playerToRevive = playerToRevive;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (!IsAnyReviveModInstalled())
                {
                    Plugin.LogWarning($"[{npcController.Npc.playerUsername}] RescueAndReviveState entered but no revive mods are loaded! Reverting to previous state.");
                    ChangeBackToPreviousState();
                    return;
                }

                reviveMethod = DetermineBestReviveMethod();
            }
            shouldPickupBody = true; // If we were interrupted, we should pickup the body and try again
            StartFallbackCoroutine(); // Always keep our fallback spot updated
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            // If we got interupted while using the Zap Gun, break the beam!
            if (ai.HeldItem is PatcherTool patcherTool && patcherTool.isShocking)
            {
                patcherTool.UseItemOnClient(true);
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

            // If the player becomes invalid or is no longer dead, change back to previous state!
            if (this.playerToRevive == null || !this.playerToRevive.isPlayerDead)
            {
                ChangeBackToPreviousState();
                return;
            }

            // If they are no longer reviveable, revert to previous state
            if (!CanRevivePlayer())
            {
                Plugin.LogInfo($"[{npcController.Npc.playerUsername}] Cannot revive player {playerToRevive.playerUsername} with method {reviveMethod}. Reverting to previous state.");
                ChangeBackToPreviousState();
                return;
            }

            if (shouldPickupBody)
            {
                GrabbableObject? heldItem = ai.HeldItem;
                GrabbableObject playerBody = this.playerToRevive.deadBody.grabBodyObject;
                if (heldItem == null || heldItem != playerBody)
                {
                    if (heldItem != null && (heldItem.itemProperties.twoHanded || !ai.HasSpaceInInventory()))
                    {
                        ai.DropItem();
                    }
                    LethalBotAI.DictJustDroppedItems.Remove(playerBody); // HACKHACK: Skip the dropped item cooldown so bot can grab the body immediately
                    if (ai.IsGrabbableObjectGrabbable(playerBody, EnumGrabbableObjectCall.Reviving))
                    {
                        ai.State = new FetchingObjectState(this, playerBody, EnumGrabbableObjectCall.Reviving);
                        return;
                    }
                    else
                    {
                        Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Cannot grab dead body of player {playerToRevive.playerUsername}. Reverting to previous state.");
                        ChangeBackToPreviousState();
                        return;
                    }
                }
            }

            switch (reviveMethod)
            {
                case ReviveMethod.ModReviveCompany:
                {
                    DoReviveCompanyLogic();
                    break;
                }
                case ReviveMethod.ModZaprillator:
                    DoZaprillatorLogic();
                    break;
                case ReviveMethod.ModBunkbedRevive:
                {
                    StopFallbackCoroutine(); // We are on the ship, we should be safe
                    DoBunkBedReviveLogic();
                    break;
                }
                default:
                    break;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return; // TODO: Update voice state to have more lines!
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopFallbackCoroutine();
            StopReviveCoroutine();
        }

        protected override Vector3? GetDesiredSafePathPosition()
        {
            return fallbackPos;
        }

        /// <summary>
        /// Specifies how the bot will attempt to revive this player.
        /// </summary>
        /// <remarks>
        /// Grandpa? Why isn't this in the enum folder with all the other enums?
        /// Well you see Timmy, this enum is only used by one class and it is very specific to that class.
        /// All the other enums are used by multiple classes and have a broader purpose.
        /// </remarks>
        private enum ReviveMethod
        {
            None,
            ModReviveCompany,
            ModBunkbedRevive,
            ModZaprillator
        }

        /// <summary>
        /// Determines the most appropriate revive method to use based on the current context.
        /// </summary>
        /// <returns>A <see cref="ReviveMethod"/> representing the selected revive method.</returns>
        private ReviveMethod DetermineBestReviveMethod()
        {
            // Alright, we prefer Revive Company, then Zaprillator, then Bunkbed Revive.
            if (Plugin.IsModReviveCompanyLoaded && ReviveCompanyCanRevivePlayer(ai, this.playerToRevive))
            {
                return ReviveMethod.ModReviveCompany;
            }
            else if (Plugin.IsModZaprillatorLoaded && ZaprillatorCanRevivePlayer(ai, this.playerToRevive))
            {
                return ReviveMethod.ModZaprillator;
            }
            else if (Plugin.IsModBunkbedReviveLoaded && BunkbedReviveCanRevivePlayer(ai, this.playerToRevive))
            {
                return ReviveMethod.ModBunkbedRevive;
            }

            return ReviveMethod.None;
        }

        /// <summary>
        /// Determines whether the specified player can be revived by the ReviveCompany mod.
        /// </summary>
        /// <remarks>
        /// This method checks for the presence of the ReviveCompany mod and calls my reverse patch to check if the player can be revived. 
        /// If the mod is not loaded or the required method is
        /// unavailable, the method returns false.
        /// </remarks>
        /// <param name="lethalBotAI">The bot who is thinking about reviving <paramref name="playerToRevive"/></param>
        /// <param name="playerToRevive">The player <paramref name="lethalBotAI"/> is thinking about reviving</param>
        /// <param name="isMissionController">Is <paramref name="lethalBotAI"/> who is thinking about teleporting this player's dead body</param>
        /// <returns>true if the player can be revived using the ReviveCompany mod; otherwise, false.</returns>
        private static bool ReviveCompanyCanRevivePlayer(LethalBotAI lethalBotAI, PlayerControllerB playerToRevive, bool isMissionController = false)
        {
            if (Plugin.IsModReviveCompanyLoaded)
            {
                if (ReviveCompanyCanReviveDelegate == null)
                {
                    try
                    {
                        MethodInfo reviveCompanyCanReviveMethod = AccessTools.Method(AccessTools.TypeByName("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch"), "canRevive");
                        if (reviveCompanyCanReviveMethod != null)
                        {
                            ReviveCompanyCanReviveDelegate = (Func<RagdollGrabbableObject, bool>)Delegate.CreateDelegate(typeof(Func<RagdollGrabbableObject, bool>), reviveCompanyCanReviveMethod);
                        }
                    }
                    catch (Exception e) { Plugin.LogError(e.Message); }
                    if (ReviveCompanyCanReviveDelegate == null)
                    {
                        Plugin.LogInfo("Failed to create Delegate!?");
                        return false;
                    }
                }

                // If we can revive teleported bodies, why not bring them back!
                if (isMissionController && ConfigVariables.reviveTeleportedBodies)
                {
                    return false;
                }

                // Sigh, for some reason, bots fail to find the RagdollGrabbableObject for the local player
                // So, I will do it myself
                if (playerToRevive?.deadBody?.grabBodyObject is RagdollGrabbableObject 
                    || playerToRevive == GameNetworkManager.Instance.localPlayerController)
                {
                    // So the base function ignores the local player, so I have to recreate the method in order for this to work....
                    RagdollGrabbableObject? ragdollGrabbableObject = playerToRevive?.deadBody?.grabBodyObject as RagdollGrabbableObject;
                    //Plugin.LogInfo($"Is playerToRevive local player {playerToRevive == GameNetworkManager.Instance.localPlayerController}");
                    if (playerToRevive == GameNetworkManager.Instance.localPlayerController)
                    {
                        if (GlobalVariables.RemainingRevives <= 0)
                        {
                            return false;
                        }
                        // Sigh, got to love being forced to recreate methods.......
                        //if (ragdollGrabbableObject != null && ragdollGrabbableObject.ragdoll != null && ragdollGrabbableObject.ragdoll.playerScript != null && GameNetworkManager.Instance.localPlayerController.playerClientId == ragdollGrabbableObject.ragdoll.playerScript.playerClientId)
                        //{
                        //    return false;
                        //}
                        int playerClientId = (int)playerToRevive.playerClientId;
                        if (GeneralUtil.HasPlayerTeleported(playerClientId) && !ConfigVariables.reviveTeleportedBodies)
                        {
                            //Plugin.LogInfo($"Local Player Was Teleported? {GeneralUtil.HasPlayerTeleported(playerClientId)}");
                            return false;
                        }
                        Plugin.LogInfo(Time.time + " | " + GeneralUtil.GetPlayersDiedAtTime(playerClientId));
                        if (Time.time - GeneralUtil.GetPlayersDiedAtTime(playerClientId) > (float)ConfigVariables.TimeUnitlCantBeRevived && !ConfigVariables.InfiniteReviveTime)
                        {
                            //Plugin.LogInfo($"Local Player dead for too long!");
                            return false;
                        }
                        //Plugin.LogInfo($"Local Player can be revived via Revive Company");
                        return true;
                    }

                    // Run the original method!
                    if (ragdollGrabbableObject != null)
                    { 
                        return ReviveCompanyCanReviveDelegate.Invoke(ragdollGrabbableObject); 
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the Zaprillator mod can revive the specified player.
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about reviving <paramref name="playerToRevive"/></param>
        /// <param name="playerToRevive">The player <paramref name="lethalBotAI"/> is thinking about reviving</param>
        /// <param name="isMissionController">Is <paramref name="lethalBotAI"/> who is thinking about teleporting this player's dead body</param>
        /// <returns>Always returns false, indicating that the Zaprillator mod cannot revive the specified player.</returns>
        private static bool ZaprillatorCanRevivePlayer(LethalBotAI lethalBotAI, PlayerControllerB playerToRevive, bool isMissionController = false)
        {
            // FIXME: This doesn't consider ANY of the config options,
            // this is due to Zaprillator being marked as Internal! 
            if (Plugin.IsModZaprillatorLoaded && !isMissionController)
            {
                return lethalBotAI.HasGrabbableObjectInInventory(FindZapGun, out _);
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified player can be revived using Bunkbed Revive.
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about reviving <paramref name="playerToRevive"/></param>
        /// <param name="playerToRevive">The player <paramref name="lethalBotAI"/> is thinking about reviving</param>
        /// <param name="isMissionController">Is <paramref name="lethalBotAI"/> who is thinking about teleporting this player's dead body</param>
        /// <returns>true if the player can be revived using Bunkbed Revive; otherwise, false.</returns>
        private static bool BunkbedReviveCanRevivePlayer(LethalBotAI lethalBotAI, PlayerControllerB playerToRevive, bool isMissionController = false)
        {
            // Bunkbed Revive can only be used on the ship!
            if (isMissionController || (!lethalBotAI.NpcController.Npc.isInElevator && !lethalBotAI.NpcController.Npc.isInHangarShipRoom))
            {
                return false;
            }

            // Find the RPC method before we do anything!
            if (BunkbedReviveRPCDelegate == null)
            {
                try
                {
                    MethodInfo bunkbedReviveRPCMethod = AccessTools.Method(AccessTools.TypeByName("BunkbedRevive.BunkbedNetworking"), "RevivePlayerServerRpc");
                    if (bunkbedReviveRPCMethod != null)
                    {
                        BunkbedReviveRPCDelegate = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), bunkbedReviveRPCMethod);
                    }
                }
                catch (Exception e) { Plugin.LogError(e.Message); }
                if (BunkbedReviveRPCDelegate == null)
                {
                    return false;
                }
            }

            // Check if the player can be revived using Bunkbed Revive
            if (Plugin.IsModBunkbedReviveLoaded)
            {
                int reviveCost = BunkbedController.GetReviveCost();
                int groupCredits = TerminalManager.Instance?.GetTerminal()?.groupCredits ?? 0;
                if (groupCredits < reviveCost)
                {
                    return false; // Not enough credits to revive
                }

                return BunkbedController.CanRevive((int)playerToRevive.playerClientId, true);
            }

            return false;
        }

        /// <summary>
        /// Helper function to check if the bot can revive the player with the selected method.
        /// </summary>
        /// <returns></returns>
        private bool CanRevivePlayer()
        {
            switch (reviveMethod)
            {
                case ReviveMethod.ModReviveCompany:
                    return ReviveCompanyCanRevivePlayer(ai, this.playerToRevive);
                case ReviveMethod.ModBunkbedRevive:
                    return BunkbedReviveCanRevivePlayer(ai, this.playerToRevive);
                case ReviveMethod.ModZaprillator:
                    return ZaprillatorCanRevivePlayer(ai, this.playerToRevive);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Simple helper function that checks if any of the supported player revive mods
        /// are active and enabled!
        /// </summary>
        /// <returns>true: at least one supported mod is installed and enabled; otherwise false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnyReviveModInstalled()
        {
            return Plugin.IsModReviveCompanyLoaded
                    || Plugin.IsModBunkbedReviveLoaded
                    || Plugin.IsModZaprillatorLoaded;
        }

        /// <summary>
        /// Helper function that tells bots if a player can be revived or not
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about reviving <paramref name="playerController"/></param>
        /// <param name="playerController">The player <paramref name="lethalBotAI"/> is thinking about reviving</param>
        /// <param name="isMissionController">Is <paramref name="lethalBotAI"/> who is thinking about teleporting this player's dead body</param>
        /// <returns>true if the player can be revived using any of the supported mods; otherwise, false.</returns>
        public static bool CanRevivePlayer(LethalBotAI lethalBotAI, PlayerControllerB playerController, bool isMissionController = false)
        {
            // Make sure there is a vaild dead body!
            GrabbableObject? playerBody = playerController.deadBody?.grabBodyObject;
            if (playerBody == null || !playerController.isPlayerDead)
            {
                return false;
            }

            // Alright, we prefer Revive Company, then Zaprillator, then Bunkbed Revive.
            if (Plugin.IsModReviveCompanyLoaded && ReviveCompanyCanRevivePlayer(lethalBotAI, playerController, isMissionController))
            {
                return true;
            }
            else if (Plugin.IsModZaprillatorLoaded && ZaprillatorCanRevivePlayer(lethalBotAI, playerController, isMissionController))
            {
                return true;
            }
            else if (Plugin.IsModBunkbedReviveLoaded && BunkbedReviveCanRevivePlayer(lethalBotAI, playerController, isMissionController))
            {
                return true;
            }

            return false;
        }

        private IEnumerator findFallbackSpot()
        {
            // Alright we need to find a safe spot to revive this player
            // FIXME: This relies on Elucian Distance rather than travel distance, this should be fixed!
            fallbackPos = null;
            var nodes = ai.allAINodes.OrderBy(node => (node.transform.position - npcController.Npc.transform.position).sqrMagnitude)
                                             .ToArray();
            Turret[] turrets = UnityEngine.Object.FindObjectsOfType<Turret>();
            SpikeRoofTrap[] spikeRoofTraps = UnityEngine.Object.FindObjectsOfType<SpikeRoofTrap>();
            yield return null;

            // NEEDTOVALIDATE: Should this be the local vector instead of just the height?
            bool ourWeOutside = ai.isOutside;
            float headOffset = npcController.Npc.gameplayCamera.transform.position.y - npcController.Npc.transform.position.y;
            for (var i = 0; i < nodes.Length; i++)
            {
                // Give the main thread a chance to do something else
                var node = nodes[i];
                if (i % 15 == 0)
                {
                    yield return null;
                }

                // Can we path to the node and is it safe?
                Vector3? nodePos = node?.transform.position;
                if (!nodePos.HasValue || !ai.IsValidPathToTarget(nodePos.Value))
                {
                    continue;
                }

                // Check if the node is exposed to enemies
                bool isNodeSafe = true;
                Vector3 simulatedHead = nodePos.Value + Vector3.up * headOffset;
                RoundManager instanceRM = RoundManager.Instance;
                for (int j = 0; j < instanceRM.SpawnedEnemies.Count; j++)
                {
                    // Give the main thread a chance to do something else
                    EnemyAI checkLOSToTarget = instanceRM.SpawnedEnemies[j];
                    if (j % 10 == 0)
                    {
                        yield return null;
                    }

                    if (checkLOSToTarget == null 
                        || checkLOSToTarget.isEnemyDead 
                        || ourWeOutside != checkLOSToTarget.isOutside)
                    {
                        continue;
                    }

                    // Check if the target is a threat!
                    float? dangerRange = ai.GetFearRangeForEnemies(checkLOSToTarget, EnumFearQueryType.PathfindingAvoid);
                    Vector3 enemyPos = checkLOSToTarget.transform.position;
                    if (dangerRange.HasValue && (enemyPos - nodePos.Value).sqrMagnitude <= dangerRange * dangerRange)
                    {
                        // Do the actual traceline check
                        Vector3 viewPos = checkLOSToTarget.eye?.position ?? enemyPos;
                        if (!Physics.Linecast(viewPos + Vector3.up * 0.2f, simulatedHead, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            isNodeSafe = false;
                            break;
                        }
                    }
                }

                // This node is dangerous! Pick another one!
                if (!isNodeSafe)
                {
                    continue;
                }

                // Now we need to make sure we are not reviving next to traps.
                for (int k = 0; k < turrets.Length; k++)
                {
                    // Give the main thread a chance to do something else
                    Turret turret = turrets[k];
                    if (k % 15 == 0)
                    {
                        yield return null;
                    }

                    if (turret == null || !turret.isActiveAndEnabled)
                    {
                        continue;
                    }

                    // Do the actual traceline check
                    Vector3 turretPos = turret.aimPoint.position;
                    if (!Physics.Linecast(turretPos, simulatedHead, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        isNodeSafe = false;
                        break;
                    }
                }

                // This node is dangerous! Pick another one!
                if (!isNodeSafe)
                {
                    continue;
                }

                // We don't need to consider landmines, but we should consider Spike Roof Traps
                for (int k = 0; k < spikeRoofTraps.Length; k++)
                {
                    // Give the main thread a chance to do something else
                    SpikeRoofTrap spikeRoofTrap = spikeRoofTraps[k];
                    if (k % 15 == 0)
                    {
                        yield return null;
                    }

                    if (spikeRoofTrap == null || !spikeRoofTrap.isActiveAndEnabled)
                    {
                        continue;
                    }

                    // Just a simple distance check should be enough
                    Vector3 spikeRoofTrapPos = spikeRoofTrap.spikeTrapAudio.transform.position;
                    const float safeDistance = 20f; // Arbitrary safe distance from spike roof traps
                    if ((spikeRoofTrapPos - nodePos.Value).sqrMagnitude <= safeDistance * safeDistance)
                    {
                        isNodeSafe = false;
                        break;
                    }
                }

                // This node is dangerous! Pick another one!
                if (!isNodeSafe)
                {
                    continue;
                }

                Plugin.LogDebug($"Bot {npcController.Npc.playerUsername} found fallback spot at {nodePos.Value}!");
                fallbackPos = nodePos.Value;
                break;
            }
            StopFallbackCoroutine();
        }

        private void StartFallbackCoroutine()
        {
            if (fallbackCoroutine == null)
            {
                fallbackCoroutine = ai.StartCoroutine(findFallbackSpot());
            }
        }

        private void StopFallbackCoroutine()
        {
            if (fallbackCoroutine != null)
            {
                ai.StopCoroutine(fallbackCoroutine);
                fallbackCoroutine = null;
            }

        }

        private IEnumerator reviveUsingReviveCompany()
        {
            RagdollGrabbableObject? playerBody = this.playerToRevive.deadBody.grabBodyObject as RagdollGrabbableObject;
            if (playerBody == null)
            {
                Plugin.LogError($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Revive Company but their body is not a RagdollGrabbableObject!");
                StopReviveCoroutine();
                yield break;
            }

            // Alright, look at the body first
            npcController.OrderToLookAtPosition(playerBody.transform.position);
            yield return null;

            // Now we fake the revive time
            yield return new WaitForSeconds(ConfigVariables.reviveTime);

            // Dang it, we were too late.....
            if (this.playerToRevive == null || !ReviveCompanyCanRevivePlayer(ai, this.playerToRevive))
            {
                StopReviveCoroutine();
                yield break;
            }

            // Its been a bit, make sure its still valid!
            if (playerBody == null)
            {
                Plugin.LogError($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Revive Company but their body is not a RagdollGrabbableObject!");
                StopReviveCoroutine();
                yield break;
            }

            // Yeah, yeah, I know. We have to send the message as the local player or the rpc will be sent to us as well.
            // You can blame how Revive Company was programed!
            RpcMessage message = new RpcMessage(MessageTasks.RevivePlayer, playerBody.ragdoll.playerScript.playerClientId.ToString(), (int)GameNetworkManager.Instance.localPlayerController.playerClientId, MessageCodes.Request);
            RpcMessageHandler.SendRpcMessage(message);
            ResponseHandler.SentMessageNeedResponses(message);
            GeneralUtil.RevivePlayer((int)playerBody.ragdoll.playerScript.playerClientId);

            // HACKHACK: Fake responses for all bots!
            foreach (LethalBotAI lethalBotAI in LethalBotManager.Instance.GetLethalBotAIs())
            {
                ResponseHandler.RecievedResponse(MessageTasks.RevivePlayer);
            }

            RpcMessage message2 = new RpcMessage(MessageTasks.TurnOffGhostMode, playerBody.ragdoll.playerScript.playerClientId.ToString(), (int)GameNetworkManager.Instance.localPlayerController.playerClientId, MessageCodes.Request);
            RpcMessageHandler.SendRpcMessage(message2);
            StopReviveCoroutine();
        }

        /// <summary>
        /// Helper function that handles reviving our target player
        /// </summary>
        /// <remarks>
        /// This is done so if revive company isn't installed, my mod won't error out!
        /// </remarks>
        private void DoReviveCompanyLogic()
        {
            // Wait for fallback to be caculated
            if (!fallbackPos.HasValue || !ai.IsValidPathToTarget(fallbackPos.Value))
            {
                StartFallbackCoroutine(); // Just in case the coroutine failed somehow!
                return;
            }

            // Move towards our fallback position via safe path
            float sqrDistToFallback = (fallbackPos.Value - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToFallback >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                // Find a safe path to our fallback spot
                StartSafePathCoroutine();

                float sqrMagDistanceToSafePos = (this.safePathPos - npcController.Npc.transform.position).sqrMagnitude;
                if (sqrMagDistanceToSafePos >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    // Alright lets go outside!
                    ai.SetDestinationToPositionLethalBotAI(this.safePathPos);

                    // Sprint if far enough
                    if (!npcController.WaitForFullStamina && sqrMagDistanceToSafePos > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING) // NEEDTOVALIDATE: Should we use the distance to the ship or the safe position?
                    {
                        npcController.OrderToSprint();
                    }
                    else
                    {
                        npcController.OrderToStopSprint();
                    }

                    ai.OrderMoveToDestination();
                }
                else
                {
                    // Wait here until its safe to move to the entrance
                    ai.StopMoving();
                    npcController.OrderToStopSprint();
                }

            }
            else
            {
                // Alright we are close enough to the fallback position, stop moving
                ai.StopMoving();
                npcController.OrderToStopSprint();
                StopSafePathCoroutine(); // Don't need this anymore

                // We need to set down the body before reviving
                shouldPickupBody = false; // Don't try to pick up the body again, we need it on the ground for the revive
                if (!ai.AreHandsFree())
                {
                    ai.DropItem();
                    return;
                }

                if (reviveCoroutine == null)
                {
                    reviveCoroutine = ai.StartCoroutine(reviveUsingReviveCompany());
                }
            }
        }

        private IEnumerator reviveUsingZaprillator()
        {
            RagdollGrabbableObject? playerBody = this.playerToRevive.deadBody.grabBodyObject as RagdollGrabbableObject;
            if (playerBody == null)
            {
                Plugin.LogError($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Zaprillator but their body is not a RagdollGrabbableObject!");
                StopReviveCoroutine();
                yield break;
            }

            // Alright, look at the body first
            npcController.OrderToLookAtPosition(playerBody.transform.position);
            yield return new WaitForSeconds(2f); // Two seconds should be enough time!

            if (!ai.HasGrabbableObjectInInventory(FindZapGun, out int itemSlot))
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Zaprillator but they don't have a zap gun!");
                StopReviveCoroutine();
                yield break;
            }

            // If we are somehow holding a two handed item, drop it first!
            if (!ai.AreHandsFree() && ai.HeldItem.itemProperties.twoHanded)
            {
                ai.DropItem();
                yield return null;
            }

            // Swap to zap gun and give time for the weapon switch to happen!
            float startTime = Time.timeSinceLevelLoad;
            ai.SwitchItemSlotsAndSync(itemSlot);
            yield return new WaitUntil(() => npcController.Npc.currentItemSlot == itemSlot || (Time.timeSinceLevelLoad - startTime) > 1f); // One second to allow RPC to got to server and back to us!

            // Alright, are we holding the Zap Gun?
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem is not PatcherTool patcherTool)
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Zaprillator but they either don't have a zap gun or item switch failed!");
                StopReviveCoroutine();
                yield break;
            }

            // Revive them now!
            heldItem.UseItemOnClient(true);
            yield return null;
            yield return new WaitUntil(() => patcherTool == null || (bool)isScanning.GetValue(patcherTool) == false); // Wait a bit!

            // Did we hit em?
            if (!patcherTool.isShocking)
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Zaprillator but the Zap Gun failed to find the body!");

                // Alright before we end the coroutine, make the bot step back a bit!
                if (!movedFromFallback)
                {
                    Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - playerBody.transform.position + Vector3.up * 0.2f);
                    ray.direction = new Vector3(ray.direction.x, 0f, ray.direction.z);
                    Vector3 pos = (!Physics.Raycast(ray, out RaycastHit hit, 4f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) ? ray.GetPoint(4f) : hit.point;
                    fallbackPos = RoundManager.Instance.GetNavMeshPosition(pos, default, 2.7f);
                    movedFromFallback = true;
                }
                // Moved and we still didn't hit em?
                // Just pickup and try again!
                else
                {
                    fallbackPos = null;
                    movedFromFallback = false;
                    shouldPickupBody = true;
                }

                StopReviveCoroutine();
                yield break;
            }

            // Alright, hold the beam.
            // Zaprillator revives the player after one second!
            yield return new WaitForSeconds(1f);
            yield return null; // Make sure the revive actually goes through!

            // Still shocking......I think we hit the wrong target....
            if (patcherTool.isShocking)
            {
                heldItem.UseItemOnClient(true); // Break the beam!
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to revive player {playerToRevive.playerUsername} using Zaprillator but the Zap Gun hit the wrong target!");
                StopReviveCoroutine();
                yield break;
            }

            // Alright, we are no longer shocking the target, but the revive is done through an RPC.
            // This means we need to wait a bit before terminating the coroutine. Incase we have to do this again.
            yield return new WaitForSeconds(1f); // One second to allow RPC to got to server and back to us!
            StopReviveCoroutine();
        }

        /// <summary>
        /// Helper function that handles reviving our target player
        /// </summary>
        /// <remarks>
        /// This is done so if revive company isn't installed, my mod won't error out!
        /// </remarks>
        private void DoZaprillatorLogic()
        {
            // Wait for fallback to be caculated
            if (!fallbackPos.HasValue || !ai.IsValidPathToTarget(fallbackPos.Value))
            {
                StartFallbackCoroutine(); // Just in case the coroutine failed somehow!
                return;
            }

            // Move towards our fallback position via safe path
            float sqrDistToFallback = (fallbackPos.Value - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToFallback >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                // Find a safe path to our fallback spot
                StartSafePathCoroutine();

                float sqrMagDistanceToSafePos = (this.safePathPos - npcController.Npc.transform.position).sqrMagnitude;
                if (sqrMagDistanceToSafePos >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                {
                    // Alright lets go outside!
                    ai.SetDestinationToPositionLethalBotAI(this.safePathPos);

                    // Sprint if far enough
                    if (!npcController.WaitForFullStamina && sqrMagDistanceToSafePos > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING) // NEEDTOVALIDATE: Should we use the distance to the ship or the safe position?
                    {
                        npcController.OrderToSprint();
                    }
                    else
                    {
                        npcController.OrderToStopSprint();
                    }

                    ai.OrderMoveToDestination();
                }
                else
                {
                    // Wait here until its safe to move to the entrance
                    ai.StopMoving();
                    npcController.OrderToStopSprint();
                }

            }
            else
            {
                // Alright we are close enough to the fallback position, stop moving
                ai.StopMoving();
                npcController.OrderToStopSprint();
                StopSafePathCoroutine(); // Don't need this anymore

                // We need to set down the body before reviving
                shouldPickupBody = false; // Don't try to pick up the body again, we need it on the ground for the revive
                if (!ai.AreHandsFree() && ai.HeldItem is not PatcherTool)
                {
                    ai.DropItem();
                    return;
                }

                if (reviveCoroutine == null)
                {
                    reviveCoroutine = ai.StartCoroutine(reviveUsingZaprillator());
                }
            }
        }

        /// <summary>
        /// Helper function that moves the bot over to the bunkbed to do the revive
        /// </summary>
        /// <remarks>
        /// This is done so if bunkbed revive isn't installed, my mod won't error out!
        /// </remarks>
        private void DoBunkBedReviveLogic()
        {
            // First things first, find the bunkbed
            BunkbedController? bunkbedController = BunkbedController.Instance;
            if (bunkbedController == null)
            {
                return;
            }

            // Move towards the bunkbed
            Vector3 bunkbedPos = RoundManager.Instance.GetNavMeshPosition(bunkbedController.transform.position, default, 2.7f);
            float sqrDistToBunkbed = (bunkbedPos - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToBunkbed >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                // Alright lets go outside!
                ai.SetDestinationToPositionLethalBotAI(bunkbedPos);

                // Sprint if far enough
                if (!npcController.WaitForFullStamina && sqrDistToBunkbed > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING)
                {
                    npcController.OrderToSprint();
                }
                else
                {
                    npcController.OrderToStopSprint();
                }

                ai.OrderMoveToDestination();

            }
            else
            {
                // Alright we are close enough to the bunkbed position, stop moving
                ai.StopMoving();
                npcController.OrderToStopSprint();

                if (LethalBotManager.Instance.IsPlayerLethalBot(this.playerToRevive))
                {
                    BunkbedReviveBot();
                    return;
                }

                RagdollGrabbableObject? ragdollGrabbableObject = ai.HeldItem as RagdollGrabbableObject;
                if (ragdollGrabbableObject == null || BunkbedReviveRPCDelegate == null)
                {
                    return;
                }

                int reviveCost = BunkbedController.GetReviveCost();
                if (TerminalManager.Instance.GetTerminal().groupCredits < reviveCost)
                {
                    return;
                }
                if (!BunkbedController.CanRevive(ragdollGrabbableObject.bodyID.Value, logStuff: true))
                {
                    return;
                }
                Terminal terminalScript = TerminalManager.Instance.GetTerminal();
                terminalScript.groupCredits -= reviveCost;
                LethalBotManager.Instance.SyncGroupCreditsForNotOwnerTerminalServerRpc(terminalScript.groupCredits, terminalScript.numberOfItemsInDropship);
                BunkbedReviveRPCDelegate.Invoke(ragdollGrabbableObject.bodyID.Value);
                npcController.Npc?.DespawnHeldObject();
            }
        }

        /// <summary>
        /// So uh, the way Bunkbed revive was patched makes it not work with bots. I have to recreate its logic as a result!
        /// </summary>
        private void BunkbedReviveBot()
        {
            RagdollGrabbableObject? ragdollGrabbableObject = ai.HeldItem as RagdollGrabbableObject;
            if (ragdollGrabbableObject == null)
            {
                return;
            }

            int playerClientId = (int)ragdollGrabbableObject.ragdoll.playerScript.playerClientId;
            string name = ragdollGrabbableObject.ragdoll.gameObject.GetComponentInChildren<ScanNodeProperties>().headerText;
            LethalBotIdentity? lethalBotIdentity = IdentityManager.Instance.FindIdentityFromBodyName(name);
            if (lethalBotIdentity == null)
            {
                return;
            }

            // Get the same logic as the mod at the beginning
            if (lethalBotIdentity.Alive)
            {
                Plugin.LogError($"BunkbedRevive with LethalBot: error when trying to revive bot \"{lethalBotIdentity.Name}\", bot is already alive! do nothing more");
                return;
            }

            int reviveCost = BunkbedController.GetReviveCost();
            if (TerminalManager.Instance.GetTerminal().groupCredits < reviveCost)
            {
                return;
            }
            if (!BunkbedController.CanRevive(ragdollGrabbableObject.bodyID.Value, logStuff: true))
            {
                return;
            }
            Terminal terminalScript = TerminalManager.Instance.GetTerminal();
            terminalScript.groupCredits -= reviveCost;
            LethalBotManager.Instance.SyncGroupCreditsForNotOwnerTerminalServerRpc(terminalScript.groupCredits, terminalScript.numberOfItemsInDropship);

            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(StartOfRound.Instance, playerClientId, simpleTeleport: false),
                                                                YRot = 0,
                                                                IsOutside = true,
                                                                IndexNextPlayerObject = playerClientId
                                                            });
            LethalBotManager.Instance.UpdateReviveCountServerRpc(lethalBotIdentity.IdIdentity + Plugin.PluginIrlPlayersCount);
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is spawned
            StartOfRound.Instance.livingPlayers++;
            npcController.Npc?.DespawnHeldObject();
        }

        /// <summary>
        /// Checks if the bot has the Zap Gun in its inventory!
        /// </summary>
        /// <remarks>
        /// Can't use the default FindObject since its a member function not static!
        /// </remarks>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private static bool FindZapGun(GrabbableObject item)
        {
            return item is PatcherTool zapGun && !zapGun.insertedBattery.empty; // For anyone wondering PatcherTool is the internal class name for the Zap Gun!
        }

        private void StopReviveCoroutine()
        {
            if (reviveCoroutine != null)
            {
                ai.StopCoroutine(reviveCoroutine);
                reviveCoroutine = null;
            }
        }
    }
}
