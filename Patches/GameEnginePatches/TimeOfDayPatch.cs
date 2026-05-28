using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using UnityEngine;
using static Unity.Netcode.NetworkBehaviour;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(TimeOfDay))]
    public class TimeOfDayPatch
    {
        [HarmonyPatch("SendHostInfoForShowingAdClientRpc")]
        [HarmonyPostfix]
        static void SendHostInfoForShowingAdClientRpc_Postfix(TimeOfDay __instance)
        {
            // Lets us know we got the RPC
            Plugin.LogDebug("Received client rpc Ad!");

            // Grab all of the bots owned by the local client!
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotsAIOwnedByLocal();
            foreach (var lethalBotAI in lethalBotAIs)
            {
                // Make sure the bot is vaild
                if (lethalBotAI == null)
                {
                    continue;
                }

                // Make sure the controller is vaild and not dead!
                PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
                if (lethalBotController == null || lethalBotController.isPlayerDead)
                {
                    continue;
                }

                // Mimic the same logic from the base game
                // NEEDTOVALIDATE: Should I ditch the physics check and check if the bot is paniking or fighting instead?
                // The bots might collide with the check making it never pass!
                bool doesntMeetRequirements = false;
                float num = Mathf.Max(lethalBotController.timeSinceTakingDamage, lethalBotController.timeSinceFearLevelUp);
                if (num > 12f)
                {
                    if (lethalBotController.isInsideFactory && Physics.CheckSphere(lethalBotController.transform.position, 20f, 524288, QueryTriggerInteraction.Collide))
                    {
                        doesntMeetRequirements = true;
                        Plugin.LogDebug("Can't show ad client; 1");
                    }
                }
                else if (num < 1.7f && lethalBotController.isInsideFactory && Physics.CheckSphere(lethalBotController.transform.position, 12f, 524288, QueryTriggerInteraction.Collide))
                {
                    doesntMeetRequirements = true;
                    Plugin.LogDebug("Can't show ad client; 2");
                }

                __instance.ReceiveInfoFromClientForShowingAdServerRpc(doesntMeetRequirements);
            }
        }
    }
}
