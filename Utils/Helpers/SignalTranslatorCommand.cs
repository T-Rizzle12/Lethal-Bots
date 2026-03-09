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
        public Func<AIState, string, bool> Execute;

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
            Func<AIState, string, bool> execute)
        {
            Keyword = keyword.ToLower();
            Execute = execute;
        }
    }
}
