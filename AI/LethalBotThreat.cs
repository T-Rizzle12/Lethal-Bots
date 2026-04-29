using System;
using LethalBots.Enums;

namespace LethalBots.AI
{
    /// <summary>
    /// Class for the <c>LethalBotThreat</c> defines the fear ranges of an enemy
    /// </summary>
    public class LethalBotThreat
    {
        public LethalBotThreat(EnemyAI enemyAI, Func<LethalBotFearQuery, float?> pankFunc, Func<LethalBotFearQuery, float?> missionControlFunc, Func<LethalBotFearQuery, float?> pathfindFunc) : this(enemyAI.GetType(), pankFunc, missionControlFunc, pathfindFunc) { }

        public LethalBotThreat(Type threatType, Func<LethalBotFearQuery, float?> pankFunc, Func<LethalBotFearQuery, float?> missionControlFunc, Func<LethalBotFearQuery, float?> pathfindFunc)
        {
            ThreatType = threatType;
            panikFearRange = pankFunc;
            missionControlFearRange = missionControlFunc;
            pathfindingFearRange = pathfindFunc;
        }
        public Type ThreatType { get; private set; }
        private readonly Func<LethalBotFearQuery, float?> panikFearRange;
        private readonly Func<LethalBotFearQuery, float?> missionControlFearRange;
        private readonly Func<LethalBotFearQuery, float?> pathfindingFearRange;

        public float? GetFearRangeForEnemy(LethalBotFearQuery fearQuery)
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
