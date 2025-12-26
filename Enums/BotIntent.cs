namespace LethalBots.Enums
{
    /// <summary>
    /// Enumeration of the different intents the bot can recognize from player messages.
    /// </summary>
    public enum BotIntent
    {
        Unknown,
        StayOnShip,
        GoToShip,
        StartShip,
        RequestMonitoring,
        RequestTeleport,
        ClearMonitoring,
        Transmit,
        Jester,
        LeaveTerminal,
        FollowMe,
        Explore,
        DropItem,
        ChangeSuit
    }
}
