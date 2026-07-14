namespace LethalBots.Enums
{
    /// <summary>
    /// Enumeration of the state of the AI of the bot
    /// </summary>
    public enum EnumAIStates
    {
        BrainDead,
        SearchingForPlayer,
        GetCloseToPlayer,
        JustLostPlayer,
        ChillWithPlayer,
        FetchingObject,
        PlayerInCruiser,
        Panik,
        ReturnToShip,
        ChillAtShip,
        SearchingForScrap,
        UseInverseTeleport,
        UseKeyOnLockedDoor,
        MissionControl,
        SellScrap,
        CollectScrapToSell,
        FightEnemy,
        ChargeHeldItem,
        UseTZPInhalant,
        LostInFacility,
        TransferLoot,
        RescueAndRevive,
        GrabLoadout,
        CollectPurchasedItems,
        HealPlayer,
        HoldPosition,
        CustomAIState // This is a custom AI state that can be used for any purpose. This exists to allow modders to add their own AI states.
    }
}
