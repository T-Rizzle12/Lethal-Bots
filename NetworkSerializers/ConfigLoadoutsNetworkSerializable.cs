using LethalLib.Modules;
using System;
using System.Text;
using System.Xml.Linq;
using Unity.Netcode;

namespace LethalBots.NetworkSerializers
{
    [Serializable]
    public struct ConfigLoadout : INetworkSerializable
    {
        public string name; // Name of the loadout. Used by the identity manager to find our desired loadout!
        public string[] itemNames; // The names of all of the items in the loadout

        public ConfigLoadout()
        {
            name = "Empty";
            itemNames = new string[0];
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref name);

            // Sigh, stupid Unity doesn't support string arrays by default.
            // I have to do it myself!
            LethalBotNetworkSerializer.SerializeStringArray(serializer, ref itemNames);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Loadout Name: {name}");
            sb.AppendLine("Items:");
            if (itemNames.Length == 0)
            {
                sb.AppendLine("(None)");
            }
            else
            {
                foreach (string item in itemNames)
                {
                    if (item == null)
                    {
                        sb.AppendLine("(null Item)");
                        continue;
                    }
                    sb.AppendLine($"Name: {item}");
                }
            }
            return sb.ToString();
        }
    }

    public struct ConfigLoadoutsNetworkSerializable : INetworkSerializable
    {
        public ConfigLoadout[] ConfigLoadouts;

        // INetworkSerializable
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ConfigLoadouts);
        }
    }
}
