using LethalBots.Constants;
using LethalBots.Enums;
using System;
using Unity.Netcode;

namespace LethalBots.NetworkSerializers
{
    [Serializable]
    public struct ConfigIdentity : INetworkSerializable
    {
        public string name;
        public int suitID;
        public int suitConfigOption;
        public int defaultAIState;
        public string voiceFolder;
        public float volume;
        public float voicePitch;
        public string loadoutName;

        // Constructor with default values
        public ConfigIdentity()
        {
            name = ConfigConst.DEFAULT_BOT_NAME;
            suitConfigOption = (int)EnumOptionSuitConfig.Random;
            suitID = 0;
            voiceFolder = "Mathew_kelly";
            volume = 0.5f;
            defaultAIState = (int)EnumDefaultAIState.Dynamic;
            loadoutName = "Empty";
            // voice pitch set after
        }

        // Constructor with parameters
        public ConfigIdentity(string name, int suitID, int suitConfigOption, int defaultAIState, string voiceFolder, float volume, float voicePitch, string loadoutName)
        {
            this.name = name;
            this.suitID = suitID;
            this.suitConfigOption = suitConfigOption;
            this.defaultAIState = defaultAIState;
            this.voiceFolder = voiceFolder;
            this.volume = volume;
            this.voicePitch = voicePitch;
            this.loadoutName = loadoutName;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref suitID);
            serializer.SerializeValue(ref suitConfigOption);
            serializer.SerializeValue(ref defaultAIState);
            serializer.SerializeValue(ref voiceFolder);
            serializer.SerializeValue(ref volume);
            serializer.SerializeValue(ref voicePitch);
            serializer.SerializeValue(ref loadoutName);
        }

        public override string ToString()
        {
            return $"name: {name}, suitID {suitID}, suitConfigOption {suitConfigOption} {(EnumOptionSuitConfig)suitConfigOption}, defaultAIState {defaultAIState} {(EnumDefaultAIState)defaultAIState} voiceFolder {voiceFolder}, volume {volume}, voicePitch {voicePitch}, loadoutName {loadoutName}";
        }
    }

    public struct ConfigIdentitiesNetworkSerializable : INetworkSerializable
    {
        public ConfigIdentity[] ConfigIdentities;

        // INetworkSerializable
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ConfigIdentities);
        }
    }
}
