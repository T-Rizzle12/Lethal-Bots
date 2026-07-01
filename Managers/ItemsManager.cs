using LethalBots.Utils.Helpers;
using LethalBots.Utils.Items.Weapons;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling items for the bots in the game.
    /// </summary>
    public class ItemsManager : MonoBehaviour
    {
        public static ItemsManager Instance { get; private set; } = null!;

        /// <summary>
        /// A hook that is called after <see cref="ItemsManager"/> registers the default weapons
        /// </summary>
        public static UnityEvent<ItemsManager> RegisterWeapons = new UnityEvent<ItemsManager>();

        /// <summary>
        /// This is the base type that all weapons inherit from
        /// </summary>
        private static readonly Type RootWeaponType = typeof(GrabbableObject);

        /// <summary>
        /// Actual info for each type
        /// </summary>
        private readonly Dictionary<Type, WeaponInfo> weaponInfos = new Dictionary<Type, WeaponInfo>();

        /// <summary>
        /// Resolved info for each type
        /// </summary>
        /// <remarks>
        /// Only exists to maintain previous bot behavior with modded weapons that worked with the bots
        /// </remarks>
        private readonly Dictionary<Type, WeaponInfo?> resolvedWeaponInfos = new Dictionary<Type, WeaponInfo?>();

        private void Awake()
        {
            // Prevent multiple instances of ItemsManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake ItemsManager =====================");
        }

        private void Start()
        {
            // Register default items
            weaponInfos.Clear();
            ClearResolvedWeaponsCache(); // Reset the cache!
            RegisterNewWeapon<Shovel>(new ShovelInfo());
            RegisterNewWeapon<KnifeItem>(new KnifeInfo());
            RegisterNewWeapon<ShotgunItem>(new ShotgunInfo());
            RegisterNewWeapon<PatcherTool>(new ZapGunInfo());

            // Call hook
            RegisterWeapons.Invoke(this);
        }

        #region Weapon Registration and Info

        /// <summary>
        /// Registeres the given type as a new weapon for the bots
        /// </summary>
        /// <param name="weaponInfo">The info about this weapon</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterNewWeapon<T>(WeaponInfo weaponInfo)
            where T : GrabbableObject
        {
            RegisterNewWeapon(typeof(T), weaponInfo);
        }

        /// <summary>
        /// Registeres the given type as a new weapon for the bots
        /// </summary>
        /// <param name="weaponType">The weapon type to set the info for.</param>
        /// <param name="weaponInfo">The info about this weapon</param>
        public void RegisterNewWeapon(Type weaponType, WeaponInfo weaponInfo)
        {
            if (weaponInfo == null)
            {
                throw new ArgumentNullException(nameof(weaponInfo));
            }

            if (!typeof(GrabbableObject).IsAssignableFrom(weaponType))
            {
                throw new ArgumentException("Should inherit from GrabbableObject", nameof(weaponType));
            }

            if (weaponInfos.ContainsKey(weaponType))
            {
                Plugin.LogWarning($"Weapon '{weaponType.Name}' was already registered. Overwriting!");
                ClearResolvedWeaponsCache(); // Reset the cache!
            }

            weaponInfos[weaponType] = weaponInfo;
            resolvedWeaponInfos[weaponType] = weaponInfo; // We already know what info to use for this!
            Plugin.LogInfo($"Registered {weaponType.Name} as a weapon for LethalBots!");
        }

        /// <summary>
        /// Unregisters a weapon by its name.
        /// </summary>
        /// <param name="weaponType">The type of the weapon to unregister.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnRegisterWeapon(Type weaponType)
        {
            weaponInfos.Remove(weaponType);
            ClearResolvedWeaponsCache(); // Clear the cache!
        }

        /// <summary>
        /// Public helper that allows you to reset the <see cref="resolvedWeaponInfos"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearResolvedWeaponsCache()
        {
            resolvedWeaponInfos.Clear();
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weapon"/>
        /// </summary>
        /// <param name="weapon">The weapon to get the info for.</param>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <paramref name="weapon"/> or null</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeaponInfo? GetWeaponInfo(GrabbableObject? weapon)
        {
            return weapon != null ? GetWeaponInfo(weapon.GetType()) : null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weaponType"/>
        /// </summary>
        /// <param name="weaponType">The weapon type to get the info for.</param>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <paramref name="weaponType"/> or null</returns>
        public WeaponInfo? GetWeaponInfo(Type weaponType)
        {
            if (resolvedWeaponInfos.TryGetValue(weaponType, out WeaponInfo? cached))
                return cached;

            Type? current = weaponType;
            while (current != null && current != RootWeaponType)
            {
                if (weaponInfos.TryGetValue(current, out WeaponInfo? info))
                {
                    resolvedWeaponInfos[weaponType] = info;
                    return info;
                }

                current = current.BaseType;
            }

            resolvedWeaponInfos[weaponType] = null;
            return null;
        }

        /// <summary>
        /// Gets the info for the following <typeparamref name="T"/>
        /// </summary>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <typeparamref name="T"/> or null</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeaponInfo? GetWeaponInfo<T>()
            where T : GrabbableObject
        {
            return GetWeaponInfo(typeof(T));
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weapon"/>
        /// </summary>
        /// <param name="weapon">The weapon to get the info for.</param>
        /// <param name="weaponInfo">The <see cref="WeaponInfo"/> associated with the given <paramref name="weapon"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="WeaponInfo"/>; otherwise <see langword="false"/></returns>
        public bool TryGetWeaponInfo([NotNullWhen(true)] GrabbableObject? weapon, [NotNullWhen(true)] out WeaponInfo? weaponInfo)
        {
            weaponInfo = GetWeaponInfo(weapon);
            return weaponInfo != null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weaponType"/>
        /// </summary>
        /// <param name="weaponType">The weapon type to get the info for.</param>
        /// <param name="weaponInfo">The <see cref="WeaponInfo"/> associated with the given <paramref name="weaponType"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="WeaponInfo"/>; otherwise <see langword="false"/></returns>
        public bool TryGetWeaponInfo(Type weaponType, [NotNullWhen(true)] out WeaponInfo? weaponInfo)
        {
            weaponInfo = GetWeaponInfo(weaponType);
            return weaponInfo != null;
        }

        #endregion

        #region Item Info

        /// <summary>
        /// Is the given item a weapon?
        /// </summary>
        /// <param name="weapon">The item to check</param>
        /// <returns>I mean come on</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsItemWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            return GetWeaponInfo(weapon) != null;
        }

        /// <summary>
        /// Is the given item a ranged weapon?
        /// </summary>
        /// <returns>I mean come on</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsItemRangedWeapon([NotNullWhen(true)] GrabbableObject? weapon)
        {
            return TryGetWeaponInfo(weapon, out WeaponInfo? weaponInfo) && weaponInfo.IsRanged(weapon);
        }

        /// <summary>
        /// Helper function to check if an item has a charge or not, 
        /// this is used for the bots to know if they can use an item or not!
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <param name="requiredChargeLevel">The level of charge the battery should have.<br/> Should be a value between 0.0-1.0.</param>
        /// <returns>true: the item has a charge or doesn't use batteries; otherwise false</returns>
        public static bool HasRequiredCharge([NotNullWhen(true)] GrabbableObject? item, float requiredChargeLevel = 0.0f)
        {
            if (item == null)
            {
                return false;
            }

            if (!item.itemProperties.requiresBattery)
            {
                return true; // Battery is not required, so it has a "charge"
            }

            Battery battery = item.insertedBattery;
            if (battery == null || battery.empty || battery.charge < requiredChargeLevel)
            {
                return false; // No battery or battery is empty, so it has no charge
            }

            return true; // Item requires a battery and has a non-empty battery, so it has charge
        }

        /// <summary>
        /// Is the given item a key or lockpicker ?
        /// </summary>
        /// <remarks>
        /// TODO: Allow modders to add custom keys and lockpickers to this
        /// </remarks>
        /// <param name="item"></param>
        /// <param name="keyOnly">Should we only consider "actual" keys</param>
        /// <returns>I mean come on</returns>
        public static bool IsItemKey([NotNullWhen(true)] GrabbableObject? item, bool keyOnly = false)
        {
            if (item == null)
            {
                return false;
            }

            if (item is KeyItem)
            {
                return true;
            }
            return !keyOnly && item is LockPicker;
        }

        /// <summary>
        /// Is the given item scrap?
        /// </summary>
        /// <remarks>
        /// TODO: Allow modders to override specific scrap item to this
        /// </remarks>
        /// <param name="item">The item to check</param>
        /// <returns>I mean come on</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsItemScrap([NotNullWhen(true)] GrabbableObject? item)
        {
            return item != null && item.itemProperties.isScrap && item.scrapValue > 0;
        }

        #endregion
    }
}
