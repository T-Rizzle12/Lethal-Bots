using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Utils.Helpers
{
    [Serializable]
    public class IntervalTimer : INetworkSerializable, IEquatable<IntervalTimer>
    {
        public float timestamp = -1.0f;

        /// <summary>
        /// Restarts the Interval Timer
        /// </summary>
        public void Reset()
        {
            timestamp = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Starts the Interval Timer
        /// </summary>
        public void Start()
        {
            timestamp = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Stops the Interval Timer
        /// </summary>
        public void Invalidate()
        {
            timestamp = -1.0f;
        }

        /// <summary>
        /// Was the Interval Timer started?
        /// </summary>
        /// <returns>true: if we were started; otherwise false</returns>
        public bool HasStarted()
        {
            return timestamp > 0f;
        }

        /// <summary>
        /// How long has this timer been running!
        /// </summary>
        /// <returns></returns>
        public float GetElapsedTime()
        {
            return HasStarted() ? Time.realtimeSinceStartup - timestamp : -1.0f;
        }

        /// <summary>
        /// Has the timer been running longer than the given <paramref name="duration"/>
        /// </summary>
        /// <param name="duration">The duration to test!</param>
        /// <returns>true: if we have been running longer than <paramref name="duration"/>; otherwise false</returns>
        public bool IsGreaterThan(float duration)
        {
            return GetElapsedTime() > duration;
        }

        /// <summary>
        /// Has the timer been running less than the given <paramref name="duration"/>
        /// </summary>
        /// <param name="duration">The duration to test!</param>
        /// <returns>true: if we have been running less than <paramref name="duration"/>; otherwise false</returns>
        public bool IsLessThan(float duration)
        {
            return GetElapsedTime() < duration;
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="IntervalTimer"/> instance
        /// </summary>
        /// <returns></returns>
        public IntervalTimer Clone()
        {
            return new IntervalTimer()
            {
                timestamp = this.timestamp
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref timestamp);
        }

        public bool Equals(IntervalTimer other)
        {
            return timestamp == other.timestamp;
        }

        public override bool Equals(object? obj)
        {
            return obj is IntervalTimer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(timestamp);
        }

        public static bool operator ==(IntervalTimer? left, IntervalTimer? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(IntervalTimer? left, IntervalTimer? right)
        {
            return !(left == right);
        }
    }
}
