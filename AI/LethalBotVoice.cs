using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using AudioManager = LethalBots.Managers.AudioManager;
using Random = UnityEngine.Random;

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

        // These are used for StartOfRoundPatch.DetectVoiceChatAmplitude
        public int averageCount;
        public float averageVoiceAmplitude;
        public float voiceChatNoiseCooldown;

        // This event is called when a bot talks, it provides the bot that talked and the detected amplitude of the voice chat audio.
        public static LethalBotTalkEvent lethalBotTalkEvent = new LethalBotTalkEvent();

        // Cooldown for talkativeness voice states
        private float cooldownTalkativeness = 0f;
        // Cooldown for responsiveness voice states
        private float cooldownResponsiveness = 0f;

        private readonly float[] tempSamples = new float[256];
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

        public void SetCooldownAudio(EnumVoicesState voiceState, float cooldown)
        {
            if (IsResponsivenessState(voiceState))
            {
                cooldownResponsiveness = cooldown;
            }
            else
            {
                cooldownTalkativeness = cooldown;
            }
        }

        /// <summary>
        /// Set a new random cooldown for both types/states of the voice lines.
        /// </summary>
        public void SetNewRandomCooldownForBothAudio()
        {
            // Reset Talkativeness cooldown
            SetNewRandomCooldownAudio(isResponsiveness: false); // Added 'isResponsiveness:' for clarity
            // Reset Responsiveness cooldown
            SetNewRandomCooldownAudio(isResponsiveness: true); // Added 'isResponsiveness:' for clarity
        }

        // inputs EnumVoicesState, meaning it will run the IsResponsivenessState()
        // If IsResponsivenessState() already has run, then use the SetNewRandomCooldownAudio(bool isResponsiveness) instead
        public void SetNewRandomCooldownAudio(EnumVoicesState voiceState)
        {
            SetNewRandomCooldownAudio(IsResponsivenessState(voiceState));
        }

        /// <summary>
        /// Set a new random cooldown for the voice lines.
        /// </summary>
        public void SetNewRandomCooldownAudio(bool isResponsiveness) // Used for passing through the IsResponsivenessState() result
        {
            if (isResponsiveness)
            {
                cooldownResponsiveness = GetRandomCooldown(true);
                //Plugin.LogInfo($"New cooldownResponsiveness value: {cooldownResponsiveness}");
            }
            else
            {
                cooldownTalkativeness = GetRandomCooldown(false);
                //Plugin.LogInfo($"New cooldownTalkativeness value: {cooldownTalkativeness}");
            }
        }

        public void ReduceCooldown(float time)
        {
            // CooldownPlayAudio
            // Talkativeness cooldown
            if (cooldownTalkativeness > 0f)
            {
                cooldownTalkativeness -= time;
                if (cooldownTalkativeness < 0f)
                {
                    cooldownTalkativeness = 0f;
                }
            }

            // Responsiveness cooldown
            if (cooldownResponsiveness > 0f)
            {
                cooldownResponsiveness -= time;
                if (cooldownResponsiveness < 0f)
                {
                    cooldownResponsiveness = 0f;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsResponsivenessState(EnumVoicesState voiceState)
        {
            return VoicesConst.ResponsivenessVoiceStates.Contains(voiceState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanPlayAudioAfterCooldown(EnumVoicesState voiceState)
        {
            return IsResponsivenessState(voiceState) ? cooldownResponsiveness == 0f : cooldownTalkativeness == 0f;
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
            // If we don't have an audio source, or it's not playing, or it doesn't have a clip, return 0 amplitude
            if (CurrentAudioSource == null 
                || !CurrentAudioSource.isPlaying 
                || CurrentAudioSource.clip == null)
            {
                return 0f;
            }

            // Make sure we have channels to avoid errors, if no channels return 0 amplitude
            int channels = Mathf.Min(CurrentAudioSource.clip.channels, GetOutputChannelCount());
            if (channels <= 0)
                return 0f;

            // Get the total voice amplitude by averaging the RMS of all channels,
            // and then normalizing it to a 0-1 value (assuming max amplitude is 1)
            float totalRms = 0f;
            int totalSamples = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                // GetOutputData can sometimes throw errors if the audio source is in a weird state,
                // so we catch any errors and just skip that channel if it happens
                try
                {
                    CurrentAudioSource.GetOutputData(tempSamples, channel);
                }
                catch (Exception e)
                {
                    Plugin.LogError($"Error getting output data for channel {channel} of audio source on bot {BotID}: {e}");
                    continue;
                }

                // Total the samples for this channel.
                float sum = 0f;
                for (int i = 0; i < tempSamples.Length; i++)
                {
                    float sample = tempSamples[i];
                    sum += sample * sample;
                }

                // Get average
                totalRms += Mathf.Sqrt(sum / tempSamples.Length);
                totalSamples++;
            }

            // Get total average
            return Mathf.Clamp01(totalRms / Mathf.Max(totalSamples, 1));
        }

        /// <summary>
        /// Helper that gets the user's audio channel limit.
        /// </summary>
        /// <returns></returns>
        private static int GetOutputChannelCount()
        {
            switch (AudioSettings.GetConfiguration().speakerMode)
            {
                case AudioSpeakerMode.Mono:
                    return 1;
                case AudioSpeakerMode.Stereo:
                    return 2;
                case AudioSpeakerMode.Quad:
                    return 4;
                case AudioSpeakerMode.Surround:
                    return 5;
                case AudioSpeakerMode.Mode5point1:
                    return 6;
                case AudioSpeakerMode.Mode7point1:
                    return 8;
                default:
                    return 2; // Same as stereo
            }
        }

        public void TryPlayVoiceAudio(PlayVoiceParameters parameters)
        {
            // Check the correct value depending on which slider controls this state
            // Cooldown check in the 'is responsive' if statement, otherwise the responsive voice lines have no cooldown
            bool isResponsiveness = IsResponsivenessState(parameters.VoiceState);
            if (isResponsiveness)
            {
                if (Plugin.Config.Responsiveness.Value == (int)EnumResponsiveness.NoResponses)
                {
                    return;
                }

                if (!CanPlayAudioAfterCooldown(parameters.VoiceState))
                {
                    return;
                }
            }
            else
            {
                if (Plugin.Config.Talkativeness.Value == (int)EnumTalkativeness.NoTalking)
                {
                    return;
                }

                if (parameters.WaitForCooldown)
                {
                    if (!CanPlayAudioAfterCooldown(parameters.VoiceState))
                    {
                        return;
                    }
                }
            }

            if (!parameters.CanTalkIfOtherLethalBotTalk)
            {
                if (LethalBotManager.Instance.DidAnLethalBotJustTalkedClose(this.BotID))
                {
                    SetNewRandomCooldownAudio(parameters.VoiceState);
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

            SetCooldownAudio(LastVoiceState, audioClip.length + GetRandomCooldown(LastVoiceState));
        }

        // inputs EnumVoicesState, meaning it will run the IsResponsivenessState()
        // If IsResponsivenessState() already has run, then use the GetRandomCooldown(bool isResponsiveness) instead
        private float GetRandomCooldown(EnumVoicesState voiceState)
        {
            return GetRandomCooldown(IsResponsivenessState(voiceState));
        }

        /// <summary>
        /// Returns a random cooldown duration using whichever slider (talkativeness or responsiveness) controls <paramref name="voiceState"/>.
        /// </summary>
        private float GetRandomCooldown(bool isResponsiveness) // Used for passing through the IsResponsivenessState() result
        {
            // Set random cooldown
            if (isResponsiveness)
            {
                switch (Plugin.Config.Responsiveness.Value)
                {
                    case EnumResponsiveness.Shy:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_SHY, VoicesConst.MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_SHY);
                    case EnumResponsiveness.Normal:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_NORMAL, VoicesConst.MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_NORMAL);
                    case EnumResponsiveness.Responsive:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_RESPONSIVE, VoicesConst.MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_RESPONSIVE);
                    case EnumResponsiveness.AlwaysRespond:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_RESPONSIVE_ALWAYSRESPOND, VoicesConst.MAX_COOLDOWN_PLAYVOICE_RESPONSIVE_ALWAYSRESPOND);
                    default:
                        return 0f;
                }
            }
            else
            {
                switch (Plugin.Config.Talkativeness.Value)
                {
                    case EnumTalkativeness.Shy:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_SHY, VoicesConst.MAX_COOLDOWN_PLAYVOICE_SHY);
                    case EnumTalkativeness.Normal:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_NORMAL, VoicesConst.MAX_COOLDOWN_PLAYVOICE_NORMAL);
                    case EnumTalkativeness.Talkative:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_TALKATIVE, VoicesConst.MAX_COOLDOWN_PLAYVOICE_TALKATIVE);
                    case EnumTalkativeness.CantStopTalking:
                        return Random.Range(VoicesConst.MIN_COOLDOWN_PLAYVOICE_CANTSTOPTALKING, VoicesConst.MAX_COOLDOWN_PLAYVOICE_CANTSTOPTALKING);
                    default:
                        return 0f;
                }
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
            int index = Random.Range(0, audioClipPaths.Count);
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
