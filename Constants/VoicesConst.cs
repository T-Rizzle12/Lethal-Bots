using LethalBots.AI;
using LethalBots.Enums;
using System.Collections.Generic;

namespace LethalBots.Constants
{
    public class VoicesConst
    {
        public const EnumTalkativeness DEFAULT_CONFIG_ENUM_TALKATIVENESS = EnumTalkativeness.Normal;
        public const EnumResponsiveness DEFAULT_CONFIG_ENUM_RESPONSIVENESS = EnumResponsiveness.Normal;

        public static readonly PlayVoiceParameters DEFAULT_VOICE_PARAMETERS = new PlayVoiceParameters() { VoiceState = EnumVoicesState.None };

        public const float DISTANCE_HEAR_OTHER_BOTS = 10f;
        public const string SWEAR_KEYWORD = "_cuss";
        public const string INSIDE_KEYWORD = "_inside";
        public const string OUTSIDE_KEYWORD = "_outside";

        // Talkativeness cooldowns
        // in seconds
        public const float MIN_COOLDOWN_PLAYVOICE_SHY = 60f;
        public const float MAX_COOLDOWN_PLAYVOICE_SHY = 120f;

        public const float MIN_COOLDOWN_PLAYVOICE_NORMAL = 10f;
        public const float MAX_COOLDOWN_PLAYVOICE_NORMAL = 40f;

        public const float MIN_COOLDOWN_PLAYVOICE_TALKATIVE = 5f;
        public const float MAX_COOLDOWN_PLAYVOICE_TALKATIVE = 20f;

        public const float MIN_COOLDOWN_PLAYVOICE_CANTSTOPTALKING = 0f;
        public const float MAX_COOLDOWN_PLAYVOICE_CANTSTOPTALKING = 0f;

        // Responsiveness cooldowns
        // in seconds
        public const float MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_SHY = 6f;
        public const float MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_SHY = 12f;

        public const float MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_NORMAL = 2f;
        public const float MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_NORMAL = 6f;

        public const float MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_RESPONSIVE = 1f;
        public const float MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_RESPONSIVE = 4f;

        public const float MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_ALWAYSRESPOND = 0f;
        public const float MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_ALWAYSRESPOND = 0f;

        /// <summary>
        /// Voice states that are driven by the responsiveness slider
        /// Everything NOT in this set is driven by the Talkativeness slider
        /// </summary>
        public static readonly HashSet<EnumVoicesState> ResponsivenessVoiceStates = new HashSet<EnumVoicesState>
        {
            EnumVoicesState.AttackingWithGun,
            EnumVoicesState.AttackingWithMelee,
            EnumVoicesState.Hit,
            EnumVoicesState.OrderedToFollow,
            EnumVoicesState.OrderedToStay,
            EnumVoicesState.RunningFromMonster,
            EnumVoicesState.Sinking,
            EnumVoicesState.SteppedOnTrap,
        };
    }
}
