using LethalBots.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Random = System.Random;

namespace LethalBots.AI
{
    /// <summary>
    /// Represents the identity of a lethal bot, including its attributes, status, and associated properties.
    /// </summary>
    /// <remarks>
    /// This class encapsulates the core information about a lethal bot, such as its name, health
    /// points, status, and suit. It provides methods to update the bot's identity and retrieve information about its
    /// current state.
    /// </remarks>
    public class LethalBotIdentity
    {
        public int IdIdentity { get; }
        public string Name { get; set; }
        public int? SuitID { get; set; }
        public LethalBotVoice Voice { get; set; }
        public LethalBotLoadout Loadout { get; set; }
        public DeadBodyInfo? DeadBody { get; set; }
        public object? BodyReplacementBase { get; set; }

        public int HpMax { get; set; }
        public int Hp { get; set; }
        public bool DiedLastRound { get; set; }
        public bool JustJoinedServer { get; set; }
        public int? XP { get; set; }
        public int Level { get; set; }
        public EnumStatusIdentity Status { get; set; }
        public EnumDefaultAIState DefaultAIState { get; set; }

        public bool Alive 
        { 
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get 
            { 
                return Hp > 0; 
            } 
        }
        public string Suit
        {
            get
            {
                if (!SuitID.HasValue)
                {
                    return "";
                }

                string suitName = SuitID.Value > StartOfRound.Instance.unlockablesList.unlockables.Count() ? "Not found" : StartOfRound.Instance.unlockablesList.unlockables[SuitID.Value].unlockableName;
                return $"{SuitID.Value}: {suitName}";
            }
        }

        public LethalBotIdentity(int idIdentity, string name, int? suitID, LethalBotVoice voice, LethalBotLoadout loadout, EnumDefaultAIState defaultAIState = EnumDefaultAIState.FollowPlayer, int? Xp = null)
        {
            IdIdentity = idIdentity;
            Name = name;
            SuitID = suitID;
            Voice = voice;
            Loadout = loadout;
            HpMax = 100;
            Hp = HpMax;
            DiedLastRound = true;
            JustJoinedServer = true;
            XP = Xp;
            Status = EnumStatusIdentity.Available;
            DefaultAIState = defaultAIState;
        }

        public void UpdateIdentity(int Hp, int? suitID, int? Xp, int level, EnumStatusIdentity enumStatusIdentity)
        {
            this.Hp = Hp;
            this.SuitID = suitID;
            this.XP = Xp;
            this.Level = level;
            this.Status = enumStatusIdentity;
        }

        public override string ToString()
        {
            return $"IdIdentity: {IdIdentity}, name: {Name}, suit {Suit}, Hp {Hp}/{HpMax}, XP {XP}, Level {Level}, Status {(int)Status} '{Status}', Voice : {{{Voice.ToString()}}}, Loadout : {{{Loadout.ToString()}}}";
        }

        public int GetRandomSuitID()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;
            UnlockableItem unlockableItem;
            List<int> indexesSpawnedUnlockables = new List<int>();
            foreach (var unlockable in instanceSOR.SpawnedShipUnlockables)
            {
                if (unlockable.Value == null)
                {
                    continue;
                }

                unlockableItem = instanceSOR.unlockablesList.unlockables[unlockable.Key];
                if (unlockableItem != null
                    && unlockableItem.unlockableType == 0)
                {
                    // Suits
                    indexesSpawnedUnlockables.Add(unlockable.Key);
                    //Plugin.LogDebug($"unlockable index {unlockable.Key}");
                }
            }

            if (indexesSpawnedUnlockables.Count == 0)
            {
                return 0;
            }

            //Plugin.LogDebug($"indexesSpawnedUnlockables.Count {indexesSpawnedUnlockables.Count}");
            Random randomInstance = new Random();
            int randomIndex = randomInstance.Next(0, indexesSpawnedUnlockables.Count);
            if (randomIndex >= indexesSpawnedUnlockables.Count)
            {
                return 0;
            }

            return indexesSpawnedUnlockables[randomIndex];
        }
    }
}
