using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using AudioManager = LethalBots.Managers.AudioManager;
using Random = System.Random;

namespace LethalBots.AI
{
    /// <summary>
    /// The class that manages the bot's voice
    /// </summary>
    /// <remarks>
    /// I should consider using System.Speech.Synthesis and making the bots use text to speech!
    /// This would make them 10x as efficent as mission controllers
    /// </remarks>
    public class LethalBotVoice
    {
        public int BotID { get; set; }
        public string VoiceFolder { get; set; }
        public float Volume { get; set; }
        public float VoicePitch { get; set; }
        public AudioSource CurrentAudioSource { get; set; } = null!;

        private float cooldownPlayAudio = 0f;
        private float[] leftSamples = new float[256];
        private float[] rightSamples = new float[256];
        private bool aboutToTalk;
        public EnumVoicesState LastVoiceState
        {
            get;
            private set;
        }

        private Dictionary<EnumVoicesState, List<string>> dictAvailableAudioClipPathsByState = new Dictionary<EnumVoicesState, List<string>>();
        private Dictionary<EnumVoicesState, List<string>> availableAudioClipPaths = new Dictionary<EnumVoicesState, List<string>>();

        private bool wasInside;
        private bool wasAllowedToSwear;

        public LethalBotVoice(string voiceFolder, float volume, float voicePitch)
        {
            this.VoiceFolder = voiceFolder;
            this.Volume = volume;
            this.VoicePitch = voicePitch;
        }

        public override string ToString()
        {
            return $"BotID: {BotID}, VoiceFolder: {VoiceFolder}, Volume: {Volume}, VoicePitch {VoicePitch}";
        }

        public void SetCooldownAudio(float cooldown)
        {
            cooldownPlayAudio = cooldown;
        }

        public void SetNewRandomCooldownAudio()
        {
            cooldownPlayAudio = GetRandomCooldown();
        }

        public void ReduceCooldown(float time)
        {
            // CooldownPlayAudio
            if (cooldownPlayAudio > 0f)
            {
                cooldownPlayAudio -= time;
            }
            if (cooldownPlayAudio < 0f)
            {
                cooldownPlayAudio = 0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanPlayAudioAfterCooldown()
        {
            return cooldownPlayAudio == 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTalking()
        {
            return CurrentAudioSource.isPlaying || aboutToTalk;
        }

        /// <summary>
        /// Get the current amplitude of the voice audio source.
        /// </summary>
        /// <returns>0-1 value based on the current amplitude of our voice chat audio</returns>
        public float GetVoiceAmplitude()
        {
            if (CurrentAudioSource == null 
                || !CurrentAudioSource.isPlaying)
            {
                return 0f;
            }

            // Get the current amplitude of the audio source
            CurrentAudioSource.GetOutputData(leftSamples, 0); // Channel 0 is the left channel
            CurrentAudioSource.GetOutputData(rightSamples, 1); // Channel 1 is the right channel

            float leftSum = 0f;
            float rightSum = 0f;
            foreach (float sample in leftSamples)
            {
                leftSum += sample * sample;
            }
            foreach (float sample in rightSamples)
            {
                rightSum += sample * sample;
            }

            float rmsLeft = Mathf.Sqrt(leftSum / leftSamples.Length);
            float rmsRight = Mathf.Sqrt(rightSum / rightSamples.Length);

            return Mathf.Clamp01((rmsLeft + rmsRight) / 2f);
        }

        public void TryPlayVoiceAudio(PlayVoiceParameters parameters)
        {
            if (Plugin.Config.Talkativeness.Value == (int)EnumTalkativeness.NoTalking)
            {
                return;
            }

            if (!parameters.CanTalkIfOtherLethalBotTalk)
            {
                if (LethalBotManager.Instance.DidAnLethalBotJustTalkedClose(this.BotID))
                {
                    SetNewRandomCooldownAudio();
                    return;
                }
            }

            if (parameters.WaitForCooldown)
            {
                if (!CanPlayAudioAfterCooldown())
                {
                    return;
                }
            }

            if (!parameters.CutCurrentVoiceStateToTalk)
            {
                if (IsTalking())
                {
                    return;
                }
            }

            if (parameters.CanRepeatVoiceState)
            {
                // Wait if already in state
                if (LastVoiceState == parameters.VoiceState
                    && IsTalking())
                {
                    // We wait for end
                    return;
                }
            }
            else
            {
                // Cannot repeat allowed, if in same state no cut talking
                if (LastVoiceState == parameters.VoiceState)
                {
                    return;
                }
            }

            PlayRandomVoiceAudio(parameters.VoiceState, parameters);
            LastVoiceState = parameters.VoiceState;
            LethalBotManager.Instance.PlayAudibleNoiseForLethalBot(this.BotID, CurrentAudioSource.transform.position, 16f, 0.9f, 5);
        }

        public void PlayRandomVoiceAudio(EnumVoicesState enumVoicesState, PlayVoiceParameters parameters)
        {
            StopAudioFadeOut();
            ResetAboutToTalk();
            string audioClipPath = GetRandomAudioClipByState(enumVoicesState, parameters);
            if (string.IsNullOrWhiteSpace(audioClipPath))
            {
                return;
            }

            aboutToTalk = true;
            if (parameters.ShouldSync)
            {
                // Can take time, coroutine stuff
                AudioManager.Instance.SyncPlayAudio(audioClipPath, BotID);
            }
            else
            {
                AudioManager.Instance.PlayAudio(audioClipPath, this);
            }
        }

        public void PlayAudioClip(AudioClip audioClip)
        {
            ResetAboutToTalk();

            /*float desiredPitch = VoicePitch;
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(BotID);
            if (lethalBotAI != null)
            {
                desiredPitch = SoundManager.Instance.playerVoicePitchTargets[lethalBotAI.NpcController.Npc.playerClientId];
            }
            CurrentAudioSource.pitch = desiredPitch;*/
            CurrentAudioSource.clip = audioClip;
            CurrentAudioSource.Play();

            SetCooldownAudio(audioClip.length + GetRandomCooldown());
        }

        private float GetRandomCooldown()
        {
            // Set random cooldown
            Random randomInstance = new Random();
            switch (Plugin.Config.Talkativeness.Value)
            {
                case (int)EnumTalkativeness.Shy:
                    return (float)randomInstance.Next(VoicesConst.MIN_COOLDOWN_PLAYVOICE_SHY, VoicesConst.MAX_COOLDOWN_PLAYVOICE_SHY);
                case (int)EnumTalkativeness.Normal:
                    return (float)randomInstance.Next(VoicesConst.MIN_COOLDOWN_PLAYVOICE_NORMAL, VoicesConst.MAX_COOLDOWN_PLAYVOICE_NORMAL);
                case (int)EnumTalkativeness.Talkative:
                    return (float)randomInstance.Next(VoicesConst.MIN_COOLDOWN_PLAYVOICE_TALKATIVE, VoicesConst.MAX_COOLDOWN_PLAYVOICE_TALKATIVE);
                case (int)EnumTalkativeness.CantStopTalking:
                    return (float)randomInstance.Next(VoicesConst.MIN_COOLDOWN_PLAYVOICE_CANTSTOPTALKING, VoicesConst.MAX_COOLDOWN_PLAYVOICE_CANTSTOPTALKING);
                default:
                    return 0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetAboutToTalk()
        {
            aboutToTalk = false;
        }

        private string GetRandomAudioClipByState(EnumVoicesState enumVoicesState,
                                                 PlayVoiceParameters parameters)
        {
            if (!dictAvailableAudioClipPathsByState.ContainsKey(enumVoicesState))
            {
                dictAvailableAudioClipPathsByState.Add(enumVoicesState, LoadAudioClipPathsByState(enumVoicesState).ToList());
            }

            if (!availableAudioClipPaths.ContainsKey(enumVoicesState))
            {
                availableAudioClipPaths.Add(enumVoicesState, FilterAudioClipPaths(dictAvailableAudioClipPathsByState[enumVoicesState], parameters).ToList());
            }
            else if (DidParametersChanged(parameters))
            {
                // Reset pool of audio path
                availableAudioClipPaths[enumVoicesState].Clear();
            }

            if (availableAudioClipPaths[enumVoicesState].Count == 0)
            {
                //Plugin.LogDebug($"reset audio paths");
                availableAudioClipPaths[enumVoicesState] = FilterAudioClipPaths(dictAvailableAudioClipPathsByState[enumVoicesState], parameters).ToList();
            }

            List<string> audioClipPaths = availableAudioClipPaths[enumVoicesState];
            if (audioClipPaths.Count == 0)
            {
                return string.Empty;
            }

            string audioClipPath;
            Random randomInstance = new Random();
            int index = randomInstance.Next(0, audioClipPaths.Count);
            audioClipPath = audioClipPaths[index];
            audioClipPaths.RemoveAt(index);

            return audioClipPath;
        }

        private IEnumerable<string> FilterAudioClipPaths(List<string> audioClipPaths,
                                                         PlayVoiceParameters parameters)
        {
            var query = audioClipPaths.AsEnumerable();

            if (!parameters.AllowSwearing)
            {
                query = query.Where(x => !x.ToLower().Contains(VoicesConst.SWEAR_KEYWORD.ToLower()));
            }

            if (parameters.IsLethalBotInside)
            {
                query = query.Where(x => !x.ToLower().Contains(VoicesConst.OUTSIDE_KEYWORD.ToLower()));
            }
            else
            {
                query = query.Where(x => !x.ToLower().Contains(VoicesConst.INSIDE_KEYWORD.ToLower()));
            }

            return query;
        }

        private bool DidParametersChanged(PlayVoiceParameters parameters)
        {
            bool parametersChanged = false;
            if (wasInside != parameters.IsLethalBotInside)
            {
                wasInside = parameters.IsLethalBotInside;
                parametersChanged = true;
            }
            if (wasAllowedToSwear != parameters.AllowSwearing)
            {
                wasAllowedToSwear = parameters.AllowSwearing;
                parametersChanged = true;
            }

            return parametersChanged;
        }

        private string[] LoadAudioClipPathsByState(EnumVoicesState enumVoicesState)
        {
            string path = string.Join(' ', VoiceFolder + "\\" + enumVoicesState.ToString()).Replace("_", "").ToLower();

            var audioClipPaths = AudioManager.Instance.DictAudioClipsByPath
                                    .Where(x => x.Key.Replace(" ", "").Replace("_", "").ToLower().Contains(path));

            Plugin.LogDebug($"Loaded {audioClipPaths.Count()} path containing {path}");
            return audioClipPaths.Select(y => y.Key).ToArray();
        }

        public void ResetAvailableAudioPaths()
        {
            dictAvailableAudioClipPathsByState.Clear();
            availableAudioClipPaths.Clear();
        }

        public void TryStopAudioFadeOut()
        {
            if (LastVoiceState != EnumVoicesState.Hit
                && LastVoiceState != EnumVoicesState.SteppedOnTrap
                && LastVoiceState != EnumVoicesState.RunningFromMonster)
            {
                StopAudioFadeOut();
            }
        }

        public void StopAudioFadeOut()
        {
            if (CurrentAudioSource.isPlaying)
            {
                CurrentAudioSource.Stop();
                LastVoiceState = EnumVoicesState.None;
            }
        }
    }

    public struct PlayVoiceParameters
    {
        public bool CanTalkIfOtherLethalBotTalk { get; set; }
        public bool WaitForCooldown { get; set; }
        public bool CutCurrentVoiceStateToTalk { get; set; }
        public bool CanRepeatVoiceState { get; set; }

        public EnumVoicesState VoiceState { get; set; }

        public bool ShouldSync { get; set; }
        public bool IsLethalBotInside { get; set; }
        public bool AllowSwearing { get; set; }
    }
}
