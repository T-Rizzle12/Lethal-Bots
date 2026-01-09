using LethalBots.Enums;
using System;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.NetworkSerializers
{
    [Serializable]
    public struct SpawnLethalBotParamsNetworkSerializable : INetworkSerializable
    {
        public int IndexNextLethalBot;
        public int? IndexNextPlayerObject = null;
        public int LethalBotIdentityID;
        public int Hp;
        public int SuitID;
        public EnumSpawnAnimation enumSpawnAnimation;
        public Vector3? SpawnPosition;
        public float YRot;
        public bool IsOutside;
        public bool ShouldDestroyDeadBody;

        public SpawnLethalBotParamsNetworkSerializable()
        {
        }

        // INetworkSerializable
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref IndexNextLethalBot);
            LethalBotNetworkSerializer.SerializeNullable(serializer, ref IndexNextPlayerObject);
            serializer.SerializeValue(ref LethalBotIdentityID);
            serializer.SerializeValue(ref Hp);
            serializer.SerializeValue(ref SuitID);
            serializer.SerializeValue(ref enumSpawnAnimation);
            LethalBotNetworkSerializer.SerializeNullable(serializer, ref SpawnPosition);
            serializer.SerializeValue(ref YRot);
            serializer.SerializeValue(ref IsOutside);
            serializer.SerializeValue(ref ShouldDestroyDeadBody);
        }
    }
}
