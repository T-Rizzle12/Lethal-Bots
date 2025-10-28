using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem.HID;
using static UnityEngine.SendMouseEvents;

namespace LethalBots.AI.AIStates
{
    /// <summary>
    /// A state where the bot goes to fight an enemy!
    /// </summary>
    public class FightEnemyState : AIState
    {
        private static readonly FieldInfo knifeMask = AccessTools.Field(typeof(KnifeItem), "knifeMask");
        private static readonly FieldInfo shovelMask = AccessTools.Field(typeof(Shovel), "shovelMask");
        private RaycastHit[]? enemyColliders;
        private Coroutine? currentAttackRoutine;
        private Collider? _enemyCollision;
        private EnemyAI? _lastEnemy;
        private Collider? EnemyCollision
        {
            get
            {
                if (_lastEnemy != currentEnemy)
                {
                    _enemyCollision = FindEnemyCollider(currentEnemy);
                    _lastEnemy = currentEnemy;
                    Plugin.LogDebug($"Enemy: {currentEnemy} Enemy Collider: {_enemyCollision}");
                }
                return _enemyCollision;
            }
        }
        public FightEnemyState(AIState oldState, EnemyAI enemyAI, AIState? changeToOnEnd = null) : base(oldState, changeToOnEnd)
        {
            CurrentState = EnumAIStates.FightEnemy;

            this.currentEnemy = enemyAI;
        }

        public override void OnEnterState()
        {
            if (!hasBeenStarted)
            {
                if (this.currentEnemy == null 
                    || this.currentEnemy.isEnemyDead)
                {
                    Plugin.LogWarning("FightEnemyState: currentEnemy is null or dead, cannot start the state!");
                    ChangeBackToPreviousState();
                    return;
                }
                float? fearRange = ai.GetFearRangeForEnemies(this.currentEnemy);
                if (!fearRange.HasValue
                    || !ai.CanEnemyBeKilled(this.currentEnemy)
                    || !ai.HasCombatWeapon())
                {
                    ChangeBackToPreviousState();
                    return;
                }
                StartAttackCoroutine(); // Start the attack coroutine!
            }
            base.OnEnterState();
        }

        public override void DoAI()
        {
            // Enemy is either dead or invaild!
            if (currentEnemy == null || currentEnemy.isEnemyDead)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Kinda hard to kill an enemy without a weapon
            if (!ai.CanEnemyBeKilled(currentEnemy) || !ai.HasCombatWeapon())
            {
                ChangeBackToPreviousState(); 
                return;
            }

            // Not a threat!
            float? fearRange = ai.GetFearRangeForEnemies(currentEnemy);
            if (!fearRange.HasValue)
            {
                ChangeBackToPreviousState();
                return;
            }

            // Check if another enemy is closer
            EnemyAI? newEnemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (newEnemyAI != null && newEnemyAI != this.currentEnemy)
            {
                float? newFearRange = ai.GetFearRangeForEnemies(newEnemyAI);
                if (newFearRange.HasValue && ai.CanEnemyBeKilled(newEnemyAI))
                {
                    this.currentEnemy = newEnemyAI;
                    fearRange = newFearRange.Value;
                }
                // else no fear range, ignore this enemy, already ignored by CheckLOSForEnemy but hey better be safe
            }

            // Alright, lets select out weapon!
            // We prefer a ranged weapon if possible!
            int weaponSlot = -1;
            for (int i = 0; i < npcController.Npc.ItemSlots.Length; i++)
            {
                GrabbableObject? weapon = npcController.Npc.ItemSlots[i];
                if (LethalBotAI.IsItemWeapon(weapon))
                {
                    // Don't pick an empty weapon!
                    // FIXME: We should use a helper function and some enum variables rather
                    // than this hacky mess!
                    if (!ai.HasAmmoForWeapon(weapon))
                    {
                        continue;
                    }
                    weaponSlot = i;
                    if (this.currentEnemy is CentipedeAI)
                    {
                        if (!LethalBotAI.IsItemRangedWeapon(weapon))
                        {
                            break; // We want to use a melee weapon on the snare flea!
                        }
                        else
                        {
                            // We don't want to use a ranged weapon on the snare flea if possible!
                            continue;
                        }
                    }
                    else if (LethalBotAI.IsItemRangedWeapon(weapon))
                    {
                        break;
                    }
                }
            }

            // HOW DID THIS HAPPEN!!!!!
            // HasCombatWeapon checks if the bot has a weapon in the first place!
            // This may be caused by a race conditon or another mod!
            if (weaponSlot == -1)
            {
                Plugin.LogWarning($"Bot {npcController.Npc.playerUsername} didn't have a weapon despite HasCombatWeapon telling us we did!");
                ChangeBackToPreviousState();
                return;
            }

            // Switch to our weapon!
            if (npcController.Npc.currentItemSlot != weaponSlot 
                || !ai.IsHoldingCombatWeapon())
            {
                GrabbableObject? heldItem = ai.HeldItem;
                if (heldItem != null && heldItem.itemProperties.twoHanded)
                {
                    ai.DropItem();
                    LethalBotAI.DictJustDroppedItems.Remove(heldItem); //HACKHACK: Since DropItem set the just dropped item timer, we clear it here!
                    return;
                }
                ai.SwitchItemSlotsAndSync(weaponSlot);
                return;
            }

            // Close enough to use item, attempt to use
            float enemySize = EnemyCollision != null ? EnemyCollision.bounds.extents.magnitude : 0.4f;
            float sqrMagDistanceEnemy = (this.currentEnemy.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            float maxEnemyDistance = GetAttackRangeForWeapon(ai.HeldItem) + enemySize;
            float fallBackDistance = maxEnemyDistance * 0.75f;
            Vector3 targetPos = EnemyCollision != null ? EnemyCollision.bounds.center : this.currentEnemy.eye.position;
            if (sqrMagDistanceEnemy < maxEnemyDistance * maxEnemyDistance && !Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, targetPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                // We are close enough to the enemy, lets attack!
                if (!npcController.Npc.inAnimationWithEnemy)
                {
                    // If we are too close to the enemy, we need to back off a bit!
                    // After all, we don't want to be in grabbing range of the enemy!
                    if (sqrMagDistanceEnemy < fallBackDistance * fallBackDistance)
                    {
                        ai.SetDestinationToPositionLethalBotAI(this.currentEnemy.transform.position - npcController.Npc.transform.position + Vector3.back * maxEnemyDistance);
                        npcController.OrderToSprint(); // Sprint, we need to move NOW!
                        ai.OrderMoveToDestination();
                    }
                    else
                    {
                        ai.StopMoving();
                    }

                    // Start the attack coroutine if not already done so!
                    StartAttackCoroutine();
                }
            }
            // Enemy is outside our retreat range, abort!
            else if (sqrMagDistanceEnemy > (fearRange * 2) * (fearRange * 2) 
                || !ai.IsValidPathToTarget(currentEnemy.transform.position))
            {
                ChangeBackToPreviousState();
                return;
            }
            else
            {
                // Else get close to target
                ai.SetDestinationToPositionLethalBotAI(currentEnemy.transform.position);
                npcController.OrderToSprint(); // Sprint, we need to move NOW!
                ai.OrderMoveToDestination();
            }

            // Look at target or not if hidden by stuff
            if (!Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, targetPos + Vector3.up * 0.2f, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault)
                || hitInfo.collider.gameObject.GetComponentInParent<EnemyAI>() == this.currentEnemy)
            {
                npcController.OrderToLookAtPosition(targetPos);
            }
            else
            {
                npcController.OrderToLookForward();
            }
        }

        public override void UseHeldItem()
        {
            // Don't use our held item, we manage it ourselves!
            return;
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopAttackCoroutine();
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return;
        }

        // We are fighting right now, these messages should be queued!
        public override void OnSignalTranslatorMessageReceived(string message)
        {
            // Return to the ship when we finish!
            if (message == "return")
            {
                if (this.currentEnemy != null && this.currentEnemy.targetPlayer != npcController.Npc)
                {
                    ai.State = new ReturnToShipState(this);
                    return;
                }
                previousAIState = new ReturnToShipState(this);
                return;
            }
            base.OnSignalTranslatorMessageReceived(message);
        }

        public override bool? ShouldBotCrouch()
        {
            return false;
        }

        /// <summary>
        /// Grabs the desired attack range for the bot!
        /// </summary>
        /// <remarks>
        /// NOTE: This assumes the shotgun and shovel only!
        /// </remarks>
        /// <param name="weapon"></param>
        /// <returns></returns>
        private float GetAttackRangeForWeapon(GrabbableObject? weapon)
        {
            if (weapon == null)
            {
                return 2f; // Assume its the shovel!
            }
            if (LethalBotAI.IsItemRangedWeapon(weapon))
            {
                return 5f;
            }
            return 2f;
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
                    ai.State = new ReturnToShipState(this);
                    return;
                }
            }
            base.ChangeBackToPreviousState();
        }

        /// <summary>
        /// Helper function to find the collider of an enemy!
        /// </summary>
        /// <param name="currentEnemy">the enemy to find the collider for</param>
        /// <returns>the found collider or null</returns>
        private static Collider? FindEnemyCollider(EnemyAI? currentEnemy)
        {
            Collider? result = null;
            if (currentEnemy != null)
            {
                Collider[] colliders;
                Plugin.LogDebug($"Attempt 1 to get the enemy collider!");
                colliders = currentEnemy.gameObject.GetComponents<Collider>();
                foreach (Collider collider in colliders)
                {
                    // Scan nodes are not the enemy's collider!
                    ScanNodeProperties? component = result?.transform.gameObject.GetComponent<ScanNodeProperties>();
                    if (collider != null && component == null)
                    {
                        result = collider;
                        break; // For now stop at the first valid instance!
                    }
                }

                Plugin.LogDebug($"{(result != null ? "Found" : "Not Found")}");

                if (result == null)
                {
                    Plugin.LogDebug($"Attempt 2 to get the enemy collider!");
                    colliders = currentEnemy.gameObject.GetComponentsInChildren<Collider>();
                    foreach (Collider childCollider in colliders)
                    {
                        // Scan nodes are not the enemy's collider!
                        ScanNodeProperties? component = result?.transform.gameObject.GetComponent<ScanNodeProperties>();
                        if (childCollider != null && component == null)
                        {
                            result = childCollider;
                            break; // For now stop at the first valid instance!
                        }
                    }

                    Plugin.LogDebug($"{(result != null ? "Found" : "Not Found")}");

                }

                return result;
            }
            else
            {
               return null; // Clear cache if no current enemy
            }
        }

        private IEnumerator weaponAttackCoroutine()
        {
            while (ai.State != null
                && ai.State == this
                && this.currentEnemy != null 
                && !currentEnemy.isEnemyDead)
            {
                // Make sure we have a weapon!
                if (ai.AreHandsFree() || !ai.IsHoldingCombatWeapon())
                {
                    yield return null;
                    continue;
                }

                // Check if we are still close enough!
                GrabbableObject heldItem = ai.HeldItem;
                //float enemySize = EnemyCollision != null ? EnemyCollision.bounds.extents.magnitude : 0.4f;
                Vector3 targetPos = EnemyCollision != null ? EnemyCollision.bounds.center : this.currentEnemy.eye.position;
                //float sqrMagDistanceEnemy = (targetPos - npcController.Npc.transform.position).sqrMagnitude;
                //float maxEnemyDistance = GetAttackRangeForWeapon(heldItem) + enemySize;
                if (!CanHitEnemyWithHeldItem(heldItem, targetPos)) // sqrMagDistanceEnemy >= maxEnemyDistance * maxEnemyDistance
                {
                    yield return null;
                    continue;
                }

                if (heldItem is ShotgunItem shotgun)
                {
                    // Can't fire, we are reloading!
                    if (shotgun.isReloading)
                    {
                        yield return null;
                        continue;
                    }
                    // Kinda hard to use the shotgun with the safety on!
                    else if (shotgun.safetyOn)
                    {
                        shotgun.ItemInteractLeftRightOnClient(false);
                        yield return null;
                        continue;
                    }
                    // Reload the shotgun!
                    else if (shotgun.shellsLoaded <= 0)
                    {
                        shotgun.ItemInteractLeftRightOnClient(true);
                        yield return null;
                        continue;
                    }
                    else
                    {
                        heldItem.UseItemOnClient(true); // Should I call ItemActivate instead?
                    }
                }
                else
                {
                    heldItem.UseItemOnClient(true); // Should I call ItemActivate instead?
                    yield return null;
                    // holdButtonUse is true for the shovel!
                    // This means we need to release it next frame!
                    if (heldItem.itemProperties.holdButtonUse)
                    {
                        heldItem.UseItemOnClient(false); // HACKHACK: Fake release the button!
                    }
                }

                yield return new WaitForSeconds(GetWeaponAttackInterval(heldItem));
            }
        }

        /// <summary>
        /// Helper function that checks if we are aiming on target!
        /// </summary>
        /// <param name="heldItem"></param>
        /// <param name="targetPos"></param>
        /// <returns></returns>
        private bool CanHitEnemyWithHeldItem(GrabbableObject heldItem, Vector3 targetPos)
        {
            if (this.currentEnemy == null)
                return false;

            Vector3 toEnemy = targetPos - npcController.Npc.gameplayCamera.transform.position;
            float angleToEnemy = Vector3.Angle(npcController.Npc.playerEye.forward, toEnemy);
            float maxFOV = 60f;
            float radius = 5f;
            float maxRange = 15f;
            LayerMask hitMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;

            // Configure by weapon type
            if (heldItem is ShotgunItem)
            {
                maxFOV = 30f; // Found in source code!
                radius = 5f;
                maxRange = 15f;
                hitMask = 524288; // Found in shotgun source code!
            }
            else if (heldItem is KnifeItem knife)
            {
                maxFOV = 45f;
                radius = 0.3f;
                maxRange = 0.75f;
                hitMask = (int)knifeMask.GetValue(knife);
            }
            else if (heldItem is Shovel shovel)
            {
                maxFOV = 75f;
                radius = 0.8f;
                maxRange = 1.5f;
                hitMask = (int)shovelMask.GetValue(shovel);
            }

            // Check if we can potentially hit!
            if (angleToEnemy < maxFOV)
            {
                // Check if we hit the target!
                enemyColliders ??= new RaycastHit[10];

                // Do an initial linecast!
                if (Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, targetPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    return false;
                }

                // Now check if we would actually hit based on the weapon's hitmask!
                Ray ray = new Ray(npcController.Npc.gameplayCamera.transform.position, npcController.Npc.gameplayCamera.transform.forward);
                int numHit = Physics.SphereCastNonAlloc(ray, radius, enemyColliders, maxRange, hitMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < numHit; i++)
                {
                    // Check if we hit the target!
                    var hitInfo = enemyColliders[i];
                    if (hitInfo.collider == EnemyCollision
                        || (hitInfo.collider.gameObject.GetComponentInParent<EnemyAI>() is EnemyAI hitTarget 
                            && hitTarget == this.currentEnemy))
                    {
                        return true;
                    }
                }

            }

            return false;
        }


        /// <summary>
        /// Helper function to return how often we press our primary attack button!
        /// </summary>
        /// <param name="weapon"></param>
        /// <returns></returns>
        private float GetWeaponAttackInterval(GrabbableObject? weapon)
        {
            // Assume use cooldown!
            // NEEDTOVALIDATE: Should I just set it to the use cooldown instead?
            if (!LethalBotAI.IsItemWeapon(weapon))
            {
                return (weapon != null && weapon.useCooldown > 0f) ? weapon.useCooldown : 0.0f;
            }

            // Let us shoot a bit faster
            if (LethalBotAI.IsItemRangedWeapon(weapon))
            {
                return 0.1f;
            }
            // Knife go brrr
            else if (weapon is KnifeItem)
            {
                return 0.43f;
            }

            return 0.78f; // Speed for shovels
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
