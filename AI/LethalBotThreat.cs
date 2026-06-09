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

        public LethalBotThreat(EnemyAI enemyAI, FearRangeDelegate pankFunc, FearRangeDelegate missionControlFunc, FearRangeDelegate pathfindFunc) : this(enemyAI.GetType(), pankFunc, missionControlFunc, pathfindFunc) { }

        public LethalBotThreat(Type threatType, FearRangeDelegate pankFunc, FearRangeDelegate missionControlFunc, FearRangeDelegate pathfindFunc)
        {
            ThreatType = threatType;
            panikFearRange = pankFunc;
            missionControlFearRange = missionControlFunc;
            pathfindingFearRange = pathfindFunc;
        }
        public Type ThreatType { get; private set; }
        private readonly FearRangeDelegate panikFearRange;
        private readonly FearRangeDelegate missionControlFearRange;
        private readonly FearRangeDelegate pathfindingFearRange;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float? GetFearRangeForEnemy(in LethalBotFearQuery fearQuery)
        {
            switch (fearQuery.QueryType)
            {
                case EnumFearQueryType.BotPanic:
                    return panikFearRange?.Invoke(fearQuery);
                case EnumFearQueryType.PlayerTeleport:
                    return missionControlFearRange?.Invoke(fearQuery);
                case EnumFearQueryType.PathfindingAvoid:
                    return pathfindingFearRange?.Invoke(fearQuery);
                default:
                    Plugin.LogError($"Unknown fear query type: {fearQuery.QueryType}");
                    return null;
            }
        }
    }
}
