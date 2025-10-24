using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Managers;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(RoundManager))]
    public class RoundManagerPatch
    {
        /// <summary>
        /// Patch for debug spawn bush spawn point
        /// </summary>
        [HarmonyPatch("LoadNewLevel")]
        [HarmonyPrefix]
        public static bool LoadNewLevel_Postfix(RoundManager __instance)
        {
            if (!DebugConst.SPAWN_BUSH_WOLVES_FOR_DEBUG)
            {
                return true;
            }

            StartOfRound.Instance.currentLevel.moldStartPosition = 5;
            __instance.currentLevel.moldSpreadIterations = 5;
            Plugin.LogDebug($"StartOfRound.Instance.currentLevel.moldStartPosition {StartOfRound.Instance.currentLevel.moldStartPosition}");
            Plugin.LogDebug($"__instance.currentLevel.moldSpreadIterations {__instance.currentLevel.moldSpreadIterations}");

            return true;
        }

        [HarmonyPatch("GenerateNewFloor")]
        [HarmonyPrefix]
        static void GenerateNewFloor_Postfix(RoundManager __instance)
        {
            if (!DebugConst.SPAWN_MINESHAFT_FOR_DEBUG)
            {
                return;
            }

            IntWithRarity intWithRarity;
            for (int i = 0; i < __instance.currentLevel.dungeonFlowTypes.Length; i++)
            {
                intWithRarity = __instance.currentLevel.dungeonFlowTypes[i];
                // Factory
                if (intWithRarity.id == 0)
                {
                    intWithRarity.rarity = 0;
                }
                // Manor
                if (intWithRarity.id == 1)
                {
                    intWithRarity.rarity = 0;
                }
                // Cave
                if (intWithRarity.id == 4)
                {
                    intWithRarity.rarity = 300;
                }
                Plugin.LogDebug($"dungeonFlowTypes {intWithRarity.id} {intWithRarity.rarity}");
            }
        }

        /// <summary>
        /// This spawns the bots right as the ship starts to land!
        /// </summary>
        [HarmonyPatch("FinishGeneratingNewLevelClientRpc")]
        [HarmonyPostfix]
        static void FinishGeneratingNewLevelClientRpc_PostFix()
        {
            Plugin.LogDebug(
                $"[SpawnLethalBotsAtShip] Manager Instance: {LethalBotManager.Instance}, " +
                $"IsSpawned: {LethalBotManager.Instance?.IsSpawned}, " +
                $"NetworkObject: {LethalBotManager.Instance?.NetworkObject}, " +
                $"NetObjID: {LethalBotManager.Instance?.NetworkObject?.NetworkObjectId}, " +
                $"IsServer: {LethalBotManager.Instance?.IsServer}, " +
                $"IsHost: {LethalBotManager.Instance?.IsHost}");

            // FIXME: I need to find out why this is called twice for the host!
            //Plugin.LogInfo("FinishGeneratingNewLevelClientRpc called!");
            LethalBotManager.Instance?.SpawnLethalBotsAtShip();
        }
    }
}
