using LethalBots.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// This is basicially <see cref="Queue{T}"/>, but its designed to allow me to set the priority of messages!
    /// </summary>
    public class PriorityQueue<T>
    {
        private readonly Dictionary<QueuePriority, Queue<T>> _queues;
        public PriorityQueue()
        {
            _queues = new Dictionary<QueuePriority, Queue<T>>()
                {
                    { QueuePriority.Critical, new Queue<T>() },
                    { QueuePriority.High, new Queue<T>() },
                    { QueuePriority.Normal, new Queue<T>() },
                    { QueuePriority.Low, new Queue<T>() }
                };
        }

        /// <inheritdoc cref="Queue{T}.Enqueue(T)"/>
        public void Enqueue(T message, QueuePriority priority = QueuePriority.Low)
        {
            _queues[priority].Enqueue(message);
        }

        /// <inheritdoc cref="Queue{T}.TryDequeue(out T)"/>
        public bool TryDequeue(out T? message)
        {
            for (var priority = QueuePriority.Critical; priority <= QueuePriority.Low; priority++)
            {
                var q = _queues[priority];
                if (q.TryDequeue(out message))
                {
                    return true;
                }
            }

            message = default;
            return false;
        }

        /// <inheritdoc cref="Queue{T}.TryPeek(out T)"/>
        public bool TryPeek(out T? message)
        {
            for (var priority = QueuePriority.Critical; priority <= QueuePriority.Low; priority++)
            {
                var q = _queues[priority];
                if (q.TryPeek(out message))
                {
                    return true;
                }
            }

            message = default;
            return false;
        }

        /// <inheritdoc cref="Queue{T}.Count"/>
        public int Count
        {
            get
            {
                int total = 0;
                foreach (var q in _queues.Values)
                {
                    total += q.Count;
                }
                return total;
            }
        }
    }
}
