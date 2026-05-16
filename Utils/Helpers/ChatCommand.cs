using GameNetcodeStuff;
using LethalBots.AI;
using System;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that represents a chat command!
    /// </summary>
    public class ChatCommand
    {
        public string[] Keywords;
        public Func<AIState, LethalBotAI, PlayerControllerB, string, bool, bool> Execute;

        /// <summary>
        /// Creates a new chat command
        /// </summary>
        /// <remarks>
        /// WARNING: <paramref name="keyword"/> will be forced into lower case!
        /// </remarks>
        /// <param name="keyword"></param>
        /// <param name="execute"></param>
        public ChatCommand(
            string keyword,
            Func<AIState, LethalBotAI, PlayerControllerB, string, bool, bool> execute)
        {
            Keywords = new string[] { keyword.ToLower() };
            Execute = execute;
        }

        /// <summary>
        /// Creates a new chat command
        /// </summary>
        /// <remarks>
        /// WARNING: <paramref name="keywords"/> will be forced into lower case!
        /// </remarks>
        /// <param name="keywords"></param>
        /// <param name="execute"></param>

        public ChatCommand(
            string[] keywords,
            Func<AIState, LethalBotAI, PlayerControllerB, string, bool, bool> execute)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                keywords[i] = keywords[i].ToLower();
            }
            Keywords = keywords;
            Execute = execute;
        }
    }
}
