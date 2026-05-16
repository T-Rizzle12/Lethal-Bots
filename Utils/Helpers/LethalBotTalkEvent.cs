using LethalBots.AI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Events;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Class that represents a UnityEvent for when a Lethal Bot talks, it contains the bot that is talking and the amplitude of what the bot said
    /// </summary>
    public class LethalBotTalkEvent : UnityEvent<LethalBotVoice, float>
    {
    }
}
