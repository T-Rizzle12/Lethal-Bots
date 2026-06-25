using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using UnityEngine;
using LethalBots.AI.AIStates;
using Object = UnityEngine.Object;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class made for <see cref="ItemsManager"/>
    /// </summary>
    public abstract class WeaponInfo
    {
        /// <summary>
        /// Some usefull colliders for use in <see cref="CanHitWithWeapon(PlayerControllerB, EnemyAI, Collider?, Ray, float, float, LayerMask)"/>
        /// </summary>
        /// <remarks>
        /// You can Init this in the parameterless constructor
        /// </remarks>
        protected RaycastHit[] enemyColliders = null!;

        /// <summary>
        /// Is the following weapon ranged
        /// </summary>
        /// <param name="weapon">The weapon we are checking</param>
        /// <returns></returns>
        public virtual bool IsRanged(GrabbableObject weapon)
        {
            return false;
        }

        /// <summary>
        /// Does the follow weapon need to be reloaded
        /// </summary>
        /// <param name="weapon">The weapon we are checking</param>
        /// <returns></returns>
        public virtual bool NeedsToReload(GrabbableObject weapon)
        {
            return false;
        }

        /// <summary>
        /// Called by <see cref="AIState.SelectBestItemFromInventoryFilter(GrabbableObject)"/> to see if the bot should
        /// equip this weapon while not in combat.
        /// </summary>
        /// <param name="weapon">The weapon we are checking</param>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <returns></returns>
        public virtual bool ShouldEquip(GrabbableObject weapon, PlayerControllerB lethalBotController)
        {
            return false;
        }

        /// <summary>
        /// Only used by <see cref="AIState.GetItemPriority(GrabbableObject)"/> for now,
        /// but will be expanded for later down the line for weapon selection.
        /// </summary>
        /// <remarks>
        /// By default the priority is 2. This is less than the <see cref="WalkieTalkie"/>, but higher than <see cref="FlashlightItem"/>.
        /// </remarks>
        /// <param name="weapon">The weapon we are checking</param>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <returns></returns>
        public virtual int EquipPriority(GrabbableObject weapon, PlayerControllerB lethalBotController)
        {
            return 2;
        }

        /// <summary>
        /// Checks if the current <paramref name="weapon"/> has ammo.
        /// </summary>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <param name="weapon">The weapon we are checking</param>
        /// <param name="spareOnly">Should we only consider the spare ammunation for this weapon?</param>
        /// <returns></returns>
        public virtual bool HasAmmo(PlayerControllerB lethalBotController, GrabbableObject weapon, bool spareOnly = false)
        {
            return ItemsManager.HasRequiredCharge(weapon); // Check the battery
        }

        /// <summary>
        /// Grabs the desired attack range for the bot!
        /// </summary>
        /// <param name="weapon">The weapon we are checking</param>
        /// <returns></returns>
        public abstract float GetAttackRangeForWeapon(GrabbableObject weapon);

        /// <summary>
        /// Helper function to return how often we press our primary attack button!
        /// </summary>
        /// <param name="weapon">The weapon we are checking</param>
        /// <returns></returns>
        public virtual float GetWeaponAttackInterval(GrabbableObject weapon)
        {
            return weapon.useCooldown > 0f ? weapon.useCooldown : 0.0f;
        }

        /// <summary>
        /// Helper function that grabs the information on a weapon
        /// </summary>
        /// <param name="weapon">The weapon the bot is using</param>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <param name="ray">The <see cref="Ray"/> we want to assess</param>
        /// <param name="maxFOV">The FOV of the bot should use when aiming and when deciding if an attack could hit</param>
        /// <param name="radius"></param>
        /// <param name="maxRange"></param>
        /// <param name="hitMask"></param>
        public virtual void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
            // Default values!
            maxFOV = 60f;
            radius = 5f;
            maxRange = 15f;
            hitMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            ray = new Ray(lethalBotController.gameplayCamera.transform.position, lethalBotController.gameplayCamera.transform.forward);
        }

        /// <summary>
        /// Helper function that checks if the bot can hit the <paramref name="currentEnemy"/> with the given <paramref name="enemyCollider"/>.
        /// </summary>
        /// <remarks>
        /// This is an almost 1:1 recreation of the <see cref="Shovel.HitShovel(bool)"/> and <see cref="KnifeItem.HitKnife(bool)"/>.<br/>
        /// </remarks>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <param name="currentEnemy">The enemy the bot is attempting to hit</param>
        /// <param name="enemyCollider">The collider of the enemy the bot is attepting to hit</param>
        /// <param name="ray">The <see cref="Ray"/> we want to assess</param>
        /// <param name="radius"></param>
        /// <param name="maxRange"></param>
        /// <param name="hitMask"></param>
        /// <returns></returns>
        public virtual bool CanHitWithWeapon(PlayerControllerB lethalBotController, EnemyAI currentEnemy, Collider? enemyCollider, Ray ray, float radius, float maxRange, LayerMask hitMask)
        {
            // Check if we hit the target based on the weapon's hitmask!
            FootstepSurface[] footstepSurfaces = StartOfRound.Instance.footstepSurfaces;
            RaycastHit[] raycastHits = Physics.SphereCastAll(ray, radius, maxRange, hitMask, QueryTriggerInteraction.Collide);
            List<RaycastHit> orderdHitList = raycastHits.OrderBy((RaycastHit x) => x.distance).ToList();
            for (int i = 0; i < raycastHits.Length; i++)
            {
                // Check if we hit a wall!
                var hitInfo = raycastHits[i];
                if (hitInfo.transform.gameObject.layer == 8 || hitInfo.transform.gameObject.layer == 11)
                {
                    if (hitInfo.collider.isTrigger)
                    {
                        continue;
                    }
                    string text = hitInfo.collider.gameObject.tag;
                    for (int j = 0; j < footstepSurfaces.Length; j++)
                    {
                        var surface = footstepSurfaces[j];
                        if (surface != null && surface.surfaceTag == text)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // Check if we hit a valid target
                    if (!hitInfo.transform.TryGetComponent<IHittable>(out var component) || (hitInfo.point != Vector3.zero && Physics.Linecast(lethalBotController.gameplayCamera.transform.position, hitInfo.point, out _, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
                    {
                        continue;
                    }
                    Vector3 forward = lethalBotController.gameplayCamera.transform.forward;
                    EnemyAICollisionDetect component2 = hitInfo.transform.GetComponent<EnemyAICollisionDetect>();
                    bool isIHittablePlayer = hitInfo.transform.GetComponent<PlayerControllerB>() != null;
                    if (component2 != null || !isIHittablePlayer)
                    {
                        if (!isIHittablePlayer || (component2?.mainScript != null && (!StartOfRound.Instance.hangarDoorsClosed || component2.mainScript.isInsidePlayerShip == lethalBotController.isInHangarShipRoom)))
                        {
                            // Check if we hit the target!
                            if (hitInfo.collider == enemyCollider
                                || (component2 != null && component2 == currentEnemy))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Called by the bot when it meets the conditions to use the weapon
        /// </summary>
        /// <remarks>
        /// By default, this is called in the <see cref="FightEnemyState.weaponAttackCoroutine"/>.
        /// </remarks>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <param name="weapon">The weapon the bot is using</param>
        /// <param name="currentEnemy">The enemy the bot is attempting to hit</param>
        /// <param name="enemyCollider">The collider of the enemy the bot is attepting to hit</param>
        /// <param name="setSkipCooldown">This is a delegate function passed in by <see cref="FightEnemyState.weaponAttackCoroutine"/> to allow you to skip setting the attack cooldown.<br/> This is for cases where you want to use your own cooldown.</param>
        /// <returns></returns>
        public virtual IEnumerator AttackWithWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, EnemyAI currentEnemy, Collider? enemyCollider, Action<bool> setSkipCooldown)
        {
            weapon.UseItemOnClient(true);
            yield return null;
            // holdButtonUse is true for the shovel!
            // This means we need to release it next frame!
            if (weapon.itemProperties.holdButtonUse)
            {
                weapon.UseItemOnClient(false); // HACKHACK: Fake release the button!
            }
        }

        /// <summary>
        /// Called by the bot in <see cref="AIState.UseHeldItem"/>
        /// </summary>
        /// <remarks>
        /// This allows you to have the bot reload or turn on the safety when not in combat.
        /// </remarks>
        /// <param name="lethalBotController">The bot's <see cref="PlayerControllerB"/></param>
        /// <param name="weapon">The weapon the bot is using</param>
        /// <param name="canUseLethalPhones">(Lethal Phones) Should the bot be allowed to use their phone when you finish your current action?</param>
        public virtual void UseHeldWeapon(PlayerControllerB lethalBotController, GrabbableObject weapon, ref bool canUseLethalPhones) { }
    }
}
