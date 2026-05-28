using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalLib.Modules;
using System.Collections;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// State when the bot is using the inverse teleporter to teleport into the facility.
    /// </summary>
    public class UseInverseTeleporterState : AIState
    {
        public UseInverseTeleporterState(AIState oldState) : base(oldState)
        {
            CurrentState = EnumAIStates.UseInverseTeleport;
        }

        public UseInverseTeleporterState(LethalBotAI ai) : base(ai)
        {
            CurrentState = EnumAIStates.UseInverseTeleport;
        }

        public override void DoAI()
        {
            // Teleporter has finished teleporting players, but we didn't make it!
            if (!SingletonManager.InverseTeleporter.TryGet(out ShipTeleporter? inverseTeleporter)
                || !LethalBotManager.IsInverseTeleporterActive)
            {
                // Took this from the TimeOfDay class file,
                // if we just started the day we should just charge in!
                TimeOfDay timeOfDay = TimeOfDay.Instance;
                if (timeOfDay != null)
                {
                    DayMode dayMode = timeOfDay.GetDayPhase(timeOfDay.currentDayTime / timeOfDay.totalTime);
                    if (dayMode == DayMode.Dawn)
                    {
                        ai.targetPlayer = null;
                        ai.State = new SearchingForScrapState(this);
                        return;
                    }
                }
                ai.State = new ReturnToShipState(this);
                return;
            }

            float sqrMagDistanceTeleport = (inverseTeleporter.teleportOutPosition.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrMagDistanceTeleport >= Const.DISTANCE_FROM_INVERSE_TELEPORTER * Const.DISTANCE_FROM_INVERSE_TELEPORTER)
            {
                npcController.OrderToSprint();
                ai.SetDestinationToPositionLethalBotAI(inverseTeleporter.teleportOutPosition.position);
                ai.OrderMoveToDestination();
            }
            else
            {
                ai.StopMoving();
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }
    }
}
