using GameNetcodeStuff;
using LethalBots.Enums;
using UnityEngine;
using LethalBots.Managers;
using System;
using Object = UnityEngine.Object;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace LethalBots.AI
{
    /// <summary>
    /// Struct for the <c>LethalBotFearQuery</c> defines the fear query for use in <see cref="LethalBotThreat"/> and <seealso cref="LethalBotManager.GetFearRangeForEnemy"/>
    /// </summary>
    public readonly struct LethalBotFearQuery
    {
        [Obsolete("You should pass in the LethalBotAI object instead!", true)]
        public LethalBotFearQuery(EnemyAI? bot, Object threat, PlayerControllerB? playerToCheck, EnumFearQueryType queryType)
            : this(bot, threat, queryType)
        {
            PlayerToCheck = playerToCheck;
        }

        [Obsolete("You should pass in the LethalBotAI object instead!", true)]
        public LethalBotFearQuery(EnemyAI? bot, Object threat, EnumFearQueryType queryType)
        {
            LethalBotAI = bot as LethalBotAI;
            Threat = threat;
            QueryType = queryType;
        }

        public LethalBotFearQuery(LethalBotAI? bot, Object threat, PlayerControllerB? playerToCheck, EnumFearQueryType queryType)
            : this(bot, threat, queryType)
        {
            PlayerToCheck = playerToCheck;
        }
        public LethalBotFearQuery(LethalBotAI? bot, Object threat, EnumFearQueryType queryType)
        {
            LethalBotAI = bot;
            Threat = threat;
            QueryType = queryType;
        }

        public readonly LethalBotAI? LethalBotAI;
        public readonly Object Threat;
        public readonly PlayerControllerB? PlayerToCheck;
        public readonly EnumFearQueryType QueryType;

        [Obsolete("Its not recommended to use this, you should just cast Threat or use GetThreat instead!")]
        public readonly EnemyAI? EnemyAI => Threat as EnemyAI;

        [Obsolete("Its not recommended to use this, you should use LethalBotAI instead!")]
        public readonly EnemyAI? Bot => LethalBotAI;

        /// <summary>
        /// Helper function to return <see cref="Threat"/> as the given <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T? GetThreat<T>() 
            where T : Object
        {
            return Threat as T;
        }

        /// <summary>
        /// <inheritdoc cref="GetThreat{T}()"/>
        /// </summary>
        /// <remarks>
        /// This has build in null checking
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetThreat<T>([NotNullWhen(true)] out T? threat) 
            where T : Object
        {
            threat = Threat as T;
            return threat != null;
        }
    }
}
