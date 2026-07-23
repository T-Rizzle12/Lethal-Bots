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
                const int defaultAgentID = 0;
                NavMeshBuildSettings defaultSettings = NavMesh.GetSettingsByID(defaultAgentID);
                __result.agentSlope = 48; // Same as default player slope height
                __result.agentClimb = defaultSettings.agentClimb; // Same as default step height
                __result.agentHeight = 4; // TODO: Adjust as needed
                __result.agentRadius = 2; // TODO: Adjust as needed
                __result.minRegionArea = defaultSettings.minRegionArea;
                __result.overrideTileSize = defaultSettings.overrideTileSize;
                __result.tileSize = defaultSettings.tileSize;
                __result.overrideVoxelSize = defaultSettings.overrideVoxelSize;
                __result.voxelSize = defaultSettings.voxelSize;
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
                const int defaultAgentID = 0;
                NavMeshBuildSettings defaultSettings = NavMesh.GetSettingsByID(defaultAgentID);
                __result.agentSlope = 48; // Same as default player slope height
                __result.agentClimb = defaultSettings.agentClimb; // Same as default step height
                __result.agentHeight = 4; // TODO: Adjust as needed
                __result.agentRadius = 2; // TODO: Adjust as needed
                __result.minRegionArea = defaultSettings.minRegionArea;
                __result.overrideTileSize = defaultSettings.overrideTileSize;
                __result.tileSize = defaultSettings.tileSize;
                __result.overrideVoxelSize = defaultSettings.overrideVoxelSize;
                __result.voxelSize = defaultSettings.voxelSize;
                Plugin.LogDebug($"Overriding GetSettingsByIndex NavMeshBuildSettings for Bot Cruiser NavMesh. Agent ID: {Const.LETHAL_BOT_CRUISER_NAV_SETTINGS_ID}");
            }
        }
    }
}
