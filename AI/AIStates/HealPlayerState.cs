using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Managers;
using LethalBots.Patches.EnemiesPatches;
using LethalBots.Utils.Helpers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using UnityEngine;
using UsualScrap.Behaviors;

namespace LethalBots.AI.AIStates
{
    public class HealPlayerState : AIState
    {
        private static readonly AccessTools.FieldRef<SprayPaintItem, float> sprayCanTank = AccessTools.FieldRefAccess<float>(typeof(SprayPaintItem), "sprayCanTank");
        private static readonly AccessTools.FieldRef<SprayPaintItem, bool> isSpraying = AccessTools.FieldRefAccess<bool>(typeof(SprayPaintItem), "isSpraying");

        private PlayerControllerB healTarget;
        private GrabbableObject? neededMedicalTool;
        private bool allowRecharging;
        private HealMethod healMethod = HealMethod.None;
        private Coroutine? healCoroutine;

        public HealPlayerState(AIState oldState, PlayerControllerB targetPlayer) : base(oldState)
        {
            CurrentState = EnumAIStates.HealPlayer;
            healTarget = targetPlayer;
        }

        public HealPlayerState(LethalBotAI ai, PlayerControllerB targetPlayer) : base(ai)
        {
            CurrentState = EnumAIStates.HealPlayer;
            healTarget = targetPlayer;
        }

        public override void OnEnterState()
        {
            if (healTarget == null
                || !healTarget.isPlayerControlled
                || healTarget.isPlayerDead)
            {
                Plugin.LogWarning("HealPlayerState: healTarget is null or dead, cannot heal!");
                ChangeBackToPreviousState();
                return;
            }
            if (!hasBeenStarted)
            {
                healMethod = DetermineBestHealMethod();
            }
            allowRecharging = true;
            base.OnEnterState();
        }

        public override void OnExitState(AIState newState)
        {
            // If we got interupted while using the Weed Killer, stop spraying!
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem is SprayPaintItem sprayPaintItem && isSpraying.Invoke(sprayPaintItem))
            {
                sprayPaintItem.UseItemOnClient(false);
            }
            else if (Plugin.IsModUsualScrapLoaded && FindUsualScrapMedkit(heldItem))
            {
                heldItem.UseItemOnClient(false);
            }
            base.OnExitState(newState);
        }

        public override void DoAI()
        {
            // Check for enemies
            EnemyAI? enemyAI = ai.CheckLOSForEnemy(Const.LETHAL_BOT_FOV, Const.LETHAL_BOT_ENTITIES_RANGE, (int)Const.DISTANCE_CLOSE_ENOUGH_HOR);
            if (enemyAI != null)
            {
                ai.State = new PanikState(this, enemyAI);
                return;
            }

            // Check on our patient
            if (healTarget == null
                || !healTarget.isPlayerControlled
                || healTarget.isPlayerDead 
                || !ai.IsValidPathToTarget(healTarget.transform.position))
            {
                // Patient is dead or gone, go back to what we were doing before
                ChangeBackToPreviousState();
                return;
            }

            // Can we still heal the patient? If not, go back to what we were doing before.
            if (!CanHealPlayer())
            {
                ChangeBackToPreviousState();
                return;
            }

            // One of our heal methods request we grab the needed medical tool
            GrabbableObject? neededMedicalTool = this.neededMedicalTool;
            if (neededMedicalTool != null)
            {
                this.neededMedicalTool = null; // Clear the tool!
                ai.State = new FetchingObjectState(this, neededMedicalTool);
                return;
            }

            // If our tool needs to be charged, lets charge it
            if (allowRecharging
                && (npcController.Npc.isInElevator || npcController.Npc.isInHangarShipRoom)
                && ChargeHeldItemState.HasItemToCharge(ai, out _))
            {
                ai.State = new ChargeHeldItemState(this, true);
                return;
            }

            switch (healMethod)
            {
                case HealMethod.WeedKiller:
                    DoWeedKillerHealingLogic();
                    break;
                case HealMethod.ModUsualScrapMedkit:
                    DoUsualScrapMedkitLogic();
                    break;
                case HealMethod.ModUsualScrapBandage:
                    DoUsualScrapBandageLogic();
                    break;
                default:
                    break;
            }
        }

        public override void TryPlayCurrentStateVoiceAudio()
        {
            return; // TODO: Get some healing voice lines and play them here!
        }

        public override void StopAllCoroutines()
        {
            base.StopAllCoroutines();
            StopHealCoroutine();
        }

        /// <summary>
        /// Specifies how the bot will attempt to heal their target player.
        /// </summary>
        /// <remarks>
        /// Grandpa? Why isn't this in the enum folder with all the other enums?
        /// Well you see Timmy, this enum is only used by one class and it is very specific to that class.
        /// All the other enums are used by multiple classes and have a broader purpose.
        /// </remarks>
        public enum HealMethod
        {
            None,
            WeedKiller, // Yes, we can heal with weed killer, its only for the Cadaver infection.
            ModUsualScrapMedkit,
            ModUsualScrapBandage
        }

        /// <summary>
        /// Determines the most appropriate heal method to use based on the current context.
        /// </summary>
        /// <returns>A <see cref="HealMethod"/> representing the selected heal method.</returns>
        private HealMethod DetermineBestHealMethod()
        {
            // Alright, we heal players based on the best source of healing we have available.
            // If the player is infected with the Cadaver infection, we use weed killer to heal them.
            // Otherwise, it depends on what mods are installed.
            if (CanHealPlayerWithWeedKiller(ai, this.healTarget))
            {
                return HealMethod.WeedKiller;
            }
            else if (Plugin.IsModUsualScrapLoaded && CanHealPlayerWithUsualScrapMedkit(ai, this.healTarget))
            {
                return HealMethod.ModUsualScrapMedkit;
            }
            else if (Plugin.IsModUsualScrapLoaded && CanHealPlayerWithUsualScrapBandage(ai, this.healTarget))
            {
                return HealMethod.ModUsualScrapBandage;
            }

            return HealMethod.None;
        }

        /// <summary>
        /// Helper function that tells bots if a player can be healed by using weed killer or not
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about healing <paramref name="healTarget"/></param>
        /// <param name="healTarget">The player <paramref name="lethalBotAI"/> is thinking about healing</param>
        /// <param name="requiredInfectionLevel">The level the <paramref name="healTarget"/>'s infection needs to be at before we consider curing them</param>
        /// <returns>true if the player can be healed using weedkiller; otherwise, false.</returns>
        private static bool CanHealPlayerWithWeedKiller(LethalBotAI lethalBotAI, PlayerControllerB healTarget, float requiredInfectionLevel = 0.0f)
        {
            PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
            if (lethalBotController == healTarget)
            {
                return false; // We can't heal ourselves with weed killer, someone else has to do it for us.
            }

            if (CadaverGrowthAIPatch.CadaverGrowthAI == null)
            {
                //Plugin.LogDebug("HealPlayerState: Cannot find CadaverGrowthAI, cannot heal with weed killer!");
                return false;
            }
            PlayerInfection playerInfection = CadaverGrowthAIPatch.CadaverGrowthAI.playerInfections[healTarget.playerClientId];
            if (!playerInfection.infected || playerInfection.infectionMeter < requiredInfectionLevel)
            {
                //Plugin.LogDebug("HealPlayerState: Player is not infected with the Cadaver infection, cannot heal with weed killer!");
                return false;
            }
            // Intentionally allow healing with weed killer even if the player's burst meter is above 0.
            // The V80 update just recently came out and I want to simulate the lack of knowledge the bots would have about it,
            // so they won't know that a player with a burst meter above 0 is too far gone to heal with weed killer.
            //if (playerInfection.burstMeter > 0f)
            //{
            //    Plugin.LogDebug("HealPlayerState: Player's burst meter is above 0, the player is too far gone!");
            //    return false;
            //}
            // Check if we have weedkiller in our inventory
            if (lethalBotAI != null && !lethalBotAI.HasGrabbableObjectInInventory(FindWeedKiller, out _))
            {
                // Check if there is weedkiller on the ship
                if ((lethalBotController != null 
                    && !lethalBotController.isInElevator 
                    && !lethalBotController.isInHangarShipRoom) 
                    || lethalBotAI.FindItemOnShip(FindWeedKiller) is not GrabbableObject foundItem
                    || !lethalBotAI.HasSpaceInInventory(foundItem))
                {
                    //Plugin.LogDebug("HealPlayerState: Can't heal player from infection, bot doesn't have weed killer in inventory!");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Helper function that tells bots if a player can be healed by using a Usual Scrap Medkit or not
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about healing <paramref name="healTarget"/></param>
        /// <param name="healTarget">The player <paramref name="lethalBotAI"/> is thinking about healing</param>
        /// <returns>true if the player can be healed using a Usual Scrap Medkit; otherwise, false.</returns>
        private static bool CanHealPlayerWithUsualScrapMedkit(LethalBotAI lethalBotAI, PlayerControllerB healTarget)
        {
            if (Plugin.IsModUsualScrapLoaded)
            {
                // Grab our player object
                PlayerControllerB? lethalBotController = lethalBotAI?.NpcController?.Npc;
                if (healTarget.health >= 100)
                {
                    return false;
                }

                // Check if we have medkit in our inventory
                if (lethalBotAI != null && !lethalBotAI.HasGrabbableObjectInInventory(FindUsualScrapMedkit, out _))
                {
                    // Check if there is medkit on the ship
                    if ((lethalBotController != null
                        && !lethalBotController.isInElevator
                        && !lethalBotController.isInHangarShipRoom)
                        || lethalBotAI.FindItemOnShip(FindUsualScrapMedkit) is not GrabbableObject foundItem
                        || !lethalBotAI.HasSpaceInInventory(foundItem))
                    {
                        return false;
                    }
                }

                return true;
            }
            return false;
        }

        /// <summary>
        /// Helper function that tells bots if a player can be healed by using a Usual Scrap Bandage or not
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about healing <paramref name="healTarget"/></param>
        /// <param name="healTarget">The player <paramref name="lethalBotAI"/> is thinking about healing</param>
        /// <returns>true if the player can be healed using a Usual Scrap Bandage; otherwise, false.</returns>
        private static bool CanHealPlayerWithUsualScrapBandage(LethalBotAI lethalBotAI, PlayerControllerB healTarget)
        {
            if (Plugin.IsModUsualScrapLoaded)
            {
                // Grab our player object
                PlayerControllerB? lethalBotController = lethalBotAI.NpcController?.Npc;
                if (lethalBotController != healTarget || healTarget.health >= Mathf.Min(100f, lethalBotAI.LethalBotIdentity.HpMax - 20))
                {
                    return false; // We can't heal other players with the bandages
                }

                // Check if we have bandage in our inventory
                if (lethalBotAI != null && !lethalBotAI.HasGrabbableObjectInInventory(FindUsualScrapBandage, out _))
                {
                    // Check if there is bandage on the ship
                    if ((lethalBotController != null
                        && !lethalBotController.isInElevator
                        && !lethalBotController.isInHangarShipRoom)
                        || lethalBotAI.FindItemOnShip(FindUsualScrapBandage) is not GrabbableObject foundItem
                        || !lethalBotAI.HasSpaceInInventory(foundItem))
                    {
                        return false;
                    }
                }

                return true;
            }
            return false;
        }

        /// <summary>
        /// Simple helper that checks if we can still heal the player with our selected healing method.
        /// </summary>
        /// <returns></returns>
        private bool CanHealPlayer()
        {
            switch (healMethod)
            {
                case HealMethod.WeedKiller:
                    return CanHealPlayerWithWeedKiller(ai, this.healTarget);
                case HealMethod.ModUsualScrapMedkit:
                    return CanHealPlayerWithUsualScrapMedkit(ai, this.healTarget);
                case HealMethod.ModUsualScrapBandage:
                    return CanHealPlayerWithUsualScrapBandage(ai, this.healTarget);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Helper function that tells bots if a player can be healed or not
        /// </summary>
        /// <param name="lethalBotAI">The bot who is thinking about healing <paramref name="healTarget"/></param>
        /// <param name="healTarget">The player <paramref name="lethalBotAI"/> is thinking about healing</param>
        /// <param name="requiredInfectionLevel">The level the <paramref name="healTarget"/>'s infection needs to be at before we consider curing them</param>
        /// <returns>true if the player can be healed using any of the supported mods or methods; otherwise, false.</returns>
        public static bool CanHealPlayer(LethalBotAI lethalBotAI, PlayerControllerB healTarget, float requiredInfectionLevel = 0.0f)
        {
            // Make sure the player is a valid target.
            if (healTarget == null || !healTarget.isPlayerControlled || healTarget.isPlayerDead)
            {
                return false;
            }

            // Alright, we heal players based on the best source of healing we have available.
            // If the player is infected with the Cadaver infection, we use weed killer to heal them.
            // Otherwise, it depends on what mods are installed.
            if (CanHealPlayerWithWeedKiller(lethalBotAI, healTarget, requiredInfectionLevel))
            {
                return true;
            }
            else if (Plugin.IsModUsualScrapLoaded && CanHealPlayerWithUsualScrapMedkit(lethalBotAI, healTarget))
            {
                return true;
            }
            else if (Plugin.IsModUsualScrapLoaded && CanHealPlayerWithUsualScrapBandage(lethalBotAI, healTarget))
            {
                return true;
            }

            return false;
        }

        private IEnumerator healUsingWeedKiller()
        {
            // Alright, look at our heal target
            npcController.OrderToLookAtPosition(this.healTarget.NetworkObject, EnumLookAtPriority.HIGH_PRIORITY, 1f);
            yield return null;
            yield return new WaitUntil(() => npcController.LookAtTarget.IsHeadAimingOnTarget() && npcController.LookAtTarget.hasBeenSightedIn);

            if (!ai.HasGrabbableObjectInInventory(FindWeedKiller, out int itemSlot))
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to heal player {healTarget.playerUsername} using Weed Killer but we don't have a Weed Killer!");
                StopHealCoroutine();
                yield break;
            }

            // If we are somehow holding a two handed item, drop it first!
            if (!ai.AreHandsFree()
                && (ai.HeldItem is not SprayPaintItem sprayPaintItem || !sprayPaintItem.isWeedKillerSprayBottle)
                && ai.HeldItem.itemProperties.twoHanded)
            {
                ai.DropItem();
                yield return null;
            }

            // Swap to weed killer and give time for the switch to happen!
            float startTime = Time.timeSinceLevelLoad;
            if (npcController.Npc.currentItemSlot != itemSlot 
                || ai.HeldItem is not SprayPaintItem sprayPaintItem1 
                || !sprayPaintItem1.isWeedKillerSprayBottle)
            {
                ai.SwitchItemSlotsAndSync(itemSlot);
                yield return new WaitUntil(() => npcController.Npc.currentItemSlot == itemSlot || (Time.timeSinceLevelLoad - startTime) > 1f); // One second to allow RPC to got to server and back to us!
            }

            // Alright, are we holding the Weed Killer?
            GrabbableObject? heldItem = ai.HeldItem;
            if (heldItem is not SprayPaintItem weedKiller || !weedKiller.isWeedKillerSprayBottle)
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to heal player {healTarget.playerUsername} using Weed Killer but they either don't have weed killer or item switch failed!");
                StopHealCoroutine();
                yield break;
            }

            // WAIT, don't spray while the player is critically injured......
            // I learned that the hard way....poor Claire Annette......
            // She ended up killing Amy Stake in a fit of rage......
            yield return new WaitUntil(() => healTarget == null || healTarget.isPlayerDead || !healTarget.isPlayerControlled || !healTarget.criticallyInjured);

            // Alright, juice em!
            startTime = Time.timeSinceLevelLoad;
            weedKiller.UseItemOnClient(true);
            yield return null;
            yield return new WaitUntil(() => weedKiller == null || isSpraying.Invoke(weedKiller) == false || (Time.timeSinceLevelLoad - startTime) > 5f);
            if (weedKiller != null && weedKiller.itemProperties.holdButtonUse)
            {
                weedKiller.UseItemOnClient(false);
            }

            // Just restart the corotine until we finish!
            StopHealCoroutine();
        }

        private void DoWeedKillerHealingLogic()
        {
            // Lets go and cure a player
            if (!ai.HasGrabbableObjectInInventory(FindWeedKiller, out _))
            {
                neededMedicalTool = ai.FindItemOnShip(FindWeedKiller);
                if (neededMedicalTool == null || !ai.HasSpaceInInventory(neededMedicalTool))
                {
                    ChangeBackToPreviousState(); // Odd, we can't find the weedkiller, just give up!
                }
                return;
            }

            float sqrDistToHealTarget = (healTarget.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToHealTarget > Const.DISTANCE_CLOSE_ENOUGH_TO_HEAL_TARGET * Const.DISTANCE_CLOSE_ENOUGH_TO_HEAL_TARGET 
                || (Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, healTarget.gameplayCamera.transform.position, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault)
                && hitInfo.transform.GetComponent<PlayerControllerB>() != healTarget))
            {
                // Select and use items based on our current situation, if needed
                SelectBestItemFromInventory();

                // Alright lets go to them!
                ai.SetDestinationToPositionLethalBotAI(this.healTarget.transform.position);

                // Sprint if far enough
                if (!npcController.WaitForFullStamina && sqrDistToHealTarget > Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION * Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION) 
                {
                    npcController.OrderToSprint();
                }
                else
                {
                    npcController.OrderToStopSprint();
                }

                ai.OrderMoveToDestination();
            }
            else
            {
                // Alright we are close enough to the heal target, stop moving
                ai.StopMoving();
                npcController.OrderToStopSprint();

                if (healCoroutine == null)
                {
                    healCoroutine = ai.StartCoroutine(healUsingWeedKiller());
                }
            }
        }

        private IEnumerator healUsingUsualScrapMedkit()
        {
            // Alright, look at our heal target
            if (this.healTarget != npcController.Npc)
            {
                npcController.OrderToLookAtPosition(this.healTarget.NetworkObject, EnumLookAtPriority.HIGH_PRIORITY, 1f);
                yield return null;
                yield return new WaitUntil(() => npcController.LookAtTarget.IsHeadAimingOnTarget() && npcController.LookAtTarget.hasBeenSightedIn);
            }

            if (!ai.HasGrabbableObjectInInventory(FindUsualScrapMedkit, out int itemSlot))
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to heal player {healTarget.playerUsername} using Usual Scrap Medkit but we don't have a Medkit!");
                StopHealCoroutine();
                yield break;
            }

            // If we are somehow holding a two handed item, drop it first!
            if (!ai.AreHandsFree()
                && !FindUsualScrapMedkit(ai.HeldItem)
                && ai.HeldItem.itemProperties.twoHanded)
            {
                ai.DropItem();
                yield return null;
            }

            // Swap to weed killer and give time for the switch to happen!
            float startTime = Time.timeSinceLevelLoad;
            if (npcController.Npc.currentItemSlot != itemSlot
                || !FindUsualScrapMedkit(ai.HeldItem))
            {
                ai.SwitchItemSlotsAndSync(itemSlot);
                yield return new WaitUntil(() => npcController.Npc.currentItemSlot == itemSlot || (Time.timeSinceLevelLoad - startTime) > 1f); // One second to allow RPC to got to server and back to us!
            }

            // Alright, are we holding the Weed Killer?
            GrabbableObject? heldItem = ai.HeldItem;
            if (!FindUsualScrapMedkit(heldItem))
            {
                Plugin.LogWarning($"[{npcController.Npc.playerUsername}] Tried to heal player {healTarget.playerUsername} using Usual Scrap Medkit but they either don't have a medkit or item switch failed!");
                StopHealCoroutine();
                yield break;
            }

            // Alright, juice em!
            FieldInfo healthpoolField = AccessTools.Field(typeof(MedicalKitScript), "Healthpool");
            FieldInfo healCoroutineRunningField = AccessTools.Field(typeof(MedicalKitScript), "healCoroutineRunning");
            heldItem.UseItemOnClient(true);
            while (this.healTarget != null 
                && this.healTarget.health < 100
                && FindUsualScrapMedkit(ai.HeldItem))
            {
                // Ok, we keep looping until the player is fully healed
                yield return null;
                npcController.OrderToLookAtPosition(this.healTarget.NetworkObject, EnumLookAtPriority.HIGH_PRIORITY, 1f);
                if ((bool)healCoroutineRunningField.GetValue(heldItem) == false)
                {
                    heldItem.UseItemOnClient(true);
                }

                // If the medkit is out of juice, just wait until we get more
                int Healthpool = (int)healthpoolField.GetValue(heldItem);
                if (Healthpool <= 0)
                {
                    if (heldItem.itemProperties.holdButtonUse 
                        && (bool)healCoroutineRunningField.GetValue(heldItem) == true)
                    {
                        heldItem.UseItemOnClient(false);
                    }
                    yield return new WaitUntil(() => (int)healthpoolField.GetValue(heldItem) > 0);
                }
            }
            if (heldItem != null && heldItem == ai.HeldItem && heldItem.itemProperties.holdButtonUse)
            {
                heldItem.UseItemOnClient(false);
            }

            // Just restart the corotine until we finish!
            StopHealCoroutine();
        }

        private void DoUsualScrapMedkitLogic()
        {
            // Lets go and heal a player
            if (!ai.HasGrabbableObjectInInventory(FindUsualScrapMedkit, out _))
            {
                neededMedicalTool = ai.FindItemOnShip(FindUsualScrapMedkit);
                if (neededMedicalTool == null || !ai.HasSpaceInInventory(neededMedicalTool))
                {
                    ChangeBackToPreviousState(); // Odd, we can't find the medkit, just give up!
                }
                return;
            }

            float sqrDistToHealTarget = (healTarget.transform.position - npcController.Npc.transform.position).sqrMagnitude;
            if (sqrDistToHealTarget > Const.DISTANCE_CLOSE_ENOUGH_TO_HEAL_TARGET * Const.DISTANCE_CLOSE_ENOUGH_TO_HEAL_TARGET
                || (Physics.Linecast(npcController.Npc.gameplayCamera.transform.position, healTarget.gameplayCamera.transform.position, out RaycastHit hitInfo, StartOfRound.Instance.collidersAndRoomMaskAndDefault)
                && hitInfo.transform.GetComponent<PlayerControllerB>() != healTarget))
            {
                // Select and use items based on our current situation, if needed
                SelectBestItemFromInventory();

                // Alright lets go to them!
                ai.SetDestinationToPositionLethalBotAI(this.healTarget.transform.position);

                // Sprint if far enough
                if (!npcController.WaitForFullStamina && sqrDistToHealTarget > Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION * Const.DISTANCE_STOP_SPRINT_LAST_KNOWN_POSITION)
                {
                    npcController.OrderToSprint();
                }
                else
                {
                    npcController.OrderToStopSprint();
                }

                ai.OrderMoveToDestination();
            }
            else
            {
                // Alright we are close enough to the heal target, stop moving
                ai.StopMoving();
                npcController.OrderToStopSprint();

                if (healCoroutine == null)
                {
                    healCoroutine = ai.StartCoroutine(healUsingUsualScrapMedkit());
                }
            }
        }

        private void DoUsualScrapBandageLogic()
        {
            // We need to patch ourself up!
            if (!ai.HasGrabbableObjectInInventory(FindUsualScrapBandage, out int bandageSlot))
            {
                neededMedicalTool = ai.FindItemOnShip(FindUsualScrapBandage);
                if (neededMedicalTool == null || !ai.HasSpaceInInventory(neededMedicalTool))
                {
                    ChangeBackToPreviousState(); // Odd, we can't find the bandages, just give up!
                }
                return;
            }

            // Make sure we have the bandage equiped
            if (!FindUsualScrapBandage(ai.HeldItem) 
                || npcController.Npc.currentItemSlot != bandageSlot)
            {
                if (ai.HeldItem != null && ai.HeldItem.itemProperties.twoHanded)
                {
                    ai.DropItem();
                    return;
                }
                ai.SwitchItemSlotsAndSync(bandageSlot);
                return;
            }

            // Patch ourself up!
            ai.HeldItem.UseItemOnClient(true);
        }

        /// <summary>
        /// Checks if the bot has weed killer in its inventory!
        /// </summary>
        /// <remarks>
        /// Can't use the default FindObject since its a member function not static!
        /// </remarks>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private static bool FindWeedKiller(GrabbableObject item)
        {
            return item is SprayPaintItem weedKiller && weedKiller.isWeedKillerSprayBottle && sprayCanTank.Invoke(weedKiller) > 0f; // For anyone wondering SprayPaintItem is the same class used for weed killer.
        }

        /// <summary>
        /// Checks if the bot has Usual Scrap Medkit in its inventory!
        /// </summary>
        /// <remarks>
        /// Can't use the default FindObject since its a member function not static!
        /// </remarks>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private static bool FindUsualScrapMedkit([NotNullWhen(true)] GrabbableObject? item)
        {
            return item is MedicalKitScript;
        }

        /// <summary>
        /// Checks if the bot has Usual Scrap Bandage in its inventory!
        /// </summary>
        /// <remarks>
        /// Can't use the default FindObject since its a member function not static!
        /// </remarks>
        /// <inheritdoc cref="AIState.FindObject(GrabbableObject)"/>
        private static bool FindUsualScrapBandage([NotNullWhen(true)] GrabbableObject? item)
        {
            return item is BandagesScript;
        }

        private void StopHealCoroutine()
        {
            if (healCoroutine != null)
            {
                ai.StopCoroutine(healCoroutine);
                healCoroutine = null;
            }
        }
    }
}
