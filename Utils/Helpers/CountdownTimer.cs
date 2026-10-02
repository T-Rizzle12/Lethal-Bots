using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Utils.Helpers
{
    [Serializable]
    public struct CountdownTimer : INetworkSerializable, IEquatable<CountdownTimer>
    {
        public const double INVALID_TIME = -1.0f;
        public double startTime;
        public double endTime;

        public CountdownTimer()
        {
            startTime = INVALID_TIME;
            endTime = INVALID_TIME;
        }

        public CountdownTimer(float time) : this()
        {
            Start(time);
        }

        /// <summary>
        /// Restarts the Interval Timer
        /// </summary>
        public void Reset()
        {
            startTime = INVALID_TIME;
            endTime = INVALID_TIME;
        }

        /// <summary>
        /// Starts the Countdown Timer with the given <paramref name="time"/>
        /// </summary>
        /// <param name="time">How long should this timer run</param>
        public void Start(float time)
        {
            double now = GetServerTime();
            startTime = now;
            endTime = now + (time >= 0 ? time : 0);
        }

        /// <summary>
        /// Was the Countdown Timer started?
        /// </summary>
        /// <returns>true: if we were started; otherwise false</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasStarted()
        {
            return endTime > 0f;
        }

        /// <summary>
        /// How long has this timer been running!
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetElapsedTime()
        {
            return HasStarted() ? (GetServerTime() - startTime) : INVALID_TIME;
        }

        /// <summary>
        /// Has this Countdown Timer elapsed
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Elapsed()
        {
            return HasStarted() && endTime <= GetServerTime();
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="CountdownTimer"/> instance
        /// </summary>
        /// <returns></returns>
        public CountdownTimer Clone()
        {
            return new CountdownTimer()
            {
                startTime = this.startTime,
                endTime = this.endTime
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref startTime);
            serializer.SerializeValue(ref endTime);
        }

        public bool Equals(CountdownTimer other)
        {
            return startTime == other.startTime
                && endTime == other.endTime;
        }

        public override bool Equals(object? obj)
        {
            return obj is CountdownTimer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(startTime, endTime);
        }

        public static bool operator ==(CountdownTimer? left, CountdownTimer? right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CountdownTimer? left, CountdownTimer? right)
        {
            return !(left == right);
        }

        internal static double GetServerTime()
        {
            var networkManager = NetworkManager.Singleton;
            return networkManager != null && networkManager.IsListening ? networkManager.ServerTime.Time : INVALID_TIME;
        }
    }
}
