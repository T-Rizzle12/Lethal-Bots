using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager that handles chat commands for the bots
    /// </summary>
    public static class ChatCommandsManager
    {
        private static readonly List<ChatCommand> globalCommands = new List<ChatCommand>();

        private static readonly Dictionary<Type, List<ChatCommand>> stateCommands = new Dictionary<Type, List<ChatCommand>>();

        private static readonly HashSet<Type> ignoreGlobalCommands = new HashSet<Type>();

        /// <summary>
        /// Registers a chat command for all <see cref="AIState"/>s
        /// </summary>
        /// <param name="command"></param>
        public static void RegisterGlobalCommand(ChatCommand command)
        {
            globalCommands.Add(command);
        }

        /// <summary>
        /// Registers <typeparamref name="T"/> to not call the default chat commands!
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void RegisterIgnoreDefaultForState<T>()
            where T : AIState
        {
            ignoreGlobalCommands.Add(typeof(T));
        }

        /// <summary>
        /// Registers a chat command or chat command override for <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="command"></param>
        public static void RegisterCommandForState<T>(ChatCommand command)
            where T : AIState
        {
            Type type = typeof(T);
            if (!stateCommands.TryGetValue(type, out var list))
            {
                list = new List<ChatCommand>();
                stateCommands[type] = list;
            }

            list.Add(command);
        }

        /// <summary>
        /// Returns all of the registered chat commands.
        /// </summary>
        /// <returns>An <see cref="Array"/> with every registered chat command.</returns>
        public static ChatCommand[] GetAllRegisteredChatCommands()
        {
            // Add all of the chat commands together
            List<ChatCommand> chatCommands = new List<ChatCommand>(globalCommands);
            foreach (var chatCommand in stateCommands)
            {
                chatCommands.AddRange(chatCommand.Value);
            }
            return chatCommands.ToArray(); // Return as a beautiful array
        }

        /// <summary>
        /// For use in the voice recogniton register
        /// </summary>
        /// <remarks>
        /// This uses a <see cref="HashSet{T}"/> to prevent duplicate keywords from appearing
        /// </remarks>
        /// <returns></returns>
        public static string[] GetAllRegisteredChatCommandKeywords()
        {
            // Add all of the keywords together
            HashSet<string> keywords = new HashSet<string>();
            ChatCommand[] allChatCommands = GetAllRegisteredChatCommands();
            foreach (var chatCommand in allChatCommands)
            {
                // Some commands have mulitple keywords, so we need to add them all!
                foreach (var keyword in chatCommand.Keywords)
                {
                    keywords.Add(keyword);
                }
            }
            return keywords.ToArray();
        }

        /// <summary>
        /// This removes all registered chat commands!
        /// </summary>
        internal static void RemoveAllChatCommands()
        {
            // Chat commands
            globalCommands.Clear();
            stateCommands.Clear();
            ignoreGlobalCommands.Clear();
        }

        /// <summary>
        /// Called when the bot receives a chat message. This can be from a player or bot!
        /// You can use <see cref="Managers.LethalBotManager.IsPlayerLethalBot(PlayerControllerB)"/> to check who is a bot or not!
        /// </summary>
        /// <remarks>
        /// WARNING: All messages are forced into lower case!<br/>
        /// NOTE: This is not called for messages sent by the bot itself!
        /// </remarks>
        /// <param name="state">The AI state to respond to this message.</param>
        /// <param name="message">The message we received</param>
        /// <param name="playerWhoSentMessage">The player who sent the message!</param>
        /// <param name="isVoice">Was the message spoken or was typed out in the chat?</param>
        /// <returns><see langword="true"/> if the <paramref name="state"/> responded to the chat command; otherwise <see langword="false"/></returns>
        public static bool OnPlayerChatMessageReceived(AIState state, string message, PlayerControllerB playerWhoSentMessage, bool isVoice)
        {
            // Grab type and AI
            Type stateType = state.GetType();
            LethalBotAI lethalBotAI = state.ai;

            // Check for state specific overrides or custom commands.
            if (stateCommands.TryGetValue(stateType, out var list))
            {
                // Go through each command.
                foreach (var command in list)
                {
                    // Some commands have mulitple keywords, so we need to check them all!
                    foreach (var keyword in command.Keywords)
                    {
                        // If the message contains the keyword, execute the command!
                        if (message.Contains(keyword))
                        {
                            if (command.Execute(state, lethalBotAI, playerWhoSentMessage, message, isVoice))
                            {
                                return true; // If the command tells us that it wants to stop processing, then we stop processing!
                            }
                        }
                    }
                }
            }

            // Should we check the default chat commands
            if (ignoreGlobalCommands.Contains(stateType))
            {
                return false;
            }

            // Check for the default chat commands.
            foreach (var command in globalCommands)
            {
                // Some commands have mulitple keywords, so we need to check them all!
                foreach (var keyword in command.Keywords)
                {
                    // If the message contains the keyword, execute the command!
                    if (message.Contains(keyword))
                    {
                        if (command.Execute(state, lethalBotAI, playerWhoSentMessage, message, isVoice))
                        {
                            return true; // If the command tells us that it wants to stop processing, then we stop processing!
                        }
                    }
                }
            }

            return false;
        }
    }
}
