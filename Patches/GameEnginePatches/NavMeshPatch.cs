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

        private static void OverrideNavMeshSettings(ref NavMeshBuildSettings buildSettings)
        {
            const int defaultAgentID = 0;
            NavMeshBuildSettings defaultSettings = NavMesh.GetSettingsByID(defaultAgentID);
            buildSettings.agentSlope = 45; // Same as default player slope height // Was 48, testing smaller value
            buildSettings.agentClimb = 1.33f; // TODO: Adjust as needed
            buildSettings.agentHeight = 4f; // TODO: Adjust as needed
            buildSettings.agentRadius = 4.4f; // TODO: Adjust as needed // Was 2.5f and 4f, testing a larger number
            buildSettings.minRegionArea = defaultSettings.minRegionArea;
            buildSettings.overrideTileSize = true;
            buildSettings.tileSize = 256;
            buildSettings.overrideVoxelSize = true;
            buildSettings.voxelSize = 0.6666667f;
        }
    }
}
