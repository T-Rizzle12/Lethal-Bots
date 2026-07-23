using GameNetcodeStuff;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UsualScrap.Behaviors;

namespace LethalBots.Patches.ModPatches.UsualScrap
{
    public class UsualScrapWeaponsPatch
    {
        public static void RegisterWeaponsForBots(ItemsManager itemsManager)
        {
            Plugin.LogInfo("Loading weapon support for Usual Scrap!");
            itemsManager.RegisterNewWeapon<CrowbarScript>(new CrowbarInfo());
            itemsManager.RegisterNewWeapon<SizableScissorsScript>(new SizableScissorsInfo());
            itemsManager.RegisterNewWeapon<CandyDispenserScript>(new CandyDispenserInfo());
            itemsManager.RegisterNewWeapon<SledgehammerScript>(new SledgehammerInfo());
            itemsManager.RegisterNewWeapon<RedoneBlowtorch>(new RedoneBlowtorchInfo());
        }

        private class CrowbarInfo : WeaponInfo
        {
            public override float GetWeaponAttackInterval(GrabbableObject weapon)
            {
                return 0.78f; // As found in the source code
            }

            public override float GetAttackRangeForWeapon(GrabbableObject weapon)
            {
                return 2f; // Same as the shovel
            }

            public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
            {
                // Provide the info about how to use the crowbar.
                if (weapon is CrowbarScript crowbar)
                {
                    // This is how the mod does it.
                    ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                    maxFOV = 75f;
                    radius = 0.8f;
                    maxRange = 1.5f;
                    hitMask = crowbar.meleeWeaponMask;
                }
                else
                {
                    // If we somehow were not given the crowbar object, just return the default
                    base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
                }
            }
        }

        private class SizableScissorsInfo : WeaponInfo
        {
            public override float GetWeaponAttackInterval(GrabbableObject weapon)
            {
                return 0.78f; // As found in the source code
            }

            public override float GetAttackRangeForWeapon(GrabbableObject weapon)
            {
                return 2f; // Same as the shovel
            }

            public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
            {
                // Provide the info about how to use the sizable scissors.
                if (weapon is SizableScissorsScript scissorsScript)
                {
                    // This is how the mod does it.
                    ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                    maxFOV = 75f;
                    radius = 0.8f;
                    maxRange = 1.5f;
                    hitMask = scissorsScript.meleeWeaponMask;
                }
                else
                {
                    // If we somehow were not given the scissors object, just return the default
                    base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
                }
            }
        }

        private class CandyDispenserInfo : WeaponInfo
        {
            public override float GetWeaponAttackInterval(GrabbableObject weapon)
            {
                return 0.78f; // As found in the source code
            }

            public override float GetAttackRangeForWeapon(GrabbableObject weapon)
            {
                return 2f; // Same as the shovel
            }

            public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
            {
                // Provide the info about how to use the candy dispenser
                if (weapon is CandyDispenserScript candyDispenser)
                {
                    // This is how the mod does it.
                    ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                    maxFOV = 75f;
                    radius = 0.8f;
                    maxRange = 1.5f;
                    hitMask = candyDispenser.meleeWeaponMask;
                }
                else
                {
                    // If we somehow were not given the candy dispenser object, just return the default
                    base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
                }
            }
        }

        private class SledgehammerInfo : WeaponInfo
        {
            public override float GetWeaponAttackInterval(GrabbableObject weapon)
            {
                return 0.78f; // As found in the source code
            }

            public override float GetAttackRangeForWeapon(GrabbableObject weapon)
            {
                return 2f; // Same as the shovel
            }

            public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
            {
                // Provide the info about how to use the blowtorch
                if (weapon is SledgehammerScript sledgehammer)
                {
                    // This is how the mod does it.
                    ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                    maxFOV = 75f;
                    radius = 0.8f;
                    maxRange = 1.5f;
                    hitMask = sledgehammer.meleeWeaponMask;
                }
                else
                {
                    // If we somehow were not given the candy dispenser object, just return the default
                    base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
                }
            }
        }

        private class RedoneBlowtorchInfo : WeaponInfo
        {
            private static readonly LayerMask attackMask = LayerMask.GetMask("Enemies");

            public RedoneBlowtorchInfo()
            {
                enemyColliders = new RaycastHit[10];
            }

            public override bool IsRanged(GrabbableObject weapon)
            {
                return true;
            }

            public override float GetAttackRangeForWeapon(GrabbableObject weapon)
            {
                return 4f; // Based on the range found in the source code
            }

            public override float GetWeaponAttackInterval(GrabbableObject weapon)
            {
                return 0.1f; // Let us update a bit faster
            }

            public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
            {
                // Provide the info about how to use the blowtorch
                if (weapon is RedoneBlowtorch blowtorch)
                {
                    // This is how the mod does it.
                    ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.forward * 0.5f, lethalBotController.gameplayCamera.transform.forward);
                    maxFOV = 30f;
                    radius = 0.35f;
                    maxRange = 3.5f;
                    hitMask = attackMask;
                }
                else
                {
                    // If we somehow were not given the candy dispenser object, just return the default
                    base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
                }
            }

            public override bool CanHitWithWeapon(PlayerControllerB lethalBotController, EnemyAI currentEnemy, Collider? enemyCollider, Ray ray, float radius, float maxRange, LayerMask hitMask)
            {
                // Check if we hit the target based on the weapon's hitmask!
                enemyColliders ??= new RaycastHit[10];
                int hitCount = Physics.SphereCastNonAlloc(ray, radius, enemyColliders, maxRange, hitMask, QueryTriggerInteraction.Collide);
                EnemyAICollisionDetect? closestEnemy = null;
                float closestDistance = float.MaxValue;
                for (int i = 0; i < hitCount; i++)
                {
                    // Do the same logic the mod does
                    RaycastHit raycastHit = enemyColliders[i];
                    EnemyAICollisionDetect detect = raycastHit.collider.GetComponentInParent<EnemyAICollisionDetect>();
                    if (detect != null && detect.TryGetComponent<IHittable>(out _) && (!detect.onlyCollideWhenGrounded || detect.alwaysAllowHitting))
                    {
                        if (raycastHit.distance < closestDistance)
                        {
                            closestDistance = raycastHit.distance;
                            closestEnemy = detect;
                        }
                    }
                }

                // Don't need to check collider here, since we grab the enemy itself
                return closestEnemy != null && closestEnemy.mainScript == currentEnemy;
            }

            public override IEnumerator AttackWithWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, EnemyAI currentEnemy, Collider? enemyCollider, bool canHitTarget, Action<bool> setSkipCooldown)
            {
                // Here is a good example of the canHitTarget being useful. I can tell the bot to stop firing their weapon
                // without needing to add some complex buttons system for the bot.
                if (weapon is RedoneBlowtorch blowtorch)
                {
                    // Don't let it overheat!
                    if (!canHitTarget || blowtorch.currentHeat >= blowtorch.heatThreshold - blowtorch.heatGainRate)
                    {
                        // Release our attack key
                        if (blowtorch.isHoldingButton)
                        {
                            weapon.UseItemOnClient(false);
                        }
                        yield return null;
                        yield break;
                    }
                    // Attack!
                    else if (blowtorch.currentHeat < blowtorch.heatThreshold / 2f)
                    {
                        // Press and hold!
                        if (!blowtorch.isHoldingButton)
                        {
                            weapon.UseItemOnClient(true);
                        }
                    }
                }

            }

            public override void UseHeldWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, ref bool canUseLethalPhones)
            {
                if (weapon is RedoneBlowtorch blowtorch)
                {
                    // Turn off the blowtorch, we are not using it anymore
                    if (blowtorch.isHoldingButton)
                    {
                        weapon.UseItemOnClient(false);
                    }
                }
            }
        }
    }
}
