using Dusk;
using LethalBots.Utils.Helpers;
using LethalBots.Utils.Helpers.VehicleHelpers;
using LethalBots.Utils.Items.Weapons;
using LethalBots.Utils.Vehicles;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace LethalBots.Managers
{
    /// <summary>
    /// The main manager for handling vehicles for the bots in the game.
    /// </summary>
    public class VehicleManager : MonoBehaviour
    {
        public static VehicleManager Instance { get; private set; } = null!;

        internal GameObject CruiserNavMeshAgentObject = null!;

        public NavMeshAgent CruiserNavMeshAgent
        {
            get
            {
                if (CruiserNavMeshAgentObject == null)
                {
                    GameObject cruiserAgent = new GameObject("CrusierNavMeshAgent");
                    field = cruiserAgent.AddComponent<NavMeshAgent>();
                    CruiserNavMeshAgentObject = cruiserAgent;
                }
                if (field == null)
                {
                    field = CruiserNavMeshAgentObject.GetComponent<NavMeshAgent>() ?? CruiserNavMeshAgentObject.AddComponent<NavMeshAgent>();
                }
                return field;
            }
        }

        /// <summary>
        /// A hook that is called after <see cref="VehicleManager"/> registers the default vehicles
        /// </summary>
        public static readonly UnityEvent<VehicleManager> RegisterVehicles = new UnityEvent<VehicleManager>();

        private readonly Dictionary<Type, IVehicleAdapter> registeredVehicles = new Dictionary<Type, IVehicleAdapter>();

        private void Awake()
        {
            // Prevent multiple instances of VehicleManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;
            Plugin.LogDebug("=============== awake VehicleManager =====================");
        }

        private void Start()
        {
            // Register default vehicle
            RegisterVehicle<VehicleController, CompanyCruiserGearRequest>(new CompanyCruiserInfo());

            // Call hook
            RegisterVehicles.Invoke(this);
        }

        private void OnDestroy()
        {
            if (CruiserNavMeshAgentObject != null)
            {
                UnityEngine.Object.Destroy(CruiserNavMeshAgentObject);
            }
        }

        /// <summary>
        /// Registers a new vehicle for Lethal Bots to use
        /// </summary>
        /// <typeparam name="TVehicle"></typeparam>
        /// <typeparam name="TGearRequest"></typeparam>
        /// <param name="vehicleAdapter"></param>
        public void RegisterVehicle<TVehicle, TGearRequest>(VehicleAdapter<TVehicle, TGearRequest> vehicleAdapter)
            where TVehicle : VehicleController
            where TGearRequest : IChangeGearRequest
        {
            RegisterVehicle(typeof(TVehicle), vehicleAdapter);
        }

        /// <summary>
        /// Registers a new vehicle for Lethal Bots to use
        /// </summary>
        /// <param name="vehicleType"></param>
        /// <param name="vehicleAdapter"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void RegisterVehicle(Type vehicleType, IVehicleAdapter vehicleAdapter)
        {
            if (vehicleAdapter == null)
            {
                throw new ArgumentNullException(nameof(vehicleAdapter));
            }

            if (!typeof(VehicleController).IsAssignableFrom(vehicleType))
            {
                throw new ArgumentException("Should inherit from VehicleController", nameof(vehicleType));
            }

            if (registeredVehicles.ContainsKey(vehicleType))
            {
                Plugin.LogWarning($"Vehicle '{vehicleType.Name}' was already registered. Overwriting!");
            }

            registeredVehicles[vehicleType] = vehicleAdapter;
            Plugin.LogInfo($"Registered {vehicleType.Name} as a vehicle for LethalBots!");
        }

        /// <summary>
        /// Gets the info for the following <typeparamref name="TVehicle"/>
        /// </summary>
        /// <typeparam name="TVehicle"></typeparam>
        /// <typeparam name="TGearRequest"></typeparam>
        /// <returns>The <see cref="VehicleAdapter{TVehicle, TGearRequest}"/> associated with the given <typeparamref name="TVehicle"/> or null</returns>
        public VehicleAdapter<TVehicle, TGearRequest>? GetVehicleInfo<TVehicle, TGearRequest>()
            where TVehicle : VehicleController
            where TGearRequest : IChangeGearRequest
        {
            return GetVehicleInfo(typeof(TVehicle)) as VehicleAdapter<TVehicle, TGearRequest>;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="vehicleController"/>
        /// </summary>
        /// <param name="vehicleController"></param>
        /// <returns>The <see cref="IVehicleAdapter"/> associated with the given <paramref name="vehicleController"/> or null</returns>
        public IVehicleAdapter? GetVehicleInfo(VehicleController? vehicleController)
        {
            return vehicleController != null ? GetVehicleInfo(vehicleController.GetType()) : null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="vehicleType"/>
        /// </summary>
        /// <param name="vehicleType"></param>
        /// <returns>The <see cref="IVehicleAdapter"/> associated with the given <paramref name="vehicleType"/> or null</returns>
        public IVehicleAdapter? GetVehicleInfo(Type vehicleType)
        {
            if (registeredVehicles.TryGetValue(vehicleType, out var vehicleInfo))
            {
                return vehicleInfo;
            }

            return null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="vehicleType"/>
        /// </summary>
        /// <param name="vehicleType">The vehicle type to get the info for.</param>
        /// <param name="vehicleInfo">The <see cref="IVehicleAdapter"/> associated with the given <paramref name="vehicleType"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="IVehicleAdapter"/>; otherwise <see langword="false"/></returns>
        public bool TryGetVehicleInfo(Type vehicleType, [NotNullWhen(true)] out IVehicleAdapter? vehicleInfo)
        {
            vehicleInfo = GetVehicleInfo(vehicleType);
            return vehicleInfo != null;
        }

        /// <summary>
        /// Gets the info for the following <paramref name="vehicleController"/>
        /// </summary>
        /// <param name="vehicleController">The vehicle to get the info for.</param>
        /// <param name="vehicleInfo">The <see cref="IVehicleAdapter"/> associated with the given <paramref name="vehicleController"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="IVehicleAdapter"/>; otherwise <see langword="false"/></returns>
        public bool TryGetVehicleInfo([NotNullWhen(true)] VehicleController? vehicleController, [NotNullWhen(true)] out IVehicleAdapter? vehicleInfo)
        {
            vehicleInfo = GetVehicleInfo(vehicleController);
            return vehicleInfo != null;
        }

        /// <summary>
        /// Gets the info for the following <typeparamref name="TVehicle"/>
        /// </summary>
        /// <typeparam name="TVehicle">The vehicle type to get the info for.</typeparam>
        /// <typeparam name="TGearRequest"></typeparam>
        /// <param name="vehicleInfo">The <see cref="VehicleAdapter{TVehicle, TGearRequest}"/> associated with the given <typeparamref name="TVehicle"/> or null</param>
        /// <returns><see langword="true"/> if there is a valid <see cref="VehicleAdapter{TVehicle, TGearRequest}"/>; otherwise <see langword="false"/></returns>
        public bool TryGetVehicleInfo<TVehicle, TGearRequest>([NotNullWhen(true)] out VehicleAdapter<TVehicle, TGearRequest>? vehicleInfo)
            where TVehicle : VehicleController
            where TGearRequest : IChangeGearRequest
        {
            vehicleInfo = GetVehicleInfo<TVehicle, TGearRequest>();
            return vehicleInfo != null;
        }
    }
}
