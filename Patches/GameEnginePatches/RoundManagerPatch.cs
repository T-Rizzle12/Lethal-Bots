using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using LethalBots.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.AI.Navigation;
using Unity.Netcode;

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
        /// This disables the <see cref="NavMeshSurface"/> object used by the bots in orbit
        /// </summary>
        [HarmonyPatch("BakeDunGenNavMesh")]
        [HarmonyPrefix]
        static void BakeDunGenNavMesh_Prefix()
        {
            Plugin.LogDebug("Disabling ship NavMeshSurface object. Reason: Landing on moon to gather scrap.");
            LethalBotManager.Instance.DisableShipNavMesh();
        }

        /// <summary>
        /// This disables the <see cref="NavMeshSurface"/> object used by the bots in orbit
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPatch("GenerateNewLevelClientRpc")]
        [HarmonyPrefix]
        static void GenerateNewLevelClientRpc_Prefix(RoundManager __instance)
        {
            // BakeDunGenNavMesh isn't called for moons that don't spawn enemies and scrap
            // We work around this by just disabling the mesh here
            if (!__instance.currentLevel.spawnEnemiesAndScrap)
            {
                Plugin.LogDebug("Disabling ship NavMeshSurface object. Reason: At Company Building.");
                LethalBotManager.Instance.DisableShipNavMesh();
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
