using GameNetcodeStuff;
using LethalBots.Enums;
using UnityEngine;
using LethalBots.Managers;

namespace LethalBots.AI
{
    /// <summary>
    /// Struct for the <c>LethalBotFearQuery</c> defines the fear query for use in <see cref="LethalBotThreat>"/> and <seealso cref="LethalBotManager.GetFearRangeForEnemy"/>
    /// </summary>
    public readonly struct LethalBotFearQuery
    {
        public LethalBotFearQuery(EnemyAI? bot, EnemyAI enemyAI, PlayerControllerB? playerToCheck, EnumFearQueryType queryType) 
            : this(bot, enemyAI, queryType)
        {
            PlayerToCheck = playerToCheck;
        }
        public LethalBotFearQuery(EnemyAI? bot, EnemyAI enemyAI, EnumFearQueryType queryType)
        {
            Bot = bot;
            EnemyAI = enemyAI;
            QueryType = queryType;
        }
        public readonly EnemyAI? Bot;
        public readonly EnemyAI EnemyAI;
        public readonly PlayerControllerB? PlayerToCheck;
        public readonly EnumFearQueryType QueryType;
    }
}
