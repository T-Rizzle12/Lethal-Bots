using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Utils.Helpers;
using LethalInternship.AI;
using PathfindingLib.API.SmartPathfinding;
using System;
using System.Collections;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;

namespace LethalBots.AI.Pathfinding.LinkTypes
{
    /// <summary>
    /// This class implements the <see cref="INavMeshLinkInfo"/> interface for handling traversal of elevator links in the navigation mesh.
    /// </summary>
    [RequireComponent(typeof(NavMeshLink))]
    public class ElevatorNavMeshLinkTraversal : MonoBehaviour, INavMeshLinkInfo
    {
        /// <summary>
        /// The <see cref="MineshaftElevatorController"/> associated with this elevator link.
        /// </summary>
        public MineshaftElevatorController elevatorController = null!;

        [SerializeField]
        private NavMeshLink _navMeshLink = null!;

        /// <inheritdoc cref="INavMeshLinkInfo.navMeshLink"/>
        public NavMeshLink navMeshLink { set => _navMeshLink = value; get => _navMeshLink; }

        public const float ELEVATOR_TIMEOUT = 90f; // 90 seconds timeout for elevator traversal

        // NOTE: We use Start instead of Awake so runtime creation of this component is possible!
        public void Start()
        {
            if (elevatorController == null)
            {
                Plugin.LogError("ElevatorNavMeshLinkTraversal: MineshaftElevatorController is not assigned. Destroying component.");
                UnityEngine.Object.Destroy(this);
                return;
            }
            if (navMeshLink == null)
            {
                Plugin.LogError("ElevatorNavMeshLinkTraversal: NavMeshLink is not assigned. Destroying component.");
                UnityEngine.Object.Destroy(this);
                return;
            }
        }

        public IEnumerator TraverseNavMeshLink(LinkTraversalContext context)
        {
            // Make sure we are valid
            if (!IsTraversalValid(context))
            {
                context.TraversalOutcome = EnumLinkTraversalOutcome.Failure;
                yield break;
            }

            // Mark the NavMeshLink as completed, so other agents can use it.....we will handle the actual movement
            UnityEngine.AI.NavMeshAgent navMeshAgent = context.NavMeshAgent;
            Vector3 oldPos = navMeshAgent.transform.position;
            navMeshAgent.ResetPath(); // Clear the bot's current path and clear its current OffMeshLink
            navMeshAgent.transform.position = oldPos; // CompleteOffMeshLink moves the NavMeshAgent to the end of the link....we undo that here
            navMeshAgent.Warp(oldPos);

            // Alright, now to define our "local" variables for the traversal logic
            LethalBotAI lethalBotAI = context.LethalBotAI;
            PlayerControllerB lethalBotController = context.LethalBotController;
            bool goUp = Vector3.Distance(context.OffMeshLinkData.endPos, elevatorController.elevatorTopPoint.position) < Vector3.Distance(context.OffMeshLinkData.endPos, elevatorController.elevatorBottomPoint.position);
            CountdownTimer timeoutTimer = new CountdownTimer();

            // Log the traversal start
            Plugin.LogDebug($"ElevatorNavMeshLinkTraversal: Starting traversal for bot {lethalBotController.playerUsername} going {(goUp ? "up" : "down")}.");

            Vector3 waitingPoint = goUp ? elevatorController.elevatorBottomPoint.position : elevatorController.elevatorTopPoint.position;
            Vector3 exitPoint = context.OffMeshLinkData.endPos;

            // Remember when we're physically exiting the elevator.
            bool exiting = false;
            timeoutTimer.Start(ELEVATOR_TIMEOUT);
            while (timeoutTimer.HasStarted() && !timeoutTimer.Elapsed())
            {
                // If the traversal is no longer valid, stop the traversal and exit
                if (!IsTraversalValid(context))
                {
                    context.TraversalOutcome = EnumLinkTraversalOutcome.Failure;
                    yield break;
                }

                // If we are going to use the elevator to go up,
                // we must drop the baby maneater before using the elevator
                if (goUp && lethalBotController.currentlyHeldObject is CaveDwellerPhysicsProp)
                {
                    lethalBotController.DiscardHeldObject();
                }

                // Reevaluate the elevator's current state every frame!
                bool finishedMoving = elevatorController.elevatorFinishedMoving;
                bool insideElevator = lethalBotAI.IsInsideElevator;

                // Preserve the original goUp / elevatorMovingDown convention.
                bool atBoardingFloor = finishedMoving && elevatorController.elevatorMovingDown == goUp;
                bool atDestinationFloor = finishedMoving && elevatorController.elevatorMovingDown != goUp;

                // STEP 1: Exit the elevator if we reached our floor.
                if (exiting)
                {
                    // Someone moved the elevator again before we got out.
                    if (insideElevator && !atDestinationFloor)
                    {
                        exiting = false;
                    }
                    else
                    {
                        // Wait until the elevator door is open
                        if (!insideElevator || elevatorController.elevatorDoorOpen)
                        {
                            // Exit the elevator
                            this.MoveBotOnLink(context, exitPoint);

                            // Did we successfully reach the end of the elevator?
                            if (Vector3.Distance(lethalBotController.transform.position, exitPoint) <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION)
                            {
                                // Arrived! Complete the traversal.
                                context.TraversalOutcome = EnumLinkTraversalOutcome.Success;
                                yield break;
                            }

                        }
                        yield return null;
                        continue;
                    }
                }

                // STEP 2: We're inside the elevator.
                if (insideElevator)
                {
                    if (atDestinationFloor)
                    {
                        // Arrived! Start exiting.
                        exiting = true;
                    }
                    else if (atBoardingFloor && CanSendElevatorCommand(context))
                    {
                        // We're at the wrong floor; press the button.
                        LethalBotAI.pressElevatorButtonCooldown.Reset();
                        elevatorController.PressElevatorButton();
                        timeoutTimer.Start(ELEVATOR_TIMEOUT); // Reset the timeout timer after pressing the button
                    }
                    else
                    {
                        // Otherwise, the elevator is moving.
                        // Stay inside and wait!
                        context.LethalBotAI.StopMoving();
                    }
                }
                // STEP 3: Elevator is at our boarding floor.
                else if (atBoardingFloor)
                {
                    // Try boarding the elevator
                    this.MoveBotOnLink(context, elevatorController.elevatorInsidePoint.position);
                }
                // STEP 4: Elevator is unavailable or at the wrong floor.
                else
                {
                    // Stay near the waiting area.
                    this.MoveBotOnLink(context, waitingPoint);

                    // If we reach the waiting area, make the bot call the elevator
                    float distance = Vector3.Distance(lethalBotController.transform.position, waitingPoint);
                    if (finishedMoving 
                        && distance <= Const.DISTANCE_CLOSE_ENOUGH_TO_DESTINATION
                        && !elevatorController.elevatorCalled 
                        && CanSendElevatorCommand(context))
                    {
                        // Call the elevator back to us.
                        LethalBotAI.pressElevatorButtonCooldown.Reset();
                        elevatorController.CallElevator(goUp);
                        timeoutTimer.Start(ELEVATOR_TIMEOUT); // Reset the timeout timer after calling the elevator
                    }
                }

                // Wait a frame
                yield return null;
            }

            // If we exit the loop, it means we timed out.
            Plugin.LogDebug($"ElevatorNavMeshLinkTraversal: Traversal for bot {lethalBotController.playerUsername} timed out.");
            context.TraversalOutcome = EnumLinkTraversalOutcome.Failure;
        }

        private bool IsTraversalValid(LinkTraversalContext context)
        {
            // Make sure we are valid
            if (navMeshLink == null || elevatorController == null)
            {
                Plugin.LogError("ElevatorNavMeshLinkTraversal: NavMeshLink or MineshaftElevatorController is not assigned. Cannot traverse link.");
                return false;
            }

            // Ensure the context's NavMeshLink matches this link
            if (context.NavMeshLink != navMeshLink)
            {
                Plugin.LogError("ElevatorNavMeshLinkTraversal: Invalid NavMeshLink context!");
                return false;
            }

            // Ensure the OffMeshLinkData is valid
            if (!context.OffMeshLinkData.valid)
            {
                Plugin.LogError("ElevatorNavMeshLinkTraversal: Invalid OffMeshLinkData context!");
                return false;
            }

            // If we were teleported for ANY reason, give up
            if (context.WasTeleported)
            {
                Plugin.LogWarning("ElevatorNavMeshLinkTraversal: Bot was teleported! Cannot traverse link.");
                return false;
            }

            return true;
        }

        private bool CanSendElevatorCommand(LinkTraversalContext context)
        {
            LethalBotAI lethalBotAI = context.LethalBotAI;
            if (lethalBotAI.timerElevatorCooldown > Const.TIMER_USE_ELEVATOR
                    && LethalBotAI.pressElevatorButtonCooldown.IsGreaterThan(lethalBotAI.AIIntervalTime + 0.16f))
            {
                return true;
            }
            return false;
        }

        public bool IsSafeToCancelTraversal(LinkTraversalContext context)
        {
            // If the NavMeshLink is not active and enabled, it is safe to cancel traversal.
            if (!navMeshLink.isActiveAndEnabled)
            {
                return true;
            }

            // If the elevator is not moving, it is safe to cancel traversal.
            if (!elevatorController.elevatorFinishedMoving 
                && context.LethalBotAI.IsInsideElevator)
            {
                return false;
            }

            return true;
        }
    }
}
