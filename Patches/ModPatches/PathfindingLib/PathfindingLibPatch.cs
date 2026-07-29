using PathfindingLib.Utilities.Native;
using System;
using System.Collections.Generic;
using System.Text;
using LethalBots.Constants;
using PathfindingLib.API;
using HarmonyLib;
using PathfindingLib.Patches;

namespace LethalBots.Patches.ModPatches.PathfindingLib
{
    public class PathfindingLibPatch
    {
        /// <summary>
        /// Helper function that registeres the bot's custom area types using PathfindingLib
        /// </summary>
        public static void AddCustomAreaMasks()
        {
            Plugin.LogInfo("[PathfindingLibPatch] Adding custom area names for bots");
            NativeHelpers.SetAreaName(Const.LETHAL_BOT_LANDMINE_NAVAREA, "LethalBotLandmine");
            NativeHelpers.SetAreaName(Const.LETHAL_BOT_QUICKSAND_NAVAREA, "LethalBotQuicksand");
            NativeHelpers.SetAreaName(Const.LETHAL_BOT_BRIDGE_NAVAREA, "LethalBotBridge");
            NativeHelpers.SetAreaName(Const.LETHAL_BOT_ONLY_NAVAREA, "LethalBotOnly");
        }

        internal static void BeginNavMeshWrite()
        {
            NavMeshLock.BeginWrite();
        }

        internal static void EndNavMeshWrite()
        {
            NavMeshLock.EndWrite();
        }

        //[HarmonyPatch(typeof(NavMeshLock), "BeginWrite")]
        //[HarmonyPrefix]
        //public static void BeginWrite_Prefix()
        //{
        //    Plugin.LogInfo("NavMeshLock: BeginWrite called");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "BeginWrite")]
        //[HarmonyPostfix]
        //public static void BeginWrite_Postfix()
        //{
        //    Plugin.LogInfo("NavMeshLock: BeginWrite ended");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "EndWrite")]
        //public static void EndWrite_Prefix()
        //{
        //    Plugin.LogInfo("NavMeshLock: EndWrite called");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "EndWrite")]
        //public static void EndWrite_Postfix()
        //{
        //    Plugin.LogInfo("NavMeshLock: EndWrite ended");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "BeginRead")]
        //[HarmonyPrefix]
        //public static void BeginRead_Prefix()
        //{
        //    Plugin.LogInfo("NavMeshLock: EndWrite called");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "BeginRead")]
        //[HarmonyPostfix]
        //public static void BeginRead_Postfix()
        //{
        //    Plugin.LogInfo("NavMeshLock: BeginRead ended");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "EndRead")]
        //public static void EndRead_Prefix()
        //{
        //    Plugin.LogInfo("NavMeshLock: EndRead called");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(NavMeshLock), "EndRead")]
        //public static void EndRead_Postfix()
        //{
        //    Plugin.LogInfo("NavMeshLock: EndRead ended");
        //    Plugin.LogInfo(Environment.StackTrace);
        //}

        //[HarmonyPatch(typeof(PatchNavMeshSurface), nameof(PatchNavMeshSurface.EndNavMeshWriteAtEndOfAsyncOperation))]
        //[HarmonyPostfix]
        //public static void EndNavMeshWriteAtEndOfAsyncOperation_Postfix()
        //{
        //    Plugin.LogInfo($"EndNavMeshWriteAtEndOfAsyncOperation was successfully called!");
        //}
    }
}
