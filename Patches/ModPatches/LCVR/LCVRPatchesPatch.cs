using GameNetcodeStuff;
using LethalBots.Managers;

namespace LethalBots.Patches.ModPatches.LCVR
{
    public class LCVRPatchesPatch
    {
        public static bool BeforePlayerDeath_Prefix(PlayerControllerB __0)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
            {
                return false;
            }
            return true;
        }

        public static bool OnPlayerDeath_Prefix(PlayerControllerB __0)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
            {
                return false;
            }
            return true;
        }

        public static bool DisplaySafetyPatch_Prefix(PlayerControllerB __0)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
            {
                return false;
            }
            return true;
        }

        public static bool AfterDamagePlayer_Prefix(PlayerControllerB __0)
        {
            if (LethalBotManager.Instance.IsPlayerLethalBot(__0))
            {
                return false;
            }
            return true;
        }
    }
}
