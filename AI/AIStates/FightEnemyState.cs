using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.NetworkSerializers;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// A state where the bot goes to fight an enemy!
    /// </summary>
    public class FightEnemyState : AIState
    {
        private float attackFOV;
        private bool canHitTarget;
        private Coroutine? currentAttackRoutine;
        private Collider? _enemyCollision;
        private EnemyAI? _lastEnemy;
        private float lastColliderUpdateTimer;
        private Collider? EnemyCollider
        {
            get
            {
                if (_lastEnemy != CurrentEnemy || (Time.timeSinceLevelLoad - lastColliderUpdateTimer) > 2f)
                {
                    _enemyCollision = FindEnemyCollider(CurrentEnemy, npcController.Npc.gameplayCamera.transform.position);
                    _lastEnemy = CurrentEnemy;
                    lastColliderUpdateTimer = Time.timeSinceLevelLoad;
                    Plugin.LogDebug($"Enemy: {CurrentEnemy} Enemy Collider: {_enemyCollision}");
                }
                return _enemyCollision;
            }
        }
        public FightEnemyState(AIState oldState, EnemyAI enemyAI, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.FightEnemy;

            this.CurrentEnemy = enemyAI;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (this.CurrentEnemy == null 
                    || this.CurrentEnemy.isEnemyDead)
                {
                    Plugin.LogWarning("FightEnemyState: CurrentEnemy is null or dead, cannot start the state!");
                    ChangeBackToPreviousState();
                    return;
                }
                float? fearRange = ai.GetFearRangeForEnemies(this.CurrentEnemy);
                if (!fearRange.HasValue
                    || !ai.ShouldAttackEnemy(this.CurrentEnemy, LethalBotManager.Instance.MissionControlPlayer == npcController.Npc)
                    || !ai.HasCombatWeapon())
                {
                    ChangeBackToPreviousState();
                    return;
                }
                canHitTarget = false;
                StartAttackCoroutine(); // Start the attack coroutine!
            }
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            // If we got interupted while using the Zap Gun, break the beam!
            if (ai.HeldItem is PatcherTool patcherTool && patcherTool.isShocking)
            {
                patcherTool.UseItemOnClient(true);
            }
            base.OnExitState(newState);
        }

        public override void DoAI()
        {
            // Enemy is either dead or invaild!
            if (CurrentEnemy == null || CurrentEnemy.isEnemyDead)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Kinda hard to kill an enemy without a weapon
            if (!ai.ShouldAttackEnemy(CurrentEnemy, LethalBotManager.Instance.MissionControlPlayer == npcController.Npc) || !ai.HasCombatWeapon())
            {
                ChangeBackToPreviousState(); 
                return;
            }

            // Not a threat!
            float? fearRange = ai.GetFearRangeForEnemies(CurrentEnemy);
            if (!fearRange.HasValue)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Check if another enemy is closer
            EnemyAI? newEnemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (newEnemyAI != null && newEnemyAI != this.CurrentEnemy)
            {
                float? newFearRange = ai.GetFearRangeForEnemies(newEnemyAI);
                if (newFearRange.HasValue && ai.ShouldAttackEnemy(newEnemyAI, LethalBotManager.Instance.MissionControlPlayer == npcController.Npc))
                {
                    this.CurrentEnemy = newEnemyAI;
                    fearRange = newFearRange.Value;
                }
                // else no fear range, ignore this enemy, already ignored by CheckLOSForEnemy but hey better be safe
            }

            // Alright, lets select out weapon!
            // We prefer a ranged weapon if possible!
            if (!ai.TryFindItemInInventory(FindObject, FindBetterObject, out int weaponSlot))
            {
                // HOW DID THIS HAPPEN!!!!!
                // HasCombatWeapon checks if the bot has a weapon in the first place!
                // This may be caused by a race conditon or another mod!
                Plugin.LogWarning($"Bot {npcController.Npc.playerUsername} didn't have a weapon despite HasCombatWeapon telling us we did!");
                ChangeBackToPreviousState();
                return;
            }

            // We don't have time to be in a phone call right now!
            if (Plugin.IsModLethalPhonesLoaded)
            {
                ai.HangupPhone();
            }

            // Switch to our weapon!
            GrabbableObject? heldItem = ai.HeldItem;
            if (npcController.Npc.currentItemSlot != weaponSlot 
                || !ItemsManager.Instance.TryGetWeaponInfo(heldItem, out WeaponInfo? weaponInfo))
            {
                if (heldItem != null && heldItem.itemProperties.twoHanded)
                {
                    npcController.Npc.DiscardHeldObject();
                    LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                    return;
                }
                ai.SwitchItemSlotsAndSync(weaponSlot);
                return;
            }

            // ATTACK!
            StartAttackCoroutine();

            // Close enough to use item, attempt to use
            float enemySize = EnemyCollider != null ? EnemyCollider.bounds.extents.magnitude : 0.4f;
            float sqrMagDistanceEnemy = (this.CurrentEnemy.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            float maxEnemyDistance = weaponInfo.GetAttackRangeForWeapon(heldItem) + enemySize;
            float fallBackDistance = maxEnemyDistance * 0.75f;
            float giveupRange = fearRange.Value * 1.5f;
            Vector3 targetPos = EnemyCollider != null ? EnemyCollider.bounds.center : this.CurrentEnemy.eye.position;
            Vector3 enemyPos = RoundManager.Instance.GetNavMeshPosition(CurrentEnemy.transform.position, default, 2.7f);
            if (sqrMagDistanceEnemy < maxEnemyDistance * maxEnemyDistance && canHitTarget)
            {
                // We are close enough to the enemy, lets attack!
                if (!npcController.Npc.inAnimationWithEnemy)
                {
                    // If we are too close to the enemy, we need to back off a bit!
                    // After all, we don't want to be in grabbing range of the enemy!
                    if (sqrMagDistanceEnemy < fallBackDistance * fallBackDistance)
                    {
                        Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - this.CurrentEnemy.transform.position + Vector3.up * 0.2f);
                        ray.direction = new Vector3(ray.direction.x, 0f, ray.direction.z);
                        Vector3 pos = (!Physics.Raycast(ray, out RaycastHit hit, maxEnemyDistance, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) ? ray.GetPoint(maxEnemyDistance) : hit.point;
                        Vector3 fallbackPos = RoundManager.Instance.GetNavMeshPosition(pos, default, 2.7f);
                        ai.SetDestinationToPositionLethalBotAI(fallbackPos);
                        npcController.OrderToSprint(); // Sprint, we need to move NOW!
                        ai.OrderMoveToDestination();
                    }
                    else
                    {
                        ai.StopMoving();
                    }
                }
            }
            // Enemy is outside our retreat range, abort!
            else if (sqrMagDistanceEnemy > giveupRange * giveupRange
                || !ai.IsValidPathToTarget(enemyPos))
            {
                ChangeBackToPreviousState();
                return;
            }
            else
            {
                // Else get close to target
                ai.SetDestinationToPositionLethalBotAI(enemyPos);
                npcController.OrderToSprint(); // Sprint, we need to move NOW!
                ai.OrderMoveToDestination();
            }

            // Look at target or not if hidden by stuff
            if (!Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, targetPos + Vector3.up * 0.2f, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault)
                || hitInfo.collider.gameObject.GetComponentInParent<EnemyAI>() == this.CurrentEnemy)
            {
                npcController.OrderToLookAtPosition(this.CurrentEnemy.NetworkObject, EnumLookAtPriority.HIGH_PRIORITY, 1f, true, maxBodyFOV: attackFOV);
            }
            else
            {
                npcController.OrderToLookForward();
            }
        }

        public override void UseHeldItem()
        {
            // Don't use our held item, we manage it ourselves!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem == null
                || !ai.CanUseHeldItem())
            {
                return;
            }

            // We manage weapons!
            if (ItemsManager.Instance.IsItemWeapon(heldItem))
            {
                return;
            }
            else if (heldItem is WalkieTalkie walkieTalkie)
            {
                // Stop talking on the walkie, we are in combat!
                if (walkieTalkie.isBeingUsed && walkieTalkie.isHoldingButton)
                {
                    walkieTalkie.UseItemOnClient(false);
                }

                return;
            }

            base.UseHeldItem();
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopAttackCoroutine();
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            // Wait for cooldown and that we are holding a weapon.
            if (ai.AreHandsFree() || !ai.IsHoldingCombatWeapon())
            {
                return;
            }

            // Play attack voice line based on weapon type!
            EnumVoicesState voiceState = ai.IsHoldingRangedWeapon() ? EnumVoicesState.AttackingWithGun : EnumVoicesState.AttackingWithMelee;
            ai.LethalBotIdentity.Voice.TryPlayVoiceAudio(new PlayVoiceParameters()
            {
                VoiceState = voiceState,
                CanTalkIfOtherLethalBotTalk = true,
                WaitForCooldown = ai.LethalBotIdentity.Voice.LastVoiceState == voiceState, // Only wait for cooldown if we are trying to repeat the same voice state, otherwise we can interrupt ourselves!
                CutCurrentVoiceStateToTalk = true,
                CanRepeatVoiceState = true,

                ShouldSync = true,
                IsLethalBotInside = npcController.Npc.isInsideFactory,
                AllowSwearing = Plugin.Config.AllowSwearing.Value
            });
        }

        /// <inheritdoc cref="AIState.RegisterSignalTranslatorCommands"/>
        public static new void RegisterSignalTranslatorCommands()
        {
            // We are fighting right now, these messages should be queued!
            // Return to the ship when we finish!
            SignalTranslatorCommandsManager.RegisterCommandForState<FightEnemyState>(new SignalTranslatorCommand(Const.RETURN_COMMAND, (state, lethalBotAI, message) =>
            {
                FightEnemyState fightEnemyState = (FightEnemyState)state;
                if (state.CurrentEnemy != null && state.CurrentEnemy.targetPlayer != lethalBotAI.NpcController.Npc)
                {
                    lethalBotAI.State = new ReturnToShipState(state);
                    return true;
                }
                fightEnemyState.previousAIState = new ReturnToShipState(state);
                return true;
            }));
        }

        public override bool? ShouldBotCrouch()
        {
            return false;
        }

        /// <summary>
        /// Changes back to the previous state
        /// </summary>
        protected override void ChangeBackToPreviousState()
        {
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem != null)
            {
                if (heldItem is ShotgunItem shotgun && !shotgun.safetyOn)
                {
                    // Put the safety back on!
                    shotgun.ItemInteractLeftRightOnClient(false);
                }
            }

            if (previousState == EnumAIStates.SearchingForScrap
                    || (previousState == EnumAIStates.FetchingObject && !ai.IsFollowingTargetPlayer()))
            {
                // If we have some scrap, it might be a good time to bring it back,
                // just in case.....
                if (ai.HasScrapInInventory())
                {
                    // Now, lets check if someone is assigned to transfer loot
                    bool shouldWalkLootToShip = true;
                    if (LethalBotManager.Instance.LootTransferPlayers.Count > 0)
                    {
                        shouldWalkLootToShip = false;
                    }
                    ai.State = new ReturnToShipState(this, !shouldWalkLootToShip, new SearchingForScrapState(this));
                    return;
                }
            }
            base.ChangeBackToPreviousState();
        }

        /// <summary>
        /// Helper function to find the collider of an enemy!
        /// </summary>
        /// <param name="CurrentEnemy">the enemy to find the collider for</param>
        /// <param name="ourPos"></param>
        /// <returns>the found collider or null</returns>
        private static Collider? FindEnemyCollider(EnemyAI? CurrentEnemy, Vector3 ourPos)
        {
            Collider? result = null;
            float resultDistSqr = float.MaxValue;
            if (CurrentEnemy != null)
            {
                Plugin.LogDebug($"Attempting to get the enemy collider!");
                Collider[] colliders = CurrentEnemy.gameObject.GetComponentsInChildren<Collider>();
                foreach (Collider childCollider in colliders)
                {
                    // Scan nodes are not the enemy's collider!
                    ScanNodeProperties? component = childCollider?.transform.gameObject.GetComponent<ScanNodeProperties>();
                    if (childCollider != null && component == null)
                    {
                        Vector3 closestPoint = childCollider.ClosestPoint(ourPos);
                        float childColliderDistSqr = (closestPoint - ourPos).sqrMagnitude;
                        if (result == null || childColliderDistSqr < resultDistSqr)
                        {
                            result = childCollider;
                            resultDistSqr = childColliderDistSqr;
                        }
                        //break; // For now stop at the first valid instance!
                    }
                }

                Plugin.LogDebug($"{(result != null ? "Found" : "Not Found")}");

                return result;
            }
            else
            {
               return null; // Clear cache if no current enemy
            }
        }

        private IEnumerator weaponAttackCoroutine()
        {
            CountdownTimer attackCooldownTimer = new CountdownTimer();
            while (ai.State != null
                && ai.State == this
                && this.CurrentEnemy != null 
                && !CurrentEnemy.isEnemyDead)
            {
                // Make sure we have a weapon and this is a weapon we know how to use!
                GrabbableObject? heldItem = ai.HeldItem;
                if (!ItemsManager.Instance.TryGetWeaponInfo(heldItem, out WeaponInfo? weaponInfo))
                {
                    yield return null;
                    continue;
                }

                // Check if we are still close enough!
                Vector3 targetPos = EnemyCollider != null ? EnemyCollider.bounds.center : this.CurrentEnemy.eye.position;
                if (!CanHitEnemyWithHeldItem(heldItem, targetPos))
                {
                    canHitTarget = false;
                    yield return null;
                    continue;
                }

                // We have a shot!
                canHitTarget = true;
                if (attackCooldownTimer.HasStarted() && !attackCooldownTimer.Elapsed())
                {
                    yield return null;
                    continue;
                }

                // ATTACK!
                bool skipCooldown = false;
                yield return weaponInfo.AttackWithWeapon(npcController.Npc, heldItem, CurrentEnemy, EnemyCollider, r => skipCooldown = r);

                // Did the weapon do its own cooldown, skip the default one then
                if (skipCooldown) continue;

                // Start the cooldown timer
                attackCooldownTimer.Start(weaponInfo.GetWeaponAttackInterval(heldItem));
            }

            StopAttackCoroutine();
        }

        /// <summary>
        /// Helper function that checks if we are aiming on target!
        /// </summary>
        /// <param name="heldItem">The weapon the bot is using.</param>
        /// <param name="targetPos">The position we want to hit.</param>
        /// <returns></returns>
        private bool CanHitEnemyWithHeldItem(GrabbableObject heldItem, Vector3 targetPos)
        {
            if (this.CurrentEnemy == null)
                return false;

            WeaponInfo? weaponInfo = ItemsManager.Instance.GetWeaponInfo(heldItem);
            if (weaponInfo == null)
                return false;

            PlayerControllerB lethalBotController = npcController.Npc;
            if (this.CurrentEnemy is BushWolfEnemy bushWolf 
                && bushWolf.draggingPlayer == lethalBotController
                && !weaponInfo.IsRanged(heldItem))
            {
                return true; // SAVE OURSELF!
            }

            Vector3 toEnemy = targetPos - lethalBotController.gameplayCamera.transform.position;
            float angleToEnemy = Vector3.Angle(lethalBotController.playerEye.forward, toEnemy);

            // Check if we can potentially hit!
            weaponInfo.GetWeaponAttackInfo(heldItem, lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask);
            attackFOV = Mathf.Clamp(maxFOV, 0f, Const.LETHAL_BOT_FOV);
            if (angleToEnemy < maxFOV)
            {
                // Choose a checker function based on the weapon!
                return weaponInfo.CanHitWithWeapon(lethalBotController, CurrentEnemy, EnemyCollider, ray, radius, maxRange, hitMask);
            }

            return false;
        }

        public override Vector3? SelectSubjectTargetPoint(LookAtTarget lookAtTarget, NetworkObject? subject, PlayerControllerB ourController)
        {
            // Change where we are aiming based on the given network object
            if (subject != null && subject.TryGetComponent<EnemyAI>(out var enemyAI))
            {
                Collider? lookAtSubjectCollider = enemyAI == this.CurrentEnemy ? EnemyCollider : FindEnemyCollider(enemyAI, ourController.gameplayCamera.transform.position);
                if (lookAtSubjectCollider != null)
                {
                    return lookAtSubjectCollider.bounds.center;
                }
            }
            
            // Base logic for everything else
            return base.SelectSubjectTargetPoint(lookAtTarget, subject, ourController);
        }

        /// <summary>
        /// We need to find a weapon that has ammo!
        /// </summary>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        protected override bool FindObject(GrabbableObject item)
        {
            // Don't pick an empty weapon!
            // NOTE: HasAmmoForWeapon, checks if the item is a weapon internally!
            return ai.HasAmmoForWeapon(item);
        }

        /// <summary>
        /// Determines whether the specified candidate object is a better choice than the current best object for the
        /// bot to use, based on weapon type and the current enemy.
        /// </summary>
        /// <remarks>
        /// When the current enemy is a Snare Flea, melee weapons are preferred over ranged
        /// weapons. In other cases, ranged weapons are preferred if the current best is not ranged and the candidate
        /// is.<br/>
        /// <inheritdoc cref="AIState.FindBetterObject(GrabbableObject, GrabbableObject)"/>
        /// </remarks>
        /// <inheritdoc cref="AIState.FindBetterObject(GrabbableObject, GrabbableObject)"/>
        protected override bool FindBetterObject(GrabbableObject currentBest, GrabbableObject canidate)
        {
            ItemsManager instanceIM = ItemsManager.Instance;
            bool isCurrentRanged = instanceIM.IsItemRangedWeapon(currentBest);
            bool isCanidateRanged = instanceIM.IsItemRangedWeapon(canidate);
            if (this.CurrentEnemy is CentipedeAI)
            {
                if (isCurrentRanged && !isCanidateRanged)
                {
                    return true; // We want to use a melee weapon on the snare flea!
                }
                else
                {
                    // We don't want to use a ranged weapon on the snare flea if possible!
                    return false;
                }
            }
            else if (!isCurrentRanged && isCanidateRanged)
            {
                return true; // Prefer ranged weapons otherwise
            }

            return false;
        }

        private void StartAttackCoroutine()
        {
            if (currentAttackRoutine == null)
            {
                currentAttackRoutine = ai.StartCoroutine(weaponAttackCoroutine());
            }
        }

        private void StopAttackCoroutine()
        {
            if (currentAttackRoutine != null)
            {
                ai.StopCoroutine(currentAttackRoutine);
                currentAttackRoutine = null;
            }
        }
    }
}
