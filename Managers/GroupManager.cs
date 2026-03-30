using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.NetworkSerializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// This is the manager than handles all of the groups whether  it be only bots or a mix of human players and bots.<br/>
    /// This manager also handles networking all of the groups automatically, even for players that join in late.<br/>
    /// </summary>
    /// <remarks>
    /// The manager keeps cached information about the active groups. This makes lookups quick as the data is only reassed if the groups change themselves.
    /// </remarks>
    public class GroupManager : NetworkBehaviour
    {
        public const int INVALID_GROUP_INDEX = -1;
        private const int DEFAULT_GROUP_INDEX = 0;

        public static GroupManager Instance { get; private set; } = null!;

        public NetworkList<LethalBotGroupMemberNetworkSerializable> LethalBotGroups = new NetworkList<LethalBotGroupMemberNetworkSerializable>(writePerm: NetworkVariableWritePermission.Server);

        private readonly Dictionary<ulong, int> memberToGroup = new Dictionary<ulong, int>();
        private readonly Dictionary<int, HashSet<ulong>> groupMembers = new Dictionary<int, HashSet<ulong>>();
        private readonly Dictionary<int, ulong> groupLeaders = new Dictionary<int, ulong>();

        private int nextGroupId = DEFAULT_GROUP_INDEX;

        /// <summary>
        /// When manager awake, setup the manager instance
        /// </summary>
        private void Awake()
        {
            // Prevent multiple instances of the GroupManager
            if (Instance != null && Instance != this)
            {
                if (Instance.IsSpawned && Instance.IsServer)
                {
                    Instance.NetworkObject.Despawn(destroy: true);
                }
                else
                {
                    Destroy(Instance.gameObject);
                }
            }

            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            LethalBotGroups.OnListChanged += OnGroupListChanged;

            if (!base.NetworkManager.IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    // Destory Local manager
                    Destroy(Instance.gameObject);
                }
                Instance = this;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            LethalBotGroups.OnListChanged -= OnGroupListChanged;
        }

        #region Group Creation, Addition, and Removal Rpcs

        /// <summary>
        /// Creates a new group with <paramref name="leader"/> as the leader
        /// </summary>
        /// <remarks>
        /// This always fails if not called on the server.
        /// </remarks>
        /// <param name="leader"></param>
        /// <returns>The newly created group id</returns>
        private int CreateGroup(PlayerControllerB leader)
        {
            if (!IsServer)
            {
                return INVALID_GROUP_INDEX;
            }

            RemoveFromCurrentGroup(leader);

            int groupId = nextGroupId++;
            LethalBotGroups.Add(new LethalBotGroupMemberNetworkSerializable
            {
                GroupId = groupId,
                Member = leader,
                IsLeader = true
            });
            return groupId;
        }

        /// <summary>
        /// <inheritdoc cref="CreateGroup(PlayerControllerB)"/>
        /// </summary>
        /// <remarks>
        /// This will automatically call <see cref="CreateGroupServerRpc(NetworkBehaviourReference)"/> if called on a client.
        /// </remarks>
        /// <param name="leader"></param>
        public void CreateGroupAndSync(PlayerControllerB leader)
        {
            if (IsServer)
            {
                CreateGroup(leader);
            }
            else
            {
                CreateGroupServerRpc(leader);
            }
        }

        /// <summary>
        /// Helper rpc that allows clients to create groups!
        /// </summary>
        /// <param name="player"></param>
        [ServerRpc(RequireOwnership = false)]
        private void CreateGroupServerRpc(NetworkBehaviourReference player)
        {
            if (player.TryGet(out PlayerControllerB groupLeader))
            {
                CreateGroup(groupLeader);
            }
        }

        /// <summary>
        /// Adds the given <paramref name="member"/> to the given <paramref name="groupId"/>
        /// </summary>
        /// <remarks>
        /// This function does nothing if the given <paramref name="member"/> is already in the given <paramref name="groupId"/>
        /// </remarks>
        /// <param name="groupId"></param>
        /// <param name="member"></param>
        private void AddToGroup(int groupId, PlayerControllerB member)
        {
            // This should only be called on the server
            if (!IsServer 
                || !DoesGroupExist(groupId) 
                || IsPlayerInGroup(member, groupId))
                return;

            RemoveFromCurrentGroup(member);

            LethalBotGroups.Add(new LethalBotGroupMemberNetworkSerializable
            {
                GroupId = groupId,
                Member = member,
                IsLeader = false
            });
        }

        /// <summary>
        /// <inheritdoc cref="AddToGroup(int, PlayerControllerB)"/>
        /// </summary>
        /// <remarks>
        /// This will automatically call <see cref="AddToGroupServerRpc(int, NetworkBehaviourReference)"/> if called on a client.
        /// </remarks>
        /// <param name="groupId"></param>
        /// <param name="member"></param>
        public void AddToGroupAndSync(int groupId, PlayerControllerB member)
        {
            if (IsServer)
            {
                AddToGroup(groupId, member); 
            }
            else
            {
                AddToGroupServerRpc(groupId, member);
            }
        }

        /// <summary>
        /// Helper rpc that allows clients to add themselves to groups
        /// </summary>
        /// <param name="groupId"></param>
        /// <param name="player"></param>
        [ServerRpc(RequireOwnership = false)]
        private void AddToGroupServerRpc(int groupId, NetworkBehaviourReference player)
        {
            if (player.TryGet(out PlayerControllerB groupLeader))
            {
                AddToGroup(groupId, groupLeader);
            }
        }

        /// <summary>
        /// Used by <see cref="LethalBotAI"/> for their predefined groups!
        /// </summary>
        /// <remarks>
        /// This function will silently call <see cref="CreateGroup(PlayerControllerB)"/> if a human player is given.
        /// </remarks>
        /// <param name="player"></param>
        public void CreateOrJoinGroupAndSync(PlayerControllerB player)
        {
            // This only works for bots!
            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAI(player);
            if (lethalBotAI == null)
            {
                CreateGroupAndSync(player);
                return;
            }

            if (IsServer)
            {
                // Alright, do we join a group or start one?
                if (DoesInternalGroupExist(lethalBotAI, out int groupID))
                {
                    AddToGroup(groupID, player); // Another bot has already created the group
                }
                else
                {
                    CreateGroup(player); // No other bot with our id is a leader of a group, make our own!
                }
            }
            else
            {
                CreateOrJoinGroupServerRpc(player);
            }
        }

        /// <summary>
        /// Helper rpc that allows bots that are not owned by the host to auto create or join groups
        /// </summary>
        /// <param name="player"></param>
        [ServerRpc(RequireOwnership = false)]
        private void CreateOrJoinGroupServerRpc(NetworkBehaviourReference player)
        {
            if (player.TryGet(out PlayerControllerB groupLeader))
            {
                CreateOrJoinGroupAndSync(groupLeader);
            }
        }

        /// <inheritdoc cref="CreateOrJoinGroupWithMembers(PlayerControllerB, PlayerControllerB[], bool)"/>
        /// <remarks>
        /// This will automatically call <see cref="CreateGroupServerRpc(NetworkBehaviourReference)"/> if called on a client.
        /// </remarks>
        public void CreateOrJoinGroupWithMembersAndSync(PlayerControllerB leader, PlayerControllerB[] members, bool forceNewGroup = false)
        {
            if (IsServer)
            {
                CreateOrJoinGroupWithMembers(leader, members, forceNewGroup);
            }
            else
            {
                CreateOrJoinGroupWithMembersServerRpc(leader, members.Select(m => new NetworkBehaviourReference(m)).ToArray(), forceNewGroup);
            }
        }

        /// <summary>
        /// Creates a group with the given <paramref name="leader"/> and the given <paramref name="members"/>.
        /// </summary>
        /// <remarks>
        /// If the group already exists and the <paramref name="leader"/> is the leader of the group, 
        /// the given <paramref name="members"/> will just be added to the already existing group instead.<br/>
        /// Otherwise a new group with the given <paramref name="leader"/> and <paramref name="members"/> will be created.
        /// </remarks>
        /// <param name="leader">The player controller to make the leader of this group.</param>
        /// <param name="members">The player controllers to add as members to the group</param>
        /// <param name="forceNewGroup">Should we make a new group even if the given <paramref name="leader"/> is already a leader of another group?</param>
        private void CreateOrJoinGroupWithMembers(PlayerControllerB leader, PlayerControllerB[] members, bool forceNewGroup = false)
        {
            if (!IsServer)
                return;

            // Either we join or create a new group
            if (forceNewGroup
                || !IsPlayerGroupLeader(leader, out int groupID)
                || groupID == INVALID_GROUP_INDEX)
            {
                // Create our new group
                groupID = CreateGroup(leader);
            }

            // Make sure we have a vaild group here
            if (groupID != INVALID_GROUP_INDEX)
            {
                // Add all of the founding members
                foreach (PlayerControllerB member in members)
                {
                    AddToGroup(groupID, member);
                }
            }
        }

        /// <summary>
        /// Helper rpc that allows bots that are not owned by the host to auto create or join groups
        /// </summary>
        /// <param name="player"></param>
        /// <param name="players"></param>
        /// <param name="forceNewGroup"></param>
        [ServerRpc(RequireOwnership = false)]
        private void CreateOrJoinGroupWithMembersServerRpc(NetworkBehaviourReference player, NetworkBehaviourReference[] players, bool forceNewGroup = false)
        {
            // First get the group leader
            if (player.TryGet(out PlayerControllerB groupLeader))
            {
                // Now get the new group members
                HashSet<PlayerControllerB> members = new HashSet<PlayerControllerB>();
                foreach (NetworkBehaviourReference memberReference in players)
                {
                    if (memberReference.TryGet(out PlayerControllerB member))
                    {
                        members.Add(member);
                    }
                }
                CreateOrJoinGroupWithMembers(groupLeader, members.ToArray(), forceNewGroup);
            }
        }

        /// <summary>
        /// Removes the given <paramref name="member"/> from every group
        /// </summary>
        /// <param name="member"></param>
        private void RemoveFromCurrentGroup(PlayerControllerB member)
        {
            if (!IsServer)
                return;

            for (int i = LethalBotGroups.Count - 1; i >= 0; i--)
            {
                var group = LethalBotGroups[i];
                if (group.Member.TryGet(out PlayerControllerB m) && m == member)
                {
                    LethalBotGroups.RemoveAt(i);
                    if (group.IsLeader)
                    {
                        HandleLeaderRemoval(group.GroupId);
                    }
                }
            }
        }

        /// <summary>
        /// <inheritdoc cref="RemoveFromCurrentGroup(PlayerControllerB)"/>
        /// </summary>
        /// <remarks>
        /// This will automatically call <see cref="RemoveFromCurrentGroupServerRpc(NetworkBehaviourReference)"/> if called on a client.
        /// </remarks>
        /// <param name="member"></param>
        public void RemoveFromCurrentGroupAndSync(PlayerControllerB member)
        {
            if (IsServer)
            {
                RemoveFromCurrentGroup(member);
            }
            else
            {
                RemoveFromCurrentGroupServerRpc(member);
            }
        }

        /// <summary>
        /// Helper rpc that allows clients to remove themselves from groups
        /// </summary>
        /// <param name="player"></param>
        [ServerRpc(RequireOwnership = false)]
        private void RemoveFromCurrentGroupServerRpc(NetworkBehaviourReference player)
        {
            if (player.TryGet(out PlayerControllerB groupLeader))
            {
                RemoveFromCurrentGroup(groupLeader);
            }
        }

        #endregion

        #region Group Helper functions

        /// <summary>
        /// Checks if the given <paramref name="player"/> is in a group.
        /// </summary>
        /// <remarks>
        /// If you want the actual group id use <see cref="GetGroupId(PlayerControllerB)"/> instead.
        /// </remarks>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool IsPlayerInGroup(PlayerControllerB player)
        {
            return GetGroupId(player) != INVALID_GROUP_INDEX;
        }

        /// <summary>
        /// Checks if the given <paramref name="player"/> is in the given <paramref name="groupID"/>.
        /// </summary>
        /// <remarks>
        /// If you want to check if the player is in a group in general, use <see cref="IsPlayerInGroup(PlayerControllerB)"/> instead.
        /// </remarks>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool IsPlayerInGroup(PlayerControllerB player, int groupID)
        {
            return GetGroupId(player) == groupID;
        }

        /// <summary>
        /// Checks if the given <paramref name="player"/> is a group leader
        /// </summary>
        /// <param name="player"></param>
        /// <param name="groupId">The id of the group the given <paramref name="player"/> is in. <see cref="INVALID_GROUP_INDEX"/> if they are not in a group.</param>
        /// <returns></returns>
        public bool IsPlayerGroupLeader(PlayerControllerB player, out int groupId)
        {
            groupId = GetGroupId(player);
            if (groupId == INVALID_GROUP_INDEX)
                return false;

            if (groupLeaders.TryGetValue(groupId, out ulong leaderId))
                return leaderId == player.NetworkObjectId;

            return false;
        }

        /// <summary>
        /// Checks if the given players, <paramref name="a"/> and <paramref name="b"/> are in the same group
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns><see langword="true"/> if both players are in the same group; otherwise <see langword="false"/></returns>
        public bool ArePlayersInSameGroup(PlayerControllerB a, PlayerControllerB b)
        {
            int groupA = GetGroupId(a);
            int groupB = GetGroupId(b);

            return groupA != INVALID_GROUP_INDEX && groupA == groupB;
        }

        /// <summary>
        /// Returns the id of the group <paramref name="player"/> is in.
        /// </summary>
        /// <param name="player"></param>
        /// <returns>The id of the group the player is in or <see cref="INVALID_GROUP_INDEX"/> if the player isn't in a group</returns>
        public int GetGroupId(PlayerControllerB player)
        {
            // Check to see if this player is in a group
            if (memberToGroup.TryGetValue(player.NetworkObjectId, out int group))
            {
                return group;
            }

            // Return the invaild group id
            return INVALID_GROUP_INDEX;
        }

        /// <summary>
        /// Checks if the group with the given Id exists
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns><see langword="true"/> if the given <paramref name="groupId"/> exists; otherwise <see langword="false"/></returns>
        public bool DoesGroupExist(int groupId)
        {
            return groupMembers.ContainsKey(groupId);
        }

        /// <summary>
        /// Returns every registed group this round.
        /// </summary>
        /// <returns>The Id's of every group in this round</returns>
        public IEnumerable<int> GetAllGroups()
        {
            return groupMembers.Keys;
        }

        /// <summary>
        /// Returns the size of the given group
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns>The size of the given <paramref name="groupId"/>; 0 if the group doesn't exist.</returns>
        public int GetGroupSize(int groupId)
        {
            if (groupMembers.TryGetValue(groupId, out var members))
                return members.Count;

            return 0;
        }

        /// <summary>
        /// Returns every player that is in the given <paramref name="groupId"/>
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns>List of all the players in the <paramref name="groupId"/></returns>
        public List<PlayerControllerB> GetGroupMembers(int groupId)
        {
            List<PlayerControllerB> result = new List<PlayerControllerB>();
            if (!groupMembers.TryGetValue(groupId, out var members))
                return result;

            var spawnedObjects = NetworkManager.SpawnManager.SpawnedObjects;
            foreach (ulong id in members)
            {
                // Find and resolve the network objects
                if (spawnedObjects.TryGetValue(id, out var obj) 
                    && obj.TryGetComponent(out PlayerControllerB member))
                {
                    result.Add(member);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns every player that is in the same group as <paramref name="player"/>
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public List<PlayerControllerB> GetOtherGroupMembers(PlayerControllerB player)
        {
            int groupId = GetGroupId(player);
            if (groupId == INVALID_GROUP_INDEX)
                return new List<PlayerControllerB>();

            List<PlayerControllerB> members = GetGroupMembers(groupId);
            members.Remove(player);
            return members;
        }

        /// <summary>
        /// This returns the leader of the given <paramref name="groupId"/>
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns>The leader of the group or null</returns>
        public PlayerControllerB? GetGroupLeader(int groupId)
        {
            if (!groupLeaders.TryGetValue(groupId, out var leaderId))
                return null;

            // Find and resolve the network objects
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(leaderId, out var obj)
                && obj.TryGetComponent(out PlayerControllerB leader))
            {
                return leader;
            }

            return null;
        }

        /// <summary>
        /// Checks if anyone in the group is holding onto scrap.
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns></returns>
        public bool DoesGroupHaveScrap(int groupId)
        {
            List<PlayerControllerB> groupMembers = GetGroupMembers(groupId);
            foreach (PlayerControllerB member in groupMembers)
            {
                // Must be a valid player
                if (member == null) continue;
                LethalBotAI? isPlayerBot = LethalBotManager.Instance.GetLethalBotAI(member);
                if (isPlayerBot != null)
                {
                    // This makes bots ignore loadout items
                    if (isPlayerBot.HasScrapInInventory())
                    {
                        return true;
                    }
                }
                else
                {
                    // Support for human players
                    GrabbableObject? itemOnlySlot = member.ItemOnlySlot;
                    if (itemOnlySlot != null && LethalBotAI.IsItemScrap(itemOnlySlot))
                    {
                        return true;
                    }
                    foreach (GrabbableObject item in member.ItemSlots)
                    {
                        if (item != null && LethalBotAI.IsItemScrap(item))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the closest group member to <paramref name="player"/>.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="playerGroupId"></param>
        /// <returns>The closest group member or <see langword="null"/>, if <paramref name="player"/> is the only member in the group</returns>
        public PlayerControllerB? GetClosestGroupMember(PlayerControllerB player, int? playerGroupId = null)
        {
            int groupId = playerGroupId ?? GetGroupId(player);
            if (groupId == INVALID_GROUP_INDEX || GetGroupSize(groupId) <= 1)
                return null;

            var members = GetGroupMembers(groupId);
            Vector3 playerPos = player.transform.position;
            PlayerControllerB? closest = null;
            float closestDistSqr = float.MaxValue;
            foreach (var member in members)
            {
                if (member == player)
                    continue;

                float dist = (member.transform.position - playerPos).sqrMagnitude;
                if (dist < closestDistSqr)
                {
                    closestDistSqr = dist;
                    closest = member;
                }
            }

            return closest;
        }

        /// <summary>
        /// <inheritdoc cref="GetGroupCenter(int)"/>
        /// </summary>
        /// <param name="player"></param>
        /// <param name="groupId"></param>
        /// <returns><inheritdoc cref="GetGroupCenter(int)"/></returns>
        public Vector3 GetGroupCenter(PlayerControllerB player, int? groupId = null)
        {
            int id = groupId ?? GetGroupId(player);
            if (id == INVALID_GROUP_INDEX)
                return player.transform.position;

            return GetGroupCenter(id);
        }

        /// <summary>
        /// Returns the center position of the given group.
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns>
        /// The average position of all members in the group.<br/>
        /// Returns <see cref="Vector3.zero"/> if the group does not exist.
        /// </returns>
        public Vector3 GetGroupCenter(int groupId)
        {
            if (!groupMembers.TryGetValue(groupId, out var members) || members.Count == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            var spawnedObjects = NetworkManager.SpawnManager.SpawnedObjects;
            foreach (ulong id in members)
            {
                if (spawnedObjects.TryGetValue(id, out var obj) 
                    && obj.TryGetComponent(out PlayerControllerB member))
                {
                    sum += member.transform.position;
                    count++;
                }
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        /// <summary>
        /// Returns the furthest member from the group.
        /// </summary>
        /// <remarks>
        /// This is a great way to find stragglers in the group.
        /// </remarks>
        /// <param name="groupId"></param>
        /// <returns>The furthest <see cref="PlayerControllerB"/> that is in our given group.</returns>
        public PlayerControllerB? GetFurthestMemberFromCenter(int groupId)
        {
            // Make sure the group exists and has more than one member.
            if (!groupMembers.TryGetValue(groupId, out var members) || members.Count <= 1)
                return null;

            Vector3 center = GetGroupCenter(groupId);
            PlayerControllerB? furthest = null;
            float furthestDistSqr = 0f;
            var spawnedObjects = NetworkManager.SpawnManager.SpawnedObjects;
            foreach (ulong id in members)
            {
                if (spawnedObjects.TryGetValue(id, out var obj) &&
                    obj.TryGetComponent(out PlayerControllerB member))
                {
                    float dist = (member.transform.position - center).sqrMagnitude;
                    if (dist > furthestDistSqr)
                    {
                        furthestDistSqr = dist;
                        furthest = member;
                    }
                }
            }

            return furthest;
        }

        /// <summary>
        /// Helper function to reset and clear all groups
        /// </summary>
        public void ResetAndRemoveAllGroups()
        {
            // Server only
            if (!base.IsServer)
            {
                return;
            }

            nextGroupId = DEFAULT_GROUP_INDEX;
            memberToGroup.Clear();
            groupMembers.Clear();
            groupLeaders.Clear();
            LethalBotGroups.Clear();
        }

        #endregion

        #region Bot Helpers

        /// <summary>
        /// Internal helper that checks if an internal group exists
        /// </summary>
        /// <param name="lethalBotAI"></param>
        /// <param name="groupID">The group id for the runtime group. Will return <see cref="INVALID_GROUP_INDEX"/> if no internal group exists</param>
        /// <returns></returns>
        public bool DoesInternalGroupExist(LethalBotAI lethalBotAI, out int groupID)
        {
            groupID = INVALID_GROUP_INDEX;
            if (lethalBotAI == null)
            {
                return false;
            }

            int desiredInternalGroupID = lethalBotAI.LethalBotIdentity.GroupID;
            LethalBotAI[] lethalBotAIs = LethalBotManager.Instance.GetLethalBotAIs();
            foreach (var otherLethalBotAI in lethalBotAIs)
            {
                // Must be valid and not us.
                if (otherLethalBotAI == null 
                    || otherLethalBotAI == lethalBotAI)
                {
                    continue;
                }

                // They are dead, not a valid member of a group.....anymore
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (lethalBotController == null 
                    || !lethalBotController.isPlayerControlled 
                    || lethalBotController.isPlayerDead)
                {
                    continue;
                }

                // Check if we have the same internal group
                if (otherLethalBotAI.LethalBotIdentity.GroupID == desiredInternalGroupID)
                {
                    if (IsPlayerGroupLeader(otherLethalBotAI.NpcController.Npc, out groupID))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        private void HandleLeaderRemoval(int groupId)
        {
            // Find the best person to make the leader of our group
            // We prefer human players over bots
            LethalBotGroupMemberNetworkSerializable? newLeader = null;
            int groupIndex = -1;
            for (int i = 0; i < LethalBotGroups.Count; i++)
            {
                var group = LethalBotGroups[i];
                if (group.GroupId == groupId 
                    && !group.IsLeader 
                    && group.Member.TryGet(out PlayerControllerB member))
                {
                    // The first bot we find will be the leader unless we find a human player.
                    bool isHumanPlayer = !LethalBotManager.Instance.IsPlayerLethalBot(member);
                    if (newLeader == null || isHumanPlayer)
                    {
                        groupIndex = i;
                        newLeader = group;
                        if (isHumanPlayer)
                        {
                            break;
                        }
                    }
                }
            }

            if (newLeader != null)
            {
                var updated = newLeader.Value;
                updated.IsLeader = true;
                LethalBotGroups[groupIndex] = updated;
            }
        }

        private void OnGroupListChanged(NetworkListEvent<LethalBotGroupMemberNetworkSerializable> change)
        {
            RebuildLookups();
        }

        /// <summary>
        /// This rebuilds the lookup cache for all groups!
        /// </summary>
        private void RebuildLookups()
        {
            // Clean up the old tables
            memberToGroup.Clear();
            groupMembers.Clear();
            groupLeaders.Clear();

            foreach (var group in LethalBotGroups)
            {
                // NOTE: WE don't need the PlayerControllerB objects here, since we are only using the NetworkObjectIds
                if (group.Member.TryGet(out NetworkBehaviour member))
                {
                    // Attach IDs to what group they are assigned to
                    ulong id = member.NetworkObjectId;
                    memberToGroup[id] = group.GroupId;
                    if (group.IsLeader)
                    {
                        groupLeaders[group.GroupId] = id;
                    }

                    // If this is a new group, we need to setup the lookup cache
                    if (!groupMembers.TryGetValue(group.GroupId, out var set))
                    {
                        set = new HashSet<ulong>();
                        groupMembers[group.GroupId] = set;
                    }

                    // Add this ID to the set
                    set.Add(id);
                }
            }
        }

    }
}
