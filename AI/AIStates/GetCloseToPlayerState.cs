using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State where the bot has a target player and wants to get close to them.
    /// </summary>
    public class GetCloseToPlayerState : AIState
    {
        private Vector3? currentFollowPosition;
        private LethalBotSearchRoutine wanderNearbyPlayer = null!;

        public GetCloseToPlayerState(AIState state) : base(state)
        {
            CurrentState = EnumAIStates.GetCloseToPlayer;
            InitializeWanderRoutine();
        }

        public GetCloseToPlayerState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.GetCloseToPlayer;
            InitializeWanderRoutine();
        }

        public GetCloseToPlayerState(LethalBotAI ai, PlayerControllerB targetPlayer) : this(ai)
        {
            ai.targetPlayer = targetPlayer;
        }

        public GetCloseToPlayerState(AIState state, PlayerControllerB targetPlayer) : this(state)
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
            PlayerControllerB lethalBotController = npcController.Npc;
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Wait for ship to land before doing anything!
            StartOfRound instanceSOR = StartOfRound.Instance;
            if ((lethalBotController.isInElevator || lethalBotController.isInHangarShipRoom)
                && !LethalBotManager.AreWeInOrbit(instanceSOR)
                && (LethalBotManager.IsTheShipLeaving(instanceSOR)
                    || !LethalBotManager.IsTheShipLanded(instanceSOR)))
            {
                return;
            }

            // If we are in a group, only follow the group leader
            int groupID = GroupManager.Instance.GetGroupId(lethalBotController);
            if (groupID != GroupManager.INVALID_GROUP_INDEX)
            {
                PlayerControllerB? groupLeader = GroupManager.Instance.GetGroupLeader(groupID);
                if (groupLeader != null)
                {
                    if (groupLeader == lethalBotController)
                    {
                        ai.State = new SearchingForScrapState(this);
                        return;
                    }
                    else if (ai.targetPlayer != groupLeader)
                    {
                        ai.targetPlayer = groupLeader;
                        targetLastKnownPosition = groupLeader.transform.position;
                    }
                }
                // This should never happen, but if it does......
                else
                {
                    GroupManager.Instance.RemoveFromCurrentGroupAndSync(lethalBotController);
                }
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

            if (!ai.PlayerIsTargetable(ai.targetPlayer, false, true, false))
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

            // Check to see if we can heal someone!
            playerController = ai.LookingForPlayerToHeal();
            if (playerController != null)
            {
                ai.State = new HealPlayerState(this, playerController);
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
                if (!ai.searchForScrap.visitInProgress && !ai.isOutside)
                {
                    ai.searchForScrap.StartVisit();
                }
            }
            else if (ai.searchForScrap.visitInProgress)
            {
                ai.searchForScrap.StopSearch();
            }

            // If we are at the company building and have sellable items, we should sell it!
            if (LethalBotManager.AreWeAtTheCompanyBuilding() 
                && ai.HasSellableItemInInventory())
            {
                ai.State = new SellScrapState(this);
                return;
            }

            // Select and use items based on our current situation, if needed
            SelectBestItemFromInventory();

            Vector3 targetPlayerPos = ai.targetPlayer.transform.position;
            EnumFollowType enumFollowType = ai.GetFollowType();
            Vector3 followPos = GetFollowPosition(enumFollowType);
            if (enumFollowType != EnumFollowType.Wander)
            {
                if (wanderNearbyPlayer.searchInProgress)
                {
                    wanderNearbyPlayer.StopSearch();
                }
            }

            // Target is in awarness range
            float sqrHorizontalDistanceWithTarget = Vector3.Scale((followPos - lethalBotController.transform.position), new Vector3(1, 0, 1)).sqrMagnitude;
            float sqrVerticalDistanceWithTarget = Vector3.Scale((followPos - lethalBotController.transform.position), new Vector3(0, 1, 0)).sqrMagnitude;
            if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR
                    && sqrVerticalDistanceWithTarget < Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER)
            {
                targetLastKnownPosition = targetPlayerPos;

                // Don't interrupt elevator code!
                if (!TryToUseElevator())
                {
                    MoveTowardsFollowPosition(enumFollowType, followPos);
                }
            }
            else
            {
                // Target outside of awareness range, if ai does not see target, then the target is lost
                //Plugin.LogDebug($"{ai.lethalBotController.playerUsername} no see target, still in range ? too far {sqrHorizontalDistanceWithTarget > Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR}, too high/low {sqrVerticalDistanceWithTarget > Const.DISTANCE_AWARENESS_VER * Const.DISTANCE_AWARENESS_VER}");
                PlayerControllerB? checkTarget = ai.CheckLOSForTarget(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
                if (checkTarget == null)
                {
                    ai.State = new JustLostPlayerState(this);
                    return;
                }
                else
                {
                    // Target still visible
                    targetLastKnownPosition = targetPlayerPos;

                    // If we can't path to the player, this is probably a mineshaft map and they are probably on a diffrent floor than us!
                    if (targetLastKnownPosition.HasValue && LethalBotAI.ElevatorScript != null && !ai.IsValidPathToTarget(targetLastKnownPosition.Value, false))
                    {
                        // Don't interrupt elevator code!
                        if (!TryToUseElevator())
                        {
                            MoveTowardsFollowPosition(enumFollowType, followPos);
                        }
                    }
                    else
                    {
                        MoveTowardsFollowPosition(enumFollowType, followPos, allowTeleport: true);
                    }
                }
            }

            // Follow player
            // If close enough, chill with player
            // Sprint if far, stop sprinting if close
            if (sqrHorizontalDistanceWithTarget < Const.DISTANCE_CLOSE_ENOUGH_HOR * Const.DISTANCE_CLOSE_ENOUGH_HOR
                && sqrVerticalDistanceWithTarget < Const.DISTANCE_CLOSE_ENOUGH_VER * Const.DISTANCE_CLOSE_ENOUGH_VER
                && enumFollowType != EnumFollowType.Wander)
            {
                ai.State = new ChillWithPlayerState(this, currentFollowPosition);
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

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            if (wanderNearbyPlayer.searchInProgress)
            {
                wanderNearbyPlayer.StopSearch();
            }
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are following a player, these messages mean nothing to us!
            SignalTranslatorCommandsManager.RegisterIgnoreDefaultForState<GetCloseToPlayerState>();
        }

        /// <summary>
        /// This returns the position the bot wants to move to in order to follow <see cref="EnemyAI.targetPlayer"/>
        /// </summary>
        /// <returns></returns>
        public Vector3 GetFollowPosition(EnumFollowType enumFollowType)
        {
            Vector3 targetPlayerPos = ai.targetPlayer.transform.position;
            switch (enumFollowType)
            {
                case EnumFollowType.Nearby:
                {
                    if (!currentFollowPosition.HasValue 
                        || (currentFollowPosition.Value - targetPlayerPos).sqrMagnitude > Const.DISTANCE_CLOSE_ENOUGH_HOR * Const.DISTANCE_CLOSE_ENOUGH_HOR)
                    {
                        // Ripped right from how the base game does it for Babbon Hawks.
                        currentFollowPosition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(targetPlayerPos, Const.DISTANCE_CLOSE_ENOUGH_HOR, RoundManager.Instance.navHit);
                    }
                    return currentFollowPosition.Value; // Head to our selected follow position
                }

                case EnumFollowType.Wander:
                {
                    wanderNearbyPlayer.searchCenter = targetPlayerPos;
                    if (!wanderNearbyPlayer.searchInProgress)
                    {
                        // Start the coroutine to wander nearby the player we our following
                        wanderNearbyPlayer.StartSearch();
                    }

                    // Now then, let the search routine find a place to wander to
                    Vector3? target = wanderNearbyPlayer.GetTargetPosition();
                    if (target.HasValue)
                    {
                        currentFollowPosition = target.Value;
                    }

                    // Just in case the player is moving far across the map, we should do standard following until the player stops moving as much
                    if (currentFollowPosition.HasValue 
                        && (currentFollowPosition.Value - targetPlayerPos).sqrMagnitude > Const.DISTANCE_AWARENESS_HOR * Const.DISTANCE_AWARENESS_HOR)
                    {
                        currentFollowPosition = null;
                    }

                    // Lets go
                    return currentFollowPosition ?? targetPlayerPos;
                }

                case EnumFollowType.Standard:
                default:
                {
                    currentFollowPosition = null;
                    return targetPlayerPos; // Lets follow the leader!
                }
            }
        }

        private bool TryToUseElevator()
        {
            // Lets see if the bot is trying to use the elevator
            bool usingElevator = false;
            bool planningToUseElevator = false;
            if (ai.targetPlayer.isInsideFactory)
            {
                bool isPlayerNearElevatorEntrance = ai.IsPlayerNearElevatorEntrance(ai.targetPlayer);
                if (isPlayerNearElevatorEntrance && !ai.IsInElevatorStartRoom)
                {
                    usingElevator = ai.UseElevator(true);
                    planningToUseElevator = true;

                    // If we are going to use the elevator to go up,
                    // we must drop the baby maneater before using the elevator
                    if (usingElevator
                        && ai.HeldItem is CaveDwellerPhysicsProp)
                    {
                        npcController.Npc.DiscardHeldObject();
                    }
                }
                else if (!isPlayerNearElevatorEntrance && ai.IsInElevatorStartRoom)
                {
                    usingElevator = ai.UseElevator(false);
                    planningToUseElevator = true;
                }
            }
            return usingElevator || planningToUseElevator;
        }

        private void MoveTowardsFollowPosition(EnumFollowType enumFollowType, Vector3? followPos = null, bool allowTeleport = false)
        {
            followPos ??= GetFollowPosition(enumFollowType);
            ai.SyncAssignTargetAndSetMovingTo(ai.targetPlayer);
            ai.SetDestinationToPositionLethalBotAI(followPos.Value);

            if (allowTeleport)
            {
                // Bring closer with teleport if possible
                ai.CheckAndBringCloserTeleportLethalBot(0.8f);
            }

            ai.OrderMoveToDestination();
        }

        private void InitializeWanderRoutine()
        {
            wanderNearbyPlayer = new LethalBotSearchRoutine(ai)
            {
                searchCenterFollowsAI = EnumSearchCenter.SetPosition,
                searchRadius = Const.DISTANCE_AWARENESS_HOR,
                //proximityThreshold = 0f
            };
        }
    }
}
