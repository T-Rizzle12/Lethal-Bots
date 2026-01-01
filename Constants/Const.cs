using LethalBots.Enums;
using LethalBots.NetworkSerializers;
using UnityEngine;

namespace LethalBots.Constants
{
    /// <summary>
    /// Class of constants, used in various places in the plugin code
    /// </summary>
    public class Const
    {
        public const string CSYNC_GUID = "com.sigurd.csync";
        public const string PYSPEECH_GUID = "JS03.PySpeech";

        public const string MORECOMPANY_GUID = "me.swipez.melonloader.morecompany";
        public const string BETTER_EXP_GUID = "Swaggies.BetterEXP";
        public const string MOREEMOTES_GUID = "MoreEmotes";
        public const string BETTEREMOTES_GUID = "BetterEmotes";
        public const string MODELREPLACEMENT_GUID = "meow.ModelReplacementAPI";
        public const string LETHALPHONES_GUID = "LethalPhones";
        public const string FASTERITEMDROPSHIP_GUID = "FlipMods.FasterItemDropship";
        public const string SHOWCAPACITY_GUID = "Piggy.ShowCapacity";
        public const string REVIVECOMPANY_GUID = "OpJosMod.ReviveCompany";
        public const string BUNKBEDREVIVE_GUID = "viviko.BunkbedRevive";
        public const string ZAPRILLATOR_GUID = "Zaprillator";
        public const string TOOMANYEMOTES_GUID = "FlipMods.TooManyEmotes";
        public const string RESERVEDITEMSLOTCORE_GUID = "FlipMods.ReservedItemSlotCore";
        public const string LETHALPROGRESSION_GUID = "Stoneman.LethalProgression";
        public const string LETHALINTERNS_GUID = "Szumi57.LethalInterns";
        public const string CUSTOMITEMBEHAVIOURLIBRARY_GUID = "com.github.WhiteSpike.CustomItemBehaviourLibrary";
        public const string LCALWAYSHEARWALKIEMOD_GUID = "suskitech.LCAlwaysHearActiveWalkie";
        public const string BUTTERYFIXES_GUID = "butterystancakes.lethalcompany.butteryfixes";
        public const string PEEPERS_GUID = "x753.Peepers";
        public const string LETHALMIN_GUID = "NoteBoxz.LethalMin";
        public const string FACILITYMELTDOWN_GUID = "me.loaforc.facilitymeltdown";
        public const string NAVMESHINCOMPANY_GUID = "dev.kittenji.NavMeshInCompany";

        public const string ADDITIONALNETWORKING_DLLFILENAME = "AdditionalNetworking_Preloader.dll";

        public const float EPSILON = 0.01f; // NOTE: This is different from float.Epsilon!
        public const bool DISABLE_ORIGINAL_GAME_DEBUG_LOGS = false;
        public const ulong LETHAL_BOT_ACTUAL_ID_OFFSET = 200000ul; // Was 100000ul, but changed to not conflict with LethalInternship

        // Bot Info
        public const float LETHAL_BOT_FOV = 90f;
        public const int LETHAL_BOT_ENTITIES_RANGE = 40;
        public const float LETHAL_BOT_OBJECT_AWARNESS = 4f; // Was 3f raised to see how it affects the bots!
        public const float LETHAL_BOT_OBJECT_RANGE = 15f;
        public const float WAIT_TIME_FOR_GRAB_DROPPED_OBJECTS = 10f;
        public const float COLLISION_RANGE = 2.8f;
        public const float CLIMB_SPEED = 8f;
        public const int INIT_RAGDOLL_ID = -2;
        public const float LETHAL_BOT_TIMER_LEAVE_PLANET = 5f;
        public const float TIMER_CHECK_EXPOSED = 0.9f;

        public const float AGENT_SPEED = 3.5f;
        public const float AGENT_SPEED_CROUCH = 1.75f;
        public const float BASE_MAX_SPEED = 0.9f;
        public const float BASE_MIN_SPEED = 0.01f;
        public const float BODY_TURNSPEED = 6f;
        public const float CAMERA_TURNSPEED = 4f;

        public const float DISTANCE_CLOSE_ENOUGH_TO_DESTINATION = 1f;
        public const float DISTANCE_CHECK_FOR_HOLES = 2.5f;
        public const float DISTANCE_ITEMS_TO_ENTRANCE = 6f;

        // NEEDTOVAILDATE: Are these correct?
        // FIXME: Yeah, these IDs are not correct, only the company moon id is correct!
        // I know that the company moon's id is correct!
        public const int EXPERIMENTATION_MOON_ID = 0;
        public const int ASSURANCE_MOON_ID = 1;
        public const int VOW_MOON_ID = 2;
        public const int COMPANY_BUILDING_MOON_ID = 3;
        public const int MARCH_MOON_ID = 3;
        public const int ADAMANCE_MOON_ID = 4;
        public const int REND_MOON_ID = 5;
        public const int DINE_MOON_ID = 6;
        public const int OFFENSE_MOON_ID = 7;
        public const int TITAN_MOON_ID = 8;
        public const int ARTIFICE_MOON_ID = 9;
        public const int LIQUIDATION_MOON_ID = 10;
        public const int EMBRION_MOON_ID = 11;
        public const float SHIP_EXPANDING_BOUNDS_DIFFERENCE = 10f;

        // NpcController
        public const int PLAYER_MASK = 8;
        public const float MAX_CHAT_RANGE = 25f;
        public const string PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSRIGHTHAND = "HoldingItemsRightHand";
        public const string PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSBOTHHANDS = "HoldingItemsBothHands";
        public const string PLAYER_ANIMATION_WEIGHT_SPECIALANIMATIONS = "SpecialAnimations";
        public const string PLAYER_ANIMATION_WEIGHT_EMOTESNOARMS = "EmotesNoArms";

        public const string PLAYER_ANIMATION_BOOL_GRAB = "Grab";
        public const string PLAYER_ANIMATION_BOOL_GRABVALIDATED = "GrabValidated";
        public const string PLAYER_ANIMATION_BOOL_GRABINVALIDATED = "GrabInvalidated";
        public const string PLAYER_ANIMATION_BOOL_CANCELHOLDING = "cancelHolding";
        public const string PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATION = "SwitchHoldAnimation";
        public const string PLAYER_ANIMATION_BOOL_SWITCHHOLDANIMATIONTWOHANDED = "SwitchHoldAnimationTwoHanded";
        public const string PLAYER_ANIMATION_BOOL_WALKING = "Walking";
        public const string PLAYER_ANIMATION_BOOL_SPRINTING = "Sprinting";
        public const string PLAYER_ANIMATION_BOOL_SIDEWAYS = "Sideways";
        public const string PLAYER_ANIMATION_BOOL_ANIMATIONSPEED = "animationSpeed";
        public const string PLAYER_ANIMATION_BOOL_HINDEREDMOVEMENT = "hinderedMovement";
        public const string PLAYER_ANIMATION_BOOL_CROUCHING = "crouching";
        public const string PLAYER_ANIMATION_BOOL_FALLNOJUMP = "FallNoJump";
        public const string PLAYER_ANIMATION_BOOL_SHORTFALLLANDING = "ShortFallLanding";
        public const string PLAYER_ANIMATION_BOOL_LIMP = "Limp";

        public const string PLAYER_ANIMATION_TRIGGER_THROW = "Throw";
        public const string PLAYER_ANIMATION_TRIGGER_DAMAGE = "Damage";
        public const string PLAYER_ANINATION_TRIGGER_TERMINAL = "Terminal";
        public const string PLAYER_ANIMATION_TRIGGER_SHORTFALLLANDING = "ShortFallLanding";

        public const string PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED = "animationSpeed";
        public const string PLAYER_ANIMATION_FLOAT_TIREDAMOUNT = "tiredAmount";

        public const string MAPDOT_ANIMATION_BOOL_DEAD = "dead";

        // Idle
        // -1437577361
        // -1904134370,
        // -1204949837,
        // 1942734694,
        // -291778088,
        // -822567509};

        // FIXME: These hashes may be out of date!!!!
        public const int IDLE_STATE_HASH = -1437577361;
        public const int WALKING_STATE_HASH = 81563449;
        public const int SPRINTING_STATE_HASH = -350224702;
        public const int CROUCHING_IDLE_STATE_HASH = 1917280335;
        public const int CROUCHING_WALKING_STATE_HASH = -483816927;

        // BrainDeadState
        public const float MIN_TIME_TO_VOTE = 0.5f;
        public const float MAX_TIME_TO_VOTE = 1.5f;

        // SearchingForPlayerState 
        public const float MIN_TIME_SPRINT_SEARCH_WANDER = 1f;
        public const float MAX_TIME_SPRINT_SEARCH_WANDER = 3f;
        public const float MAX_TIME_SEARCH_FOR_PLAYER = 20f;

        // GetCloseToPlayerState
        public const float DISTANCE_START_RUNNING = 8f;
        public const float DISTANCE_STOP_RUNNING = 7f;
        public const float DISTANCE_CLOSE_ENOUGH_HOR = 5f;
        public const float DISTANCE_CLOSE_ENOUGH_VER = 2f;
        public const float DISTANCE_AWARENESS_HOR = 25f;
        public const float DISTANCE_AWARENESS_VER = 50f;

        // JustLostPlayerState
        public const float TIMER_LOOKING_AROUND = 6f;
        public const float DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION = 2f;
        public const float WAIT_TIME_TO_TELEPORT = 1f;
        public const float DISTANCE_TO_ENTRANCE = 4f;
        public const float MIN_TIME_FREEZE_LOOKING_AROUND = 0.5f;
        public const float MAX_TIME_FREEZE_LOOKING_AROUND = 2f;

        // Stuck
        public const float TIMER_STUCK_TOO_MUCH = 2f;

        // Player in ShipState
        public const float DISTANCE_TO_SHIP_BOUND_CLOSEST_POINT = 1f;
        public const float DISTANCE_OF_DROPPED_OBJECT_SHIP_BOUND_CLOSEST_POINT = 2f;

        // Player in cruiser vehicle
        public static readonly Vector3 POS1_ENTRY_LETHALBOT_CRUISER = new Vector3(-0.5f, -1f, -5.5f);
        public static readonly Vector3 POS2_ENTRY_LETHALBOT_CRUISER = new Vector3(0.9f, -1f, -5.5f);

        public static readonly Vector3 FIRST_CORNER_LETHALBOT_IN_CRUISER = new Vector3(-0.5f, -0.5f, -0.4f);
        public static readonly Vector3 SECOND_CORNER_LETHALBOT_IN_CRUISER = new Vector3(0.9f, -0.5f, -2.5f);

        // PanikState
        public const float DISTANCE_FLEEING = 20f;
        public const float DISTANCE_FLEEING_NO_LOS = 5f;
        public const float FLEEING_UPDATE_ENTRANCE = 2f;
        public const float DISTANCE_NEARBY_ENTRANCE = 20f;
        public const float FLEEING_CALM_DOWN_TIME = 10f;
        public const float FLEEING_BREAK_LOS_TIME = 4f;

        // Return to ship state
        public const float DISTANCE_TO_CHILL_POINT = 2f;
        public const float RETURN_UPDATE_ENTRANCE = 10f;
        public const float DRUNKNESS_CUTOFF = 0.15f;

        // Chill at ship state
        public const float TIMER_CHILL_AT_SHIP = 20f;
        public const float TIMER_CHILL_AT_SHIP_AT_COMPANY = 2f;

        // Searching for scrap state
        public const float TIMER_SEARCH_FOR_SCRAP = 60f;
        public const float TIMER_CHECK_FOR_TRAPPED_PLAYER = 2.5f;
        public const float MIN_TIME_SEARCH_LOOKING_AROUND = 2f;
        public const float MAX_TIME_SEARCH_LOOKING_AROUND = 10f;
        public const float WAIT_TIME_FOR_SAFE_PATH = 60f;
        public const int MAX_ENTRANCE_ATTEMPTS = 3;

        // Use Inverse Teleporter State
        public const float DISTANCE_FROM_INVERSE_TELEPORTER = 1.5f;

        // Use Key On Locked Door State
        public const float TIMER_USE_KEY_UNSTUCK = 3f;

        // Sell Scrap State
        public const float DISTANCE_TO_COMPANY_DESK = 3f;
        public const float DISTANCE_TO_WAIT_AT_DESK = 10f;

        // Fetching Object State
        public const int MAX_GRAB_OBJECT_ATTEMPTS = 3;

        // Mission Control State
        public const float TIMER_NEXT_ENEMY_CALL = 60f;
        public const float LETHAL_BOT_TIMER_WAIT_FOR_TERMINAL = 5f;
        public const string TRANSMIT_KEYWORD = "transmit";
        public static readonly int TRANSMIT_KEYWORD_LENGTH = TRANSMIT_KEYWORD.Length; // Length of the word "transmit"

        // Misc
        public const float DISTANCE_NPCBODY_FROM_LADDER = 2;
        public const float DISTANCE_NPCBODY_FROM_DOOR = 2.5f;
        public const float TIMER_CHECK_DOOR = 0.9f;
        public const float TIMER_USE_ELEVATOR = 4.0f; // Should this be a config option instead?
        public const float DISTANCE_TO_ELEVATOR_BOTTOM = 10f;
        public const float DISTANCE_TO_ELEVATOR_TOP = 20f;
        public const float LETHAL_BOT_DROWN_TIME = 10f; // Makes it easier to update how long a bot has until it drowns!

        // Tips
        public const string TOOLTIP_DROP_ITEM = "Drop your item : [{0}]";
        public const string TOOLTIP_FOLLOW_ME = "Follow me: [{0}]";
        public const string TOOLTIP_LEAD_THE_WAY = "Lead the way: [{0}]";
        public const string TOOLTIP_CHANGE_SUIT_BOTS = "Change suit : [{0}]";
        public const string TOOLTIP_MAKE_BOT_LOOK = "Make bots look : [{0}]";
        public const string TOOLTIPS_ORDER_1 = "order 1 : [{0}]";
        
    }
}
