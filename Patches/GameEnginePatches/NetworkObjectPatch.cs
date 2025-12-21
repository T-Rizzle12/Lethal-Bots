using HarmonyLib;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Managers;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Patches.GameEnginePatches
{
    /// <summary>
    /// Patch for <c>NetworkObject</c>
    /// </summary>
    [HarmonyPatch(typeof(NetworkObject))]
    public class NetworkObjectPatch
    {
        /// <summary>
        /// Patch for intercepting the change of ownership on a network object.
        /// If the owner ship goes to an bot, it should go to the owner of the bot
        /// </summary>
        /// <remarks>
        /// Patch maybe useless with the change of method for grabbing object for an bot
        /// </remarks>
        /// <param name="newOwnerClientId"></param>
        /// <returns></returns>
        [HarmonyPatch("ChangeOwnership")]
        [HarmonyPrefix]
        static bool ChangeOwnership_PreFix(ref ulong newOwnerClientId)
        {
            Plugin.LogDebug($"Try network object ChangeOwnership newOwnerClientId : {(int)newOwnerClientId}");
            if(newOwnerClientId > Const.LETHAL_BOT_ACTUAL_ID_OFFSET)
            {
                LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI((int)(newOwnerClientId - Const.LETHAL_BOT_ACTUAL_ID_OFFSET));
                if (lethalBotAI != null)
                {
                    Plugin.LogDebug($"network ChangeOwnership not on lethalBot but on lethalBot owner : {lethalBotAI.OwnerClientId}");
                    newOwnerClientId = lethalBotAI.OwnerClientId;
                }
            }
            
            return true;
        }

        [HarmonyPatch("OnTransformParentChanged")]
        [HarmonyPrefix]
        static bool OnTransformParentChanged_PreFix(NetworkObject __instance,
                                                    Transform ___m_CachedParent)
        {
            if (!DebugConst.SHOW_LOG_DEBUG_ONTRANSFORMPARENTCHANGED)
            {
                return true;
            }

#pragma warning disable CS0162
            if (!__instance.AutoObjectParentSync)
            {
                return true;
            }

            if (__instance.transform.parent == ___m_CachedParent)
            {
                return true;
            }
            if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening)
            {
                return true;
            }
            if (!__instance.NetworkManager.IsServer)
            {
                return true;
            }
            if (!__instance.IsSpawned)
            {
                return true;
            }

            Transform parent = __instance.transform.parent;
            if (parent != null)
            {
                NetworkObject networkObject;
                if (!__instance.transform.parent.TryGetComponent<NetworkObject>(out networkObject))
                {
                    Plugin.LogDebug($"{__instance.transform.parent} Invalid parenting, NetworkObject moved under a non-NetworkObject parent");
                    return true;
                }
                if (!networkObject.IsSpawned)
                {
                    Plugin.LogDebug($"{networkObject} {networkObject.name} NetworkObject can only be reparented under another spawned NetworkObject");
                    return true;
                }
            }

            return true;
#pragma warning restore CS0162
        }
    }
}
