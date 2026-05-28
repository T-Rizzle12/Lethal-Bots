using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that is used to find a singleton instance of a Unity <see cref="Object"/>
    /// </summary>
    /// <remarks>
    /// This has build-in throttling to keep <see cref="findSingletonFunc"/> from being called every frame
    /// </remarks>
    /// <typeparam name="T">The Unity <see cref="Object"/> to make the Singleton for</typeparam>
    public sealed class Singleton<T> 
        where T : Object
    {
        private readonly UpdateLimiter nextFindSingletonCheck;

        private Func<T?> findSingletonFunc;

        /// <summary>
        /// Gets or sets the cached singleton instance of <typeparamref name="T"/>.<br/>
        /// If the cached field is null and the <see cref="nextFindSingletonCheck"/> is allowed to update, 
        /// this invokes <see cref="findSingletonFunc"/> and caches its result.
        /// </summary>
        public T? Instance
        {
            set;
            get
            {
                if (field == null && nextFindSingletonCheck.CanUpdate())
                {
                    nextFindSingletonCheck.Invalidate();
                    field = findSingletonFunc.Invoke();
                }
                return field;
            }
        }

        internal Singleton(float updateInterval = 0.5f)
        {
            nextFindSingletonCheck = new UpdateLimiter(updateInterval);
            this.findSingletonFunc = Object.FindObjectOfType<T>;
        }

        internal Singleton(Func<T?> findSingletonFunc, float updateInterval = 0.5f)
        {
            nextFindSingletonCheck = new UpdateLimiter(updateInterval);
            this.findSingletonFunc = findSingletonFunc;
        }

        /// <summary>
        /// Checks if the instance of <typeparamref name="T"/> is not <see langword="null"/>
        /// </summary>
        /// <returns><see langword="true"/> if we have a valid instance of <typeparamref name="T"/>; otherwise <see langword="false"/></returns>
        [MemberNotNullWhen(true, nameof(Instance))]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid()
        {
            return Instance != null;
        }

        /// <summary>
        /// Checks if the instance of <typeparamref name="T"/> is not <see langword="null"/>
        /// </summary>
        /// <remarks>
        /// This also returns the instance as well for easy access. This will be <see langword="null"/> if instance is invalid
        /// </remarks>
        /// <returns><see langword="true"/> if we have a valid instance of <typeparamref name="T"/>; otherwise <see langword="false"/></returns>
        [MemberNotNullWhen(true, nameof(Instance))]
        public bool TryGet([NotNullWhen(true)] out T? instance)
        {
            instance = Instance;
            return instance != null;
        }

        public static implicit operator T?(Singleton<T> instance)
        {
            return instance.Instance;
        }
    }
}
