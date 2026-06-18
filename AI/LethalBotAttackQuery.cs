using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Object = UnityEngine.Object;

namespace LethalBots.AI
{
    /// <summary>
    /// Struct for the <c>LethalBotFearQuery</c> defines the attack query for use in <see cref="LethalBotThreat"/> and <seealso cref="LethalBotManager.GetFearRangeForEnemy"/>
    /// </summary>
    public readonly struct LethalBotAttackQuery
    {
        public LethalBotAttackQuery(LethalBotAI? bot, Object threat, bool hasRangedWeapon = false, bool isHumanPlayer = false, bool isMissionController = false)
        {
            LethalBotAI = bot;
            Threat = threat;
            this.hasRangedWeapon = hasRangedWeapon;
            this.isHumanPlayer = isHumanPlayer;
            this.isMissionController = isMissionController;
        }

        public readonly LethalBotAI? LethalBotAI;
        public readonly Object Threat;
        public readonly bool hasRangedWeapon;
        public readonly bool isHumanPlayer;
        public readonly bool isMissionController;

        /// <summary>
        /// Helper function for <see cref="EnemyAI"/> threats to check if they are stunned.
        /// </summary>
        /// <returns></returns>
        public bool IsEnemyStunned()
        {
            return GetThreat(out EnemyAI? enemy) && (enemy.stunnedIndefinitely > 0f || enemy.stunNormalizedTimer > 0f);
        }

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
