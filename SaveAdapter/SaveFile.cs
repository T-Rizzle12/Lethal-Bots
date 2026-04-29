using LethalBots.AI;
using LethalBots.Enums;
using Newtonsoft.Json;
using System;
using UnityEngine;

namespace LethalBots.SaveAdapter
{
    /// <summary>
    /// Represents the date serializable, to be saved on disk, necessay for LethalBot (not much obviously)
    /// </summary>
    [Serializable]
    public class SaveFile
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public IdentitySaveFile[] IdentitiesSaveFiles;
        public LethalBotBlacklistedItem[] BlacklistedItems;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }

    [Serializable]
    public class IdentitySaveFile
    {
        public int IdIdentity;
        public int SuitID;
        public int Hp;
        public int Status;
        public int XP;
        public int Level;

        public override string ToString()
        {
            return $"IdIdentity: {IdIdentity}, suitID {SuitID}, Hp {Hp}, XP {XP}, Level {Level}, Status {Status} {(EnumStatusIdentity)Status}";
        }
    }

    /// <summary>
    /// Represents a blacklisted item that bots are not allowed to sell.
    /// </summary>
    /// <remarks>
    /// This is what is saved when the game is closed and read from when a save is opened.
    /// </remarks>
    [Serializable]
    public class LethalBotBlacklistedItem
    {
        public string itemName;
        public bool hasScrapValue;
        public int scrapValue;

        // JsonConvert doesn't support Vector3,
        // I store the position using member variables instead
        public float x;
        public float y;
        public float z;

        [JsonIgnore]
        public Vector3 SavedPosition
        {
            get => new Vector3(x, y, z);
            set
            {
                x = value.x; 
                y = value.y;
                z = value.z;
            }
        }

        public LethalBotBlacklistedItem()
        {
            this.itemName = string.Empty;
            this.SavedPosition = Vector3.zero;
            this.hasScrapValue = false;
            this.scrapValue = 0;
        }

        public LethalBotBlacklistedItem(GrabbableObject blacklistedItem)
        {
            this.itemName = blacklistedItem.itemProperties.itemName;
            this.SavedPosition = blacklistedItem.transform.position;
            if (blacklistedItem.itemProperties.isScrap)
            {
                this.hasScrapValue = true;
                this.scrapValue = blacklistedItem.scrapValue;
            }
            else
            {
                this.hasScrapValue = false;
                this.scrapValue = 0;
            }
        }

        public override string ToString()
        {
            return $"Blacklisted Item: {itemName}, Saved Position: {SavedPosition}, Scrap Value {scrapValue}";
        }
    }
}
