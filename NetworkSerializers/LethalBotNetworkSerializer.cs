using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.NetworkSerializers
{
    // A helper class for custom seialization functions!
    public static class LethalBotNetworkSerializer
    {
        public static void SerializeNullable<T, TReaderWriter>(BufferSerializer<TReaderWriter> serializer, ref T? value)
            where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T>
            where TReaderWriter : IReaderWriter
        {
            bool hasValue = value.HasValue;
            serializer.SerializeValue(ref hasValue);

            if (hasValue)
            {
                T temp = value.GetValueOrDefault();
                serializer.SerializeValue(ref temp);
                if (serializer.IsReader)
                {
                    value = temp;
                }
            }
            else if (serializer.IsReader)
            {
                value = null;
            }
        }

        public static void SerializeNullable<TReaderWriter>(BufferSerializer<TReaderWriter> serializer, ref Vector3? value)
            where TReaderWriter : IReaderWriter
        {
            bool hasValue = value.HasValue;
            serializer.SerializeValue(ref hasValue);

            if (hasValue)
            {
                Vector3 temp = value.GetValueOrDefault();
                serializer.SerializeValue(ref temp);
                if (serializer.IsReader)
                {
                    value = temp;
                }
            }
            else if (serializer.IsReader)
            {
                value = null;
            }
        }

        public static void SerializeNullable<TReaderWriter>(BufferSerializer<TReaderWriter> serializer, ref NetworkObjectReference? value)
            where TReaderWriter : IReaderWriter
        {
            bool hasValue = value.HasValue;
            serializer.SerializeValue(ref hasValue);

            if (hasValue)
            {
                NetworkObjectReference temp = value.GetValueOrDefault();
                serializer.SerializeValue(ref temp);
                if (serializer.IsReader)
                {
                    value = temp;
                }
            }
            else if (serializer.IsReader)
            {
                value = null;
            }
        }

        public static void SerializeStringArray<TReaderWriter>(BufferSerializer<TReaderWriter> serializer, ref string[] value)
            where TReaderWriter : IReaderWriter
        {
            int count = value.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            { 
                value = new string[count]; 
            }

            for (int i = 0; i < count; i++)
            { 
                serializer.SerializeValue(ref value[i]); 
            }
        }
    }
}
