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

            PlayerControllerB playerReviving = GeneralUtil.GetClosestAlivePlayer(ragdollGrabbableObjectToRevive.transform.position);
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
                                                                Hp = ConfigVariables.ReviveToHealth, // FIXME: Fix this to work with the less health on revive!
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
    }
}
