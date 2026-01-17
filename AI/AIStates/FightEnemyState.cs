using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
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
        private static readonly FieldInfo isScanning = AccessTools.Field(typeof(PatcherTool), "isScanning");
        private static readonly FieldInfo anomalyMask = AccessTools.Field(typeof(PatcherTool), "anomalyMask");
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
            if (!ai.TryFindItemInInventory(FindObject, FindBetterObject, out int weaponSlot))
            {
                // HOW DID THIS HAPPEN!!!!!
                // HasCombatWeapon checks if the bot has a weapon in the first place!
                // This may be caused by a race conditon or another mod!
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
            float giveupRange = fearRange.Value * 2;
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
                        Ray ray = new Ray(npcController.Npc.transform.position, npcController.Npc.transform.position + Vector3.up * 0.2f - this.currentEnemy.transform.position + Vector3.up * 0.2f);
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

                    // Start the attack coroutine if not already done so!
                    StartAttackCoroutine();
                }
            }
            // Enemy is outside our retreat range, abort!
            else if (sqrMagDistanceEnemy > giveupRange * giveupRange
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
                npcController.OrderToLookAtPosition(targetPos, EnumLookAtPriority.HIGH_PRIORITY, ai.AIIntervalTime, true);
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
                    ai.State = new ReturnToShipState(this, !shouldWalkLootToShip);
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
                    ScanNodeProperties? component = collider?.transform.gameObject.GetComponent<ScanNodeProperties>();
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
                        ScanNodeProperties? component = childCollider?.transform.gameObject.GetComponent<ScanNodeProperties>();
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

                // TODO: move this into a function outside of the corutine, so
                // modders can add custom support for ranged weapons.....
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
                        heldItem.UseItemOnClient(true);
                    }
                }
                else if (heldItem is PatcherTool patcherTool)
                {
                    Transform shockingTarget = npcController.Npc.shockingTarget;
                    if (patcherTool.isShocking)
                    {
                        // We are already stunning our target, keep at it
                        IShockableWithGun? shockableWithGun = this.currentEnemy.transform.GetComponentInChildren<IShockableWithGun>();
                        Plugin.LogDebug($"Shocking Target: {shockingTarget}, Current Enemy Shockable: {shockableWithGun?.GetShockableTransform()}");
                        if (shockingTarget == shockableWithGun?.GetShockableTransform())
                        {
                            // We handle aiming our stun gun elsewhere
                            yield return null;
                            continue;
                        }
                        // We have the wrong guy, break the beam!
                        else
                        {
                            heldItem.UseItemOnClient(true);
                            yield return null;
                            continue;
                        }
                    }
                    // We should already be on target, aim and FIRE
                    else if (!(bool)isScanning.GetValue(patcherTool))
                    {
                        heldItem.UseItemOnClient(true);
                    }
                }
                else
                {
                    heldItem.UseItemOnClient(true);
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
        /// <param name="heldItem">The weapon the bot is using.</param>
        /// <param name="targetPos">The position we want to hit.</param>
        /// <returns></returns>
        private bool CanHitEnemyWithHeldItem(GrabbableObject heldItem, Vector3 targetPos)
        {
            if (this.currentEnemy == null)
                return false;

            PlayerControllerB lethalBotController = npcController.Npc;
            Vector3 toEnemy = targetPos - lethalBotController.gameplayCamera.transform.position;
            float angleToEnemy = Vector3.Angle(lethalBotController.playerEye.forward, toEnemy);

            // Check if we can potentially hit!
            GetWeaponAttackInfo(heldItem, lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask);
            if (angleToEnemy < maxFOV)
            {
                // Check if we hit the target!
                enemyColliders ??= new RaycastHit[10];

                // Do an initial linecast!
                if (Physics.Linecast(lethalBotController.gameplayCamera.transform.position, targetPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    return false;
                }

                // Now check if we would actually hit based on the weapon's hitmask!
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

        /// <summary>
        /// Helper function that grabs the information on a weapon
        /// </summary>
        /// <param name="weapon"></param>
        /// <param name="lethalBotController"></param>
        /// <param name="ray"></param>
        /// <param name="maxFOV"></param>
        /// <param name="radius"></param>
        /// <param name="maxRange"></param>
        /// <param name="hitMask"></param>
        private void GetWeaponAttackInfo(GrabbableObject? weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
            // Default values!
            maxFOV = 60f;
            radius = 5f;
            maxRange = 15f;
            hitMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            ray = new Ray(lethalBotController.gameplayCamera.transform.position, lethalBotController.gameplayCamera.transform.forward);

            if (weapon == null)
            {
                return;
            }

            // Configure by weapon type
            if (weapon is ShotgunItem)
            {
                // The ray and direction for the shotgun are diffrent!
                Vector3 shotgunPostion = lethalBotController.gameplayCamera.transform.position - lethalBotController.gameplayCamera.transform.up * 0.45f;
                Vector3 shotgunForward = lethalBotController.gameplayCamera.transform.forward;
                ray = new Ray(shotgunPostion - shotgunForward * 10f, shotgunForward);
                maxFOV = 30f; // Found in source code!
                radius = 5f;
                maxRange = 15f;
                hitMask = 524288; // Found in shotgun source code!
            }
            else if (weapon is PatcherTool patcherTool)
            {
                Vector3 patcherToolForward = lethalBotController.gameplayCamera.transform.forward;
                ray = new Ray(lethalBotController.gameplayCamera.transform.position - patcherToolForward * 3, patcherToolForward);
                maxFOV = 60f; // Found in source code!
                radius = 5f;
                maxRange = 5f;
                hitMask = (int)anomalyMask.GetValue(patcherTool);
            }
            else if (weapon is KnifeItem knife)
            {
                ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * 0.1f, lethalBotController.gameplayCamera.transform.forward);
                maxFOV = 45f;
                radius = 0.3f;
                maxRange = 0.75f;
                hitMask = (int)knifeMask.GetValue(knife);
            }
            else if (weapon is Shovel shovel)
            {
                ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                maxFOV = 75f;
                radius = 0.8f;
                maxRange = 1.5f;
                hitMask = (int)shovelMask.GetValue(shovel);
            }
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
            bool isCurrentRanged = LethalBotAI.IsItemRangedWeapon(currentBest);
            bool isCanidateRanged = LethalBotAI.IsItemRangedWeapon(canidate);
            if (this.currentEnemy is CentipedeAI)
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
