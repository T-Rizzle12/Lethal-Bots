using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper struct that holds a value that should only be updated every once in a while.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct CachedValue<T>
    {
        private readonly UpdateLimiter nextValueUpdate;

        public T Value 
        { 
            get; 
            set
            {
                field = value;
                MarkUpdated();
            }
        } = default!;

        public CachedValue(T value = default!, float updateInterval = 1.0f) 
        {
            nextValueUpdate = new UpdateLimiter(updateInterval);
            Value = value;
        }

        /// <inheritdoc cref="UpdateLimiter.CanUpdate"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanUpdate()
        {
            return nextValueUpdate.CanUpdate();
        }

        /// <inheritdoc cref="UpdateLimiter.Invalidate"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkUpdated()
        {
            nextValueUpdate.Invalidate();
        }

        public static implicit operator T(CachedValue<T> cachedValue)
        {
            return cachedValue.Value;
        }
    }
}
