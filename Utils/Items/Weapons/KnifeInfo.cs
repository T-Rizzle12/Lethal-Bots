using GameNetcodeStuff;
using LethalBots.Utils.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Items.Weapons
{
    public class KnifeInfo : WeaponInfo
    {
        public override float GetAttackRangeForWeapon(GrabbableObject weapon)
        {
            return 1f; // Found in source code!
        }

        public override float GetWeaponAttackInterval(GrabbableObject weapon)
        {
            return 0.43f; // Knife go brrr
        }

        public override void GetWeaponAttackInfo(GrabbableObject weapon, PlayerControllerB lethalBotController, out Ray ray, out float maxFOV, out float radius, out float maxRange, out LayerMask hitMask)
        {
            if (weapon is KnifeItem knife)
            {
                ray = new Ray(lethalBotController.gameplayCamera.transform.position + lethalBotController.gameplayCamera.transform.right * 0.1f, lethalBotController.gameplayCamera.transform.forward);
                maxFOV = 45f;
                radius = 0.3f;
                maxRange = 0.75f;
                hitMask = knife.knifeMask;
            }
            else
            { 
                base.GetWeaponAttackInfo(weapon, lethalBotController, out ray, out maxFOV, out radius, out maxRange, out hitMask); 
            }
        }
    }
}
