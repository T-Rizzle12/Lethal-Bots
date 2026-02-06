using GameNetcodeStuff;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using UnityEngine;

namespace LethalBots.AI.AIStates
{
    public class BrainDeadState : AIState
    {
        private bool hasVotedToLeave;
        private float voteInterval;
        public BrainDeadState(LethalBotAI ai) : base(ai)
        {
            hasVotedToLeave = false;
            CurrentState = EnumAIStates.BrainDead;
        }

        public override void DoAI()
        {
            ai.StopMoving();

            // Don't need to do the rest of the logic if we already voted to leave!
            if (hasVotedToLeave 
                || StartOfRound.Instance.shipIsLeaving 
                || TimeOfDay.Instance.shipLeavingAlertCalled)
            {
                return;
            }

            // Only check if we need to vote every few seconds!
            if (voteInterval > 0f)
            {
                voteInterval -= ai.AIIntervalTime;
                return;
            }

            voteInterval = Random.Range(Const.MIN_TIME_TO_VOTE, Const.MAX_TIME_TO_VOTE);

            // Only dead players can vote to leave early!
            if (npcController.Npc.isPlayerControlled || !npcController.Npc.isPlayerDead)
            {
                // We are not dead, we are either not running ai on this client
                // or the round just ended!
                if (ai.IsOwner)
                {
                    ai.State = ai.GetDesiredAIState();
                }
                return;
            }

            // Kinda hard to transfer loot when you're dead!
            if (LethalBotManager.Instance.LootTransferPlayers.Contains(npcController.Npc))
            {
                LethalBotManager.Instance.RemovePlayerFromLootTransferListAndSync(npcController.Npc);
            }

            // Check if every human player is dead,
            // and if our fellow players and bots are on the ship
            bool allLivingPlayersOnShip = LethalBotManager.Instance.AreAllPlayersOnTheShip();
            bool allHumanPlayersDead = LethalBotManager.Instance.AreAllHumanPlayersDead();

            // If the ship is compromised,
            // we should vote to leave if players are on it!
            bool isShipCompromised = LethalBotManager.IsShipCompromised(ai);

            // If every human player is dead and all of us are on the ship,
            // we should vote to leave early!
            // We will also vote to leave early if the ship is compromised!
            // The compromised ship check works even if there are alive human players!
            //Plugin.LogDebug($"All Human Players Dead: {allHumanPlayersDead} All Living Players on Ship: {allLivingPlayersOnShip}");
            if (allLivingPlayersOnShip && (allHumanPlayersDead || isShipCompromised))
            {
                if (ShouldReturnToShip() 
                    || (StartOfRound.Instance.livingPlayers <= 1 && isShipCompromised))
                {
                    Plugin.LogDebug($"Bot {npcController.Npc.playerUsername} is attempting to vote to leave early!");
                    //ai.LethalBotVoteToLeaveEarlyServerRpc();
                    TimeOfDay.Instance.SetShipLeaveEarlyServerRpc();
                    hasVotedToLeave = true;
                }
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            //ai.LethalBotIdentity.Voice.StopAudioFadeOut();

            if (Plugin.Config.AllowTalkingWhileDead.Value)
            {
                // Default states, wait for cooldown and if no one is talking close
                ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
                {
                    VoiceState = EnumVoicesState.Chilling, // TODO: Add dedicated dead voice state!
                    CanTalkIfOtherLethalBotTalk = false,
                    WaitForCooldown = true,
                    CutCurrentVoiceStateToTalk = false,
                    CanRepeatVoiceState = true,

                    ShouldSync = true,
                    IsLethalBotInside = npcController.Npc.isInsideFactory,
                    AllowSwearing = Plugin.Config.AllowSwearing.Value
                });
            }
            else
            {
                ai.LethalBotIdentity.Voice.StopAudioFadeOut();
            }
        }

        // We are dead, these messages mean nothing to us!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            return;
        }

        // We are dead, these messages mean nothing to us!
        public override void OnPlayerChatMessageReceived(string message, PlayerControllerB playerWhoSentMessage, bool isVoice)
        {
            return;
        }
    }
}
