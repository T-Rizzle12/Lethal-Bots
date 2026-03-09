using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Managers
{
    public static class SignalTranslatorCommandsManager
    {
        private static readonly List<SignalTranslatorCommand> globalCommands = new List<SignalTranslatorCommand>();

        private static readonly Dictionary<Type, List<SignalTranslatorCommand>> stateCommands = new Dictionary<Type, List<SignalTranslatorCommand>>();

        private static readonly HashSet<Type> ignoreGlobalCommands = new HashSet<Type>();

        /// <summary>
        /// Registers a signal translator command for all <see cref="AIState"/>s
        /// </summary>
        /// <param name="command"></param>
        public static void RegisterGlobalCommand(SignalTranslatorCommand command)
        {
            globalCommands.Add(command);
        }

        /// <summary>
        /// Registers <typeparamref name="T"/> to not call the default signal translator commands!
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void RegisterIgnoreDefaultForState<T>()
            where T : AIState
        {
            ignoreGlobalCommands.Add(typeof(T));
        }

        /// <summary>
        /// Registes a signal translator commmand or signal translator command override for <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="command"></param>
        public static void RegisterCommandForState<T>(SignalTranslatorCommand command)
            where T : AIState
        {
            Type type = typeof(T);
            if (!stateCommands.TryGetValue(type, out var list))
            {
                list = new List<SignalTranslatorCommand>();
                stateCommands[type] = list;
            }

            list.Add(command);
        }

        /// <summary>
        /// This removes all registered signal translator commands!
        /// </summary>
        internal static void RemoveAllSignalTranslatorCommands()
        {
            // Signal Translator commands
            globalCommands.Clear();
            stateCommands.Clear();
            ignoreGlobalCommands.Clear();
        }

        /// <summary>
        /// Called when the bot recevies a message from the signal translator! This can be from a player or bot!
        /// </summary>
        /// <remarks>
        /// WARNING: All messages are forced into lower case!
        /// </remarks>
        /// <param name="state">The AI state to respond to this message.</param>
        /// <param name="message">The message we received</param>
        /// <returns><see langword="true"/> if the <paramref name="state"/> responded to the signal translator command; otherwise <see langword="false"/></returns>
        public static bool OnSignalTranslatorMessageReceived(AIState state, string message)
        {
            Type stateType = state.GetType();

            // Check for state specific overrides or custom commands.
            if (stateCommands.TryGetValue(stateType, out var list))
            {
                foreach (var command in list)
                {
                    if (message.Contains(command.Keyword))
                    {
                        if (command.Execute(state, message))
                        {
                            return true;
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
                if (message.Contains(command.Keyword))
                {
                    if (command.Execute(state, message))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
