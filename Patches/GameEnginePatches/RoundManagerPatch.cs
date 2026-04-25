using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(RoundManager))]
    public class RoundManagerPatch
    {
        //private static Coroutine? overrideQuicksandCoroutine = null;

        //[HarmonyPatch("Awake")]
        //[HarmonyPrefix]
        //static void Awake_Prefix(RoundManager __instance)
        //{
        //    // Override the NavArea for the Quicksand
        //    if (overrideQuicksandCoroutine != null)
        //    {
        //        __instance.StopCoroutine(overrideQuicksandCoroutine);
        //    }
        //    overrideQuicksandCoroutine = __instance.StartCoroutine(OverrideQuicksandPrefab());
        //}

        //private static IEnumerator OverrideQuicksandPrefab()
        //{
        //    // Override the NavArea for the Quicksand
        //    while (RoundManager.Instance == null || RoundManager.Instance.quicksandPrefab == null)
        //    {
        //        yield return null;
        //    }
        //    yield return null;

        //    // Log what we are about to do!
        //    Plugin.LogInfo("Adding NavMeshModifierVolume to the quicksand prefab to override its path cost for bots!");

        //    GameObject quicksandPrefab = RoundManager.Instance.quicksandPrefab;
        //    if (quicksandPrefab != null)
        //    {
        //        // Add the NavMeshVolume
        //        NavMeshModifierVolume navMeshModifier = quicksandPrefab.gameObject.GetComponent<NavMeshModifierVolume>() ?? quicksandPrefab.gameObject.AddComponent<NavMeshModifierVolume>();
        //        navMeshModifier.area = Const.LETHAL_BOT_QUICKSAND_NAVAREA;

        //        // Change the bounds to contain where the quicksand is.
        //        Bounds quicksandBounds = default;
        //        bool foundCollider = false;
        //        foreach (BoxCollider collider in quicksandPrefab.gameObject.GetComponentsInChildren<BoxCollider>())
        //        {
        //            if (collider != null)
        //            {
        //                // convert local box to world-ish space using transform
        //                Vector3 worldCenter = collider.transform.TransformPoint(collider.center);

        //                Vector3 worldSize = Vector3.Scale(collider.size, collider.transform.lossyScale);

        //                Bounds bounds = new Bounds(worldCenter, worldSize);
        //                if (!foundCollider)
        //                {
        //                    quicksandBounds = bounds;
        //                    foundCollider = true;
        //                }
        //                else
        //                {
        //                    quicksandBounds.Encapsulate(bounds);
        //                }
        //            }
        //        }

        //        // Update the center and size!
        //        if (foundCollider)
        //        {
        //            navMeshModifier.center = quicksandPrefab.transform.InverseTransformPoint(quicksandBounds.center);
        //            navMeshModifier.size = quicksandBounds.size;
        //            Plugin.LogInfo($"Added NavMeshModifierVolume to quicksand prefab with center {quicksandBounds.center} and size {quicksandBounds.size}");
        //        }
        //        else
        //        {
        //            Plugin.LogWarning("Added NavMeshModifierVolume to quicksand prefab, but failed to find collider's center and size. This may cause issues!");
        //        }
        //    }
        //    overrideQuicksandCoroutine = null;
        //}

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
                LethalBotManager.Instance?.MarkBotsAsGeneratedFloorDelayed();
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
            LethalBotManager.Instance?.DisableShipNavMesh("Landing on a moon.");
            LethalBotManager.Instance?.SpawnLethalBotsAtShip();
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
