using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that allows me to limit how often a patch is run!
    /// </summary>
    public class UpdateLimiter
    {
        // Static variables
        // Conditional Weak Table since when the EnemyAI is removed, the table automatically cleans itself!
        private static ConditionalWeakTable<EnemyAI, UpdateLimiter> nextUpdateList = new ConditionalWeakTable<EnemyAI, UpdateLimiter>();

        /// <summary>
        /// Helper function that retrieves the <see cref="UpdateLimiter"/>
        /// for the given <see cref="EnemyAI"/>
        /// </summary>
        /// <param name="ai"></param>
        /// <param name="updateInterval">The amount of time that should pass between calls to the patch</param>
        /// <returns>The <see cref="UpdateLimiter"/> associated with the given <see cref="EnemyAI"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpdateLimiter GetOrCreateMonitor(EnemyAI ai, float updateInterval = 0.5f)
        {
            return nextUpdateList.GetValue(ai, _ => new UpdateLimiter(updateInterval));
        }

        /// <summary>
        /// Removes the specified enemy AI instance from the monitoring list.
        /// </summary>
        /// <param name="ai">The enemy AI instance to remove from monitoring. Cannot be null.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveMonitor(EnemyAI ai)
        {
            nextUpdateList.Remove(ai);
        }

        // Memeber variables
        private readonly CountdownTimer nextUpdateTimer = new CountdownTimer();
        private float updateInterval;

        internal UpdateLimiter(float updateInterval = 0.5f)
        {
            this.updateInterval = updateInterval;
            this.Invalidate();
        }

        /// <summary>
        /// Changes the update interval for this <see cref="UpdateLimiter"/>
        /// </summary>
        /// <param name="updateInterval"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetUpdateInterval(float updateInterval)
        {
            this.updateInterval = updateInterval;
        }

        /// <summary>
        /// Has our <see cref="updateInterval"/> elapsed
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanUpdate()
        {
            return !nextUpdateTimer.HasStarted() || nextUpdateTimer.Elapsed();
        }

        /// <summary>
        /// Restarts our update limiter by using our set <see cref="updateInterval"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invalidate()
        {
            nextUpdateTimer.Start(updateInterval);
        }
    }
}
