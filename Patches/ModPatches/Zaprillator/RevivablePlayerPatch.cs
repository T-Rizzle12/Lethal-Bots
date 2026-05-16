using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.Zaprillator
{
    public class RevivablePlayerPatch
    {
        public static bool StopShockingWithGun_Prefix(RagdollGrabbableObject ____ragdoll,
                                                      ref bool ____bodyShocked,
                                                      ref GrabbableObject ____shockedBy,
                                                      float ____batteryLevel)
        {
            if (____ragdoll == null)
            {
                return true;
            }

            if (!____bodyShocked)
            {
                return false;
            }

            if (____shockedBy == null
                || !____shockedBy.IsOwner)
            {
                return false;
            }

            string name = ____ragdoll.ragdoll.gameObject.GetComponentInChildren<ScanNodeProperties>().headerText;
            LethalBotIdentity? lethalBotIdentity = IdentityManager.Instance.FindIdentityFromBodyName(name);
            if (lethalBotIdentity == null)
            {
                return true;
            }

            // Get the same logic as the mod at the beginning
            if (lethalBotIdentity.Alive)
            {
                Plugin.LogError($"Zaprillator with LethalBot: error when trying to revive bot \"{lethalBotIdentity.Name}\", bot is already alive! do nothing more");
                return false;
            }

            ____bodyShocked = false;
            RoundManager.Instance.FlickerLights();

            var restoreHealth = LethalBotManager.Instance.MaxHealthPercent(Mathf.RoundToInt(____batteryLevel * 100), lethalBotIdentity.HpMax);
            ____shockedBy.UseUpBatteries();
            ____shockedBy.SyncBatteryServerRpc(0);
            ____shockedBy = null!;

            LethalBotManager.Instance.SpawnThisLethalBotServerRpc(lethalBotIdentity.IdIdentity,
                                                            new NetworkSerializers.SpawnLethalBotParamsNetworkSerializable()
                                                            {
                                                                ShouldDestroyDeadBody = true,
                                                                ResetBodyBlood = true,
                                                                Hp = restoreHealth,
                                                                enumSpawnAnimation = EnumSpawnAnimation.OnlyPlayerSpawnAnimation,
                                                                SpawnPosition = ____ragdoll.ragdoll.transform.position,
                                                                YRot = 0,
                                                                IsOutside = !GameNetworkManager.Instance.localPlayerController.isInsideFactory,
                                                                IndexNextPlayerObject = (int)____ragdoll.ragdoll.playerScript.playerClientId
                                                            });
            // Immediately change the number of living players
            // The host will update the number of living players when the bot is spawned
            StartOfRound.Instance.livingPlayers++;
            return false;
        }
    }
}
