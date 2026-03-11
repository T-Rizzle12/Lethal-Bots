using BunkbedRevive;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.GameEnginePatches;
using System;

namespace LethalBots.Patches.ModPatches.BunkbedRevive
{
    [HarmonyPatch(typeof(BunkbedController))]
    public class BunkbedControllerPatch
    {
        [HarmonyPatch("OnInteract")]
        [HarmonyPrefix]
        static bool OnInteract_PreFix(InteractTrigger ___interactTrigger)
        {
            RagdollGrabbableObject? ragdollGrabbableObject = GetHeldBody_ReversePatch(BunkbedController.Instance);
            if (ragdollGrabbableObject == null)
            {
                return true;
            }

            int playerClientId = (int)ragdollGrabbableObject.ragdoll.playerScript.playerClientId;
            string name = ragdollGrabbableObject.ragdoll.gameObject.GetComponentInChildren<ScanNodeProperties>().headerText;
            LethalBotIdentity? lethalBotIdentity = IdentityManager.Instance.FindIdentityFromBodyName(name);
            if (lethalBotIdentity == null)
            {
                return true;
            }

            // Get the same logic as the mod at the beginning
            if (lethalBotIdentity.Alive)
            {
                Plugin.LogError($"BunkbedRevive with LethalBot: error when trying to revive bot \"{lethalBotIdentity.Name}\", bot is already alive! do nothing more");
                return false;
            }

            int reviveCost = BunkbedController.GetReviveCost();
            if (TerminalManager.Instance.GetTerminal().groupCredits < reviveCost)
            {
                HUDManagerPatch.DisplayGlobalNotification_ReversePatch(HUDManager.Instance, "Not enough credits");
                ___interactTrigger.StopInteraction();
                return false;
            }
            if (!BunkbedController.CanRevive(ragdollGrabbableObject.bodyID.Value, logStuff: true))
            {
                HUDManagerPatch.DisplayGlobalNotification_ReversePatch(HUDManager.Instance, "Can't Revive");
                ___interactTrigger.StopInteraction();
                return false;
            }
            Terminal terminalScript = TerminalManager.Instance.GetTerminal();
            terminalScript.groupCredits -= reviveCost;
            LethalBotManager.Instance.SyncGroupCreditsForNotOwnerTerminalServerRpc(terminalScript.groupCredits, terminalScript.numberOfItemsInDropship);

            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = StartOfRoundPatch.GetPlayerSpawnPosition_ReversePatch(StartOfRound.Instance, playerClientId, simpleTeleport: false),
                                                                YRot = 0,
                                                                IsOutside = true,
                                                                IndexNextPlayerObject = playerClientId
                                                            });
            LethalBotManager.Instance.UpdateReviveCountServerRpc(playerClientId);
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is spawned
            StartOfRound.Instance.livingPlayers++;
            GameNetworkManager.Instance.localPlayerController?.DespawnHeldObject();

            HUDManagerPatch.DisplayGlobalNotification_ReversePatch(HUDManager.Instance, $"{lethalBotIdentity.Name} has been revived");
            return false;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Update_PostFix(InteractTrigger ___interactTrigger)
        {
            if (StartOfRound.Instance == null 
                || GameNetworkManager.Instance == null 
                || GameNetworkManager.Instance.localPlayerController == null)
            {
                return;
            }

            RagdollGrabbableObject? ragdollGrabbableObject = GetHeldBody_ReversePatch(BunkbedController.Instance);
            if (ragdollGrabbableObject == null)
            {
                return;
            }

            if (ragdollGrabbableObject.ragdoll != null
                && LethalBotManager.Instance.IsPlayerLethalBot(ragdollGrabbableObject.ragdoll.playerScript))
            {
                ___interactTrigger.interactable = true;
            }
        }

        [HarmonyPatch("GetHeldBody")]
        [HarmonyReversePatch(type: HarmonyReversePatchType.Snapshot)]
        [HarmonyPriority(Priority.Last)]
        public static RagdollGrabbableObject? GetHeldBody_ReversePatch(object instance) => throw new NotImplementedException("Stub LethalBot.Patches.ModPatches.BunkbedRevive.BunkbedControllerPatch.GetHeldBody_ReversePatch");

    }
}
