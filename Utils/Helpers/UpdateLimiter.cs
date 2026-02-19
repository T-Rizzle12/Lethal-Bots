using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that allows me to limit how often a patch is run!
    /// </summary>
    /// <remarks>
    /// I should probably move this into its own file
    /// </remarks>
    internal class UpdateLimiter
    {
        private float nextUpdateCheck;
        private float updateInterval;

        internal UpdateLimiter(float updateInterval = 0.5f)
        {
            this.updateInterval = updateInterval;
            this.Invalidate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanUpdate()
        {
            return nextUpdateCheck >= updateInterval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invalidate()
        {
            nextUpdateCheck = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            nextUpdateCheck += deltaTime;
        }
    }
}
