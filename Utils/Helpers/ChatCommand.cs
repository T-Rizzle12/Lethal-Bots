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
        public ChatCommandDelegate Execute;

        /// <summary>
        /// A chat command callback
        /// </summary>
        /// <param name="state">The <paramref name="lethalBotAI"/>'s current AI state</param>
        /// <param name="lethalBotAI">The bot who saw or heard the <paramref name="message"/></param>
        /// <param name="playerWhoSentMessage">The player who sent the <paramref name="message"/></param>
        /// <param name="message">The message sent by <paramref name="playerWhoSentMessage"/></param>
        /// <param name="isVoice">If the <paramref name="message"/> was said or sent in the chat</param>
        /// <returns><see langword="true"/> if the given <paramref name="state"/> took some kind of action on the given <paramref name="message"/>; otherwise <see langword="false"/></returns>
        public delegate bool ChatCommandDelegate(AIState state, LethalBotAI lethalBotAI, PlayerControllerB playerWhoSentMessage, string message, bool isVoice);

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
            ChatCommandDelegate execute)
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
            ChatCommandDelegate execute)
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
