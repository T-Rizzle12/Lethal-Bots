using DunGen;
using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Patches.ModPatches.PathfindingLib;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using NavMeshLib;
using NavMeshLib.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(RoundManager))]
    public class RoundManagerPatch
    {
        // TODO: Change this to the bot's custom agent type once I add it!
        private static readonly List<int> affectedAgents = new List<int>() { -1 };

        /// <summary>
        /// Patch to mark quicksand as well quicksand
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("SpawnOutsideHazards")]
        [HarmonyPostfix]
        static void SpawnOutsideHazards_Postfix(RoundManager __instance)
        {
            // Filter out the water quicksand triggers since those are handled by safe path.
            Vector3 colliderBuffer = new Vector3(0.8f, 0.2f, 0.8f); // Add a slight buffer to keep the bots from walking too close!
            List<CustomNavMeshModifier> customModifiers = new List<CustomNavMeshModifier>();
            QuicksandTrigger[] quicksandArray = Object.FindObjectsByType<QuicksandTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            quicksandArray = quicksandArray.Where(quicksand => quicksand != null && !quicksand.isWater).ToArray();
            if (quicksandArray.Length > 0)
            {
                // Log what we are about to do!
                Plugin.LogInfo("Adding NavMeshModifierVolume to the quicksand objects to override its path cost for bots!");

                //List<NavMeshModifierVolume> modifiers = new List<NavMeshModifierVolume>();
                for (int i = 0; i < quicksandArray.Length; i++)
                {
                    // Make sure its valid
                    QuicksandTrigger? quicksand = quicksandArray[i];
                    if (quicksand == null || quicksand.isWater) continue;

                    // Change the bounds to contain where the quicksand is.
                    Collider[] colliders = quicksand.gameObject.GetComponentsInChildren<Collider>();
                    if (colliders.Length == 0) continue;

                    // NavMeshLib will create our proxy GameObject and will handle setting the layermask and parenting
                    CustomNavMeshModifier customNavMeshModifier = CustomNavMeshModifier.CreateFromColliders(quicksand.gameObject,
                                                                                                            colliders,
                                                                                                            Const.LETHAL_BOT_QUICKSAND_NAVAREA,
                                                                                                            affectedAgents,
                                                                                                            colliderBuffer,
                                                                                                            NavMeshLib.Enums.ModifierParent.MoonEnvironment);
                    customNavMeshModifier.navMeshUpdater.rebuildType = NavMeshLib.Enums.RebuildType.OutsideSurfaces;
                    customNavMeshModifier.InitializeCustomModifier();
                    //navMeshModifierGameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Set back to default GameObject position and rotation.......InitializeCustomModifier overrides our GameObject's position, this allows me to "revert" its change
                    customModifiers.Add(customNavMeshModifier);
                    Plugin.LogInfo("Added NavMeshModifierVolume to quicksand.");
                }

            }

            // Go through all spawned bridges and mark them as such on the NavMesh
            BridgeTrigger[] bridgeTriggers = Object.FindObjectsByType<BridgeTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < bridgeTriggers.Length; i++)
            {
                // Make sure its valid
                BridgeTrigger? bridgeTrigger = bridgeTriggers[i];
                if (bridgeTrigger == null) continue;

                // Change the bounds to contain where the bridge is.
                Collider[] colliders = bridgeTrigger.gameObject.GetComponents<Collider>();
                if (colliders.Length == 0) continue;

                // NavMeshLib will create our proxy GameObject and will handle setting the layermask and parenting
                CustomNavMeshModifier customNavMeshModifier = CustomNavMeshModifier.CreateFromColliders(bridgeTrigger.gameObject,
                                                                                                        colliders,
                                                                                                        Const.LETHAL_BOT_BRIDGE_NAVAREA,
                                                                                                        affectedAgents,
                                                                                                        null,
                                                                                                        NavMeshLib.Enums.ModifierParent.MoonEnvironment);
                customNavMeshModifier.navMeshUpdater.rebuildType = NavMeshLib.Enums.RebuildType.OutsideSurfaces;
                customNavMeshModifier.InitializeCustomModifier();
                //navMeshModifierGameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Set back to default GameObject position and rotation.......InitializeCustomModifier overrides our GameObject's position, this allows me to "revert" its change
                customModifiers.Add(customNavMeshModifier);
                Plugin.LogInfo("Added NavMeshModifierVolume to bridge trigger.");
            }

            // Since we are adding NavMeshModifiers, tell the NavMesh to update
            if (customModifiers.Count > 0)
            {
                GameObject outsideNavMesh = GameObject.FindGameObjectWithTag("OutsideLevelNavMesh");
                if (outsideNavMesh == null)
                {
                    outsideNavMesh = GameObject.Find("CompanyBuildingNavMesh"); // NavMeshInCompanyRedux support!
                }
                Plugin.LogInfo($"Updating Exterior NavMesh to apply {customModifiers.Count} new modifiers!");
                NavMeshUtil.RebakeExteriorNavMesh(environmentObject: outsideNavMesh);
            }
        }

        [HarmonyPatch("SpawnMapObjects")]
        [HarmonyPostfix]
        static void SpawnMapObjects_Postfix(RoundManager __instance, List<NavMeshSurface> ___fullBakeSurfaces)
        {
            if (__instance.currentLevel.indoorMapHazards.Length == 0)
            {
                return;
            }

            Vector3 colliderBuffer = new Vector3(0.8f, 0.8f, 0.8f); // Add a slight buffer to keep the bots from walking too close!
            List<CustomNavMeshModifier> customModifiers = new List<CustomNavMeshModifier>();
            Landmine[] landmines = Object.FindObjectsByType<Landmine>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (landmines.Length == 0)
            {
                return;
            }

            // Log what we are about to do!
            Plugin.LogInfo("Adding NavMeshModifierVolume to the landmine objects to override its path cost for bots!");
            //HUDManager.Instance.DisplayTip("Landmines spawned!", "Check the Logs!");

            // Go through each landmine!
            for (int i = 0; i < landmines.Length; i++)
            {
                Landmine? landmine = landmines[i];
                if (landmine != null)
                {
                    // Check if this landmine is indoors or outdoors
                    bool isOutside = landmine.transform.position.y >= -80f;

                    // Find the landmine's colliders
                    BoxCollider[] boxColliders = landmine.gameObject.GetComponentsInChildren<BoxCollider>();
                    if (boxColliders.Length == 0) continue;

                    // NavMeshLib will create our proxy GameObject and will handle setting the layermask and parenting
                    CustomNavMeshModifier customNavMeshModifier = CustomNavMeshModifier.CreateFromColliders(landmine.gameObject, // TODO: Just edit the prefab instead of doing this here
                                                                                                            boxColliders,
                                                                                                            Const.LETHAL_BOT_LANDMINE_NAVAREA,
                                                                                                            affectedAgents,
                                                                                                            colliderBuffer,
                                                                                                            isOutside ? NavMeshLib.Enums.ModifierParent.MoonEnvironment : NavMeshLib.Enums.ModifierParent.Interior); // We consider modded cases where landmines can be outside
                    customNavMeshModifier.autoRebuildNavMeshOnMovement = true;
                    customNavMeshModifier.autoRebuildMoveThreshold = 0.2f;
                    customNavMeshModifier.navMeshUpdater.rebuildType = NavMeshLib.Enums.RebuildType.ActiveSurfaces;
                    customNavMeshModifier.InitializeCustomModifier();
                    customModifiers.Add(customNavMeshModifier);
                    Plugin.LogDebug("Added CustomNavMeshModifier to landmine.");
                }
            }

            // Don't update the mesh unless we have to
            if (customModifiers.Count > 0)
            {
                // Start the rebake!
                Plugin.LogInfo($"Updating NavMesh for all full bake surfaces in the dungeon to apply {customModifiers.Count} new modifiers!");
                NavMeshUtil.RebakeDunGenNavMesh();
            }
        }

        /// <summary>
        /// Make sure to include our custom quicksand mask to enemies!
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="enemyType"></param>
        /// <param name="supplyExistingMask"></param>
        /// <param name="__result"></param>
        [HarmonyPatch("GetLayermaskForEnemySizeLimit")]
        [HarmonyPostfix]
        static void GetLayermaskForEnemySizeLimit_Postfix(RoundManager __instance, EnemyType enemyType, bool supplyExistingMask, ref int __result)
        {
            if (supplyExistingMask)
            {
                int areaMaskToAdd = (1 << Const.LETHAL_BOT_QUICKSAND_NAVAREA) | (1 << Const.LETHAL_BOT_LANDMINE_NAVAREA) | (1 << Const.LETHAL_BOT_BRIDGE_NAVAREA);
                __result |= areaMaskToAdd;

                // Make sure only Lethal Bots path on the Lethal Bot Only areas
                int areaMaskLethalBotsOnly = 1 << Const.LETHAL_BOT_ONLY_NAVAREA;
                if (enemyType.enemyPrefab.TryGetComponent<LethalBotAI>(out _))
                {
                    __result |= areaMaskLethalBotsOnly;
                }
                else
                {
                    __result &= ~areaMaskLethalBotsOnly;
                }
            }
        }

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
        /// If this level generates a Navmesh, we need to disable the ships NavMesh now!
        /// </summary>
        [HarmonyPatch("BakeDunGenNavMesh")]
        [HarmonyPrefix]
        static void BakeDunGenNavMesh_PreFix()
        {
            // Disable the NavMesh before BakeDunGenNavMesh is called!
            LethalBotManager.Instance?.DisableShipNavMesh("Landing on a moon.");
        }

        //[HarmonyPatch("BakeDunGenNavMesh")]
        //[HarmonyTranspiler]
        //static IEnumerable<CodeInstruction> BakeDunGenNavMesh_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        //{
        //    var startIndex = -1;
        //    var codes = new List<CodeInstruction>(instructions);

        //    // Target property: array[i].thisNetworkObject.Despawn()
        //    MethodInfo despawnMethod = AccessTools.Method(typeof(NetworkObject), "Despawn");
        //    FieldInfo thisNetworkObjectField = AccessTools.Field(typeof(EnemyAI), "thisNetworkObject");

        //    // Target function
        //    MethodInfo shouldDespawnMethod = AccessTools.Method(typeof(RoundManagerPatch), "ShouldDespawn");

        //    // ----------------------------------------------------------------------
        //    for (var i = 0; i < codes.Count - 5; i++)
        //    {
        //        if (codes[i].opcode == OpCodes.Ldloc_0
        //            && codes[i + 1].opcode == OpCodes.Ldloc_3
        //            && codes[i + 2].opcode == OpCodes.Ldelem_Ref
        //            && codes[i + 3].LoadsField(thisNetworkObjectField)
        //            && codes[i + 4].opcode == OpCodes.Ldc_I4_1
        //            && codes[i + 5].Calls(despawnMethod))
        //        {
        //            startIndex = i;
        //            break;
        //        }
        //    }
        //    if (startIndex > -1)
        //    {
        //        // Insert a conditional branch (if not ShouldDespawn then skip calling NetworkObject.Despawn)
        //        //int endIndex = -1;
        //        //for (int j = startIndex; j < codes.Count; j++)
        //        //{
        //        //    if (codes[j].Calls(despawnMethod))
        //        //    {
        //        //        endIndex = j;
        //        //        break;
        //        //    }
        //        //}

        //        //// Fall back to constant endIndex
        //        //if (endIndex == -1)
        //        //{
        //        //    Plugin.LogError("Could not find despawn call!");
        //        //    endIndex = startIndex + 5;
        //        //}

        //        // Create the label to skip to!
        //        Label skipLabel = generator.DefineLabel();
        //        codes[startIndex + 6].labels.Add(skipLabel);
        //        //var nop = new CodeInstruction(OpCodes.Nop);
        //        //nop.labels.Add(skipLabel);
        //        //codes.Insert(endIndex + 1, nop);

        //        // Insert new method call to our ShouldDespawn method
        //        List<CodeInstruction> codesToAdd = new List<CodeInstruction>
        //        {
        //            new CodeInstruction(OpCodes.Ldloc_0), // Load array
        //            new CodeInstruction(OpCodes.Ldloc_3), // Load current loop index
        //            new CodeInstruction(OpCodes.Ldelem_Ref), // Load the EnemyAI reference
        //            new CodeInstruction(OpCodes.Call, shouldDespawnMethod), // Call method
        //            new CodeInstruction(OpCodes.Brfalse, skipLabel)
        //        };
        //        codes.InsertRange(startIndex, codesToAdd);

        //        startIndex = -1;
        //    }
        //    else
        //    {
        //        Plugin.LogWarning($"LethalBot.Patches.GameEnginePatches.BakeDunGenNavMesh_Transpiler could not skip interior NavMesh generation for Lethal Bot Cruiser NavMesh!");
        //    }

        //    return codes.AsEnumerable();
        //}

        /// <summary>
        /// Mark bots as finished loading the level as well
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("FinishGeneratingLevel")]
        [HarmonyPostfix]
        static void FinishGeneratingLevel_PostFix(RoundManager __instance)
        {
            // Only run this on the server
            if (__instance.IsServer || __instance.IsHost)
            { 
                LethalBotManager.Instance.MarkBotsAsGeneratedFloorDelayed(); 
            }

            // Update the dungeon for the newly added level!
            Dungeon dungeon = Object.FindObjectOfType<Dungeon>();
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotAIs();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI? lethalBotAI = lethalBotAIs[i];
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                if (lethalBotController != null 
                    && (lethalBotController.isPlayerControlled
                        || lethalBotController.isPlayerDead))
                {
                    DunGenTileTracker tileTracker = lethalBotAI.DunGenTileTracker;
                    tileTracker.SetDungeon(dungeon);
                    tileTracker.AdjacentTileDepth = __instance.dungeonFlowTypes[__instance.currentDungeonType].cullingTileDepth;
                }
            }
        }

        /// <summary>
        /// This spawns the bots right as the ship starts to land!
        /// </summary>
        [HarmonyPatch("FinishGeneratingNewLevelClientRpc")]
        [HarmonyPostfix]
        static void FinishGeneratingNewLevelClientRpc_PostFix(RoundManager __instance)
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
            LethalBotManager.Instance?.DisableShipNavMesh("Landing on a moon.");
            LethalBotManager.Instance?.SpawnLethalBotsAtShip();
            LethalBotManager.botAutoLeaveTimer.Start();
        }

        [HarmonyPatch("RefreshEnemiesList")]
        [HarmonyPostfix]
        static void RefreshEnemiesList_PostFix(RoundManager __instance)
        {
            // RefreshEnemiesList catches the bots since they use EnemyAI objects, this removes them from the list!
            int oldSpawnedEnemiesCount = __instance.SpawnedEnemies.Count;
            __instance.SpawnedEnemies.RemoveAll(enemy => enemy is LethalBotAI);
            __instance.numberOfEnemiesInScene = __instance.SpawnedEnemies.Count;
            Plugin.LogDebug($"Removed LethalBotAI objects from RoundManager.SpawnedEnemies. Old {oldSpawnedEnemiesCount} -> New {__instance.numberOfEnemiesInScene}");
        }

        [HarmonyPatch("UnloadSceneObjectsEarly")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> UnloadSceneObjectsEarly_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: array[i].thisNetworkObject.Despawn()
            MethodInfo despawnMethod = AccessTools.Method(typeof(NetworkObject), "Despawn");
            FieldInfo thisNetworkObjectField = AccessTools.Field(typeof(EnemyAI), "thisNetworkObject");

            // Target function
            MethodInfo shouldDespawnMethod = AccessTools.Method(typeof(RoundManagerPatch), "ShouldDespawn");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 5; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_0
                    && codes[i + 1].opcode == OpCodes.Ldloc_3
                    && codes[i + 2].opcode == OpCodes.Ldelem_Ref
                    && codes[i + 3].LoadsField(thisNetworkObjectField)
                    && codes[i + 4].opcode == OpCodes.Ldc_I4_1
                    && codes[i + 5].Calls(despawnMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Insert a conditional branch (if not ShouldDespawn then skip calling NetworkObject.Despawn)
                //int endIndex = -1;
                //for (int j = startIndex; j < codes.Count; j++)
                //{
                //    if (codes[j].Calls(despawnMethod))
                //    {
                //        endIndex = j;
                //        break;
                //    }
                //}

                //// Fall back to constant endIndex
                //if (endIndex == -1)
                //{
                //    Plugin.LogError("Could not find despawn call!");
                //    endIndex = startIndex + 5;
                //}

                // Create the label to skip to!
                Label skipLabel = generator.DefineLabel();
                codes[startIndex + 6].labels.Add(skipLabel);
                //var nop = new CodeInstruction(OpCodes.Nop);
                //nop.labels.Add(skipLabel);
                //codes.Insert(endIndex + 1, nop);

                // Insert new method call to our ShouldDespawn method
                List<CodeInstruction> codesToAdd = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_0), // Load array
                    new CodeInstruction(OpCodes.Ldloc_3), // Load current loop index
                    new CodeInstruction(OpCodes.Ldelem_Ref), // Load the EnemyAI reference
                    new CodeInstruction(OpCodes.Call, shouldDespawnMethod), // Call method
                    new CodeInstruction(OpCodes.Brfalse, skipLabel)
                };
                codes.InsertRange(startIndex, codesToAdd);

                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.GameEnginePatches.UnloadSceneObjectsEarly_Transpiler could not add custom check to block deletion of EnemyAI objects for Lethal Bots!");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Helper function made to skip the despawn call for LethalBotAIs!
        /// </summary>
        /// <param name="enemyAI"></param>
        /// <returns></returns>
        private static bool ShouldDespawn(EnemyAI enemyAI)
        {
            if (Plugin.Config.AllowBotsInOrbit.Value && enemyAI is LethalBotAI)
            {
                return false;
            }
            return true;
        }
    }
}
