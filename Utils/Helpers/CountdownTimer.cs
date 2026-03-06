using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Utils.Helpers
{
    [Serializable]
    public class CountdownTimer : INetworkSerializable, IEquatable<CountdownTimer>
    {
        public float startTime = -1.0f;
        public float endTime = -1.0f;

        /// <summary>
        /// Restarts the Interval Timer
        /// </summary>
        public void Reset()
        {
            startTime = -1.0f;
            endTime = -1.0f;
        }

        /// <summary>
        /// Starts the Countdown Timer with the given <paramref name="time"/>
        /// </summary>
        /// <param name="time">How long should this timer run</param>
        public void Start(float time)
        {
            startTime = Time.realtimeSinceStartup;
            endTime = Time.realtimeSinceStartup + (time >= 0 ? time : 0);
        }

        /// <summary>
        /// Was the Countdown Timer started?
        /// </summary>
        /// <returns>true: if we were started; otherwise false</returns>
        public bool HasStarted()
        {
            return endTime > 0f;
        }

        /// <summary>
        /// How long has this timer been running!
        /// </summary>
        /// <returns></returns>
        public float GetElapsedTime()
        {
            return HasStarted() ? Time.realtimeSinceStartup - startTime : -1.0f;
        }

        /// <summary>
        /// Has this Countdown Timer elapsed
        /// </summary>
        /// <returns></returns>
        public bool Elapsed()
        {
            return HasStarted() && endTime <= Time.realtimeSinceStartup;
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
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(CountdownTimer? left, CountdownTimer? right)
        {
            return !(left == right);
        }
    }
}
