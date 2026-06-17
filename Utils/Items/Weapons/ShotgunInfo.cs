using GameNetcodeStuff;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Items.Weapons
{
    public class ShotgunInfo : WeaponInfo
    {
        public ShotgunInfo()
        {
            enemyColliders = new RaycastHit[10];
        }

        public override bool IsRanged(GrabbableObject weapon)
        {
            return true; // Shotguns are a ranged weapon!
        }

        public override bool HasAmmo(PlayerControllerB lethalBotController, GrabbableObject weapon, bool spareOnly = false)
        {
            // Only do this for actual shotguns!
            if (weapon is ShotgunItem shotgun)
            {
                // Ammo is in the gun!
                if (!spareOnly && shotgun.shellsLoaded > 0)
                {
                    return true;
                }

                // Check if the lethalBot has ammo in its item only slot
                GrabbableObject? itemOnlySlot = lethalBotController.ItemOnlySlot;
                if (itemOnlySlot != null)
                {
                    GunAmmo? gunAmmo = itemOnlySlot as GunAmmo;
                    if (gunAmmo != null && gunAmmo.ammoType == shotgun.gunCompatibleAmmoID)
                    {
                        return true;
                    }
                }

                // Using for since we need the manual index tracking
                GrabbableObject[] itemSlots = lethalBotController.ItemSlots;
                for (int index = 0; index < itemSlots.Length; index++)
                {
                    var item = itemSlots[index];
                    if (item != null)
                    {
                        GunAmmo? gunAmmo = item as GunAmmo;
                        Plugin.LogDebug($"Ammo null in slot #{index}?: {gunAmmo == null}");
                        if (gunAmmo != null)
                        {
                            Plugin.LogDebug($"Ammo in slot #{index} id: {gunAmmo.ammoType}");
                        }
                        if (gunAmmo != null && gunAmmo.ammoType == shotgun.gunCompatibleAmmoID)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            return true;
        }

        public override float GetAttackRangeForWeapon(GrabbableObject weapon)
        {
            return 4f; // Based off of the ray postion and range
        }

        public override float GetWeaponAttackInterval(GrabbableObject weapon)
        {
            return 0.1f; // Let us shoot a bit faster
        }

        public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
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
            else
            {
                base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
            }
        }

        public override bool CanHitWithWeapon(PlayerControllerB lethalBotController, EnemyAI currentEnemy, Collider? enemyCollider, Ray ray, float radius, float maxRange, LayerMask hitMask)
        {
            // Check if we hit the target based on the weapon's hitmask!
            enemyColliders ??= new RaycastHit[10];
            int numHit = Physics.SphereCastNonAlloc(ray, radius, enemyColliders, maxRange, hitMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < numHit; i++)
            {
                // Check if we hit the target!
                var hitInfo = enemyColliders[i];
                if (hitInfo.distance == 0f || hitInfo.point == Vector3.zero) continue;

                // // Make sure this is a valid hit target and do an initial linecast!
                if (!hitInfo.transform.TryGetComponent<IHittable>(out _) || Physics.Linecast(lethalBotController.gameplayCamera.transform.position, hitInfo.point, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                // Check if we hit the target!
                if (hitInfo.collider == enemyCollider
                    || (hitInfo.transform.gameObject.TryGetComponent<EnemyAICollisionDetect>(out var enemyAICollisionDetect) 
                        && enemyAICollisionDetect.mainScript is EnemyAI hitTarget
                        && hitTarget == currentEnemy))
                {
                    return true;
                }
            }
            return false;
        }

        public override IEnumerator AttackWithWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, EnemyAI currentEnemy, Collider? enemyCollider, Action<bool> setSkipCooldown)
        {
            if (weapon is ShotgunItem shotgun)
            {
                // Can't fire, we are reloading!
                if (shotgun.isReloading)
                {
                    yield return null;
                    setSkipCooldown.Invoke(true);
                    yield break;
                }
                // Kinda hard to use the shotgun with the safety on!
                else if (shotgun.safetyOn)
                {
                    shotgun.ItemInteractLeftRightOnClient(false);
                    yield return null;
                    setSkipCooldown.Invoke(true);
                    yield break;
                }
                // Reload the shotgun!
                else if (shotgun.shellsLoaded <= 0)
                {
                    shotgun.ItemInteractLeftRightOnClient(true);
                    yield return null;
                    setSkipCooldown.Invoke(true);
                    yield break;
                }
                // RIP AND TEAR!
                else
                {
                    weapon.UseItemOnClient(true);
                }
            }
        }
    }
}
