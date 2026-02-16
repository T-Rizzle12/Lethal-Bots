using System;

namespace LethalBots.Enums
{
    [Serializable]
    public enum EnumVoicesState
    {
        None = 0,
        Chilling,
        EnteringCruiser,
        FollowingPlayer,
        FoundLoot,
        HearsPlayer,
        Hit,
        LosingPlayer,
        Lost,
        LostAndFound,
        RunningFromMonster,
        SteppedOnTrap,
        Sinking,
        AttackingWithGun,
        AttackingWithMelee,
        OrderedToStay,
        OrderedToFollow
    }
}
