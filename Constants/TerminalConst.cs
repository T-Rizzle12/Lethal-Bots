using LethalBots.Managers;
using LethalBots.AI.AIStates;

namespace LethalBots.Constants
{
    // TODO: Change this to help the MissionControlState with purchasing items, since this is legacy code from the original mod Lethal Internship!
    /// <summary>
    /// Const class dedicated for terminal stuff.
    /// </summary>
    /// <remarks>
    /// Used primarly for <see cref="TerminalManager"/> and <see cref="MissionControlState"/>
    /// </remarks>
    public class TerminalConst
    {
        public const int INDEX_DEFAULT_TERMINALNODE = 13; // This is the default node when a bot gets on the terimal, you can also reach this node using the help command.
        
        public const string STRING_OTHER_HELP = "other"; // Helper string to show the other command section
        public const string STRING_BUY_COMMAND = "buy {0} {1}"; // Helper string to purchase an item
        public const string STRING_ROUTE_COMMAND = "route {0}"; // Helper string to route to the given moon
        public const string STRING_CONFIRM_COMMAND = "confirm"; // Helper string to confirm an option
        public const string STRING_CANCEL_COMMAND = "deny"; // Helper string to deny an option
    }
}
