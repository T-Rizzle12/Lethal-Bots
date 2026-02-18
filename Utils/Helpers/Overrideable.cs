using LethalBots.AI;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Simple class used by <see cref="NpcController"/> so I can have variables that can ignore updates
    /// </summary>
    public sealed class Overrideable<T>
    {
        private T _value;

        /// <summary>
        /// When true, incoming updates should not overwrite the value.
        /// Automatically cleared after Apply().
        /// </summary>
        public bool IsOverridden { get; private set; }

        /// <summary>
        /// Returns the value, or sets it directly which also sets <see cref="IsOverridden"/> to true to prevent the next update from overwriting it.
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                IsOverridden = true;
            }
        }

        internal Overrideable(T initialValue)
        {
            _value = initialValue;
            IsOverridden = false;
        }

        /// <summary>
        /// Apply an external update unless the value was overridden.
        /// </summary>
        /// <remarks>
        /// This is called by <see cref="PlayerControllerBPatch.Update_PreFix(PlayerControllerB, ref bool, bool, bool, ref float, ref bool, ref float, ref bool, ref bool, ref float, ref bool, ref float, ref float, ref bool, ref float, ref float, ref float, ref float)"/>
        /// </remarks>
        public void Apply(T newValue)
        {
            if (!IsOverridden)
            {
                _value = newValue;
            }
            IsOverridden = false;
        }

        public static implicit operator T(Overrideable<T> overrideable)
        {
            return overrideable._value;
        }
    }
}
