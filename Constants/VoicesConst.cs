using LethalBots.Enums;

namespace LethalBots.Constants
{
    public class VoicesConst
    {
        public static EnumTalkativeness DEFAULT_CONFIG_ENUM_TALKATIVENESS = EnumTalkativeness.Normal;

        public const float DISTANCE_HEAR_OTHER_BOTS = 10f;
        public const float FADE_IN_TIME = 0.1f;
        public const float FADE_OUT_TIME = 0.2f;
        public const string SWEAR_KEYWORD = "_cuss";
        public const string INSIDE_KEYWORD = "_inside";
        public const string OUTSIDE_KEYWORD = "_outside";

        public const int MIN_COOLDOWN_PLAYVOICE_SHY = 10;
        public const int MAX_COOLDOWN_PLAYVOICE_SHY = 40;

        public const int MIN_COOLDOWN_PLAYVOICE_NORMAL = 5;
        public const int MAX_COOLDOWN_PLAYVOICE_NORMAL = 20;

        public const int MIN_COOLDOWN_PLAYVOICE_TALKATIVE = 2;
        public const int MAX_COOLDOWN_PLAYVOICE_TALKATIVE = 10;

        public const int MIN_COOLDOWN_PLAYVOICE_CANTSTOPTALKING = 0;
        public const int MAX_COOLDOWN_PLAYVOICE_CANTSTOPTALKING = 0;
    }
}
