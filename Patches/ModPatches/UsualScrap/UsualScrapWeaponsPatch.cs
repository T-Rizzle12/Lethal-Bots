using GameNetcodeStuff;
using LethalBots.Managers;
using LethalBots.Utils.Helpers;
using System;
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
        }

        private class CrowbarInfo : WeaponInfo
        {
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
    }
}
