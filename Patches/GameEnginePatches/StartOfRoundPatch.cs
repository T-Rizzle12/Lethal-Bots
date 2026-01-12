using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.AI;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LethalBots.Patches.GameEnginePatches
{
    /// <summary>
    /// Patches for <c>StartOfRound</c>
    /// </summary>
    [HarmonyPatch(typeof(StartOfRound))]
    public class StartOfRoundPatch
    {
        /// <summary>
        /// Load the managers if the client is host/server
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        static void Awake_Prefix(StartOfRound __instance)
        {
            Plugin.LogDebug("Initialize managers...");

            GameObject objectManager;

            // MonoBehaviours
            objectManager = new GameObject("InputManager");
            objectManager.AddComponent<InputManager>();

            objectManager = new GameObject("AudioManager");
            objectManager.AddComponent<AudioManager>();

            objectManager = new GameObject("IdentityManager");
            objectManager.AddComponent<IdentityManager>();

            objectManager = new GameObject("LoadoutManager");
            objectManager.AddComponent<LoadoutManager>();

            // NetworkBehaviours
            objectManager = Object.Instantiate(PluginManager.Instance.TerminalManagerPrefab);
            if (__instance.NetworkManager.IsHost || __instance.NetworkManager.IsServer)
            {
                objectManager.GetComponent<NetworkObject>().Spawn();
            }

            objectManager = Object.Instantiate(PluginManager.Instance.SaveManagerPrefab);
            if (__instance.NetworkManager.IsHost || __instance.NetworkManager.IsServer)
            {
                objectManager.GetComponent<NetworkObject>().Spawn();
            }

            objectManager = Object.Instantiate(PluginManager.Instance.LethalBotManagerPrefab);
            if (__instance.NetworkManager.IsHost || __instance.NetworkManager.IsServer)
            {
                objectManager.GetComponent<NetworkObject>().Spawn();
            }

            Plugin.LogDebug("... Managers started");
        }

        /// <summary>
        /// Patch to intercept the end of round for managing bots
        /// </summary>
        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPrefix]
        static void ShipHasLeft_PreFix()
        {
            LethalBotManager.Instance.SyncEndOfRoundLethalBots(true);
        }

        /// <summary>
        /// Patch to intercept the end of round for managing bots
        /// </summary>
        [HarmonyPatch("ReviveDeadPlayers")]
        [HarmonyPostfix]
        static void ReviveDeadPlayers_PostFix()
        {
            LethalBotManager.Instance.SyncEndOfRoundLethalBots();
        }

        /// <summary>
        /// Patch to update the bot's xp since the game does not do it for bots
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("EndOfGameClientRpc")]
        [HarmonyPostfix]
        static void EndOfGameClientRpc_PostFix(StartOfRound __instance, int scrapCollectedOnServer, bool ___localPlayerWasMostProfitableThisRound)
        {
            // Just like the base game does for the local player,
            // we only update the bots' XP if the planet has time.
            if (__instance.currentLevel.planetHasTime)
            { 
                LethalBotManager.Instance.UpdateLethalBotsXP(__instance, __instance.gameStats, ___localPlayerWasMostProfitableThisRound); 
            }
        }

        // Something to think about. Currently, OnShipLandedMiscEvents is called a few seconds after the ship has landed.
        // This causes the bots to spawn a few seconds after the ship has landed.
        // This is not a problem, but it would be better to spawn them at the same time as the ship starts its landing.
        // openingDoorsSequence is the coroutine that is called when the ship is landing.
        // It is called in the RoundManager class, in the FinishLevelGeneration method.
        // openingDoorsSequence sets shipDoorsEnabled to true after a few seconds.
        // This may be the best time to spawn the bots.
        /*[HarmonyPatch("OnShipLandedMiscEvents")]
        [HarmonyPostfix]
        static void OnShipLandedMiscEvents_PostFix()
        {
            LethalBotManager.Instance.SpawnLethalBotsAtShip();
        }*/

        #region Reverse patches

        [HarmonyPatch("GetPlayerSpawnPosition")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static Vector3 GetPlayerSpawnPosition_ReversePatch(object instance, int playerNum, bool simpleTeleport) => throw new NotImplementedException("Stub LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch");

        #endregion

        #region Transpilers

        /// <summary>
        /// Patch to stop resetting the spectator UI when the human player is actually alive!
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("ReviveDeadPlayers")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ReviveDeadPlayers_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(isOwnerGetter))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                //codes.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod).WithLabels(codes[startIndex].labels));
                codes[startIndex] = new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalMethod).WithLabels(codes[startIndex].labels);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.HUDManagerPatch.ReviveDeadPlayers could not stop redundant RemoveSpectatorUI calls.");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Patch for only try to revive irl players not bots
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        /// TODO: Change this since we want the game to revive the bots!
        //[HarmonyPatch("ReviveDeadPlayers")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> ReviveDeadPlayers_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 2; i++)
        //    {
        //        if (codes[i].ToString().StartsWith("ldarg.0 NULL") //410
        //            && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
        //            && codes[i + 2].ToString() == "ldlen NULL")
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex].opcode = OpCodes.Nop;
        //        codes[startIndex].operand = null;
        //        codes[startIndex + 1].opcode = OpCodes.Nop;
        //        codes[startIndex + 1].operand = null;
        //        codes[startIndex + 2].opcode = OpCodes.Call;
        //        codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.ReviveDeadPlayers_Transpiler could not use irl number of player in list.");
        //    }

        //    return codes.AsEnumerable();
        //}

        //[HarmonyPatch("SyncShipUnlockablesServerRpc")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> SyncShipUnlockablesServerRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 23; i++)
        //    {
        //        if (codes[i].ToString().StartsWith("ldarg.0 NULL") // 277
        //            && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
        //            && codes[i + 2].ToString() == "ldlen NULL"
        //            && codes[i + 23].ToString().StartsWith("call void StartOfRound::SyncShipUnlockablesClientRpc")) // 300
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex].opcode = OpCodes.Nop;
        //        codes[startIndex].operand = null;
        //        codes[startIndex + 1].opcode = OpCodes.Nop;
        //        codes[startIndex + 1].operand = null;
        //        codes[startIndex + 2].opcode = OpCodes.Call;
        //        codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.SyncShipUnlockablesServerRpc_Transpiler could not use irl number of player in list.");
        //    }

        //    return codes.AsEnumerable();
        //}


        /// <summary>
        /// Patch for sync the ship unlockable only for irl players not bots
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        //[HarmonyPatch("SyncShipUnlockablesClientRpc")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> SyncShipUnlockablesClientRpc_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 2; i++)
        //    {
        //        if (codes[i].ToString().StartsWith("ldarg.0 NULL") // 343
        //            && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
        //            && codes[i + 2].ToString() == "ldlen NULL")
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex].opcode = OpCodes.Nop;
        //        codes[startIndex].operand = null;
        //        codes[startIndex + 1].opcode = OpCodes.Nop;
        //        codes[startIndex + 1].operand = null;
        //        codes[startIndex + 2].opcode = OpCodes.Call;
        //        codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.SyncShipUnlockablesClientRpc_Transpiler could not use irl number of player in list.");
        //    }

        //    return codes.AsEnumerable();
        //}

        /// <summary>
        /// Patch for bypassing the annoying debug logs.
        /// </summary>
        /// <remarks>
        /// Todo: check for real problems in the sound sector for bots
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("RefreshPlayerVoicePlaybackObjects")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> RefreshPlayerVoicePlaybackObjects_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 6; i++)
            {
                if (codes[i].ToString().StartsWith("ldstr \"Refreshing voice playback objects. Number of voice objects found: {0}\"")//13
                    && codes[i + 6].ToString().StartsWith("call static void UnityEngine.Debug::Log(object message)")) //19
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                PatchesUtil.InsertIsBypass(codes, generator, startIndex, 7);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.RefreshPlayerVoicePlaybackObjects could not bypass debug log 1");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].ToString().StartsWith("ldstr \"Skipping player #{0} as they are not controlled or dead\"") //34
                    && codes[i + 4].ToString().StartsWith("call static void UnityEngine.Debug::Log(object message)")) //38
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                PatchesUtil.InsertIsBypass(codes, generator, startIndex, 6);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.RefreshPlayerVoicePlaybackObjects could not bypass debug log 2");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 6; i++)
            {
                if (codes[i].ToString().StartsWith("ldstr \"Found a match for voice object") // 109
                    && codes[i + 6].ToString().StartsWith("call static void UnityEngine.Debug::Log(object message)")) // 115
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                PatchesUtil.InsertIsBypass(codes, generator, startIndex, 7);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.RefreshPlayerVoicePlaybackObjects could not bypass debug log 3");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 30; i++)
            {
                if (codes[i].ToString().StartsWith("ldstr \"player voice chat audiosource:") // 142
                    && codes[i + 30].ToString().StartsWith("call static void UnityEngine.Debug::Log(object message)")) // 172
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                PatchesUtil.InsertIsBypass(codes, generator, startIndex, 31);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.RefreshPlayerVoicePlaybackObjects could not bypass debug log 4");
            }

            // ----------------------------------------------------------------------
            //for (var i = 0; i < codes.Count - 2; i++)
            //{
            //    if (codes[i].ToString().StartsWith("ldarg.0 NULL") //189
            //        && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
            //        && codes[i + 2].ToString() == "ldlen NULL")
            //    {
            //        startIndex = i;
            //        break;
            //    }
            //}
            //if (startIndex > -1)
            //{
            //    codes[startIndex].opcode = OpCodes.Nop;
            //    codes[startIndex].operand = null;
            //    codes[startIndex + 1].opcode = OpCodes.Nop;
            //    codes[startIndex + 1].operand = null;
            //    codes[startIndex + 2].opcode = OpCodes.Call;
            //    codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
            //    startIndex = -1;
            //}
            //else
            //{
            //    Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.RefreshPlayerVoicePlaybackObjects could not change limit of for loop to only real players");
            //}

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Check only real players not bots
        /// </summary>
        /// <remarks>
        /// We already do our own custom Voice effects for bots, so we don't need to call the original method.
        /// They also spam the debug log with errors about missing audio sources.
        /// </remarks>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("UpdatePlayerVoiceEffects")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> UpdatePlayerVoiceEffects_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Unity Object Equality Method
            MethodInfo opEqualityMethod = AccessTools.Method(typeof(UnityEngine.Object), "op_Equality");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_1 //282
                    && codes[i + 1].Calls(getGameNetworkManagerInstance) //284
                    && codes[i + 2].LoadsField(localPlayerControllerField)
                    && codes[i + 3].Calls(opEqualityMethod)
                    && (codes[i + 4].opcode == OpCodes.Brtrue 
                        || codes[i + 4].opcode == OpCodes.Brtrue_S))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Remove the five instructions: OpCodes.Ldloc_1, GameNetworkManager.Instance, localPlayerController, op_Equality, and BranchTrue/BranchTrue_S
                List<Label> labels = codes[startIndex].labels;
                object orginalBranchTarget = codes[startIndex + 4].operand;
                codes.RemoveRange(startIndex, 5);

                // Insert the new instruction to call the replacement method.
                List<CodeInstruction> codesToReplace = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_1).WithLabels(labels), // Load the local variable (playerControllerB)
                    new CodeInstruction(OpCodes.Call, PatchesUtil.IsPlayerLocalOrLethalBotMethod), // Call our replacement method
                    new CodeInstruction(OpCodes.Brtrue, orginalBranchTarget) // Branch to the original target if true
                };
                
                codes.InsertRange(startIndex, codesToReplace);
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.UpdatePlayerVoiceEffects_Transpiler could not change loop to only real players");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Check only real players not bots
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        //[HarmonyPatch("ResetShipFurniture")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> ResetShipFurniture_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 3; i++)
        //    {
        //        if (codes[i].ToString().StartsWith("ldarg.0 NULL") //176
        //            && codes[i + 1].ToString() == "ldfld GameNetcodeStuff.PlayerControllerB[] StartOfRound::allPlayerScripts"
        //            && codes[i + 2].ToString() == "ldlen NULL")
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex].opcode = OpCodes.Nop;
        //        codes[startIndex].operand = null;
        //        codes[startIndex + 1].opcode = OpCodes.Nop;
        //        codes[startIndex + 1].operand = null;
        //        codes[startIndex + 2].opcode = OpCodes.Call;
        //        codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogError($"LethalBot.Patches.NpcPatches.PlayerControllerBPatch.ResetShipFurniture_Transpiler could not use irl number of player in list.");
        //    }

        //    return codes.AsEnumerable();
        //}

        //[HarmonyPatch("OnClientConnect")]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> OnClientConnect_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    if (DebugConst.TEST_MORE_THAN_X_PLAYER_BYPASS)
        //    {
        //        // ----------------------------------------------------------------------
        //        for (var i = 0; i < codes.Count - 11; i++)
        //        {
        //            if (codes[i].ToString().StartsWith("ldc.i4.1 NULL") // 24
        //                && codes[i + 5].ToString().StartsWith("callvirt virtual bool System.Collections.Generic.List<int>::Contains") // 29
        //                && codes[i + 11].ToString() == "ldc.i4.1 NULL")// 35
        //            {
        //                startIndex = i;
        //                break;
        //            }
        //        }
        //        if (startIndex > -1)
        //        {
        //            codes[startIndex].opcode = OpCodes.Ldc_I4_3;
        //            codes[startIndex].operand = null;
        //            startIndex = -1;
        //        }
        //        else
        //        {
        //            Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.OnClientConnect_Transpiler could not test with making the 2nd player the nth");
        //        }
        //    }

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 2; i++)
        //    {
        //        if (codes[i].ToString() == "ldarg.0 NULL"
        //            && codes[i + 1].ToString() == "ldfld UnityEngine.GameObject[] StartOfRound::allPlayerObjects"
        //            && codes[i + 2].ToString() == "ldlen NULL")
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        codes[startIndex].opcode = OpCodes.Nop;
        //        codes[startIndex].operand = null;
        //        codes[startIndex + 1].opcode = OpCodes.Nop;
        //        codes[startIndex + 1].operand = null;
        //        codes[startIndex + 2].opcode = OpCodes.Call;
        //        codes[startIndex + 2].operand = PatchesUtil.IndexBeginOfInternsMethod;
        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogError($"LethalBot.Patches.GameEnginePatches.StartOfRoundPatch.OnClientConnect_Transpiler could not limit init of list2");
        //    }

        //    return codes.AsEnumerable();
        //}

        #endregion

        /// <summary>
        /// Patch for sync the info from the save from the server to the client (who does not load the save file)
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("OnPlayerConnectedClientRpc")]
        [HarmonyPostfix]
        static void OnPlayerConnectedClientRpc_PostFix(StartOfRound __instance, ulong clientId)
        {
            // Sync save file
            if (!__instance.IsServer 
                && !__instance.IsHost
                && __instance.NetworkManager.LocalClientId == clientId)
            {
                LethalBotManager.Instance.SyncLoadedJsonLoadoutsServerRpc(clientId);
                LethalBotManager.Instance.SyncLoadedJsonIdentitiesServerRpc(clientId);
                SaveManager.Instance.SyncCurrentValuesServerRpc(clientId);
            }
        }

        [HarmonyPatch("LateUpdate")]
        [HarmonyPostfix]
        static void LateUpdate_PostFix(StartOfRound __instance)
        {
            /*if (!__instance.inShipPhase 
                && __instance.shipDoorsEnabled 
                && !__instance.suckingPlayersOutOfShip)
            {
                LethalBotManager.Instance.SetLethalBotsInElevatorLateUpdate(Time.deltaTime);
            }*/

            LethalBotManager.Instance.UpdateOwnershipOfBotInventoryServer(Time.deltaTime);
        }

        /// <summary>
        /// Removes dupcation of event triggering when quitting to main menu and coming back
        /// </summary>
        [HarmonyPatch("OnDisable")]
        [HarmonyPostfix]
        static void OnDisable_Postfix()
        {
            InputManager.Instance.RemoveEventHandlers();
        }

        /// <summary>
        /// Override for when a player is kick to check if it's a bot
        /// since the base game does not handle that case.
        /// </summary>
        /// <param name="playerObjToKick"></param>
        [HarmonyPatch("KickPlayer")]
        [HarmonyPrefix]
        static bool KickPlayer_Prefix(int playerObjToKick)
        {
            LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(playerObjToKick);
            if (lethalBot != null)
            {
                // It's a bot, handle the kick
                LethalBotManager.Instance.OnLethalBotKicked(lethalBot);
                return false; // Skip original method
            }
            return true; // Continue with original method
        }

        [HarmonyPatch("UpdatePlayerVoiceEffects")]
        [HarmonyPostfix]
        static void UpdatePlayerVoiceEffects_PostFix()
        {
            LethalBotManager.Instance.UpdateAllLethalBotsVoiceEffects();
        }

        [HarmonyPatch("FirePlayersAfterDeadlineClientRpc")]
        [HarmonyPostfix]
        static void FirePlayersAfterDeadlineClientRpc_PostFix()
        {
            // Reset after fired
            LethalBotManager.Instance.ResetIdentities();
        }
    }
}
