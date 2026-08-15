using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using LethalBots.Utils;
using LethalBots.Utils.Helpers.VehicleHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    /// <summary>
    /// Patch for <c>VehicleController</c>
    /// </summary>
    [HarmonyPatch(typeof(VehicleController))]
    public class VehicleControllerPatch
    {
        /// <summary>
        /// HACKHACK: Postfixes are not called if the method throws an exception.
        /// Zeekerss has some kind of error in here that causes it to throw an exception.
        /// </summary>
        /// <param name="__exception"></param>
        /// <returns></returns>
        [HarmonyPatch("Start")]
        [HarmonyFinalizer]
        static Exception Start_Finalizer(Exception __exception)
        {
            // Run our code
            LethalBotManager.Instance.VehicleHasLanded();
            return __exception; // Let the original exception propagate!
        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_PostFix()
        {
            // Run our code
            LethalBotManager.Instance.VehicleHasLanded();
        }

        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            // Target property: localPlayerInControl
            FieldInfo localPlayerInControlField = AccessTools.Field(typeof(VehicleController), "localPlayerInControl");
            MethodInfo isOwnerGetter = AccessTools.PropertyGetter(typeof(NetworkBehaviour), "IsOwner");
            MethodInfo setCarEffectsMethod = AccessTools.Method(typeof(VehicleController), "SetCarEffects");
            FieldInfo steeringAnimValueField = AccessTools.Field(typeof(VehicleController), "steeringAnimValue");

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 9; i++)
            {
                // NOTE: We cannot access the fields of the coroutine class, we must manually find them instead!
                if (codes[i].IsLdarg(0)
                    && codes[i + 1].LoadsField(localPlayerInControlField)
                    && (codes[i + 2].opcode == OpCodes.Brfalse || codes[i + 2].opcode == OpCodes.Brfalse_S)
                    && codes[i + 3].IsLdarg(0)
                    && codes[i + 4].Calls(isOwnerGetter)
                    && (codes[i + 5].opcode == OpCodes.Brtrue || codes[i + 5].opcode == OpCodes.Brtrue_S)
                    && codes[i + 6].IsLdarg(0)
                    && codes[i + 7].IsLdarg(0)
                    && codes[i + 8].LoadsField(steeringAnimValueField)
                    && codes[i + 9].Calls(setCarEffectsMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the localPlayerInControl check with our own method that checks if the player is the local player or a lethal bot driver
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = AccessTools.Method(typeof(VehicleControllerPatch), nameof(IsLocalPlayerOrLethalBotDriver));
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.VehicleControllerPatch.Update_Transpiler could not allow bots to control the vehicle");
            }

            return codes.AsEnumerable();
        }

        private static bool IsLocalPlayerOrLethalBotDriver(VehicleController vehicleController)
        {
            return vehicleController.localPlayerInControl || LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(vehicleController.currentDriver);
        }

        [HarmonyPatch("OnPassengerExit")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OnPassengerExit_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Create passenger local
            var playerLocal = generator.DeclareLocal(typeof(PlayerControllerB));

            // Target property: localPlayerInControl
            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");
            FieldInfo currentPassengerField = AccessTools.Field(typeof(VehicleController), "currentPassenger");
            MethodInfo setVehicleCollisionForPlayerMethod = AccessTools.Method(typeof(VehicleController), "SetVehicleCollisionForPlayer");

            // ----------------------------------------------------------------------
            // Insert new field call so we can store the currentPassenger variable so we can use the passenger and not just the local player
            // We do this since OnPassengerExit clears the currentPassenger attribute making it impossible to check who is leaving the vehicle at this point
            List<CodeInstruction> codesToAdd = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), // Load this
                new CodeInstruction(OpCodes.Ldfld, currentPassengerField), // Load this.playerHeldBy
                new CodeInstruction(OpCodes.Stloc, playerLocal) // Store in our local variable
            };
            codes.InsertRange(startIndex, codesToAdd);
            startIndex = -1;

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 2; i++)
            {
                // Replace the SetVehicleCollisionForPlayer call with the current passenger
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField)
                    && codes[i + 2].Calls(setVehicleCollisionForPlayerMethod))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                // Replace the local player call to use the current passenger if its not null
                codes[startIndex].opcode = OpCodes.Ldloc;
                codes[startIndex].operand = playerLocal;
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = AccessTools.Method(typeof(VehicleControllerPatch), nameof(GetCurrentPassenger));
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.VehicleControllerPatch.OnPassengerExit could not replace local player with current passenger");
            }

            return codes.AsEnumerable();
        }

        private static PlayerControllerB GetCurrentPassenger(PlayerControllerB? currentPassenger)
        {
            return currentPassenger != null ? currentPassenger : GameNetworkManager.Instance.localPlayerController;
        }

        [HarmonyPatch("TryIgnition", MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TryIgnition_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var patched = false;
            var timesPatched = 0;
            var codes = new List<CodeInstruction>(instructions);

            // Find Interator Type
            Type iteratorType = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(VehicleController), "TryIgnition")).DeclaringType;

            // Find the "this" field of the iterator class, which is a reference to the VehicleController instance
            FieldInfo cachedThisField = AccessTools.GetDeclaredFields(iteratorType).FirstOrDefault(f => f.FieldType == typeof(VehicleController) && f.Name.Contains("this"));
            //Plugin.LogInfo($"Found this field: {cachedThisField} name {cachedThisField.Name}");

            // Target property: GameNetworkManager.Instance.localPlayerController
            MethodInfo getGameNetworkManagerInstance = AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance");
            FieldInfo localPlayerControllerField = AccessTools.Field(typeof(GameNetworkManager), "localPlayerController");

            // Target method: VehicleControllerPatch.GetDriverPlayer
            MethodInfo getDriverPlayer = AccessTools.Method(typeof(VehicleControllerPatch), nameof(GetDriverPlayer));

            // ------------------------------------------------
            for (var i = 0; i < codes.Count - 1; i++)
            {
                // NOTE: We cannot access the fields of the coroutine class, we must manually find them instead!
                if (codes[i].Calls(getGameNetworkManagerInstance)
                    && codes[i + 1].LoadsField(localPlayerControllerField))
                {
                    codes[i].opcode = OpCodes.Ldarg_0; // Load the iterator class instance
                    codes[i].operand = null; // Clear the operand since we are no longer calling GameNetworkManager.Instance
                    codes[i + 1].opcode = OpCodes.Ldfld; // Load the VehicleController instance from the iterator class
                    codes[i + 1].operand = cachedThisField;
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, getDriverPlayer)); // Call our method to get the driver player
                    i += 2; // Skip the next two instructions since we replaced them
                    patched = true;
                    timesPatched++;
                }
            }

            if (!patched)
            {
                Plugin.LogError($"LethalBot.Patches.MapPatches.VehicleControllerPatch.TryIgnition_Transpiler could not replace local player calls with driver calls");
            }
            else
            {
                Plugin.LogDebug($"Replaced local player calls with driver {timesPatched} times!");
            }

            return codes.AsEnumerable();
        }

        private static PlayerControllerB GetDriverPlayer(VehicleController vehicle)
        {
            //Plugin.LogInfo($"Vehicle: {vehicle} with driver {vehicle.currentDriver}");
            PlayerControllerB currentDriver = vehicle.currentDriver;
            if (currentDriver != null &&
                LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(currentDriver))
            {
                return currentDriver;
            }

            return GameNetworkManager.Instance.localPlayerController;
        }

        [HarmonyPatch("GetVehicleInput")]
        [HarmonyPrefix]
        static bool GetVehicleInput_Prefix(VehicleController __instance)
        {
            if (__instance.localPlayerInControl)
            {
                return true; // Run original method
            }
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(__instance.currentDriver);
            if (lethalBotAI != null)
            {
                VehicleInputHelper vehicleInput = lethalBotAI.NpcController.vehicleInput;
                float pedalInput = vehicleInput.Brake > 0.1f ? -vehicleInput.Brake : vehicleInput.ThrottleMagnitude;
                __instance.moveInputVector = new Vector2(vehicleInput.GetActualSteering(), pedalInput);
                float num = __instance.steeringWheelTurnSpeed;
                __instance.steeringInput = Mathf.Clamp(__instance.steeringInput + __instance.moveInputVector.x * num * Time.deltaTime, -3f, 3f);
                if (Mathf.Abs(__instance.moveInputVector.x) > 0.1f)
                {
                    __instance.steeringWheelAudio.volume = Mathf.Lerp(__instance.steeringWheelAudio.volume, Mathf.Abs(__instance.moveInputVector.x), 5f * Time.deltaTime);
                }
                else
                {
                    __instance.steeringWheelAudio.volume = Mathf.Lerp(__instance.steeringWheelAudio.volume, 0f, 5f * Time.deltaTime);
                }
                __instance.steeringAnimValue = __instance.moveInputVector.x;
                __instance.drivePedalPressed = pedalInput > 0.1f;
                __instance.brakePedalPressed = pedalInput < -0.1f;
                return false; // Skip original method
            }
            return true; // Run original method
        }

        [HarmonyPatch("LoseControlOfVehicle")]
        [HarmonyPostfix]
        static void LoseControlOfVehicle_Postfix(VehicleController __instance)
        {
            // Only do this for bots
            if (LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(__instance.currentDriver))
            {
                // Make the bot lose control of the crusier
                __instance.drivePedalPressed = false;
                __instance.brakePedalPressed = false;
                __instance.currentDriver = null;
                __instance.steeringAnimValue = 0f;
                __instance.keyIsInDriverHand = false;
                __instance.CancelIgnitionAnimation();
                __instance.chanceToStartIgnition = 20f;
                if (!__instance.testingVehicleInEditor)
                {
                    __instance.RemovePlayerControlOfVehicleServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, __instance.transform.position, __instance.transform.rotation, __instance.ignitionStarted);
                }
            }
        }

        /// <summary>
        /// Patch for damaging the bots owned by client in vehicle
        /// </summary>
        [HarmonyPatch("DamagePlayerInVehicle")]
        [HarmonyPostfix]
        static void DamagePlayerInVehicle_PostFix(VehicleController __instance,
                                                  Vector3 vel,
                                                  float magnitude)
        {
            PlayerControllerB currentDriver = GetDriverPlayer(__instance);
            PlayerControllerB lethalBotController;
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI? lethalBotAI = lethalBotAIs[i];
                lethalBotController = lethalBotAI.NpcController.Npc;

                if (currentDriver != lethalBotController)
                {
                    if (__instance.physicsRegion.physicsTransform == lethalBotController.physicsParent
                        && lethalBotController.overridePhysicsParent == null)
                    {
                        lethalBotController.DamagePlayer(10, hasDamageSFX: true, callRPC: true, CauseOfDeath.Inertia, 0, false, vel);
                        lethalBotController.externalForceAutoFade += vel;
                    }
                    return;
                }

                if (magnitude > 28f)
                {
                    lethalBotController.KillPlayer(vel, spawnBody: true, CauseOfDeath.Inertia, 0, __instance.transform.up * 0.77f);
                    return;
                }

                if (magnitude <= 24f)
                {
                    lethalBotController.DamagePlayer(30, hasDamageSFX: true, callRPC: true, CauseOfDeath.Inertia, 0, false, vel);
                    return;
                }

                if (lethalBotController.health < 20)
                {
                    lethalBotController.KillPlayer(vel, spawnBody: true, CauseOfDeath.Inertia, 0, __instance.transform.up * 0.77f);
                    return;
                }
                lethalBotController.DamagePlayer(40, hasDamageSFX: true, callRPC: true, CauseOfDeath.Inertia, 0, false, vel);
            }
        }

        [HarmonyPatch("CarReactToObstacle")]
        [HarmonyPrefix]
        static bool CarReactToObstacle_Prefix(ref bool __result, EnemyAI enemyScript = null!)
        {
            // Fixes bots shoving the car for some reason
            if (enemyScript != null 
                && enemyScript is LethalBotAI)
            {
                __result = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Patch for killing bot when car is destroyed
        /// </summary>
        /*[HarmonyPatch("DestroyCar")]
        [HarmonyPostfix]
        static void DestroyCar_PostFix()
        {
            foreach (LethalBotAI lethalBotAI in LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal())
            {
                Plugin.LogDebug($"DestroyCar Killing bot #{lethalBotAI.BotId}");
                lethalBotAI.NpcController.Npc.KillPlayer(Vector3.up * 27f + 20f * Random.insideUnitSphere, spawnBody: true, CauseOfDeath.Blast, 6, Vector3.up * 1.5f);
            }
        }*/
    }
}
