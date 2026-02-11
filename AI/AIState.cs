using GameNetcodeStuff;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.EnemiesPatches;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LethalBots.AI
{
    /// <summary>
    /// Abstract state main class for the <c>AIState</c>
    /// </summary>
    public abstract class AIState
    {
        protected LethalBotAI ai;

        protected AIState? previousAIState;
        public EnumAIStates? previousState { protected set; get; }

        private EnumAIStates currentState;
        protected EnumAIStates CurrentState
        {
            get
            {
                return this.currentState;
            }
            set
            {
                this.currentState = value;
                Plugin.LogDebug($"Bot {npcController.Npc.playerClientId} ({npcController.Npc.playerUsername}) new state :                 {this.currentState}");
            }
        }

        /// <summary>
        /// <c>NpcController</c> from the <c>LethalBotAI</c>
        /// </summary>
        protected NpcController npcController;
        protected AISearchRoutine searchForPlayers;
        protected AISearchRoutine searchForScrap;

        protected Vector3? targetLastKnownPosition;
        public GrabbableObject? TargetItem
        {
            protected set;
            get;
        }
        protected AIStateInfo previousStateUpdate;

        protected Coroutine? panikCoroutine;
        protected Coroutine? safePathCoroutine;
        protected Coroutine? lookingAroundCoroutine;
        protected EnemyAI? currentEnemy;
        protected Vector3 safePathPos; // The closest point to targetShipPos that is safe
        protected CancellationTokenSource? pathfindCancellationToken = null; // For use in the async danger pathfinder
        protected EntranceTeleport? targetEntrance = null;
        protected bool hasBeenStarted = false;
        private GameObject? lastStuckNode;
        private static Dictionary<EntranceTeleport, (bool isSafe, float lastSafetyCheck)> entranceSafetyCache = new Dictionary<EntranceTeleport, (bool isSafe, float lastSafetyCheck)>();

        /// <summary>
        /// Constructor from another state
        /// </summary>
        /// <param name="oldState"></param>
        protected AIState(AIState oldState, AIState? changeToOnEnd = null) : this(oldState.ai, changeToOnEnd)
        {
            this.previousAIState = changeToOnEnd ?? oldState;
            this.previousState = oldState.GetAIState();
            this.targetLastKnownPosition = oldState.targetLastKnownPosition;
            this.TargetItem = oldState.TargetItem;

            this.panikCoroutine = oldState.panikCoroutine;
            this.currentEnemy = oldState.currentEnemy;

            this.searchForPlayers = oldState.searchForPlayers;
            this.searchForScrap = oldState.searchForScrap;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ai"></param>
        /// <exception cref="System.NullReferenceException"><c>LethalBotAI</c> null in parameters</exception>
        protected AIState(LethalBotAI ai, AIState? changeToOnEnd = null)
        {
            if (ai == null)
            {
                throw new System.NullReferenceException("Enemy AI is null.");
            }

            this.previousAIState = changeToOnEnd;
            this.ai = ai;

            this.npcController = ai.NpcController;

            // Mark our current position as "safe"
            // We will adjust our safe pos later as required
            this.safePathPos = this.npcController.Npc.transform.position;

            if (this.searchForPlayers == null)
            {
                this.searchForPlayers = new AISearchRoutine();
                this.searchForPlayers.randomized = true;
            }
            if (this.searchForScrap == null)
            {
                this.searchForScrap = new AISearchRoutine();
                this.searchForScrap.randomized = true;
            }
        }

        // Create a destructor here to destory the token incase we somehow get removed or something
        // without calling the CancelAsyncPathfindToken function
        ~AIState()
        {
            CancelAsyncPathfindToken();
        }

        /// <summary>
        /// Executes when the bot enters this state!
        /// </summary>
        /// <remarks>
        /// By default, this is called when <see cref="LethalBotAI.State"/> is set to this state.<br/>
        /// You should use this to initialize variables, set up the bot, etc.<br/>
        /// You can use <see cref="hasBeenStarted"/> to check if this state has been started before.<br/>
        /// </remarks>
        public virtual void OnEnterState()
        {
            // Mark our current position as "safe"
            // We will adjust our safe pos later as required
            this.safePathPos = this.npcController.Npc.transform.position;
            hasBeenStarted = true;
        }

        /// <summary>
        /// Executes when the bot leaves this state!
        /// </summary>
        /// <remarks>
        /// By default, this is called when <see cref="LethalBotAI.State"/> is set to another state.<br/>
        /// You should use this to save variables, or reset the bot, etc.<br/>
        /// You can use <see cref="hasBeenStarted"/> to check if this state has been started before.<br/>
        /// </remarks>
        /// <param name="newState">The new state the bot is entering</param>
        public virtual void OnExitState(AIState newState) { }

        /// <summary>
        /// Apply the behaviour according to the type of state <see cref="Enums.EnumAIStates"><c>Enums.EnumAIStates</c></see>.<br/>
        /// </summary>
        public abstract void DoAI();

        public abstract void TryPlayCurrentStateVoiceAudio();

        // TODO: Now that we have voice recogntion code, it may be a good idea to update this!!!!!
        public virtual void PlayerHeard(Vector3 noisePosition) { }

        // TODO: Remove this function, I don't think I will ever find a way to get this to work.
        [Obsolete("Broken on purpose! This function is never called and will never be fixed since you can't tell who created a sound!")]
        public virtual void EnemyHeard(Vector3 noisePosition) { }

        /// <summary>
        /// Called when the bot is stuck and can't move!<br/>
        /// </summary>
        public virtual void OnBotStuck() 
        {
            Transform? closestNode = GetClosestNode(npcController.Npc.transform.position, lastStuckNode);
            lastStuckNode = closestNode?.gameObject;
            if (closestNode != null)
            {
                ai.SyncTeleportLethalBot(closestNode.position);
            }
        }

        /// <summary>
        /// Helper function that is similar to <see cref="RoundManager.GetClosestNode(Vector3, bool)"/>,
        /// but adjusted for bot use!
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        private Transform? GetClosestNode(Vector3 pos, GameObject? ignoreNode = null)
        {
            GameObject[] array;
            if (ai.isOutside)
            {
                if (RoundManager.Instance.outsideAINodes == null)
                {
                    RoundManager.Instance.outsideAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
                }

                array = RoundManager.Instance.outsideAINodes;
            }
            else
            {
                if (RoundManager.Instance.insideAINodes == null)
                {
                    RoundManager.Instance.insideAINodes = GameObject.FindGameObjectsWithTag("AINode");
                }

                array = RoundManager.Instance.insideAINodes;
            }

            float num = 99999f;
            GameObject? closestNode = null;
            foreach (GameObject node in array)
            {
                float sqrMagnitude = (node.transform.position - pos).sqrMagnitude;
                if (sqrMagnitude < num && ignoreNode != node)
                {
                    num = sqrMagnitude;
                    closestNode = node;
                }
            }

            return closestNode?.transform;
        }

        /// <summary>
        /// Called when the bot recevies a message from the signal translator!
        /// </summary>
        /// <remarks>
        /// WARNING: All messages are forced into lower case!
        /// </remarks>
        /// <param name="message"></param>
        public virtual void OnSignalTranslatorMessageReceived(string message)
        {
            if (message == "return")
            {
                ai.State = new ReturnToShipState(this);
            }
            else if (message == "jester")
            {
                if (ai.isOutside)
                {
                    return;
                }
                EnemyAI? enemyAI = FindNearbyJester();
                if (enemyAI == null)
                {
                    return;
                }
                ai.State = new PanikState(this, enemyAI);
            }
        }

        /// <summary>
        /// Called when the bot receives a chat message. This can be from a player or bot!
        /// You can use <see cref="Managers.LethalBotManager.IsPlayerLethalBot(PlayerControllerB)"/> to check who is a bot or not!
        /// </summary>
        /// <remarks>
        /// WARNING: All messages are forced into lower case!<br/>
        /// NOTE: This is not called for messages sent by the bot itself!
        /// </remarks>
        /// <param name="message">The message we received</param>
        /// <param name="playerWhoSentMessage">The player who sent the message!</param>
        /// <param name="isVoice">Was the message spoken or was typed out in the chat?</param>
        public virtual void OnPlayerChatMessageReceived(string message, PlayerControllerB playerWhoSentMessage, bool isVoice) 
        {
            if (message.Contains("jester"))
            {
                if (ai.isOutside)
                {
                    return;
                }
                EnemyAI? enemyAI = FindNearbyJester();
                if (enemyAI == null)
                {
                    return;
                }
                ai.State = new PanikState(this, enemyAI);
            }
            // One of us was asked to be the mission controller!
            // NOTE: playerWhoSentMessage should never be null here, but other modders could call this function directly with a null value!
            else if (message.Contains("man the ship"))
            {
                if (IsBotBeingAddressed(playerWhoSentMessage, out var lethalBotController))
                {
                    // Yay, we found a vaild bot, make it the mission controller!
                    ai.SendChatMessage("Alright, I'll head to the terminal and watch over the crew!");
                    LethalBotManager.Instance.MissionControlPlayer = lethalBotController;
                    ai.State = new MissionControlState(this); // Its fine to set the state here directly, if we are not on the ship, the state will handle moving to the ship!
                }
                return;
            }
            // One of us was asked to transfer loot!
            else if (message.Contains("transfer loot"))
            {
                if (IsBotBeingAddressed(playerWhoSentMessage, out var lethalBotController))
                {
                    // Yay, we found a vaild bot, make it transfer loot!
                    ai.SendChatMessage("I'll start transferring loot to the ship right away!");
                    if (!LethalBotManager.Instance.LootTransferPlayers.Contains(lethalBotController))
                    {
                        LethalBotManager.Instance.AddPlayerToLootTransferListAndSync(lethalBotController);
                    }
                    ai.State = new TransferLootState(this);
                }
                return;
            }
        }

        /// <summary>
        /// Determines whether the specified player is currently addressing this bot, based on the player's line of
        /// sight and ownership.
        /// </summary>
        /// <remarks>
        /// FIXME: This is REALLY bad with many bots since they all call this function. We need a better way to do this!<br/>
        /// Function taken from PULL Request by <see href="https://github.com/iSeeEthan"/>
        /// </remarks>
        /// <param name="player">The player to check for bot interaction. Cannot be null.</param>
        /// <returns>true if the player is addressing this bot and the bot is eligible to respond; otherwise, false.</returns>
        protected bool IsBotBeingAddressed(PlayerControllerB player, out PlayerControllerB? lethalBotController)
        {
            // Make sure the player is valid
            lethalBotController = null;
            if (player == null)
            {
                return false;
            }

            // FIXME: There has to be a better way to do this!
            // Get the a trace of where the player who sent the message is looking at!
            // FIXMEUPDATE: Ok, using RaycastNonAlloc should help a bit with performance here, but its still not great!
            RaycastHit[] raycastHits = new RaycastHit[3];
            Ray interactRay = new Ray(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward);
            int raycastResults = Physics.RaycastNonAlloc(interactRay, raycastHits, Const.MAX_CHAT_RANGE, StartOfRound.Instance.playersMask);
            for (int i = 0; i < raycastResults; i++)
            {
                // Check if we hit a player!
                RaycastHit hit = raycastHits[i];
                if (hit.collider == null
                    || hit.collider.tag != "Player")
                {
                    continue;
                }

                // Make sure its an actual player and not an object with the player tag!
                lethalBotController = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (lethalBotController == null)
                {
                    continue;
                }

                // Okay, we found a player, is it a bot and are they following us?
                // We can only address bots that we have ownership of!
                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(lethalBotController);
                if (lethalBot == null
                    || lethalBot != ai // Make sure its us!
                    || lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// A bool the represents if we should crouch or not
        /// </summary>
        /// <returns>true: if we should crouch. false: if we should stand. null: if no preference</returns>
        public virtual bool? ShouldBotCrouch()
        {
            // Try not to crouch if we are under water!
            // We could down if we do!
            // FIXME: We should probably let the bot crouch if they are under water,
            // but we should make them stand up if they are close to drowning!
            if (npcController.Npc.isUnderwater)
            {
                return false;
            }
            // By default, if there is an eyeless dog nearby, we should crouch!
            else if (ai.CheckProximityForEyelessDogs())
            {
                return true;
            }
            return null;
        }

        /// <remarks>
        /// Helper function that only avoids a single entrance!<br/>
        /// <inheritdoc cref="FindClosestEntrance(Vector3?, List{EntranceTeleport}?)"></inheritdoc>
        /// </remarks>
        /// <inheritdoc cref="FindClosestEntrance(Vector3?, List{EntranceTeleport}?)"></inheritdoc>
        protected virtual EntranceTeleport? FindClosestEntrance(EntranceTeleport? entranceToAvoid, Vector3? shipPos = null)
        {
            List<EntranceTeleport>? entrancesToAvoid;
            if (entranceToAvoid != null)
            {
                entrancesToAvoid = new List<EntranceTeleport>() { entranceToAvoid };
            }
            else
            {
                entrancesToAvoid = null;
            }
            return FindClosestEntrance(shipPos, entrancesToAvoid);
        }

        /// <summary>
        /// Find an entrance we can path to so we can enter or exit the main building!
        /// </summary>
        /// <remarks>
        /// We check if the bot can path to it since if the bot can't we could go into an infinite loop!
        /// We also have to check if the exit position lets the bot reach the ship. Offence is a good example where the bots can't path down the fire exit!
        /// </remarks>
        /// <returns>The closest entrance or else null</returns>
        protected virtual EntranceTeleport? FindClosestEntrance(Vector3? shipPos = null, List<EntranceTeleport>? entrancesToAvoid = null)
        {
            bool shouldOnlyUseFrontEntrance = ShouldOnlyUseFrontEntrance();
            bool isClosestEntranceFront = false;
            bool ourWeOutside = ai.isOutside;
            EntranceTeleport? closestEntrance = null;
            float closestEntranceDist = float.MaxValue;
            shipPos ??= RoundManager.Instance.GetNavMeshPosition(StartOfRound.Instance.middleOfShipNode.position);
            foreach (var entrance in LethalBotAI.EntrancesTeleportArray)
            {
                // If we are avoiding specific entrances, we should skip it!
                if (entrancesToAvoid != null && entrancesToAvoid.Contains(entrance))
                {
                    continue;
                }

                if (ourWeOutside && entrance.isEntranceToBuilding)
                {
                    // If we are outside, we should only use the front entrance if needed!
                    bool isCurrentEntranceFront = IsFrontEntrance(entrance);
                    if (shouldOnlyUseFrontEntrance
                    && isClosestEntranceFront
                    && !isCurrentEntranceFront)
                    {
                        break;
                    }

                    // NOTE: We don't need to check if the entrance can reach the ship as we will check that on the return trip
                    // If we are outside, we should consider if we should only use the front entrance or not!
                    //float entranceDistSqr = (entrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (entrance.FindExitPoint()
                        && !IsEntranceCoveredInQuickSand(entrance)
                        && CanPathToEntrance(entrance, true))
                    {
                        // If we are not using the front entrance,
                        // we can use any entrance that is not the front entrance
                        if ((shouldOnlyUseFrontEntrance
                                && !isClosestEntranceFront
                                && isCurrentEntranceFront)
                            || ai.pathDistance < closestEntranceDist)
                        {
                            closestEntrance = entrance;
                            closestEntranceDist = ai.pathDistance;
                            isClosestEntranceFront = isCurrentEntranceFront;
                        }
                    }
                }
                else if (!ourWeOutside && !entrance.isEntranceToBuilding)
                {
                    // NOTE: We use exit point here or the pathfind would always fail since the entrance we are using is inside the facility!
                    // If we are inside, we don't care about the front entrance, we just want to find the closest entrance that we can path to!
                    //float entranceDistSqr = (entrance.entrancePoint.position - npcController.Npc.transform.position).sqrMagnitude;
                    if (entrance.FindExitPoint()
                        && !IsEntranceCoveredInQuickSand(entrance)
                        && LethalBotAI.IsValidPathToTarget(RoundManager.Instance.GetNavMeshPosition(entrance.exitPoint.position), shipPos.Value, ai.agent.areaMask, ref ai.path1, false, out _)
                        && CanPathToEntrance(entrance, true) 
                        && ai.pathDistance < closestEntranceDist)
                    {
                        closestEntrance = entrance;
                        closestEntranceDist = ai.pathDistance;
                    }
                }
            }
            return closestEntrance;
        }

        /// <summary>
        /// Checks if the given entrance is a front entrance!
        /// </summary>
        /// <param name="entrance"></param>
        /// <returns>true: <paramref name="entrance"/> is the front entrance. false: <paramref name="entrance"/> is not the front entrance</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFrontEntrance([NotNullWhen(true)]EntranceTeleport? entrance)
        {
            return entrance != null && entrance.entranceId == 0;
        }

        /// <summary>
        /// Checks if the entrance is covered in quicksand
        /// </summary>
        /// <param name="entrance"></param>
        /// <returns></returns>
        protected bool IsEntranceCoveredInQuickSand(EntranceTeleport? entrance)
        {
            // Check to make sure that the quicksand array is not null or empty
            if (LethalBotAI.QuicksandArray == null || LethalBotAI.QuicksandArray.Length == 0)
            {
                return false;
            }

            // Check if the entrance is covered in quicksand
            if (entrance != null)
            {
                return IsPositionCoveredInQuickSand(entrance.isEntranceToBuilding ? entrance.entrancePoint.position : entrance.exitPoint.position);
            }
            return false;
        }

        /// <summary>
        /// Checks if the given position is covered in quicksand
        /// </summary>
        /// <param name="targetPos"></param>
        /// <returns></returns>
        protected bool IsPositionCoveredInQuickSand(Vector3 targetPos)
        {
            // Check to make sure that the quicksand array is not null or empty
            if (LethalBotAI.QuicksandArray == null || LethalBotAI.QuicksandArray.Length == 0)
            {
                return false;
            }

            // Check if the entrance is covered in quicksand
            RoundManager instanceRM = RoundManager.Instance;
            float headOffset = npcController.Npc.gameplayCamera.transform.position.y - npcController.Npc.transform.position.y;
            if (targetPos != null)
            {
                Vector3 entrancePos = instanceRM.GetNavMeshPosition(targetPos, instanceRM.navHit, 2.7f, ai.agent.areaMask);
                float quicksandBuffer = 2f;
                Plugin.LogDebug($"Testing quicksand safety for exit {targetPos}");
                foreach (var quicksand in LethalBotAI.QuicksandArray)
                {
                    if (!quicksand.isActiveAndEnabled)
                        continue;

                    Collider? collider = quicksand.gameObject.GetComponent<Collider>();
                    if (collider == null)
                        continue;

                    if (!quicksand.isWater)
                    {
                        Plugin.LogDebug("This is quicksand!");

                        // Check if the closest point is within or on the collider
                        Vector3 testPoint = Physics.ClosestPoint(entrancePos, collider, collider.transform.position, collider.transform.rotation);
                        if ((testPoint - entrancePos).sqrMagnitude < quicksandBuffer * quicksandBuffer)
                        {
                            Plugin.LogDebug("Segment intersects solid quicksand!");
                            return true;
                        }
                        /*float dangerRange = 2f;
                        Collider[] hitColliders = Physics.OverlapSphere(closestPoint, dangerRange);
                        foreach (var hitCollider in hitColliders)
                        {
                            if (hitCollider == collider)
                            {
                                Plugin.LogDebug("Segment intersects solid quicksand!");
                                return true;
                            }
                        }*/
                    }
                    else
                    {
                        Plugin.LogDebug("This is water!");

                        // For some reason this works really well like this unlike the code above
                        Vector3 simulatedHead = entrancePos + Vector3.up * headOffset;
                        if (collider.bounds.Contains(simulatedHead))
                        {
                            // We might be able to walk through the water, lets check the closest node to the entrance
                            // FIXME: This isn't the best way to do this, but it works for now
                            // We should probably get the closest node that is not in the water and check that instead
                            Transform closestNode = instanceRM.GetClosestNode(entrancePos, true);
                            Vector3 closestNodePos = instanceRM.GetNavMeshPosition(closestNode.position, instanceRM.navHit, 2.7f, ai.agent.areaMask);
                            float moveSpeed = npcController.Npc.movementSpeed > 0f ? npcController.Npc.movementSpeed : 4.5f;
                            moveSpeed /= npcController.Npc.carryWeight;
                            float modifiedMoveSpeed = moveSpeed / (2f * (1f * quicksand.movementHinderance));
                            float travelTime = Vector3.Distance(closestNodePos, entrancePos) / modifiedMoveSpeed;
                            float downingDelta = travelTime / Const.LETHAL_BOT_DROWN_TIME; // Match game logic
                            float predictedDrownTimer = 1f - downingDelta;

                            simulatedHead = closestNodePos + Vector3.up * headOffset;
                            if (predictedDrownTimer <= 0f || collider.bounds.Contains(simulatedHead))
                            {
                                Plugin.LogDebug("Simulated head intersects water!");
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Helper function that checks if we can path to the main entrance!
        /// </summary>
        /// <param name="targetEntrance">The entrance to check</param>
        /// <param name="calculatePathDistance">Should we calculate the length of the path?</param>
        /// <returns>true: the bot can path to the entrance. false: the bot can't find a path to the entrance.</returns>
        protected bool CanPathToEntrance(EntranceTeleport targetEntrance, bool calculatePathDistance = false)
        {
            // Check if we can path to the entrance!
            if (!ai.IsValidPathToTarget(targetEntrance.entrancePoint.position, calculatePathDistance))
            {
                // Check if this is the front entrance if we need to use an elevator
                if (IsFrontEntrance(targetEntrance) && !targetEntrance.isEntranceToBuilding && LethalBotAI.ElevatorScript != null)
                {
                    // Check if we can path to the bottom of the elevator
                    if (ai.IsValidPathToTarget(LethalBotAI.ElevatorScript.elevatorBottomPoint.position, calculatePathDistance))
                    {
                        return true;
                    }

                    // Check if we are inside the elevator!
                    return ai.IsInsideElevator;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if the entrance is safe to use.<br/>
        /// </summary>
        /// <param name="entrance">The target entance to test</param>
        /// <param name="useEntrancePoint">Should we use the entrance point rather than the exit?</param>
        /// <returns></returns>
        public bool IsEntranceSafe(EntranceTeleport? entrance, bool useEntrancePoint = false)
        {
            if (entrance == null 
                || !entrance.FindExitPoint())
            {
                return false;
            }

            // Check if we have a cached value for this entrance
            if (entranceSafetyCache.TryGetValue(entrance, out var cachedSafety))
            {
                // If the last safety check was less than 1 second ago, we can use the cached value
                if ((Time.timeSinceLevelLoad - cachedSafety.lastSafetyCheck) < 1f)
                {
                    return cachedSafety.isSafe;
                }
            }

            // Active jester is an automatic fail!
            if (FindNearbyJester() != null)
            {
                // If there is a jester nearby, we should not use this entrance!
                entranceSafetyCache[entrance] = (false, Time.timeSinceLevelLoad);
                return false;
            }

            // If we don't have a cached value, we need to check if the entrance is safe
            Vector3 entrancePoint = useEntrancePoint ? entrance.entrancePoint.position : entrance.exitPoint.position;
            foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
            {
                if (!enemy.isEnemyDead && enemy is not LethalBotAI && (enemy.transform.position - entrancePoint).sqrMagnitude < 7.7f * 7.7f) // 7.7f is the same distance used by the base game to show the enemy activity nearby message!
                {
                    // We found an enemy near the exit point, so we should not use this entrance!
                    entranceSafetyCache[entrance] = (false, Time.timeSinceLevelLoad);
                    return false;
                }
            }

            // If we didn't find any enemies near the exit point, we can use this entrance!
            entranceSafetyCache[entrance] = (true, Time.timeSinceLevelLoad);
            return true;
        }

        /// <summary>
        /// Determines whether the front entrance should be exclusively used.
        /// </summary>
        /// <remarks>
        /// Bots will only use the front entrance when we start the round, but are free to use others as the day progresses!
        /// </remarks>
        /// <returns><see langword="true"/> if only the front entrance should be used; otherwise, <see langword="false"/>.</returns>
        protected virtual bool ShouldOnlyUseFrontEntrance()
        {
            // Took this from the TimeOfDay class file,
            // if we just started the day we should use the front entrance!
            TimeOfDay timeOfDay = TimeOfDay.Instance;
            if (timeOfDay != null)
            {
                DayMode dayMode = timeOfDay.GetDayPhase(timeOfDay.currentDayTime / timeOfDay.totalTime);
                if (dayMode == DayMode.Dawn)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns if the bot should return to the ship!
        /// </summary>
        /// <remarks>
        /// This function is was made virtual since this may be different for each state!
        /// </remarks>
        /// <returns>true if we should return and false if we don't need to</returns>
        public virtual bool ShouldReturnToShip()
        {
            // Facility Meltdown Support!
            if (Plugin.IsModFacilityMeltdownLoaded)
            {
                // The Meltdown has been started, RUN!
                if (HasMeltdownStarted())
                {
                    return true;
                }
            }

            // Took this from the TimeOfDay class file,
            // if its getting late out, we should return to the ship!
            TimeOfDay timeOfDay = TimeOfDay.Instance;
            if (timeOfDay != null)
            {
                // TODO: Change this to be better with longer day mods.
                // I mean it works partially, but could be better!
                //DayMode dayMode = timeOfDay.GetDayPhase(timeOfDay.currentDayTime / timeOfDay.totalTime);
                //(dayMode == DayMode.Sundown || dayMode == DayMode.Midnight)
                if (timeOfDay.normalizedTimeOfDay > Plugin.Config.ReturnToShipTime
                    || timeOfDay.votesForShipToLeaveEarly >= LethalBotManager.Instance.AllRealPlayersCount 
                    || timeOfDay.shipLeavingAlertCalled)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Helper function to check if the facility meltdown has started
        /// </summary>
        /// <remarks>
        /// This only exists since the game will error out if Facility meltdown isn't installed and
        /// its class related stuff exists in a function!
        /// </remarks>
        /// <returns>true: the meltdown has started. false: the meltdown hasn't started yet</returns>
        protected static bool HasMeltdownStarted()
        {
            // Facility Meltdown Support!
            if (Plugin.IsModFacilityMeltdownLoaded)
            {
                // The Meltdown has been started, RUN!
                if (FacilityMeltdown.API.MeltdownAPI.MeltdownStarted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Called every ai interval <see cref="EnemyAI.AIIntervalTime"/> in <see cref="LethalBotAI.DoAIInterval"/> by default. The bot will use the held item if it can!
        /// </summary>
        /// <remarks>
        /// There is only AI for four items at the time; The walkie-talkie, the shotgun, TZPInhalant, and the Maneater baby!<br/>
        /// The walkie-talkie will be used if the bot is talking!<br/>
        /// The shotgun will be used if the safety is not on or the bot has spare ammo and the shotgun needs to be reloaded!<br/>
        /// The TZPInhalant is managed by the <see cref="UseTZPInhalantState"/> state!<br/>
        /// The Maneater baby will be rocked if the baby is crying!<br/>
        /// NOTE: The bot only uses <see cref="LethalBotAI.HeldItem"/>, so items in the inventory will not be used!<br/>
        /// This will be changed in the future!
        /// </remarks>
        public virtual void UseHeldItem()
        {
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem == null 
                || !ai.CanUseHeldItem())
            {
                return;
            }

            // If we are holding a walkie-talkie, we should use it!
            // NOTE: Due to the way the walkie-talkie is coded, this seems to work really well!
            if (heldItem is WalkieTalkie walkieTalkie)
            {
                if (ai.LethalBotIdentity.Voice.IsTalking())
                {
                    if (!walkieTalkie.isBeingUsed && !walkieTalkie.insertedBattery.empty)
                    {
                        walkieTalkie.ItemInteractLeftRightOnClient(false);
                    }
                    else if (walkieTalkie.isBeingUsed && !walkieTalkie.isHoldingButton)
                    {
                        walkieTalkie.UseItemOnClient(true);
                    }
                }
                else if (walkieTalkie.isBeingUsed && walkieTalkie.isHoldingButton)
                {
                    walkieTalkie.UseItemOnClient(false);
                }
            }
            else if (heldItem is ShotgunItem shotgun)
            {
                // Put the saftey back on
                if (!shotgun.safetyOn)
                {
                    shotgun.ItemInteractLeftRightOnClient(false);
                }
                // Reload as needed!
                else if (shotgun.shellsLoaded < 2 && ai.HasAmmoForWeapon(shotgun, true))
                {
                    shotgun.ItemInteractLeftRightOnClient(true);
                }
            }
            else if (heldItem is TetraChemicalItem tzpItem)
            {
                // Stop using if we are using it!
                if (tzpItem.isBeingUsed)
                { 
                    tzpItem.UseItemOnClient(false);
                }
            }
            else if (heldItem is CaveDwellerPhysicsProp caveDwellerGrabbableObject)
            {
                CaveDwellerAI? caveDwellerAI = caveDwellerGrabbableObject.caveDwellerScript;
                if (caveDwellerAI != null)
                {
                    // Rock the maneater if its crying and we are holding it!
                    if (caveDwellerAI.babyCrying)
                    {
                        // Don't rock the baby if we are already rocking it!
                        if (caveDwellerAI.rockingBaby <= 0)
                        {
                            caveDwellerGrabbableObject.UseItemOnClient(true);
                        }
                    }
                    // If the baby is not crying, we should stop rocking it!
                    else
                    {
                        // Stop rocking the maneater if its not crying anymore!
                        if (caveDwellerAI.rockingBaby > 0)
                        {
                            caveDwellerGrabbableObject.UseItemOnClient(false);
                        }
                        else
                        {
                            // Check if the baby doesn't like us, if it doesn't, we should drop it!
                            // We also check if the config option is enabled or not!
                            BabyPlayerMemory playerMemory = CaveDwellerAIPatch.GetBabyMemoryOfPlayer_ReversePatch(caveDwellerAI, npcController.Npc);
                            if ((playerMemory != null && playerMemory.likeMeter < 0.1f) 
                                || !Plugin.Config.AdvancedManeaterBabyAI.Value)
                            {
                                // The baby doesn't like us, so we should drop it!
                                // or else it will be mad at us after a while and would try to kill us!
                                ai.DropItem();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Used to stop all coroutines in a state!<br/>
        /// Called when a bot switches states!
        /// </summary>
        public virtual void StopAllCoroutines()
        {
            if (searchForPlayers.inProgress)
            {
                ai.StopSearch(searchForPlayers, true);
            }
            if (searchForScrap.inProgress)
            {
                ai.StopSearch(searchForScrap, false);
            }
            StopSafePathCoroutine();
            StopLookingAroundCoroutine();
        }

        /// <summary>
        /// Finds the closest AINode to given position that is safe.
        /// Changes <see cref="safePathPos"/> to the safe position found
        /// </summary>
        /// <remarks>
        /// This will also call <see cref="LethalBotAI.SetDestinationToPositionLethalBotAI(Vector3)"/>
        /// upon finding a safe position!
        /// </remarks>
        /// <returns></returns>
        private IEnumerator findSafePathToTarget()
        {
            yield return null;

            while (ai.State != null)
            {
                // Grab the desired position we want to make a safe path to!
                Vector3? targetDestination = GetDesiredSafePathPosition();
                if (!targetDestination.HasValue)
                {
                    StopSafePathCoroutine();
                    yield break; // End the loop, we have no set destination!
                }

                // If the direct path safe?
                CancelAsyncPathfindToken(); // Clear the old token
                pathfindCancellationToken = new CancellationTokenSource();
                Task<(bool isDangerous, float pathDistance)> pathfindTask = ai.TryStartPathDangerousAsync(targetDestination.Value, token: pathfindCancellationToken.Token);
                //yield return new WaitUntil(() => pathfindTask.IsCompleted);
                while (!pathfindTask.IsCompleted)
                {
                    yield return null;
                }

                // Check if an error occured!
                if (pathfindTask.IsFaulted)
                {
                    Plugin.LogError($"Async pathfinder had an exception: {pathfindTask.Exception}");
                    StopSafePathCoroutine();
                    yield break; // Kill the coroutine! Should we do this? Could I just let the rest of the coroutine run?
                }
                else if (pathfindTask.IsCanceled)
                {
                    Plugin.LogWarning($"Async pathfinder was canceled early!");
                }
                else if (!pathfindTask.Result.isDangerous
                    || ShouldIgnoreInitialDangerCheck())
                {
                    safePathPos = targetDestination.Value;
                    ai.SetDestinationToPositionLethalBotAI(safePathPos);
                    yield return new WaitForSeconds(ai.AIIntervalTime);
                    continue;
                }

                // FIXME: This relies on Elucian Distance rather than travel distance, this should be fixed!
                // TODO: We should use the same system the retreat system uses, should fix those cases where the bot believes there is no safe path when there actually is one!
                var nodes = ai.allAINodes.OrderBy(node => (node.transform.position - targetDestination.Value).sqrMagnitude)
                                         .ToArray();
                yield return null;

                // no need for a loop I guess
                bool foundSafePath = false;
                for (var i = 0; i < nodes.Length; i++)
                {
                    Transform nodeTransform = nodes[i].transform;
                    Vector3 nodePos = RoundManager.Instance.GetNavMeshPosition(nodeTransform.position, default, 2.7f);

                    // Give the main thread a chance to do something else
                    // We still need the yield return null here!
                    // As if the pathfind fails, we need to wait a frame!
                    // Due to the way the async pathfinder works, we may not have to yield 
                    // here as often, but it should still be done to prevent freezing
                    if (i % 15 == 0)
                    {
                        yield return null;
                    }

                    // Can we path to the node and is it safe?
                    CancelAsyncPathfindToken(); // Clear the old token
                    pathfindCancellationToken = new CancellationTokenSource();
                    pathfindTask = ai.TryStartPathDangerousAsync(nodePos, token: pathfindCancellationToken.Token);
                    //yield return new WaitUntil(() => pathfindTask.IsCompleted);
                    while (!pathfindTask.IsCompleted)
                    {
                        yield return null;
                    }

                    // Check if an error occured!
                    if (pathfindTask.IsFaulted)
                    {
                        Plugin.LogError($"Async pathfinder had an exception: {pathfindTask.Exception}");
                        StopSafePathCoroutine();
                        yield break; // Kill the coroutine!
                    }
                    else if (pathfindTask.IsCanceled)
                    {
                        Plugin.LogWarning($"Async pathfinder was canceled early, skipping node!");
                        continue;
                    }
                    // Did the task say the path was dangerous?
                    else if (pathfindTask.Result.isDangerous)
                    {
                        continue;
                    }

                    safePathPos = nodePos;
                    foundSafePath = true;
                    break;
                }

                // If we found a safe path, we should wait a bit before checking again!
                // We don't need to do the more lax checks!
                if (foundSafePath)
                {
                    ai.SetDestinationToPositionLethalBotAI(safePathPos);
                    yield return new WaitForSeconds(ai.AIIntervalTime);
                    continue;
                }

                // We failed while using eye position, lets make the checks a bit more lax!
                if (npcController.Npc.isUnderwater || ai.AreWeExposed())
                {
                    Plugin.LogDebug($"Bot {npcController.Npc.playerUsername} failed to find a node out of sight and is exposed! They will now attempt to fallback into cover if possible!");

                    // We are exposed, we should try to find a safe path again!
                    // FIXME: This relies on Elucian Distance rather than travel distance, this should be fixed!
                    // FIXME: This is also a lot slower, since we iterated through the list twice.....
                    // We should pick the node closest to us at this point!
                    nodes = ai.allAINodes.OrderBy(node => (node.transform.position - npcController.Npc.transform.position).sqrMagnitude)
                                             .ToArray();
                    yield return null;

                    // We are still exposed, find cover now!
                    // NEEDTOVALIDATE: Should this be the local vector instead of just the height?
                    bool ourWeOutside = ai.isOutside;
                    float headOffset = npcController.Npc.gameplayCamera.transform.position.y - npcController.Npc.transform.position.y;
                    for (var i = 0; i < nodes.Length; i++)
                    {
                        Transform nodeTransform = nodes[i].transform;
                        Vector3 nodePos = RoundManager.Instance.GetNavMeshPosition(nodeTransform.position, default, 2.7f);

                        // Give the main thread a chance to do something else
                        if (i % 15 == 0)
                        {
                            yield return null;
                        }

                        // Can we path to the node and is it safe?
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

                        // Node is either covered in quicksand or is underwater! Pick another one!
                        if (IsPositionCoveredInQuickSand(nodePos))
                        {
                            continue;
                        }

                        Plugin.LogDebug($"Bot {npcController.Npc.playerUsername} found fallback spot at {nodePos}!");
                        safePathPos = nodePos;
                        foundSafePath = true;
                        break;
                    }
                }

                // Hold if we found no safe path at the moment!
                bool forceMove = false;
                if (!foundSafePath)
                {
                    // If we are under water, move to the closest node instead!
                    if (!npcController.Npc.isUnderwater)
                    { 
                        // If we are not on the navmesh, we will end up stuck.
                        // Pick the closest node since this should make the stuck code realize we
                        // may be stuck and teleport us back onto the navmesh!
                        if (!ai.agent.isOnNavMesh)
                        {
                            // Alright, some really extreme stuff here, some nodes are partially off the mesh or something.
                            Transform? closestNode = GetClosestNode(npcController.Npc.transform.position);
                            if (closestNode != null && (closestNode.position - npcController.Npc.transform.position).sqrMagnitude < Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION * Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                            {
                                closestNode = GetClosestNode(npcController.Npc.transform.position, closestNode.gameObject);
                            }
                            safePathPos = RoundManager.Instance.GetNavMeshPosition(closestNode?.position ?? npcController.Npc.transform.position, default, 2.7f);
                            forceMove = true;
                            Plugin.LogWarning($"Bot {npcController.Npc.playerUsername} off NavMesh, setting safePathPos to closest node at {safePathPos}");
                        }
                        // FIXME: WHY!!!!!!!!!! For some reason, doing this can cause us to get stuck off of the mesh. 
                        // It depends on the map's terrain, but its better for the bot to walk into danger than to get stuck in
                        // some on/off NavMesh limbo as we can find a better spot later.......
                        //else
                        //{
                        //    //safePathPos = RoundManager.Instance.GetNavMeshPosition(npcController.Npc.transform.position, default, 2.7f); 
                        //}
                    }
                    else
                    {
                        safePathPos = RoundManager.Instance.GetNavMeshPosition(GetClosestNode(npcController.Npc.transform.position)?.position ?? npcController.Npc.transform.position, default, 2.7f);
                        forceMove = true;
                    }
                }

                // Successfull or not we should wait a bit before checking again!
                ai.SetDestinationToPositionLethalBotAI(safePathPos);
                if (forceMove)
                {
                    ai.OrderMoveToDestination();
                }
                yield return new WaitForSeconds(ai.AIIntervalTime);
            }

            StopSafePathCoroutine();
        }

        /// <summary>
        /// Used by <see cref="findSafePathToTarget"/> to generate a safe path to the destination
        /// given by this function!
        /// </summary>
        /// <returns>The position safe path wants to find a safe path to or null</returns>
        protected virtual Vector3? GetDesiredSafePathPosition()
        {
            return null;
        }

        /// <summary>
        /// Used by <see cref="findSafePathToTarget"/> to check if the bot should path to
        /// <see cref="GetDesiredSafePathPosition"/> even if the path is considered dangerous!
        /// </summary>
        /// <returns></returns>
        protected virtual bool ShouldIgnoreInitialDangerCheck()
        {
            return false;
        }

        /// <summary>
        /// Starts the coroutine that finds a safe path to <see cref="GetDesiredSafePathPosition"/>.<br/>
        /// </summary>
        protected void StartSafePathCoroutine()
        {
            if (safePathCoroutine == null)
            {
                safePathCoroutine = ai.StartCoroutine(findSafePathToTarget());
            }
        }

        /// <summary>
        /// Stops the coroutine and cancels the async pathfind token if it exists.<br/>
        /// </summary>
        protected void StopSafePathCoroutine()
        {
            CancelAsyncPathfindToken();
            if (safePathCoroutine != null)
            {
                ai.StopCoroutine(safePathCoroutine);
                safePathCoroutine = null;
            }
        }

        /// <summary>
        /// Public helper that allows checking if the safe path system is running.
        /// </summary>
        /// <returns></returns>
        public bool IsSafePathRunning()
        {
            return safePathCoroutine != null;
        }

        /// <summary>
        /// Cancels the async pathfind token if it exists.<br/>
        /// </summary>
        protected void CancelAsyncPathfindToken()
        {
            try
            {
                pathfindCancellationToken?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed - that's ok!
            }
            finally
            {
                pathfindCancellationToken?.Dispose();
                pathfindCancellationToken = null;
            }
        }

        /// <summary>
        /// Coroutine for making bot turn his body to look around him
        /// </summary>
        /// <returns></returns>
        protected virtual IEnumerator LookingAround()
        {
            yield return null;
            while (ai.State != null
                    && ai.State == this)
            {
                float freezeTimeRandom = Random.Range(Const.MIN_TIME_SEARCH_LOOKING_AROUND, Const.MAX_TIME_SEARCH_LOOKING_AROUND);
                float angleRandom = Random.Range(0f, 360f);

                // Convert angle to world position for looking
                // Convert to local space (relative to the bot's forward direction)
                Vector3 lookDirection = Quaternion.Euler(0, angleRandom, 0) * Vector3.forward;
                float minLookDistance = 2f; // TODO: Move these into the Const class!
                float maxLookDistance = 8f;
                float lookDistance = Random.Range(minLookDistance, maxLookDistance); // Hardcoded for now
                Vector3 lookAtPoint = npcController.Npc.gameplayCamera.transform.position + lookDirection * lookDistance;

                // Ensure bot doesn’t look at unreachable areas (optional raycast check)
                if (Physics.Raycast(npcController.Npc.thisController.transform.position, lookDirection, out RaycastHit hit, lookDistance, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    lookAtPoint = hit.point; // Adjust to the first obstacle it hits
                }

                // Use OrderToLookAtPosition as SetTurnBodyTowardsDirection can be overriden!
                npcController.OrderToLookAtPosition(lookAtPoint, EnumLookAtPriority.MEDIUM_PRIORITY, 1.0f);
                yield return new WaitForSeconds(freezeTimeRandom);
            }

            lookingAroundCoroutine = null;
        }

        protected void StartLookingAroundCoroutine()
        {
            if (this.lookingAroundCoroutine == null)
            {
                this.lookingAroundCoroutine = ai.StartCoroutine(this.LookingAround());
            }
        }

        protected void StopLookingAroundCoroutine()
        {
            if (this.lookingAroundCoroutine != null)
            {
                ai.StopCoroutine(this.lookingAroundCoroutine);
                this.lookingAroundCoroutine = null;
            }
        }

        /// <summary>
        /// Changes back to the previous state
        /// </summary>
        /// <remarks>
        /// If <see cref="previousAIState"/> is null, the bot will return to the ship!
        /// </remarks>
        protected virtual void ChangeBackToPreviousState()
        {
            if (previousAIState != null)
            {
                // Lets check if we want to change certain information on the previous state
                if (previousStateUpdate.TargetItem != null)
                {
                    previousAIState.TargetItem = previousStateUpdate.TargetItem;
                }
                if (previousStateUpdate.TargetLastKnownPosition != null)
                {
                    previousAIState.targetLastKnownPosition = previousStateUpdate.TargetLastKnownPosition;
                }

                ai.State = previousAIState;
            }
            // HOW DID THIS HAPPEN?!!?
            else
            {
                Plugin.LogError($"Bot {npcController.Npc.playerClientId} ({npcController.Npc.playerUsername}) tried to change back to previous state, but it was null! This should never happen!");
                Plugin.LogWarning($"Bot {npcController.Npc.playerClientId} ({npcController.Npc.playerUsername}) will return to the ship instead!");
                ai.State = new ReturnToShipState(this);
            }
        }

        /// <summary>
        /// A struct with one purpose, to allow me to set variables on previous classes.
        /// </summary>
        protected struct AIStateInfo
        {
            public Vector3? TargetLastKnownPosition;
            public GrabbableObject? TargetItem;
        }

        /// <summary>
        /// Function that checks if the bot should automatically get off the terminal!
        /// </summary>
        /// <returns><see langword="true"/> the bot will not automatically leave the terminal. <see langword="false"/> the bot will automatically get off the terminal</returns>
        public virtual bool CheckAllowsTerminalUse() => false;

        /// <summary>
        /// Get the <see cref="Enums.EnumAIStates"><c>Enums.EnumAIStates</c></see> of current State
        /// </summary>
        /// <returns></returns>
        public virtual EnumAIStates GetAIState() { return CurrentState; }

        public virtual string GetBillboardStateIndicator() { return string.Empty; }

        /// <summary>
        /// Helper function to find an active jester.
        /// </summary>
        /// <returns>The found jester or null</returns>
        protected EnemyAI? FindNearbyJester()
        {
            RoundManager instanceRM = RoundManager.Instance;
            foreach (EnemyAI enemy in instanceRM.SpawnedEnemies)
            {
                if (enemy == null || enemy.isEnemyDead)
                {
                    continue;
                }

                if (enemy.enemyType.enemyName != "Jester" && enemy is not JesterAI)
                {
                    continue;
                }

                float? fearRange = ai.GetFearRangeForEnemies(enemy);
                if (fearRange.HasValue)
                {
                    return enemy;
                }
            }
            return null;
        }

        /// <summary>
        /// Simple function that checks if the give <paramref name="item"/> is null or not<br/>
        /// This was designed to be overridden by states that want to drop specific items only!
        /// </summary>
        /// <remarks>
        /// This was designed for use in <see cref="LethalBotAI.HasGrabbableObjectInInventory(System.Func{GrabbableObject, bool}, out int)"/> calls.
        /// </remarks>
        /// <param name="item"></param>
        /// <returns></returns>
        protected virtual bool FindObject(GrabbableObject item)
        {
            return !ai.IsGrabbableObjectInLoadout(item) || ai.HasDuplicateLoadoutItems(item, out _); // Found an item, great, we only want to drop it if its not a part of our loadout!
        }

        /// <summary>
        /// Helper function to check if the <paramref name="canidate"/> is better than our <paramref name="currentBest"/>!
        /// </summary>
        /// <remarks>
        /// This was designed for use in <see cref="LethalBotAI.TryFindItemInInventory(System.Func{GrabbableObject, bool}, System.Func{GrabbableObject, GrabbableObject, bool}, out int)"/> calls.
        /// </remarks>
        /// <param name="currentBest">The current best grabbable object selected by the bot.</param>
        /// <param name="canidate">The candidate grabbable object to compare against the current best. Can be null.</param>
        /// <returns>true if the candidate object is considered a better choice than the current best; otherwise, false.</returns>
        protected virtual bool FindBetterObject(GrabbableObject currentBest, GrabbableObject canidate)
        {
            return false; // By default we don't care about better objects!
        }
    }
}
