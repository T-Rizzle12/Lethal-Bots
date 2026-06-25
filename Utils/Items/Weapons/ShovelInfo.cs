using GameNetcodeStuff;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Items.Weapons
{
    public class ShovelInfo : WeaponInfo
    {
        public override float GetAttackRangeForWeapon(GrabbableObject weapon)
        {
            return 2f; // For the shovel, its about 2 Unity Units.
        }

        public override float GetWeaponAttackInterval(GrabbableObject weapon)
        {
            if (weapon is Shovel)
            {
                return 0.78f; // Speed for shovels
            }
            return base.GetWeaponAttackInterval(weapon);
        }

        public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
            // Provide the info about how to use the shovel.
            if (weapon is Shovel shovel)
            {
                // This is how the base game does it.
                ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * -0.35f, lethalBotController.gameplayCamera.transform.forward);
                maxFOV = 75f;
                radius = 0.8f;
                maxRange = 1.5f;
                hitMask = shovel.shovelMask;
            }
            else
            {
                // If we somehow were not given the shovel object, just retun the default
                base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask);
            }
        }
    }
}
