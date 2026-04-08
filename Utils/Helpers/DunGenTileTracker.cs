using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using DunGen.Tags;
using LethalBots.AI;
using UnityEngine;

namespace LethalBots.Utils.Helpers
{
    public class DunGenTileTracker : MonoBehaviour
    {
        private readonly UpdateLimiter updateLimiter = new UpdateLimiter();

        public LethalBotAI lethalBotAI { get; internal set; } = null!;

        public int AdjacentTileDepth = 1;

        public bool CullBehindClosedDoors = true;

        public Transform? TargetOverride;

        public bool IncludeDisabledComponents;

        private Tag? disableCullingTag = null;

        public Tag DisableCullingTag
        {
            get
            {
                if (disableCullingTag == null)
                {
                    disableCullingTag = StartOfRound.Instance.occlusionCuller.DisableCullingTag; // Just use what the local player uses!
                }
                return disableCullingTag;
            }
            set => disableCullingTag = value;
        }

        protected List<Dungeon> dungeons = new List<Dungeon>();

        protected List<Tile> allTiles = new List<Tile>();

        protected List<Door> allDoors = new List<Door>();

        protected List<Tile> oldVisibleTiles = new List<Tile>();

        protected List<Tile> visibleTiles = new List<Tile>();

        protected Dictionary<Tile, bool> tileVisibilities = new Dictionary<Tile, bool>();

        private bool dirty;

        private DungeonGenerator generator = null!;

        public Tile? currentTile;

        private Queue<Tile> tilesToSearch = null!;

        private List<Tile> searchedTiles = null!;

        public bool Ready { get; protected set; }

        protected Transform targetTransform
        {
            get
            {
                if (TargetOverride == null)
                {
                    return base.transform;
                }
                return TargetOverride;
            }
        }

        protected virtual void OnEnable()
        {
            RuntimeDungeon runtimeDungeon = UnityUtil.FindObjectByType<RuntimeDungeon>();
            if (runtimeDungeon != null)
            {
                generator = runtimeDungeon.Generator;
                generator.OnGenerationComplete += OnDungeonGenerationComplete;
                if (generator.Status == GenerationStatus.Complete)
                {
                    AddDungeon(generator.CurrentDungeon);
                }
            }
        }

        protected virtual void OnDisable()
        {
            if (generator != null)
            {
                generator.OnGenerationComplete -= OnDungeonGenerationComplete;
            }
            for (int i = 0; i < allTiles.Count; i++)
            {
                if (allTiles[i] != null)
                {
                    SetTileVisibility(allTiles[i], visible: true);
                }
            }
            ClearAllDungeons();
        }

        public virtual void SetDungeon(Dungeon newDungeon)
        {
            if (newDungeon != null)
            {
                ClearAllDungeons();
                AddDungeon(newDungeon);
            }
        }

        public virtual void AddDungeon(Dungeon? dungeon)
        {
            if (dungeon == null || dungeons.Contains(dungeon))
            {
                return;
            }
            dungeons.Add(dungeon);
            List<Tile> list = new List<Tile>(dungeon.AllTiles);
            List<Door> list2 = new List<Door>(GetAllDoorsInDungeon(dungeon));
            allTiles.AddRange(list);
            allDoors.AddRange(list2);
            foreach (Tile item in list)
            {
                if (!item.Tags.Tags.Contains(DisableCullingTag))
                {
                    SetTileVisibility(item, visible: false);
                }
            }
            foreach (Door item2 in list2)
            {
                item2.OnDoorStateChanged += OnDoorStateChanged;
            }
            Ready = true;
            dirty = true;
        }

        private void RemoveNullKeys<TKey, TValue>(ref Dictionary<TKey, TValue> dictionary)
        {
            TKey[] array = dictionary.Keys.Where((TKey val) => val == null).ToArray();
            foreach (TKey key in array)
            {
                if (dictionary.ContainsKey(key))
                {
                    dictionary.Remove(key);
                }
            }
        }

        public virtual void RemoveDungeon(Dungeon dungeon)
        {
            if (dungeon == null || !dungeons.Contains(dungeon))
            {
                return;
            }
            dungeons.Remove(dungeon);
            allTiles.RemoveAll((Tile x) => !x);
            visibleTiles.RemoveAll((Tile x) => !x);
            allDoors.RemoveAll((Door x) => !x);
            RemoveNullKeys(ref tileVisibilities);
            foreach (Tile allTile in dungeon.AllTiles)
            {
                SetTileVisibility(allTile, visible: true);
                allTiles.Remove(allTile);
                tileVisibilities.Remove(allTile);
                visibleTiles.Remove(allTile);
                oldVisibleTiles.Remove(allTile);
            }
            foreach (GameObject door in dungeon.Doors)
            {
                if (door != null && door.TryGetComponent<Door>(out var component))
                {
                    component.OnDoorStateChanged -= OnDoorStateChanged;
                    allDoors.Remove(component);
                }
            }
            if (allTiles.Count == 0)
            {
                Ready = false;
            }
        }

        public virtual void ClearAllDungeons()
        {
            Ready = false;
            foreach (Door allDoor in allDoors)
            {
                if (allDoor != null)
                {
                    allDoor.OnDoorStateChanged -= OnDoorStateChanged;
                }
            }
            dungeons.Clear();
            allTiles.Clear();
            visibleTiles.Clear();
            allDoors.Clear();
            oldVisibleTiles.Clear();
            tileVisibilities.Clear();
        }

        public virtual bool IsTileVisible(Tile tile)
        {
            if (tileVisibilities.TryGetValue(tile, out var value))
            {
                return value;
            }
            return false;
        }

        protected IEnumerable<Door> GetAllDoorsInDungeon(Dungeon dungeon)
        {
            foreach (GameObject door in dungeon.Doors)
            {
                if (door != null)
                {
                    Door component = door.GetComponent<Door>();
                    if (component != null)
                    {
                        yield return component;
                    }
                }
            }
        }

        protected virtual void OnDoorStateChanged(Door door, bool isOpen)
        {
            dirty = true;
        }

        protected virtual void OnDungeonGenerationComplete(DungeonGenerator generator)
        {
            if ((generator.AttachmentSettings == null || generator.AttachmentSettings.TileProxy == null) && dungeons.Count > 0)
            {
                RemoveDungeon(dungeons[dungeons.Count - 1]);
            }
            AddDungeon(generator.CurrentDungeon);
        }

        protected virtual void LateUpdate()
        {
            if (Ready && updateLimiter.CanUpdate())
            {
                // Reset the update limiter
                updateLimiter.SetUpdateInterval(lethalBotAI.AIIntervalTime);
                updateLimiter.Invalidate();

                // Don't do this if we are not inside
                if (!lethalBotAI.NpcController.Npc.isInsideFactory)
                {
                    return;
                }

                // Check what tile we are on
                Tile? tile = currentTile;
                if (currentTile == null)
                {
                    currentTile = FindCurrentTile();
                }
                else if (!currentTile.Bounds.Contains(targetTransform.position))
                {
                    currentTile = SearchForNewCurrentTile();
                }
                if (currentTile != tile && currentTile != null)
                {
                    dirty = true;
                }
                if (dirty)
                {
                    //Plugin.LogDebug("DunGenTileTracker: updating outdated visibility");
                    RefreshVisibility();
                }
                //else
                //{
                //    Plugin.LogDebug("DunGenTileTracker: visibility is up to date");
                //}
                dirty = false;
            }
        }

        public Tile? GetStartTile()
        {
            for (int i = 0; i < allTiles.Count; i++)
            {
                if (allTiles[i].Placement.NormalizedPathDepth == 0f)
                {
                    return allTiles[i];
                }
            }
            return null;
        }

        public void SetToStartTile()
        {
            if (!Ready)
            {
                return;
            }
            Tile? tile = currentTile;
            if (RoundManager.Instance.dungeonGenerator == null)
            {
                Plugin.LogError("RoundManager dungeon generator is null! Cannot set StartTile as current tile!");
                return;
            }
            Tile startTile = RoundManager.Instance.dungeonGenerator.Generator.CurrentDungeon.MainPathTiles[0];
            for (int i = 0; i < allTiles.Count; i++)
            {
                if (allTiles[i] == startTile)
                {
                    Plugin.LogInfo("DunGenTileTracker: Got start tile!");
                    currentTile = allTiles[i];
                    break;
                }
            }
            if (currentTile != tile && currentTile != null)
            {
                dirty = true;
            }
            if (dirty)
            {
                RefreshVisibility();
            }
            dirty = false;
        }

        protected virtual void RefreshVisibility()
        {
            List<Tile> list = visibleTiles;
            visibleTiles = oldVisibleTiles;
            oldVisibleTiles = list;
            UpdateVisibleTiles();
            foreach (Tile oldVisibleTile in oldVisibleTiles)
            {
                if (!visibleTiles.Contains(oldVisibleTile) && !oldVisibleTile.Tags.Tags.Contains(DisableCullingTag))
                {
                    SetTileVisibility(oldVisibleTile, visible: false);
                }
            }
            foreach (Tile visibleTile in visibleTiles)
            {
                if (!oldVisibleTiles.Contains(visibleTile))
                {
                    SetTileVisibility(visibleTile, visible: true);
                }
            }
            oldVisibleTiles.Clear();
        }

        protected virtual void UpdateVisibleTiles()
        {
            visibleTiles.Clear();
            if (currentTile != null)
            {
                visibleTiles.Add(currentTile);
            }
            int num = 0;
            for (int i = 0; i < AdjacentTileDepth; i++)
            {
                int count = visibleTiles.Count;
                for (int j = num; j < count; j++)
                {
                    foreach (Doorway usedDoorway in visibleTiles[j].UsedDoorways)
                    {
                        Tile tile = usedDoorway.ConnectedDoorway.Tile;
                        if (tile == null || visibleTiles.Contains(tile))
                        {
                            continue;
                        }
                        if (CullBehindClosedDoors)
                        {
                            Door doorComponent = usedDoorway.DoorComponent;
                            if (doorComponent != null && doorComponent.ShouldCullBehind)
                            {
                                continue;
                            }
                        }
                        visibleTiles.Add(tile);
                    }
                }
                num = count;
            }
        }

        protected virtual void SetTileVisibility(Tile tile, bool visible)
        {
            tileVisibilities[tile] = visible;
        }

        protected Tile? FindCurrentTile()
        {
            foreach (Tile allTile in allTiles)
            {
                if (allTile != null && allTile.Bounds.Contains(targetTransform.position))
                {
                    return allTile;
                }
            }
            return null;
        }

        protected Tile? SearchForNewCurrentTile()
        {
            if (RoundManager.Instance.startRoomSpecialBounds != null && RoundManager.Instance.startRoomSpecialBounds.bounds.Contains(targetTransform.position))
            {
                Tile? startTile = GetStartTile();
                if (startTile != null)
                {
                    return startTile;
                }
            }
            tilesToSearch ??= new Queue<Tile>();
            searchedTiles ??= new List<Tile>();
            if (currentTile == null)
            {
                return null;
            }
            foreach (Doorway usedDoorway in currentTile.UsedDoorways)
            {
                Tile tile = usedDoorway.ConnectedDoorway.Tile;
                if (tile != null && !tilesToSearch.Contains(tile))
                {
                    tilesToSearch.Enqueue(tile);
                }
            }
            while (tilesToSearch.Count > 0)
            {
                Tile tile2 = tilesToSearch.Dequeue();
                if (tile2.Bounds.Contains(targetTransform.position))
                {
                    tilesToSearch.Clear();
                    searchedTiles.Clear();
                    return tile2;
                }
                searchedTiles.Add(tile2);
                foreach (Doorway usedDoorway2 in tile2.UsedDoorways)
                {
                    Tile tile3 = usedDoorway2.ConnectedDoorway.Tile;
                    if (tile3 != null && !tilesToSearch.Contains(tile3) && !searchedTiles.Contains(tile3))
                    {
                        tilesToSearch.Enqueue(tile3);
                    }
                }
            }
            searchedTiles.Clear();
            return null;
        }
    }
}
