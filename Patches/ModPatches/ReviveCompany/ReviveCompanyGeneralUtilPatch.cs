using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using OPJosMod.ReviveCompany;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.ReviveCompany
{
    [HarmonyPatch(typeof(GeneralUtil))]
    public class ReviveCompanyGeneralUtilPatch
    {
        [HarmonyPatch("RevivePlayer")]
        [HarmonyPrefix]
        static bool RevivePlayer_Prefix(int playerId)
        {
            if (!LethalBotManager.Instance.IsPlayerLethalBot(playerId))
            {
                return true;
            }

            // Identity and body are not sync, need to find the identity to revive not the body
            RagdollGrabbableObject? ragdollGrabbableObjectToRevive = StartOfRound.Instance.allPlayerScripts[playerId]?.deadBody?.grabBodyObject as RagdollGrabbableObject;
            if (ragdollGrabbableObjectToRevive == null)
            {
                Plugin.LogError($"Revive company with LethalBot: error when trying to revive bot, could not find body.");
                return false;
            }

            string name = ragdollGrabbableObjectToRevive.ragdoll.gameObject.GetComponentInChildren<ScanNodeProperties>().headerText;
            LethalBotIdentity? lethalBotIdentity = IdentityManager.Instance.FindIdentityFromBodyName(name);
            if (lethalBotIdentity == null)
            {
                return true;
            }

            // Get the same logic as the mod at the beginning
            if (lethalBotIdentity.Alive)
            {
                Plugin.LogError($"Revive company with LethalBot: error when trying to revive bot \"{lethalBotIdentity.Name}\", bot is already alive! do nothing more");
                return false;
            }

            // Update remaining revives
            LethalBotManager.Instance.UpdateReviveCompanyRemainingRevivesServerRpc(lethalBotIdentity.Name);

            PlayerControllerB playerReviving = GameNetworkManager.Instance.localPlayerController;
            Vector3 revivePos = ragdollGrabbableObjectToRevive.transform.position;
            float yRot = playerReviving.transform.rotation.eulerAngles.y;
            if (Vector3.Distance(revivePos, playerReviving.transform.position) > 7f)
            {
                revivePos = playerReviving.transform.position;
            }
            bool isInsideFactory = playerReviving.isInsideFactory;

            // Respawn bot
            Plugin.LogDebug($"Reviving bot {lethalBotIdentity.Name}");
            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = revivePos,
                                                                YRot = yRot,
                                                                IsOutside = !isInsideFactory,
                                                                IndexNextPlayerObject = (int)ragdollGrabbableObjectToRevive.ragdoll.playerScript.playerClientId
                                                            });
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is respawned
            StartOfRound.Instance.livingPlayers++;
            return false;
        }

        [HarmonyPatch("GetClosestDeadBody")]
        [HarmonyPostfix]
        static void GetClosestDeadBody_PostFix(ref RagdollGrabbableObject __result)
        {
            __result = GetRagdollGrabbableObjectLookingAt();

            if (__result != null && __result.ragdoll == null)
            {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                __result = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            }
        }

        private static RagdollGrabbableObject GetRagdollGrabbableObjectLookingAt()
        {
            PlayerControllerB player = GameNetworkManager.Instance.localPlayerController;
            Ray interactRay = new Ray(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward);
            if (Physics.Raycast(interactRay, out RaycastHit hit, player.grabDistance, 1073742656)
                && hit.collider.gameObject.layer != 8 && hit.collider.gameObject.layer != 30)
            {
                // Check if we are pointing to a ragdoll body of intern (not grabbable)
                if (hit.collider.tag == "PhysicsProp")
                {
                    RagdollGrabbableObject? ragdoll = hit.collider.gameObject.GetComponent<RagdollGrabbableObject>();
                    if (ragdoll == null)
                    {
                        return null!;
                    }

                    return ragdoll;
                }
            }

            return null!;
        }
    }
}
