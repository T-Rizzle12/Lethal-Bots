using HarmonyLib;
using LethalBots.Constants;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.AI;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(NavMesh))]
    public class NavMeshPatch
    {
        [HarmonyPatch("GetSettingsByID")]
        [HarmonyPostfix]
        public static void GetSettingsByID_Postfix(ref NavMeshBuildSettings __result)
        {
            Plugin.LogDebug($"GetSettingsByID returned settings with ID: {__result.agentTypeID}");
            if (__result.agentTypeID == Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID)
            {
                OverrideNavMeshSettings(ref __result);
                Plugin.LogDebug($"Overriding GetSettingsByID NavMeshBuildSettings for Bot Cruiser NavMesh. Agent ID: {Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID}");
            }
        }

        [HarmonyPatch("GetSettingsByIndex")]
        [HarmonyPostfix]
        public static void GetSettingsByIndex_Postfix(ref NavMeshBuildSettings __result)
        {
            Plugin.LogDebug($"GetSettingsByIndex returned settings with ID: {__result.agentTypeID}");
            if (__result.agentTypeID == Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID)
            {
                OverrideNavMeshSettings(ref __result);
                Plugin.LogDebug($"Overriding GetSettingsByIndex NavMeshBuildSettings for Bot Cruiser NavMesh. Agent ID: {Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID}");
            }
        }

        private static float agentSlope = 45f; // Same as default player slope height // Was 48, testing smaller value
        private static float agentClimb = 1.33f; // TODO: Adjust as needed
        private static float agentHeight = 4.5f; // TODO: Adjust as needed
        private static float agentRadius = 2f; // TODO: Adjust as needed // Was 2.5f and 4f, testing a larger number
        private static int tileSize = 90; // Was 256, testing a smaller value
        private static float voxelSize = 0.25f; // Was 1.333333f, testing a smaller number

        private static void OverrideNavMeshSettings(ref NavMeshBuildSettings buildSettings)
        {
            const int defaultAgentID = 0;
            NavMeshBuildSettings defaultSettings = NavMesh.GetSettingsByID(defaultAgentID);
            buildSettings.agentSlope = agentSlope;
            buildSettings.agentClimb = agentClimb;
            buildSettings.agentHeight = agentHeight;
            buildSettings.agentRadius = agentRadius;
            buildSettings.minRegionArea = defaultSettings.minRegionArea;
            buildSettings.overrideTileSize = true;
            buildSettings.tileSize = tileSize;
            buildSettings.overrideVoxelSize = true;
            buildSettings.voxelSize = voxelSize;
        }
    }
}
