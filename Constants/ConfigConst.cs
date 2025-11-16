using LethalBots.Enums;
using LethalBots.NetworkSerializers;

namespace LethalBots.Constants
{
    public class ConfigConst
    {
        // Config
        public static readonly string ConfigSectionMain = "1. General Settings";
        public static readonly string ConfigSectionIdentities = "2. Bot identities";
        public static readonly string ConfigSectionBehaviour = "3. Behaviour";
        public static readonly string ConfigSectionTeleporters = "4. Teleporters";
        public static readonly string ConfigSectionVoices = "5. Voices";
        public static readonly string ConfigSectionDebug = "6. Debug";

        public static readonly int DEFAULT_MAX_BOTS_AVAILABLE = 16;
        public static readonly int MIN_BOTS_AVAILABLE = 0;
        public static readonly int MAX_BOTS_AVAILABLE = 50; // Current max limit for More Company!

        public static readonly string DEFAULT_BOT_NAME = "Bot #{0}";
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

        public static EnumOptionSuitChange DEFAULT_CONFIG_ENUM_LETHALBOT_SUIT_CHANGE = EnumOptionSuitChange.Manual;

        // TODO: Find out the purpose of all of this.......
        public static readonly int DEFAULT_MAX_IDENTITIES = 22;
        public static readonly int MIN_IDENTITIES = 10;
        public static readonly int MAX_IDENTITIES = 200;
        public static readonly string FILE_NAME_CONFIG_IDENTITIES_DEFAULT = "ConfigIdentitiesDefault.json";
        public static readonly string FILE_NAME_CONFIG_IDENTITIES_USER = "ConfigIdentitiesUser.json";
        public static readonly ConfigIdentity DEFAULT_CONFIG_IDENTITY = new ConfigIdentity()
        {
            name = DEFAULT_BOT_NAME,
            suitConfigOption = (int)EnumOptionSuitConfig.Random,
            suitID = 0,
            voiceFolder = "Mathew_kelly",
            volume = 0.5f
            // voice pitch set after
        };
    }
}
