using PathfindingLib.Utilities.Native;
using System;
using System.Collections.Generic;
using System.Text;
using LethalBots.Constants;
using PathfindingLib.API;
using HarmonyLib;
using PathfindingLib.Patches;
using System.Runtime.CompilerServices;

namespace LethalBots.Patches.ModPatches.PathfindingLib
{
    public class PathfindingLibPatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void BeginNavMeshWrite()
        {
            NavMeshLock.BeginWrite();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
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
