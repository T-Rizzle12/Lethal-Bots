using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.NetworkSerializers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling bot identities in the game.
    /// </summary>
    public class IdentityManager : MonoBehaviour
    {
        public static IdentityManager Instance { get; private set; } = null!;

        public LethalBotIdentity[] LethalBotIdentities = null!;

        private ConfigIdentity[] configIdentities = null!;

        private void Awake()
        {
            // Prevent multiple instances of IdentityManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake IdentityManager =====================");
        }

        private void Update()
        {
            if (LethalBotIdentities == null)
            {
                return;
            }

            LethalBotIdentity lethalBotIdentity;
            for (int i = 0; i < LethalBotIdentities.Length; i++)
            {
                lethalBotIdentity = LethalBotIdentities[i];
                if (lethalBotIdentity != null
                    && lethalBotIdentity.Voice != null)
                {
                    lethalBotIdentity.Voice.ReduceCooldown(Time.deltaTime);
                }
            }
        }

        public void InitIdentities(ConfigIdentity[] configIdentities)
        {
            Plugin.LogDebug($"InitIdentities, nbIdentities {configIdentities.Length}");
            LethalBotIdentities = new LethalBotIdentity[configIdentities.Length];
            this.configIdentities = configIdentities;

            // InitNewIdentity
            for (int i = 0; i < configIdentities.Length; i++)
            {
                LethalBotIdentities[i] = InitNewIdentity(i);
            }
        }

        private LethalBotIdentity InitNewIdentity(int idIdentity)
        {
            // Get a config identity
            string name;
            ConfigIdentity configIdentity;
            if (idIdentity >= this.configIdentities.Length)
            {
                configIdentity = ConfigConst.DEFAULT_CONFIG_IDENTITY;
                name = string.Format(configIdentity.name, idIdentity);
                configIdentity.voicePitch = UnityEngine.Random.Range(0.8f, 1.2f);
            }
            else
            {
                configIdentity = this.configIdentities[idIdentity];
                name = configIdentity.name;
            }

            // Suit
            int? suitID = null;
            EnumOptionSuitConfig suitConfig;
            if (!Enum.IsDefined(typeof(EnumOptionSuitConfig), configIdentity.suitConfigOption))
            {
                Plugin.LogWarning($"Could not get option for bot suit config in config file, value {configIdentity.suitConfigOption}, for {configIdentity.name}, now using random suit.");
                suitConfig = EnumOptionSuitConfig.Random;
            }
            else
            {
                suitConfig = (EnumOptionSuitConfig)configIdentity.suitConfigOption;
            }

            switch (suitConfig)
            {
                case EnumOptionSuitConfig.Fixed:
                    suitID = configIdentity.suitID;
                    break;

                case EnumOptionSuitConfig.Random:
                    suitID = null;
                    break;

            }

            // Voice
            LethalBotVoice voice = new LethalBotVoice(configIdentity.voiceFolder,
                                                configIdentity.volume, 
                                                configIdentity.voicePitch);

            // Loadout
            LethalBotLoadout loadout = LoadoutManager.Instance.GetLethalBotLoadoutWithName(configIdentity.loadoutName ?? string.Empty);

            // LethalBotIdentity
            return new LethalBotIdentity(idIdentity, name, suitID, voice, loadout);
        }

        public string[] GetIdentitiesNamesLowerCaseWithoutSpace()
        {
            if (LethalBotIdentities == null)
            {
                return new string[0];
            }

            return LethalBotIdentities
                        .Select(x => string.Join(' ', x.Name).ToLowerInvariant())
                        .ToArray();
        }

        public LethalBotIdentity? this[int index]
        {
            get
            {
                if (index < 0 || index >= LethalBotIdentities.Length)
                {
                    throw new IndexOutOfRangeException();
                }
                return LethalBotIdentities[index];
            }
        }

        public LethalBotIdentity? GetIdentiyFromIndex(int index)
        {
            if (index < 0 || index > LethalBotIdentities.Length)
            {
                return null;
            }
            return LethalBotIdentities[index];
        }

        public int GetNewIdentityToSpawn()
        {
            // Get identity
            int idNewIdentity;
            if (Plugin.Config.SpawnIdentitiesRandomly)
            {
                idNewIdentity = GetRandomAvailableIdentityIndex();
            }
            else
            {
                idNewIdentity = GetNextAvailableIdentityIndex();
            }

            // No more identities
            // Create new ones
            if (idNewIdentity == -1)
            {
                ExpandWithNewDefaultIdentities(numberToAdd: 1);
                return LethalBotIdentities.Length - 1;
            }

            return idNewIdentity;
        }

        public int GetRandomAvailableIdentityIndex()
        {
            LethalBotIdentity[] availableIdentities = LethalBotIdentities.FilterAvailable().ToArray();
            if (availableIdentities.Length == 0)
            {
                return -1;
            }

            Random randomInstance = new Random();
            int randomIndex = randomInstance.Next(0, availableIdentities.Length);
            return availableIdentities[randomIndex].IdIdentity;
        }

        public int GetNextAvailableIdentityIndex()
        {
            LethalBotIdentity[] availableIdentities = LethalBotIdentities.FilterAvailable().ToArray();
            if (availableIdentities.Length == 0)
            {
                return -1;
            }

            return availableIdentities[0].IdIdentity;
        }

        public void ExpandWithNewDefaultIdentities(int numberToAdd)
        {
            Array.Resize(ref LethalBotIdentities, LethalBotIdentities.Length + numberToAdd);
            for (int i = LethalBotIdentities.Length - numberToAdd; i < LethalBotIdentities.Length; i++)
            {
                LethalBotIdentities[i] = InitNewIdentity(i);
            }
        }

        public LethalBotIdentity? FindIdentityFromBodyName(string bodyName)
        {
            string name = bodyName.Replace("Body of ", "");
            return LethalBotIdentities.FirstOrDefault(x => x.Name == name);
        }

        public int GetNbIdentitiesAvailable()
        {
            return Plugin.Config.MaxBotsAllowedToSpawn - LethalBotIdentities.FilterToDropOrSpawned().Count();
        }

        public int GetNbIdentitiesToDrop()
        {
            return LethalBotIdentities.FilterToDrop().Count();
        }

        public int GetNbIddentitiesToDropOrSpawned()
        {
            return LethalBotIdentities.FilterToDropOrSpawned().Count();
        }

        public int[] GetIdentitiesToDrop()
        {
            if (LethalBotIdentities == null)
            {
                return new int[0];
            }

            return LethalBotIdentities
                        .FilterToDrop()
                        .Select(x => x.IdIdentity)
                        .ToArray();
        }

        public int[] GetIdentitiesAvailable()
        {
            if (LethalBotIdentities == null)
            {
                return new int[0];
            }

            return LethalBotIdentities
                .FilterAvailable()
                .Select(x => x.IdIdentity)
                .ToArray();
        }

        public bool IsAnIdentityToDrop()
        {
            return LethalBotIdentities.FilterToDrop().Any();
        }

        public int GetNbIdentitiesSpawned()
        {
            return LethalBotIdentities.FilterSpawnedAlive().Count();
        }
    }

    internal static class IdentityEnumerableExtension
    {
        public static IEnumerable<LethalBotIdentity> FilterAvailable(this IEnumerable<LethalBotIdentity> enumerable)
        {
            return enumerable.Where(x => x.Status == EnumStatusIdentity.Available);
        }

        public static IEnumerable<LethalBotIdentity> FilterToDrop(this IEnumerable<LethalBotIdentity> enumerable)
        {
            return enumerable.Where(x => x.Status == EnumStatusIdentity.ToSpawn);
        }

        public static IEnumerable<LethalBotIdentity> FilterToDropOrSpawned(this IEnumerable<LethalBotIdentity> enumerable)
        {
            return enumerable.Where(x => x.Status == EnumStatusIdentity.ToSpawn || (x.Status == EnumStatusIdentity.Spawned && x.Alive));
        }

        public static IEnumerable<LethalBotIdentity> FilterSpawnedAlive(this IEnumerable<LethalBotIdentity> enumerable)
        {
            return enumerable.Where(x => x.Status == EnumStatusIdentity.Spawned && x.Alive);
        }
    }

    /// <summary>
    /// A subclass representing the statistics of a bot at the end of a round.
    /// </summary>
    public sealed class EndOfGameBotStats
    {
        public LethalBotIdentity Identity { get; set; }
        public PlayerControllerB BotController { get; set; }
        public bool IsAlive { get; set; }

        public EndOfGameBotStats(LethalBotIdentity identity, PlayerControllerB botController, bool isAlive = false)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity), "Identity cannot be null.");
            BotController = botController ?? throw new ArgumentNullException(nameof(botController), "Bot PlayerControllerB cannot be null.");
            IsAlive = isAlive;
        }
    }
}
