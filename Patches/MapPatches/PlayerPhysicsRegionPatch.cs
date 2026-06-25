using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LethalBots.Patches.MapPatches
{
    [HarmonyPatch(typeof(PlayerPhysicsRegion))]
    public class PlayerPhysicsRegionPatch
    {
        // Static variables
        // Conditional Weak Table since when the PlayerPhysicsRegion is removed, the table automatically cleans itself!
        private static readonly ConditionalWeakTable<PlayerPhysicsRegion, PlayerPhysicsRegionMonitor> playerPhysicsRegionMonitorList = new ConditionalWeakTable<PlayerPhysicsRegion, PlayerPhysicsRegionMonitor>();

        /// <summary>
        /// Helper function that retrieves the <see cref="PlayerPhysicsRegion"/>
        /// for the given <see cref="PlayerPhysicsRegion"/>
        /// </summary>
        /// <param name="playerPhysicsRegion"></param>
        /// <returns>The <see cref="QuicksandTrigger"/> associated with the given <see cref="PlayerPhysicsRegion"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PlayerPhysicsRegionMonitor GetOrCreateMonitor(PlayerPhysicsRegion playerPhysicsRegion)
        {
            return playerPhysicsRegionMonitorList.GetValue(playerPhysicsRegion, key => new PlayerPhysicsRegionMonitor(key));
        }

        [HarmonyPatch("OnTriggerStay")]
        [HarmonyPostfix]
        public static void OnTriggerStay_Postfix(PlayerPhysicsRegion __instance, Collider other)
        {
            PlayerControllerB lethalBotController = other.gameObject.GetComponent<PlayerControllerB>();
            if (lethalBotController == null)
            {
                return;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(lethalBotController);
            if (lethalBotAI == null)
            {
                return;
            }

            var botPhysicsData = GetOrCreateMonitor(__instance).GetDataForBot(lethalBotAI);
            botPhysicsData.checkInterval = 0f;
            botPhysicsData.removePlayerNextFrame = false;
            if (lethalBotAI.NpcController.CurrentLethalBotPhysicsRegions != null && !lethalBotAI.NpcController.CurrentLethalBotPhysicsRegions.Contains(__instance))
            {
                lethalBotAI.NpcController.CurrentLethalBotPhysicsRegions.Add(__instance);
                botPhysicsData.hasLethalBot = true;
            }
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(PlayerPhysicsRegion __instance)
        {
            var playerPhysicsRegionMonitor = GetOrCreateMonitor(__instance);
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotAIs();
            for (int i = 0; i < lethalBotAIs.Length; i++)
            {
                LethalBotAI lethalBotAI = lethalBotAIs[i];
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                if (lethalBotController != null)
                {
                    var botPhysicsData = playerPhysicsRegionMonitor.GetDataForBot(lethalBotAI);
                    if (!botPhysicsData.hasLethalBot) continue;

                    if (botPhysicsData.checkInterval > 0.15f)
                    {
                        if (!botPhysicsData.removePlayerNextFrame)
                        {
                            botPhysicsData.removePlayerNextFrame = true;
                            return;
                        }
                        botPhysicsData.removePlayerNextFrame = false;
                        botPhysicsData.checkInterval = 0f;
                        botPhysicsData.hasLethalBot = false;
                        lethalBotAI.NpcController?.CurrentLethalBotPhysicsRegions.Remove(__instance);
                    }
                    else
                    {
                        botPhysicsData.checkInterval += Time.deltaTime;
                    }
                }
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        public static void OnDestroy_Postfix(PlayerPhysicsRegion __instance)
        {
            var playerPhysicsRegionMonitor = GetOrCreateMonitor(__instance);
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotAIs();
            foreach (LethalBotAI lethalBotAI in lethalBotAIs)
            {
                lethalBotAI.NpcController?.CurrentLethalBotPhysicsRegions.Remove(__instance);
            }
            playerPhysicsRegionMonitorList.Remove(__instance);
        }

        /// <summary>
        /// Helper class used to mimic local player variables for bots
        /// </summary>
        public sealed class PlayerPhysicsRegionMonitor
        {
            public PlayerPhysicsRegion playerPhysicsRegion { get; private set; } = null!;
            private readonly Dictionary<LethalBotAI, PhysicsRegionData> lethalBotAIs = new Dictionary<LethalBotAI, PhysicsRegionData>();

            public class PhysicsRegionData
            {
                public float checkInterval;
                public bool removePlayerNextFrame;
                public bool hasLethalBot;

                public PhysicsRegionData() 
                { 
                    checkInterval = 0f;
                    removePlayerNextFrame = false;
                    hasLethalBot = false;
                }
            }

            internal PlayerPhysicsRegionMonitor(PlayerPhysicsRegion playerPhysicsRegion)
            {
                this.playerPhysicsRegion = playerPhysicsRegion;
            }

            /// <summary>
            /// Get the <see cref="PhysicsRegionData"/> for this bot for this <see cref="playerPhysicsRegion"/>
            /// </summary>
            /// <param name="lethalBotAI">The bot to check</param>
            /// <returns></returns>
            public PhysicsRegionData GetDataForBot(LethalBotAI lethalBotAI)
            {
                if (!lethalBotAIs.TryGetValue(lethalBotAI, out var botPhysicsData))
                {
                    botPhysicsData = new PhysicsRegionData();
                    lethalBotAIs[lethalBotAI] = botPhysicsData;
                }
                return botPhysicsData;
            }
        }
    }
}