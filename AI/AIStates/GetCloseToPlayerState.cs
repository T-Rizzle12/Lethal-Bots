using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot has a target player and wants to get close to them.
    /// </summary>
    public class GetCloseToPlayerState : AIState
    {
        /// <summary>
        /// <inheritdoc cref="AIState(AIState)"/>
        /// </summary>
        public GetCloseToPlayerState(AIState state) : base(state)
        {
            CurrentState = EnumAIStates.GetCloseToPlayer;
        }

        /// <summary>
        /// <inheritdoc cref="AIState(LethalBotAI)"/>
        /// </summary>
        public GetCloseToPlayerState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.GetCloseToPlayer;
        }

        public GetCloseToPlayerState(LethalBotAI ai, PlayerControllerB targetPlayer) : this(ai)
        {
            ai.targetPlayer = targetPlayer;
        }

        public override void OnEnterState()
        {
            // Kinda hard to transfer loot when you're following a player!
            if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
            {
                LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
            }
            base.OnEnterState();
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
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Wait for ship to land before doing anything!
            if ((npcController.Npc.isInElevator || npcController.Npc.isInHangarShipRoom)
                && (StartOfRound.Instance.shipIsLeaving
                    || !StartOfRound.Instance.shipHasLanded))
            {
                return;
            }

            // Lost target player
            if (ai.targetPlayer == null)
            {
                // Last position unknown
                if (this.targetLastKnownPosition.HasValue)
                {
                    ai.State = new JustLostPlayerState(this);
                    return;
                }

                ai.State = new SearchingForPlayerState(this);
                return;
            }

            if (!ai.PlayerIsTargetable(ai.targetPlayer, false, true))
            {
                // Target is not available anymore
                ai.State = new SearchingForPlayerState(this);
                return;
            }

            VehicleController? vehicleController = ai.GetVehicleCruiserTargetPlayerIsIn();
            if (vehicleController != null)
            {
                ai.State = new PlayerInCruiserState(this, vehicleController);
                return;
            }

            // Check to see if we can revive anyone!
            PlayerControllerB? playerController = ai.LookingForPlayerToRevive();
            if (playerController != null)
            {
                ai.State = new RescueAndReviveState(this, playerController);
                return;
            }

            // Check for object to grab
            if (ai.HasSpaceInInventory())
            {
                GrabbableObject? grabbableObject = ai.LookingForObjectToGrab();
                if (grabbableObject != null)
                {
                    ai.State = new FetchingObjectState(this, grabbableObject);
                    return;
                }
            }

            // If we are at the company building and have some scrap, we should sell it!
            if (LethalBotManager.AreWeAtTheCompanyBuilding() 
                && ai.HasScrapInInventory())
            {
                ai.State = new SellScrapState(this);
                return;
            }

            // Target is in awarness range
            float sqrHorizontalDistanceWithTarget = Vector3.Scale((ai.targetPlayer.transform.position - npcController.Npc.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
            float sqrVerticalDistanceWithTarget = Vector3.Scale((ai.targetPlayer.transform.position - npcController.Npc.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
            if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR
                    && sqrVerticalDistanceWithTarget < Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER)
            {
                targetLastKnownPosition = ai.targetPlayer.transform.position;

                // If we can't path to the player, this is probably a mineshaft map and they are probably on a diffrent floor than us!
                if (targetLastKnownPosition.HasValue && LethalBotAI.ElevatorScript != null && !ai.IsValidPathToTarget(targetLastKnownPosition.Value, false))
                {
                    if (ai.targetPlayer.isInsideFactory)
                    {
                        bool isPlayerNearElevatorEntrance = ai.IsPlayerNearElevatorEntrance(ai.targetPlayer);
                        if (isPlayerNearElevatorEntrance && !ai.IsInElevatorStartRoom)
                        {
                            bool usingElevator = ai.UseElevator(true);

                            // If we are going to use the elevator to go up,
                            // we must drop the baby maneater before using the elevator
                            if (usingElevator
                            && !ai.AreHandsFree()
                            && ai.HeldItem is CaveDwellerPhysicsProp)
                            {
                                ai.DropItem();
                            }
                        }
                        else if (!isPlayerNearElevatorEntrance && ai.IsInElevatorStartRoom)
                        {
                            ai.UseElevator(false);
                        }
                        else
                        {
                            ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
                            ai.OrderMoveToDestination();
                        }
                    }
                    else
                    {
                        ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
                        ai.OrderMoveToDestination();
                    }
                }
                else
                {
                    ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
                    ai.OrderMoveToDestination();
                }
            }
            else
            {
                // Target outside of awareness range, if ai does not see target, then the target is lost
                //Plugin.LogDebug($"{ai.NpcController.Npc.playerUsername} no see target, still in range ? too far {sqrHorizontalDistanceWithTarget > Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR}, too high/low {sqrVerticalDistanceWithTarget > Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER}");
                PlayerControllerB? checkTarget = ai.CheckLOSForTarget(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (checkTarget == null)
                {
                    ai.State = new JustLostPlayerState(this);
                    return;
                }
                else
                {
                    // Target still visible
                    targetLastKnownPosition = ai.targetPlayer.transform.position;
                    // If we can't path to the player, this is probably a mineshaft map and they are probably on a diffrent floor than us!
                    if (targetLastKnownPosition.HasValue && LethalBotAI.ElevatorScript != null && !ai.IsValidPathToTarget((Vector3)targetLastKnownPosition, false))
                    {
                        if (ai.targetPlayer.isInsideFactory)
                        {
                            bool isPlayerNearElevatorEntrance = ai.IsPlayerNearElevatorEntrance(ai.targetPlayer);
                            if (isPlayerNearElevatorEntrance && !ai.IsInElevatorStartRoom)
                            {
                                bool usingElevator = ai.UseElevator(true);

                                // If we are going to use the elevator to go up,
                                // we must drop the baby maneater before using the elevator
                                if (usingElevator
                                && !ai.AreHandsFree()
                                && ai.HeldItem is CaveDwellerPhysicsProp)
                                {
                                    ai.DropItem();
                                }
                            }
                            else if (!isPlayerNearElevatorEntrance && ai.IsInElevatorStartRoom)
                            {
                                ai.UseElevator(false);
                            }
                            else
                            {
                                ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
                                ai.OrderMoveToDestination();
                            }
                        }
                        else
                        {
                            ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
                            ai.OrderMoveToDestination();
                        }
                    }
                    else
                    {
                        ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);

                        // Bring closer with teleport if possible
                        ai.CheckAndBringCloserTeleportLethalBot(0.8f);
                        ai.OrderMoveToDestination();
                    }
                }
            }

            // Follow player
            // If close enough, chill with player
            // Sprint if far, stop sprinting if close
            if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_CLOSE_ENOUGH_HOR * Const.DISTANCE_CLOSE_ENOUGH_HOR
                && sqrVerticalDistanceWithTarget < Const.DISTANCE_CLOSE_ENOUGH_VER * Const.DISTANCE_CLOSE_ENOUGH_VER)
            {
                ai.State = new ChillWithPlayerState(this);
                return;
            }
            else if (sqrHorizontalDistanceWithTarget > Const.DISTANCE_START_RUNNING * Const.DISTANCE_START_RUNNING
                     || sqrVerticalDistanceWithTarget > 0.3f * 0.3f)
            {
                npcController.OrderToSprint();
            }
            else if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_STOP_RUNNING * Const.DISTANCE_STOP_RUNNING)
            {
                npcController.OrderToStopSprint();
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Default states, wait for cooldown and if no one is talking close
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = EnumVoicesState.FollowingPlayer,
                CanTalkIfOtherLethalBotTalk = false,
                WaitForCooldown = true,
                CutCurrentVoiceStateToTalk = false,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        // We are following a player, these messages mean nothing to us!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            return;
        }
    }
}
