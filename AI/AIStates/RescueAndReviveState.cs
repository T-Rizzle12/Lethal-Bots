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
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    public class RescueAndReviveState : AIState
    {
        private PlayerControllerB playerToRevive;
        private ReviveMethod reviveMethod = ReviveMethod.None;
        private Coroutine? fallbackCoroutine;
        private Coroutine? reviveCoroutine;
        private Vector3? fallbackPos;
        private bool shouldPickupBody;

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
                if (!Plugin.IsModReviveCompanyLoaded
                    && !Plugin.IsModBunkbedReviveLoaded
                    && !Plugin.IsModZaprillatorLoaded)
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
                    if (heldItem != null && heldItem.itemProperties.twoHanded)
                    {
                        ai.DropItem();
                    }
                    LethalBotAI.DictJustDroppedItems.Remove(playerBody); // HACKHACK: Skip the dropped item cooldown so bot can grab the body immediately
                    if (ai.IsGrabbableObjectGrabbable(playerBody))
                    {
                        ai.State = new FetchingObjectState(this, playerBody);
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
            if (Plugin.IsModReviveCompanyLoaded && ReviveCompanyCanRevivePlayer(this.playerToRevive))
            {
                return ReviveMethod.ModReviveCompany;
            }
            else if (Plugin.IsModZaprillatorLoaded && ZaprillatorCanRevivePlayer(this.playerToRevive))
            {
                return ReviveMethod.ModZaprillator;
            }
            else if (Plugin.IsModBunkbedReviveLoaded && BunkbedReviveCanRevivePlayer(this.playerToRevive))
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
        /// <param name="playerToRevive">The player to check for revival eligibility.</param>
        /// <returns>true if the player can be revived using the ReviveCompany mod; otherwise, false.</returns>
        private bool ReviveCompanyCanRevivePlayer(PlayerControllerB playerToRevive)
        {
            if (Plugin.IsModReviveCompanyLoaded)
            {
                if (playerToRevive.deadBody.grabBodyObject is RagdollGrabbableObject ragdollGrabbableObject)
                {
                    return ReviveCompanyPlayerControllerBPatchPatch.CanRevive_ReversePatch(ragdollGrabbableObject);
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the Zaprillator mod can revive the specified player.
        /// </summary>
        /// <remarks>
        /// This method currently does not support player revival through the Zaprillator mod,
        /// regardless of the player's state or mod status.
        /// </remarks>
        /// <param name="playerToRevive">The player to check for revival eligibility by the Zaprillator mod.</param>
        /// <returns>Always returns false, indicating that the Zaprillator mod cannot revive the specified player.</returns>
        private bool ZaprillatorCanRevivePlayer(PlayerControllerB playerToRevive)
        {
            // BROKEN ON PURPOSE!
            // For some reason Zaprillator is completely marked as internal, making it extremely difficult to access any of its methods or properties.
            // This will be fixed once I figure out how to use Harmony's AccessTools to create reverse patches for internal methods.
            if (Plugin.IsModZaprillatorLoaded)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified player can be revived using Bunkbed Revive.
        /// </summary>
        /// <param name="playerToRevive">The player to check for Bunkbed Revive eligibility.</param>
        /// <returns>true if the player can be revived using Bunkbed Revive; otherwise, false.</returns>
        private bool BunkbedReviveCanRevivePlayer(PlayerControllerB playerToRevive)
        {
            // Bunkbed Revive can only be used on the ship!
            if (!npcController.Npc.isInElevator && !npcController.Npc.isInHangarShipRoom)
            {
                return false;
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
                    return ReviveCompanyCanRevivePlayer(this.playerToRevive);
                case ReviveMethod.ModBunkbedRevive:
                    return BunkbedReviveCanRevivePlayer(this.playerToRevive);
                case ReviveMethod.ModZaprillator:
                    return ZaprillatorCanRevivePlayer(this.playerToRevive);
                default:
                    return false;
            }
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
                Transform nodeTransform = nodes[i].transform;

                // Give the main thread a chance to do something else
                if (i % 15 == 0)
                {
                    yield return null;
                }

                // Can we path to the node and is it safe?
                Vector3 nodePos = nodeTransform.position;
                if (!ai.IsValidPathToTarget(nodePos))
                {
                    continue;
                }

                // Check if the node is exposed to enemies
                bool isNodeSafe = true;
                Vector3 simulatedHead = nodePos + Vector3.up * headOffset;
                RoundManager instanceRM = RoundManager.Instance;
                for (int j = 0; j < instanceRM.SpawnedEnemies.Count; j++)
                {
                    EnemyAI checkLOSToTarget = instanceRM.SpawnedEnemies[j];
                    if (checkLOSToTarget.isEnemyDead || ourWeOutside != checkLOSToTarget.isOutside)
                    {
                        continue;
                    }

                    // Give the main thread a chance to do something else
                    if (j % 10 == 0)
                    {
                        yield return null;
                    }

                    // Check if the target is a threat!
                    float? dangerRange = ai.GetFearRangeForEnemies(checkLOSToTarget, EnumFearQueryType.PathfindingAvoid);
                    Vector3 enemyPos = checkLOSToTarget.transform.position;
                    if (dangerRange.HasValue && (enemyPos - nodePos).sqrMagnitude <= dangerRange * dangerRange)
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
                    Turret turret = turrets[k];
                    if (!turret.isActiveAndEnabled)
                    {
                        continue;
                    }

                    // Give the main thread a chance to do something else
                    if (k % 15 == 0)
                    {
                        yield return null;
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
                    SpikeRoofTrap spikeRoofTrap = spikeRoofTraps[k];
                    if (!spikeRoofTrap.isActiveAndEnabled)
                    {
                        continue;
                    }

                    // Give the main thread a chance to do something else
                    if (k % 15 == 0)
                    {
                        yield return null;
                    }

                    // Just a simple distance check should be enough
                    Vector3 spikeRoofTrapPos = spikeRoofTrap.spikeTrapAudio.transform.position;
                    const float safeDistance = 20f; // Arbitrary safe distance from spike roof traps
                    if ((spikeRoofTrapPos - nodePos).sqrMagnitude <= safeDistance * safeDistance)
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

                Plugin.LogDebug($"Bot {npcController.Npc.playerUsername} found fallback spot at {nodeTransform.position}!");
                fallbackPos = nodeTransform.position;
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
            if (this.playerToRevive == null || !ReviveCompanyCanRevivePlayer(this.playerToRevive))
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
            if (!fallbackPos.HasValue)
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
            float sqrDistToBunkbed = (bunkbedController.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToBunkbed >= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
            {
                // Alright lets go outside!
                ai.SetDestinationToPositionLethalBotAI(bunkbedController.transform.position);

                // Sprint if far enough
                if (!npcController.WaitForFullStamina && sqrDistToBunkbed > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING) // NEEDTOVALIDATE: Should we use the distance to the ship or the safe position?
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
                if (ragdollGrabbableObject == null)
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
                BunkbedNetworkingPatch.RevivePlayerServerRpc_ReversePatch(ragdollGrabbableObject.bodyID.Value);
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
