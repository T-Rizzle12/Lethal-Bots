using BepInEx;
using LethalBots.AI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

namespace LethalBots.Managers
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; } = null!;

        public Dictionary<string, AudioClip?> DictAudioClipsByPath = new Dictionary<string, AudioClip?>();

        private const string voicesPath = "Audio\\Voices\\";

        // Supported audio extentions
        // Only supports what UnityWebRequestMultimedia.GetAudioClip supports
        private static readonly string[] SupportedExtentions = 
        { 
            ".ogg",
            ".wav",
            ".mp3"
        };

        private void Awake()
        {
            // Prevent multiple instances of AudioManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake audio manager =====================");

            try
            {
                LoadAllVoiceLanguageAudioAssets();
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error while loading voice audios, error : {ex.Message}");
            }
        }

        private void LoadAllVoiceLanguageAudioAssets()
        {
            // Try to load user custom voices
            string folderPath = Utility.CombinePaths(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID, voicesPath);
            if (Directory.Exists(folderPath))
            {
                // Load all paths
                LoadAllPaths(folderPath);
                return;
            }

            // Try to load decompress default voices
            folderPath = Path.Combine(Plugin.DirectoryName, voicesPath);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);

                // Load zip and extract it
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Assets.Audio.Voices.DefaultVoices.zip"))
                {
                    using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read))
                    {
                        // Works if using 7zip to re-zip archive from dropbox (extract and rezip), why ?
                        archive.ExtractToDirectory(folderPath);
                    }
                }
            }

            // Load all paths
            LoadAllPaths(folderPath);
        }

        /// <summary>
        /// Helper function to load all supported audio files from a folder path
        /// </summary>
        /// <param name="folderPath"></param>
        private void LoadAllPaths(string folderPath)
        {
            foreach (string fileType in SupportedExtentions)
            {
                string[] files = Directory.GetFiles(folderPath, "*" + fileType, SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    AddPath("file://" + file);
                }
            }
        }

        private void AddPath(string path)
        {
            if (DictAudioClipsByPath == null)
            {
                DictAudioClipsByPath = new Dictionary<string, AudioClip?>();
            }

            if (DictAudioClipsByPath.ContainsKey(path))
            {
                Plugin.LogWarning($"A path of the same has already been added, path {path}");
            }
            else
            {

                DictAudioClipsByPath.Add(path, null);
            }
        }

        public void SyncPlayAudio(string path, int lethalBotID)
        {
            string smallPath = string.Empty;

            try
            {
                int indexOfSmallPath = path.IndexOf(voicesPath);
                smallPath = path.Substring(indexOfSmallPath);
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error while loading voice audios, error : {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(smallPath))
            {
                Plugin.LogError($"Problem occured while getting the small path of audio clip, original path : {path}");
                return;
            }

            LethalBotManager.Instance.SyncPlayAudioLethalBot(lethalBotID, smallPath);
        }

        public void PlayAudio(string smallPathAudioClip, LethalBotVoice lethalBotVoice)
        {
            var audioClipByPath = DictAudioClipsByPath.FirstOrDefault(x => x.Key.Contains(smallPathAudioClip));
            AudioClip? audioClip = audioClipByPath.Value;
            if (audioClip == null)
            {
                StartCoroutine(LoadAudioAndPlay(audioClipByPath.Key, lethalBotVoice));
            }
            else
            {
                lethalBotVoice.PlayAudioClip(audioClip);
            }
            Plugin.LogDebug($"New audioClip loaded {smallPathAudioClip}");
        }

        private IEnumerator LoadAudioAndPlay(string uri, LethalBotVoice lethalBotVoice)
        {
            AudioType audioType = GetAudioType(uri);
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    lethalBotVoice.ResetAboutToTalk();
                    Plugin.LogError($"Error while loading audio file at {uri} : {www.error}");
                }
                else
                {
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
                    AddAudioClip(uri, audioClip);

                    lethalBotVoice.PlayAudioClip(audioClip);
                }
            }
        }

        /// <summary>
        /// Helper function to get AudioType from file extension
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private AudioType GetAudioType(string path)
        {
            AudioType audioType = AudioType.UNKNOWN;
            string extension = Path.GetExtension(path).ToLower();
            switch (extension)
            {
                case ".ogg":
                    audioType = AudioType.OGGVORBIS;
                    break;
                case ".wav":
                    audioType = AudioType.WAV;
                    break;
                case ".mp3":
                    audioType = AudioType.MPEG;
                    break;
                default:
                    Plugin.LogWarning($"Unsupported audio extension {extension} for path {path}");
                    break;
            }
            return audioType;
        }

        private void AddAudioClip(string path, AudioClip audioClip)
        {
            if (DictAudioClipsByPath == null)
            {
                DictAudioClipsByPath = new Dictionary<string, AudioClip?>();
            }

            if (DictAudioClipsByPath.ContainsKey(path))
            {
                DictAudioClipsByPath[path] = audioClip;
            }
            else
            {
                DictAudioClipsByPath.Add(path, audioClip);
            }
        }
    }
}