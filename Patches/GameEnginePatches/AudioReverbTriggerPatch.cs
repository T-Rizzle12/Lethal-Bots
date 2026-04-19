using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.Patches.GameEnginePatches
{
    [HarmonyPatch(typeof(AudioReverbTrigger))]
    public class AudioReverbTriggerPatch
    {
        // NEEDTOVAILDATE: Is this no longer needed? I found out that activating the PlayerController may fix this!
        /// <summary>
        /// Fixes the bug where bots don't trigger changes to their audio reverb filter
        /// </summary>
        /// <remarks>
        /// This essentially duplicates the logic used in the patched class!
        /// </remarks>
        /// <param name="__instance"></param>
        /// <param name="other"></param>
        /*[HarmonyPatch("OnTriggerStay")]
        [HarmonyPrefix]
        public static bool OnTriggerStay_Prefix(AudioReverbTrigger __instance, Collider other)
        {
            if (other.gameObject.CompareTag("Player") && GameNetworkManager.Instance.localPlayerController != null)
            {
                PlayerControllerB playerControllerB = other.gameObject.GetComponent<PlayerControllerB>();
                if (playerControllerB != null && LethalBotManager.Instance.IsPlayerLethalBot(playerControllerB))
                {
                    //Plugin.LogDebug($"AudioReverbTrigger::OnTriggerStay called by Bot {playerControllerB.playerUsername}!");
                    __instance.playerScript = playerControllerB;
                    if (__instance.playerScript != null && __instance.playerScript.isPlayerControlled)
                    {
                        __instance.ChangeAudioReverbForPlayer(__instance.playerScript);
                    }
                    return false;
                }
            }
            //Plugin.LogDebug($"AudioReverbTrigger::OnTriggerStay called by {other.gameObject.name}!");
            return true;
        }*/

        [HarmonyPatch("ChangeAudioReverbForPlayer")]
        [HarmonyPrefix]
        public static bool ChangeAudioReverbForPlayer_Prefix(AudioReverbTrigger __instance, PlayerControllerB pScript, bool __runOriginal)
        {
            // Only do this if the original logic is run!
            if (!__runOriginal)
            {
                return true;
            }
            if (GameNetworkManager.Instance.localPlayerController == null || pScript.currentAudioTrigger == __instance || !pScript.isPlayerControlled)
            {
                return true;
            }

            // HACKHACK: This is a horrible way to fix the bug where the bots can sometimes "fall" out of the ship,
            // but I don't have any other solution at the moment. The bug started happening after the V80 update.
            // It only really happens on moons like Vow, Adamance, and March. This may be due to some changes made by Zeekerss!
            if (__instance.elevatorTriggerForProps
                && LethalBotManager.Instance.IsPlayerLethalBot(pScript) 
                && LethalBotManager.IsTheShipLanding()
                && (pScript.isInElevator || pScript.isInHangarShipRoom))
            {
                return false;
            }

            Plugin.LogDebug($"AudioReverbTrigger::ChangeAudioReverbForPlayer called by Player {pScript.playerUsername}!");
            return true;
        }
    }
}
