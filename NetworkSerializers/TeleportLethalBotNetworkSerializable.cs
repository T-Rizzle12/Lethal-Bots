using Unity.Netcode;
using UnityEngine;

namespace LethalBots.NetworkSerializers
{
    public struct TeleportLethalBotNetworkSerializable : INetworkSerializable
    {
        public Vector3 Pos;
        public bool? SetOutside;
        public bool WithRotation;
        public float Rot;
        public bool AllowInteractTrigger;
        public NetworkObjectReference? TargetEntrance;
        public bool SkipNavMeshCheck;

        public TeleportLethalBotNetworkSerializable()
        {
            SetOutside = null;
            WithRotation = false;
            Rot = 0f;
            AllowInteractTrigger = false;
            TargetEntrance = null;
            SkipNavMeshCheck = false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Pos);
            LethalBotNetworkSerializer.SerializeNullable(serializer, ref SetOutside);
            serializer.SerializeValue(ref WithRotation);
            serializer.SerializeValue(ref Rot);
            serializer.SerializeValue(ref AllowInteractTrigger);
            LethalBotNetworkSerializer.SerializeNullable(serializer, ref TargetEntrance);
            serializer.SerializeValue(ref SkipNavMeshCheck);
        }
    }
}
