using LethalBots.Enums;
using LethalBots.NetworkSerializers;

namespace LethalBots.Constants
{
    public class ConfigConst
    {
        // Config
        public const string ConfigSectionMain = "1. General Settings";
        public const string ConfigSectionIdentities = "2. Bot identities";
        public const string ConfigSectionBehavior = "3. Behavior";
        public const string ConfigSectionTeleporters = "4. Teleporters";
        public const string ConfigSectionVoices = "5. Voices";
        public const string ConfigSectionVoiceRecognition = "6. Voice Recognition";
        public const string ConfigSectionDebug = "7. Debug";

        public const int DEFAULT_MAX_BOTS_AVAILABLE = 16;
        public const int MIN_BOTS_AVAILABLE = 0;
        public const int MAX_BOTS_AVAILABLE = 50; // Current max limit for More Company!

        public const string DEFAULT_BOT_NAME = "Bot #{0}";
        //  "Amy Stake",
        //  "Claire Annette",
        //  "Clare Voyant",
        //  "Ella Font",
        //  "Felix Cited",
        //  "Gerry Atrick",
        //  "Harry Legg",
        //  "Justin Case",
        //  "Lee King",
        //  "Luke Atmey",
        //  "Manuel Labour",
        //  "Mia Moore",
        //  "Ophelia Pane",
        //  "Paige Turner",
        //  "Paul Atishon",
        //  "Polly Esther",
        //  "Robyn Banks",
        //  "Terry Aki",
        //  "Tim Burr",
        //  "Toby Lerone",
        //  "Uriel Lucky",
        //  "Zoltan Pepper"

        public const EnumOptionSuitChange DEFAULT_CONFIG_ENUM_LETHALBOT_SUIT_CHANGE = EnumOptionSuitChange.Manual;

        // TODO: Find out the purpose of all of this.......
        public const int DEFAULT_MAX_IDENTITIES = 22;
        public const int MIN_IDENTITIES = 10;
        public const int MAX_IDENTITIES = 200;
        public const string FILE_NAME_CONFIG_IDENTITIES_DEFAULT = "ConfigIdentitiesDefault.json";
        public const string FILE_NAME_CONFIG_IDENTITIES_USER = "ConfigIdentitiesUser.json";
        public const string FILE_NAME_CONFIG_LOADOUTS_DEFAULT = "ConfigLoadoutsDefault.json";
        public const string FILE_NAME_CONFIG_LOADOUTS_USER = "ConfigLoadoutsUser.json";
        public static readonly ConfigIdentity DEFAULT_CONFIG_IDENTITY = new ConfigIdentity();
        public static readonly ConfigLoadout DEFAULT_CONFIG_LOADOUT = new ConfigLoadout();
    }
}
