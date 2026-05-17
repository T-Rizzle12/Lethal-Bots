using Dissonance;
using LethalBots.AI;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager that holds all of the <see cref="Singleton{T}"/> instances.
    /// </summary>
    public static class SingletonManager
    {
        public static Singleton<DissonanceComms> DissonanceComms { get; } = new Singleton<DissonanceComms>();

        public static Singleton<CadaverGrowthAI> CadaverGrowthAI { get; } = new Singleton<CadaverGrowthAI>();

        public static Singleton<ItemDropship> ItemDropship { get; } = new Singleton<ItemDropship>();

        public static Singleton<ShipTeleporter> ShipTeleporter { get; } = new Singleton<ShipTeleporter>(() => LethalBotAI.FindTeleporter());

        public static Singleton<ShipTeleporter> InverseTeleporter { get; } = new Singleton<ShipTeleporter>(() => LethalBotAI.FindTeleporter(inverseTeleporter: true));

        public static Singleton<SignalTranslator> SignalTranslator { get; } = new Singleton<SignalTranslator>();

        public static Singleton<ShipAlarmCord> ShipHorn { get; } = new Singleton<ShipAlarmCord>();

        public static Singleton<ItemCharger> ItemCharger { get; } = new Singleton<ItemCharger>();

        public static Singleton<StartMatchLever> StartMatchLevel { get; } = new Singleton<StartMatchLever>();

        public static Singleton<AudioReverbPresets> AudioReverbPresets { get; } = new Singleton<AudioReverbPresets>();

        public static Singleton<DepositItemsDesk> CompanyDesk { get; } = new Singleton<DepositItemsDesk>();

        public static Singleton<HangarShipDoor> ShipDoor { get; } = new Singleton<HangarShipDoor>();

        public static Singleton<QuickMenuManager> QuickMenuManager { get; } = new Singleton<QuickMenuManager>();
    }
}
