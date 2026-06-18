using LethalBots.Enums;
using System;
using System.Runtime.CompilerServices;

namespace LethalBots.AI
{
    /// <summary>
    /// Class for the <c>LethalBotThreat</c> defines the fear ranges of an enemy
    /// </summary>
    public sealed class LethalBotThreat
    {
        /// <summary>
        /// Delegate for getting the fear range of an enemy based on a <see cref="LethalBotFearQuery"/>.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public delegate float? FearRangeDelegate(in LethalBotFearQuery query);

        /// <summary>
        /// Delegate for checking if an enemy can be killed based on a <see cref="LethalBotAttackQuery"/>
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public delegate bool ShouldAttackDelegate(in LethalBotAttackQuery query);

        [Obsolete("Use the other constructor instead!", true)]
        public LethalBotThreat(EnemyAI enemyAI, FearRangeDelegate pankFunc, FearRangeDelegate missionControlFunc, FearRangeDelegate pathfindFunc) : this(enemyAI.GetType(), pankFunc, missionControlFunc, pathfindFunc) { }

        public LethalBotThreat(Type threatType, FearRangeDelegate pankFunc, FearRangeDelegate missionControlFunc, FearRangeDelegate pathfindFunc, ShouldAttackDelegate? shouldAttackFunc = null)
        {
            ThreatType = threatType;
            panikFearRange = pankFunc;
            missionControlFearRange = missionControlFunc;
            pathfindingFearRange = pathfindFunc;
            shouldAttackEnemy = shouldAttackFunc;
        }
        public Type ThreatType { get; private set; }
        private readonly FearRangeDelegate panikFearRange;
        private readonly FearRangeDelegate missionControlFearRange;
        private readonly FearRangeDelegate pathfindingFearRange;
        private readonly ShouldAttackDelegate? shouldAttackEnemy;

        /// <summary>
        /// Returns the fear range using the given <paramref name="fearQuery"/>
        /// </summary>
        /// <param name="fearQuery"></param>
        /// <returns>The range to fear the given enemy or null if nothing to worry about</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float? GetFearRangeForEnemy(in LethalBotFearQuery fearQuery)
        {
            switch (fearQuery.QueryType)
            {
                case EnumFearQueryType.BotPanic:
                    return panikFearRange.Invoke(fearQuery);
                case EnumFearQueryType.PlayerTeleport:
                    return missionControlFearRange.Invoke(fearQuery);
                case EnumFearQueryType.PathfindingAvoid:
                    return pathfindingFearRange.Invoke(fearQuery);
                default:
                    Plugin.LogError($"Unknown fear query type: {fearQuery.QueryType}");
                    return null;
            }
        }

        /// <summary>
        /// Checks if the bot should attack an enemy using the given <paramref name="attackQuery"/>
        /// </summary>
        /// <param name="attackQuery"></param>
        /// <returns><see langword="true"/> we should attack; otherwise <see langword="false"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldAttackEnemy(in LethalBotAttackQuery attackQuery)
        {
            // If you turn this on.....just know what you are getting yourself into......
            // After all, the bots can't tell if you are outmatched here...........
            if (Plugin.Config.ShouldKillEverything)
            {
                return true;
            }
            return shouldAttackEnemy?.Invoke(attackQuery) ?? false;
        }
    }
}
