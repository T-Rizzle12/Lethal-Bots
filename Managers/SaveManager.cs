using LethalBots.AI;
using LethalBots.Enums;
using LethalBots.NetworkSerializers;
using LethalBots.SaveAdapter;
using Newtonsoft.Json;
using System;
using Unity.Netcode;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager in charge of loading and saving data relevant to the mod LethalBot
    /// </summary>
    public class SaveManager : NetworkBehaviour
    {
        private const string SAVE_DATA_KEY = "LETHAL_BOTS_SAVE_DATA";

        public static SaveManager Instance { get; private set; } = null!;

        private SaveFile Save = null!;
        private ClientRpcParams ClientRpcParams = new ClientRpcParams();

        /// <summary>
        /// When manager awake, read the save file and load infos for LethalBot, only for the host
        /// </summary>
        private void Awake()
        {
            // Prevent multiple instances of the SaveManager
            if (Instance != null && Instance != this)
            {
                if (Instance.IsSpawned && Instance.IsServer)
                {
                    Instance.NetworkObject.Despawn(destroy: true);
                }
                else
                {
                    Destroy(Instance.gameObject);
                }
            }

            Instance = this;
            FetchSaveFile();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!base.NetworkManager.IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    // Destory Local manager
                    Destroy(Instance.gameObject);
                }
                Instance = this;
            }
        }

        /// <summary>
        /// Get the save file and load it into the save data, or create a new one if no save file found
        /// </summary>
        private void FetchSaveFile()
        {
            string saveFile = GameNetworkManager.Instance.currentSaveFileName;

            try
            {
                string json = (string)ES3.Load(key: SAVE_DATA_KEY, defaultValue: null, filePath: saveFile);
                if (json != null)
                {
                    Plugin.LogInfo($"Loading save file.");
                    Save = JsonConvert.DeserializeObject<SaveFile>(json) ?? new SaveFile();
                    if (Plugin.Config.SpawnIdentitiesRandomly)
                    {
                        Plugin.LogInfo($"Plugin config set to spawn identities randomly, setting all identities to available in save file.");
                        foreach (IdentitySaveFile identitySaveFile in Save.IdentitiesSaveFiles)
                        {
                            identitySaveFile.Status = (int)EnumStatusIdentity.Available; // Ensure status is set to Available, as it might be saved as ToSpawn
                        }
                    }
                }
                else
                {
                    Plugin.LogInfo($"No save file found for slot. Creating new.");
                    Save = new SaveFile();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error when loading save file : {ex.Message}");
            }
        }

        /// <summary>
        /// Get save file, serialize save data and save it using <see cref="SAVE_DATA_KEY"><c>SAVE_DATA_KEY</c></see>, only host
        /// </summary>
        public void SavePluginInfos()
        {
            if (!NetworkManager.IsHost)
            {
                return;
            }

            if (!StartOfRound.Instance.inShipPhase)
            {
                return;
            }
            if (StartOfRound.Instance.isChallengeFile)
            {
                return;
            }

            Plugin.LogInfo($"Saving data for LethalBot plugin.");
            string saveFile = GameNetworkManager.Instance.currentSaveFileName;
            SaveInfosInSave();
            string json = JsonConvert.SerializeObject(Save);
            ES3.Save(key: SAVE_DATA_KEY, value: json, filePath: saveFile);
        }

        /// <summary>
        /// Update save data with runtime data from managers
        /// </summary>
        private void SaveInfosInSave()
        {
            Save.IdentitiesSaveFiles = new IdentitySaveFile[IdentityManager.Instance.LethalBotIdentities.Length];
            for (int i = 0; i < IdentityManager.Instance.LethalBotIdentities.Length; i++)
            {
                LethalBotIdentity lethalBotIdentity = IdentityManager.Instance.LethalBotIdentities[i];
                int suitID = i < Plugin.Config.ConfigIdentities.configIdentities.Length ? Plugin.Config.ConfigIdentities.configIdentities[i].suitID : -1;
                if (suitID != -1 && (EnumOptionSuitConfig)Plugin.Config.ConfigIdentities.configIdentities[i].suitConfigOption == EnumOptionSuitConfig.Random)
                {
                    suitID = -1;
                }
                EnumStatusIdentity status = lethalBotIdentity.Status;
                IdentitySaveFile identitySaveFile = new IdentitySaveFile()
                {
                    IdIdentity = lethalBotIdentity.IdIdentity,
                    Hp = lethalBotIdentity.Hp,
                    SuitID = suitID < 0 ? -1 : suitID,
                    XP = lethalBotIdentity.XP ?? -1,
                    Level = lethalBotIdentity.Level < 0 ? 0 : lethalBotIdentity.Level,
                    Status = lethalBotIdentity.Status == EnumStatusIdentity.Spawned ? (int)EnumStatusIdentity.ToSpawn : (int)lethalBotIdentity.Status
                };

                Plugin.LogDebug($"Saving identity {lethalBotIdentity.ToString()}");
                Save.IdentitiesSaveFiles[i] = identitySaveFile;
            }
        }

        /// <summary>
        /// Load data into managers from save data
        /// </summary>
        public void LoadAllDataFromSave()
        {
            if (Save.IdentitiesSaveFiles == null)
            {
                return;
            }

            if (Save.IdentitiesSaveFiles.Length > IdentityManager.Instance.LethalBotIdentities.Length)
            {
                IdentityManager.Instance.ExpandWithNewDefaultIdentities(Save.IdentitiesSaveFiles.Length - IdentityManager.Instance.LethalBotIdentities.Length);
            }

            for (int i = 0; i < IdentityManager.Instance.LethalBotIdentities.Length; i++)
            {
                LethalBotIdentity identity = IdentityManager.Instance.LethalBotIdentities[i];
                if (identity.IdIdentity >= Save.IdentitiesSaveFiles.Length)
                {
                    continue;
                }
                IdentitySaveFile identitySaveFile = Save.IdentitiesSaveFiles[identity.IdIdentity];
                identity.UpdateIdentity(identitySaveFile.Hp,
                                        identitySaveFile.SuitID < 0 ? null : identitySaveFile.SuitID,
                                        identitySaveFile.XP < 0 ? null : identitySaveFile.XP,
                                        identitySaveFile.Level < 0 ? 0 : identitySaveFile.Level,
                                        (EnumStatusIdentity)identitySaveFile.Status);
                Plugin.LogDebug($"Loaded and updated identity from save : {identity.ToString()}");
            }
        }

        #region Sync loaded save file

        /// <summary>
        /// Send to the specific client, the data load by the server/host, so the client can initialize its managers
        /// </summary>
        /// <remarks>
        /// Only the host loads the data from the file, so the clients needs to request the server/host for the save data to syn
        /// </remarks>
        /// <param name="clientId">Client id of caller</param>
        [ServerRpc(RequireOwnership = false)]
        public void SyncCurrentValuesServerRpc(ulong clientId)
        {
            Plugin.LogDebug($"Client {clientId} ask server/host {NetworkManager.LocalClientId} to SyncCurrentStateValuesServerRpc");
            ClientRpcParams.Send = new ClientRpcSendParams()
            {
                TargetClientIds = new ulong[] { clientId }
            };

            IdentitySaveFileNetworkSerializable[] identitiesSaveNS = new IdentitySaveFileNetworkSerializable[IdentityManager.Instance.LethalBotIdentities.Length];
            for (int i = 0; i < identitiesSaveNS.Length; i++)
            {
                LethalBotIdentity lethalBotIdentity = IdentityManager.Instance.LethalBotIdentities[i];
                IdentitySaveFileNetworkSerializable identitySaveNS = new IdentitySaveFileNetworkSerializable()
                {
                    IdIdentity = lethalBotIdentity.IdIdentity,
                    Hp = lethalBotIdentity.Hp,
                    SuitID = lethalBotIdentity.SuitID.HasValue ? lethalBotIdentity.SuitID.Value : -1,
                    XP = lethalBotIdentity.XP ?? -1,
                    Level = lethalBotIdentity.Level < 0 ? 0 : lethalBotIdentity.Level,
                    Status = (int)lethalBotIdentity.Status
                };

                identitiesSaveNS[i] = identitySaveNS;
            }

            SaveNetworkSerializable saveNS = new SaveNetworkSerializable()
            {
                Identities = identitiesSaveNS
            };

            SyncCurrentValuesClientRpc(saveNS, ClientRpcParams);
        }

        /// <summary>
        /// Client side, sync the save data send by the server/host
        /// </summary>
        /// <param name="clientRpcParams"></param>
        [ClientRpc]
        private void SyncCurrentValuesClientRpc(SaveNetworkSerializable saveNetworkSerializable,
                                                ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                return;
            }

            if (saveNetworkSerializable.Identities.Length > IdentityManager.Instance.LethalBotIdentities.Length)
            {
                IdentityManager.Instance.ExpandWithNewDefaultIdentities(saveNetworkSerializable.Identities.Length - IdentityManager.Instance.LethalBotIdentities.Length);
            }

            for (int i = 0; i < IdentityManager.Instance.LethalBotIdentities.Length; i++)
            {
                LethalBotIdentity identity = IdentityManager.Instance.LethalBotIdentities[i];
                if (identity.IdIdentity >= saveNetworkSerializable.Identities.Length)
                {
                    return;
                }
                IdentitySaveFileNetworkSerializable identitySaveNS = saveNetworkSerializable.Identities[i];
                identity.UpdateIdentity(identitySaveNS.Hp,
                                        identitySaveNS.SuitID < 0 ? null : identitySaveNS.SuitID,
                                        identitySaveNS.XP < 0 ? null : identitySaveNS.XP,
                                        identitySaveNS.Level < 0 ? 0 : identitySaveNS.Level,
                                        (EnumStatusIdentity)identitySaveNS.Status);

                Plugin.LogDebug($"Client {NetworkManager.LocalClientId} : sync in current values, identity {identity.ToString()}");
            }
        }

        #endregion
    }
}
