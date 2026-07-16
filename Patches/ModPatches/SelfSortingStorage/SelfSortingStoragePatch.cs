using LethalBots.Utils.Helpers;
using SelfSortingStorage.Cupboard;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace LethalBots.Patches.ModPatches.SelfSortingStorage
{
    public class SelfSortingStoragePatch
    {
        /// <summary>
        /// Singleton that holds the <see cref="SmartCupboard"/> instance
        /// </summary>
        /// <remarks>
        /// This holds a <see cref="NetworkBehaviour"/> to not load the <see cref="SmartCupboard"/> type if the mod is not installed
        /// </remarks>
        public static Singleton<NetworkBehaviour> SelfSortingStorage { private set; get; } = null!;

        internal static void InitSingleton()
        {
            SelfSortingStorage = new Singleton<NetworkBehaviour>(UnityEngine.Object.FindObjectOfType<SmartCupboard>);
        }
    }
}
