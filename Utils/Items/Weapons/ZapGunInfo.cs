using GameNetcodeStuff;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Items.Weapons
{
    public class ZapGunInfo : WeaponInfo
    {
        public ZapGunInfo()
        {
            enemyColliders ??= new RaycastHit[10];
        }

        public override bool IsRanged(GrabbableObject weapon)
        {
            return true; // The zap gun is a ranged weapon!
        }

        public override bool HasAmmo(PlayerControllerB lethalBotController, GrabbableObject weapon, bool spareOnly = false)
        {
            return ItemsManager.HasRequiredCharge(weapon); // Zap gun runs on batteries
        }

        public override float GetAttackRangeForWeapon(GrabbableObject weapon)
        {
            return 5f; // Found in source code!
        }

        public override float GetWeaponAttackInterval(GrabbableObject weapon)
        {
            return 0.1f; // Let us shoot a bit faster
        }

        public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
            if (weapon is PatcherTool patcherTool)
            {
                Vector3 patcherToolForward = lethalBotController.gameplayCamera.transform.forward;
                ray = new Ray(lethalBotController.gameplayCamera.transform.position - patcherToolForward * 3, patcherToolForward);
                maxFOV = 60f; // Found in source code!
                radius = 5f;
                maxRange = 5f;
                hitMask = patcherTool.anomalyMask;
            }
            else 
            {
                base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
            }
        }

        public override bool CanHitWithWeapon(PlayerControllerB lethalBotController, EnemyAI currentEnemy, Collider? enemyCollider, Ray ray, float radius, float maxRange, LayerMask hitMask)
        {
            // Check if we hit the target!
            enemyColliders ??= new RaycastHit[10];

            // Do an initial linecast!
            // NEEDTOVALIDATE: Do we actually need this?
            //if (Physics.Linecast(lethalBotController.gameplayCamera.transform.position, targetPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            //{
            //    return false;
            //}

            // Now check if we would actually hit based on the weapon's hitmask!
            int numHit = Physics.SphereCastNonAlloc(ray, radius, enemyColliders, maxRange, hitMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < numHit; i++)
            {
                // Check if we hit the target!
                var hitInfo = enemyColliders[i];
                if (hitInfo.collider == enemyCollider
                    || (hitInfo.collider.gameObject.GetComponentInParent<EnemyAI>() is EnemyAI hitTarget
                        && hitTarget == currentEnemy))
                {
                    return true;
                }
            }
            return false;
        }

        public override IEnumerator AttackWithWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, EnemyAI currentEnemy, Collider? enemyCollider, Action<bool> setSkipCooldown)
        {
            if (weapon is PatcherTool patcherTool)
            {
                Transform shockingTarget = lethalBotController.shockingTarget;
                if (patcherTool.isShocking)
                {
                    // We are already stunning our target, keep at it
                    IShockableWithGun? shockableWithGun = currentEnemy.transform.GetComponentInChildren<IShockableWithGun>();
                    Plugin.LogDebug($"Shocking Target: {shockingTarget}, Current Enemy Shockable: {shockableWithGun?.GetShockableTransform()}");
                    if (shockingTarget == shockableWithGun?.GetShockableTransform())
                    {
                        // We handle aiming our stun gun elsewhere
                        yield return null;
                        setSkipCooldown.Invoke(true);
                        yield break;
                    }
                    // We have the wrong guy, break the beam!
                    else
                    {
                        weapon.UseItemOnClient(true);
                        yield return null;
                        setSkipCooldown.Invoke(true);
                        yield break;
                    }
                }
                // We should already be on target, aim and FIRE
                else if (!patcherTool.isScanning)
                {
                    weapon.UseItemOnClient(true);
                }
            }
        }
    }
}
