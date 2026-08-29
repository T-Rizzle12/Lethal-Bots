using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.Utils
{
    /// <summary>
    /// Utility class for NavMesh related functions
    /// </summary>
    public class NavMeshUtil
    {
        /// <summary>
        /// Helper method that determines whether a complete and valid NavMesh path exists between two points.
        /// </summary>
        /// <remarks>
        /// This is an enhanced check that wraps <see cref="NavMesh.CalculatePath(Vector3, Vector3, int, NavMeshPath)"/> with additional validation:
        /// <list type="bullet">
        ///   <item>Ensures the path calculation succeeds</item>
        ///   <item>Confirms the path is not empty</item>
        ///   <item>Verifies that the last path corner is sufficiently close to the destination, ensuring the path is complete</item>
        /// </list>
        /// </remarks>
        /// <param name="startPosition">The starting position of the path</param>
        /// <param name="endPosition">The target position to reach</param>
        /// <param name="areaMask">The NavMesh area mask to use when calculating the path</param>
        /// <param name="path">A reference to the <see cref="NavMeshPath"/> that will contain the calculated path if valid</param>
        /// <param name="calculatePathDistance">This updates <paramref name="pathDistance"/> with the length of the path. <paramref name="pathDistance"/> is set to zero on failure</param>
        /// <param name="nearestNavAreaRange">The range the game will search for a nearby NavArea for targetPos</param>
        /// <param name="maxRangeToEnd">The maximum range the nearest NavArea will the path be considered vaild</param>
        /// <param name="pathDistance">The entire length of the path</param>
        /// <returns><see langword="true"/> if a valid and complete path exists; otherwise, <see langword="false"/></returns>

        public static bool IsValidPathToTarget(Vector3 startPosition, Vector3 endPosition, int areaMask, ref NavMeshPath path, out float pathDistance, bool calculatePathDistance = false, float nearestNavAreaRange = 2.7f, float maxRangeToEnd = 1.5f)
        {
            // Check if we can create a path there first!
            pathDistance = 0f;
            if (!NavMesh.CalculatePath(startPosition, endPosition, areaMask, path))
            {
                return false;
            }

            // Check to make sure the path is valid!
            Vector3[] storedPathCorners = path.corners;
            if (storedPathCorners.Length <= 0)
            {
                return false;
            }

            // This may be a partial path, make sure the end of the path actually reaches our target destiniation!
            if ((storedPathCorners[storedPathCorners.Length - 1] - RoundManager.Instance.GetNavMeshPosition(endPosition, RoundManager.Instance.navHit, nearestNavAreaRange)).sqrMagnitude > maxRangeToEnd * maxRangeToEnd)
            {
                return false;
            }

            // Calculate the path distance as needed
            if (calculatePathDistance)
            {
                for (int i = 1; i < storedPathCorners.Length; i++)
                {
                    pathDistance += Vector3.Distance(storedPathCorners[i - 1], storedPathCorners[i]);
                }
            }

            return true;
        }

        /// <summary>
        /// Helper method that determines whether a complete and valid NavMesh path exists between two points.
        /// </summary>
        /// <remarks>
        /// This is an enhanced check that wraps <see cref="NavMesh.CalculatePath(Vector3, Vector3, NavMeshQueryFilter, NavMeshPath)"/> with additional validation:
        /// <list type="bullet">
        ///   <item>Ensures the path calculation succeeds</item>
        ///   <item>Confirms the path is not empty</item>
        ///   <item>Verifies that the last path corner is sufficiently close to the destination, ensuring the path is complete</item>
        /// </list>
        /// </remarks>
        /// <param name="startPosition">The starting position of the path</param>
        /// <param name="endPosition">The target position to reach</param>
        /// <param name="queryFilter">A filter to use during the pathfinding</param>
        /// <param name="path">A reference to the <see cref="NavMeshPath"/> that will contain the calculated path if valid</param>
        /// <param name="calculatePathDistance">This updates <paramref name="pathDistance"/> with the length of the path. <paramref name="pathDistance"/> is set to zero on failure</param>
        /// <param name="nearestNavAreaRange">The range the game will search for a nearby NavArea for targetPos</param>
        /// <param name="maxRangeToEnd">The maximum range the nearest NavArea will the path be considered vaild</param>
        /// <param name="pathDistance">The entire length of the path</param>
        /// <returns><see langword="true"/> if a valid and complete path exists; otherwise, <see langword="false"/></returns>

        public static bool IsValidPathToTarget(Vector3 startPosition, Vector3 endPosition, NavMeshQueryFilter queryFilter, ref NavMeshPath path, out float pathDistance, bool calculatePathDistance = false, float nearestNavAreaRange = 2.7f, float maxRangeToEnd = 1.5f)
        {
            // Check if we can create a path there first!
            pathDistance = 0f;
            if (!NavMesh.CalculatePath(startPosition, endPosition, queryFilter, path))
            {
                return false;
            }

            // Check to make sure the path is valid!
            Vector3[] storedPathCorners = path.corners;
            if (storedPathCorners.Length <= 0)
            {
                return false;
            }

            // This may be a partial path, make sure the end of the path actually reaches our target destiniation!
            if ((storedPathCorners[storedPathCorners.Length - 1] - RoundManager.Instance.GetNavMeshPosition(endPosition, RoundManager.Instance.navHit, nearestNavAreaRange)).sqrMagnitude > maxRangeToEnd * maxRangeToEnd)
            {
                return false;
            }

            // Calculate the path distance as needed
            if (calculatePathDistance)
            {
                for (int i = 1; i < storedPathCorners.Length; i++)
                {
                    pathDistance += Vector3.Distance(storedPathCorners[i - 1], storedPathCorners[i]);
                }
            }

            return true;
        }

        /// <summary>
        /// Helper method that determines whether a complete and valid NavMesh path exists between two points for the given agent.
        /// </summary>
        /// <param name="navMeshAgent">The agent to test the path for</param>
        /// <inheritdoc cref="IsValidPathToTarget(Vector3, Vector3, NavMeshQueryFilter, ref NavMeshPath, out float, bool, float, float)"/>
        #pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public static bool IsValidPathToTarget(Vector3 startPosition, Vector3 endPosition, NavMeshAgent navMeshAgent, ref NavMeshPath path, out float pathDistance, bool calculatePathDistance = false, float nearestNavAreaRange = 2.7f, float maxRangeToEnd = 1.5f)
        #pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            // Get the area costs from the agent
            float[] costs = new float[32];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = navMeshAgent.GetAreaCost(i);
            }

            NavMeshQueryFilter navMeshQuery = new NavMeshQueryFilter() { agentTypeID = navMeshAgent.agentTypeID, areaMask = navMeshAgent.areaMask, costs = costs };
            return IsValidPathToTarget(startPosition, endPosition, navMeshQuery, ref path, out pathDistance, calculatePathDistance, nearestNavAreaRange, maxRangeToEnd);
        }
    }
}
