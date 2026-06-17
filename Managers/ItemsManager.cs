using LethalBots.Utils.Helpers;
using LethalBots.Utils.Items.Weapons;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling items for the bots in the game.
    /// </summary>
    public class ItemsManager : MonoBehaviour
    {
        public static ItemsManager Instance { get; private set; } = null!;

        private readonly Dictionary<Type, WeaponInfo> weaponInfos = new Dictionary<Type, WeaponInfo>();

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
            RegisterNewWeapon<Shovel>(new ShovelInfo());
            RegisterNewWeapon<KnifeItem>(new KnifeInfo());
            RegisterNewWeapon<ShotgunItem>(new ShotgunInfo());
            RegisterNewWeapon<PatcherTool>(new ZapGunInfo());
        }

        #region Weapon Registration and Info

        /// <summary>
        /// Registeres the given type as a new weapon for the bots
        /// </summary>
        /// <param name="weaponInfo">The info about this weapon</param>
        public void RegisterNewWeapon<T>(WeaponInfo weaponInfo)
            where T : GrabbableObject
        {
            Type weaponType = typeof(T);
            if (weaponInfos.ContainsKey(weaponType))
            {
                Plugin.LogWarning($"Weapon '{weaponType.Name}' was already registered. Overwriting!");
            }

            weaponInfos[weaponType] = weaponInfo;
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
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weapon"/>
        /// </summary>
        /// <param name="weapon">The weapon to get the info for.</param>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <paramref name="weapon"/> or null</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeaponInfo? GetWeaponInfo(GrabbableObject? weapon)
        {
            return weapon != null && weaponInfos.TryGetValue(weapon.GetType(), out WeaponInfo weaponInfo) ? weaponInfo : null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weaponType"/>
        /// </summary>
        /// <param name="weaponType">The weapon type to get the info for.</param>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <paramref name="weaponType"/> or null</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeaponInfo? GetWeaponInfo(Type weaponType)
        {
            return weaponInfos.TryGetValue(weaponType, out WeaponInfo weaponInfo) ? weaponInfo : null;
        }

        /// <summary>
        /// Gets the info for the following <typeparamref name="T"/>
        /// </summary>
        /// <returns>The <see cref="WeaponInfo"/> associated with the given <typeparamref name="T"/> or null</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeaponInfo? GetWeaponInfo<T>()
            where T : GrabbableObject
        {
            return weaponInfos.TryGetValue(typeof(T), out WeaponInfo weaponInfo) ? weaponInfo : null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weapon"/>
        /// </summary>
        /// <param name="weapon">The weapon to get the info for.</param>
        /// <param name="weaponInfo">The <see cref="WeaponInfo"/> associated with the given <paramref name="weapon"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="WeaponInfo"/>; otherwise <see langword="false"/></returns>
        public bool TryGetWeaponInfo([NotNullWhen(true)] GrabbableObject? weapon, [NotNullWhen(true)] out WeaponInfo? weaponInfo)
        {
            weaponInfo = null;
            return weapon != null && weaponInfos.TryGetValue(weapon.GetType(), out weaponInfo);
        }

        /// <summary>
        /// Gets the info for the following <paramref name="weaponType"/>
        /// </summary>
        /// <param name="weaponType">The weapon type to get the info for.</param>
        /// <param name="weaponInfo">The <see cref="WeaponInfo"/> associated with the given <paramref name="weaponType"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="WeaponInfo"/>; otherwise <see langword="false"/></returns>
        public bool TryGetWeaponInfo(Type weaponType, [NotNullWhen(true)] out WeaponInfo? weaponInfo)
        {
            weaponInfo = null;
            return weaponInfos.TryGetValue(weaponType, out weaponInfo);
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
        /// <returns>true: the item has a charge or doesn't use batteries; otherwise false</returns>
        public static bool IsItemPowered([NotNullWhen(true)] GrabbableObject? item)
        {
            if (item == null)
            {
                return false;
            }

            if (!item.itemProperties.requiresBattery)
            {
                return true; // Battery is not required, so it has a "charge"
            }

            if (item.insertedBattery == null || item.insertedBattery.empty)
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
