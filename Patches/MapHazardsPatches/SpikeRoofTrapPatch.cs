using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.Patches.MapHazardsPatches
{
    /// <summary>
    /// Patch for <c>SpikeRoofTrap</c>
    /// </summary>
    [HarmonyPatch(typeof(SpikeRoofTrap))]
    public class SpikeRoofTrapPatch
    {
        // TODO: This needs to be changed to use a transpiler since we use the player collider
        // NEEDTOVALIDATE: Should I use a transpiler or do my own logic here.......
        // It may be better to use postfixes and prefixes since I can allow players to toggle 
        // whether they want the bots to take damage from traps!
        [HarmonyPatch("OnTriggerStay")]
        [HarmonyPostfix]
        static void OnTriggerStay_PostFix(SpikeRoofTrap __instance, Collider other)
        {
            if (!__instance.trapActive || !__instance.slammingDown || (Time.realtimeSinceStartup - __instance.timeSinceMovingUp) < 0.75f)
            {
                return;
            }
            PlayerControllerB component = other.gameObject.GetComponent<PlayerControllerB>();
            if (component != null && LethalBotManager.Instance.IsPlayerLethalBotOwnerLocal(component) && !component.isPlayerDead)
            {
                component.KillPlayer(Vector3.down * 17f, spawnBody: true, CauseOfDeath.Crushing);
                return;
            }
        }

        // A function we use to find that dang TerminalAccessibleObject
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_PostFix(SpikeRoofTrap __instance)
        {
            // Ok, so I already know that GetComponent, GetComponentInChildren, and GetComponentInParent on the trap itself doesnt work, could it be on the animator or something?
            // LIST OF FIELDS TESTED:
            // Instance: FAILED
            // spikeTrapAnimator: FAILED
            // laserEye: FAILED
            // stickingPointsContainer:
            HUDManager.Instance?.DisplayTip("Spike Roof Trap Spawned", "Check the logs!!!!!!");
            TerminalAccessibleObject terminalAccessibleObject = __instance.stickingPointsContainer.GetComponent<TerminalAccessibleObject>();
            if (terminalAccessibleObject == null)
            {
                terminalAccessibleObject = __instance.stickingPointsContainer.GetComponentInParent<TerminalAccessibleObject>();
                if (terminalAccessibleObject == null)
                {
                    terminalAccessibleObject = __instance.stickingPointsContainer.GetComponentInChildren<TerminalAccessibleObject>();
                    if (terminalAccessibleObject == null)
                    {
                        Plugin.LogDebug("Failed to find TerminalAccessibleObject in SpikeRoofTrap! :(");
                        return;
                    }
                    else
                    {
                        Plugin.LogDebug("SpikeRoofTrap had TerminalAccessibleObject in animator under GetComponentInChildren.");
                    }
                }
                else
                {
                    Plugin.LogDebug("SpikeRoofTrap had TerminalAccessibleObject in animator under GetComponentInParent.");
                }
            }
            else
            {
                Plugin.LogDebug("SpikeRoofTrap had TerminalAccessibleObject in animator under GetComponent.");
            }
        }
    }
}
