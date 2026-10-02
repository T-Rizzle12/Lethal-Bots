using GameNetcodeStuff;
using LethalBots.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that represents a signal translator command!
    /// </summary>
    public class SignalTranslatorCommand
    {
        public string Keyword;
        public SignalTranslatorCommandDelegate Execute;

        /// <summary>
        /// A chat command callback
        /// </summary>
        /// <param name="state">The <paramref name="lethalBotAI"/>'s current AI state</param>
        /// <param name="lethalBotAI">The bot who saw or heard the <paramref name="message"/></param>
        /// <param name="message">The message sent on the signal translator</param>
        /// <returns><see langword="true"/> if the given <paramref name="state"/> took some kind of action on the given <paramref name="message"/>; otherwise <see langword="false"/></returns>
        public delegate bool SignalTranslatorCommandDelegate(AIState state, LethalBotAI lethalBotAI, string message);

        /// <summary>
        /// Creates a new chat command
        /// </summary>
        /// <remarks>
        /// WARNING: <paramref name="keyword"/> will be forced into lower case!
        /// </remarks>
        /// <param name="keyword"></param>
        /// <param name="execute"></param>
        public SignalTranslatorCommand(
            string keyword,
            SignalTranslatorCommandDelegate execute)
        {
            Keyword = keyword.ToLower();
            Execute = execute;
        }
    }
}
