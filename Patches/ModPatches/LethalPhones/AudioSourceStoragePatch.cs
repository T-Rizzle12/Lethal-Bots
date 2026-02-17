using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Managers;
using Scoops.service;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.InputForUI.EventModifiers;

namespace LethalBots.Patches.ModPatches.LethalPhones
{
    [HarmonyPatch(typeof(AudioSourceStorage))]
    public class AudioSourceStoragePatch
    {
        [HarmonyPatch("ApplyPhoneEffect")]
        [HarmonyPrefix]
        static bool ApplyPhoneEffect_Prefix(AudioSourceStorage __instance)
        {
            PlayerControllerB player = __instance.player;
            if (!LethalBotManager.Instance.IsPlayerLethalBot(player))
            {
                return true; // Human player, let the default code run!
            }

            // Sigh, we have to recreate the logic for now, I will work on a Transpiler later.
            // TODO: Now that I think about it, it may be better for me to just recreate this in my UpdateLethalBotVoiceEffects hook....
            // Static bypasses audio effects
            if (__instance.staticAudio)
            {
                if (__instance.audioSource.GetComponent<AudioLowPassFilter>())
                {
                    __instance.audioSource.GetComponent<AudioLowPassFilter>().cutoffFrequency = 3000f;
                    __instance.audioSource.GetComponent<AudioLowPassFilter>().lowpassResonanceQ = 3f;
                }
                return false;
            }

            // Apply Voice Audio Source specific changes
            if (__instance.voice)
            {
                //player.currentVoiceChatIngameSettings.set2D = true;
                __instance.audioSource.spatialBlend = 0f;
            }

            if (__instance.audioSource.GetComponent<AudioLowPassFilter>())
            {
                __instance.audioSource.GetComponent<AudioLowPassFilter>().cutoffFrequency = 3000f;
                __instance.audioSource.GetComponent<AudioLowPassFilter>().lowpassResonanceQ = Mathf.Lerp(3f, 10f, __instance.recordInterference);
            }
            if (__instance.audioSource.GetComponent<AudioHighPassFilter>())
            {
                __instance.audioSource.GetComponent<AudioHighPassFilter>().cutoffFrequency = Mathf.Lerp(2000f, 2500f, __instance.recordInterference);
                __instance.audioSource.GetComponent<AudioHighPassFilter>().highpassResonanceQ = Mathf.Lerp(2f, 3f, __instance.recordInterference);
            }

            if (__instance.voice && player.voiceMuffledByEnemy && __instance.audioSource.GetComponent<AudioLowPassFilter>())
            {
                __instance.audioSource.GetComponent<AudioLowPassFilter>().cutoffFrequency = 500;
            }

            return false;
        }

        [HarmonyPatch("Reset")]
        [HarmonyPrefix]
        static bool Reset_Prefix(AudioSourceStorage __instance, 
                                ref float ___origPan, 
                                ref float ___origSpatial, 
                                ref float ___origVolume, 
                                ref AnimationCurve ___origSpatialCurve,
                                ref bool ___hadOcclude,
                                ref bool ___hadLowPass,
                                ref float ___origLowPass,
                                ref float ___origLowPassResQ,
                                ref bool ___hadHighPass,
                                ref float ___origHighPass,
                                ref float ___origHighPassResQ)
        {
            PlayerControllerB player = __instance.player;
            if (!LethalBotManager.Instance.IsPlayerLethalBot(player))
            {
                return true; // Human player, let the default code run!
            }

            // Sigh, we have to recreate the logic for now, I will work on a Transpiler later.
            // TODO: Now that I think about it, it may be better for me to just recreate this in my UpdateLethalBotVoiceEffects hook....
            GameObject audioSourceHolder = __instance.audioSource.gameObject;

            if (__instance.voice)
            {
                //player.currentVoiceChatIngameSettings.set2D = false;
                __instance.audioSource.spatialBlend = 1f;
            }

            if (__instance.staticAudio && __instance.audioSource.isPlaying)
            {
                __instance.audioSource.Stop();
            }

            __instance.audioSource.panStereo = ___origPan;
            __instance.audioSource.spatialBlend = ___origSpatial;
            __instance.audioSource.volume = ___origVolume;

            __instance.audioSource.SetCustomCurve(AudioSourceCurveType.SpatialBlend, ___origSpatialCurve);

            if (___hadOcclude && audioSourceHolder.GetComponent<OccludeAudio>())
            {
                audioSourceHolder.GetComponent<OccludeAudio>().enabled = true;

                if (__instance.voice)
                {
                    audioSourceHolder.GetComponent<OccludeAudio>().overridingLowPass = player.voiceMuffledByEnemy;
                }
            }

            if (audioSourceHolder.GetComponent<AudioLowPassFilter>())
            {
                if (___hadLowPass)
                {
                    audioSourceHolder.GetComponent<AudioLowPassFilter>().cutoffFrequency = ___origLowPass;
                    audioSourceHolder.GetComponent<AudioLowPassFilter>().lowpassResonanceQ = ___origLowPassResQ;
                }
                else
                {
                    audioSourceHolder.GetComponent<AudioLowPassFilter>().enabled = false;
                }

            }

            if (audioSourceHolder.GetComponent<AudioHighPassFilter>())
            {
                if (___hadHighPass)
                {
                    audioSourceHolder.GetComponent<AudioHighPassFilter>().cutoffFrequency = ___origHighPass;
                    audioSourceHolder.GetComponent<AudioHighPassFilter>().highpassResonanceQ = ___origHighPassResQ;
                }
                else
                {
                    audioSourceHolder.GetComponent<AudioHighPassFilter>().enabled = false;
                }
            }

            __instance.modified = false;
            return false;
        }
    }
}
