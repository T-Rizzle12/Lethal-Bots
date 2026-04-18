using GameNetcodeStuff;
using LethalBots.AI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.Utils.Helpers
{
    /// <summary>
    /// Helper class that helps bots press their interact key on stuff!
    /// </summary>
    public class LethalBotInteraction
    {
        private InteractTrigger interactTrigger;
        private Action<LethalBotAI, PlayerControllerB, InteractTrigger>? postInteractFunc;
        private bool skipOriginalInteract;
        private bool ignoreHandLimit;
        private bool ignoreInteractablility;
        public bool isBeingHeldByPlayer = false;
        private float holdFillAmount = 0f;
        public bool IsCompleted { get; private set; } = false;

        /// <summary>
        /// Creates a new LethalBotInteraction for the given <paramref name="interactTrigger"/>
        /// </summary>
        /// <param name="interactTrigger">The trigger to call interaction stuff on</param>
        /// <param name="ignoreInteractablility">Should the bot ignore the <see cref="InteractTrigger.interactable"/> flag?</param>
        /// <param name="ignoreHandLimit">Should the bot ignore the held item limiations</param>
        public LethalBotInteraction(InteractTrigger interactTrigger, bool ignoreInteractablility = false, bool ignoreHandLimit = false)
        {
            this.interactTrigger = interactTrigger;
            this.ignoreHandLimit = ignoreHandLimit;
            this.ignoreInteractablility = ignoreInteractablility;
            this.skipOriginalInteract = false;
        }

        /// <summary>
        /// Creates a new LethalBotInteraction for the given <paramref name="interactTrigger"/>
        /// </summary>
        /// <param name="interactTrigger">The trigger to call interaction stuff on</param>
        /// <param name="postInteractFunc">The function to call after the interaction is completed</param>
        /// <param name="ignoreInteractablility">Should the bot ignore the <see cref="InteractTrigger.interactable"/> flag?</param>
        /// <param name="skipOriginalInteract">Should the original <see cref="InteractTrigger.Interact"/> be skipped. This is good if you want <paramref name="postInteractFunc"/> to do its own logic instead!</param>
        /// <param name="ignoreHandLimit">Should the bot ignore the held item limiations</param>
        public LethalBotInteraction(InteractTrigger interactTrigger, Action<LethalBotAI, PlayerControllerB, InteractTrigger> postInteractFunc, bool ignoreInteractablility = false, bool skipOriginalInteract = false, bool ignoreHandLimit = false) 
            : this(interactTrigger, ignoreInteractablility, ignoreHandLimit)
        {
            this.postInteractFunc = postInteractFunc;
            this.skipOriginalInteract = skipOriginalInteract;
        }

        /// <summary>
        /// Updates this interaction
        /// </summary>
        public void Update(LethalBotAI lethalBotAI, float deltaTime)
        {
            if (IsCompleted) return;

            PlayerControllerB lethalBotController = lethalBotAI.NpcController.Npc;
            if (interactTrigger == null || (!ignoreInteractablility && !interactTrigger.interactable))
            {
                StopHoldInteractionOnTrigger();
            }
            else if (!interactTrigger.gameObject.activeInHierarchy 
                || !interactTrigger.holdInteraction 
                || interactTrigger.currentCooldownValue > 0f 
                || (!ignoreHandLimit && lethalBotController.isHoldingObject && !interactTrigger.oneHandedItemAllowed) 
                || (!ignoreHandLimit && lethalBotController.twoHanded && !interactTrigger.twoHandedItemAllowed))
            {
                StopHoldInteractionOnTrigger();
            }
            // We check if we still need to "hold" our +use key
            else if (!HoldInteractFill(deltaTime, interactTrigger.timeToHold, interactTrigger.timeToHoldSpeedMultiplier))
            {
                HoldInteractNotFilled();
            }
            else
            {
                // Make sure the IsCompleted flag is set!
                IsCompleted = true;

                // Call the original function as needed
                try
                {
                    // Do we call the original interact function?
                    if (!skipOriginalInteract)
                    {
                        interactTrigger.Interact(lethalBotController.thisPlayerBody);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogError($"LethalBotInteraction had an error when calling interactTrigger.Interact on {interactTrigger}. Error: {e}");
                }

                // If this fails, it fails
                try
                {
                    postInteractFunc?.Invoke(lethalBotAI, lethalBotController, interactTrigger);
                }
                catch (Exception e)
                {
                    Plugin.LogError($"LethalBotInteraction had an error when calling postInteractFunc. Error: {e}");
                }
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="HUDManager.HoldInteractionFill(float, float)"/>, but made to work with bots
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <param name="timeToHold"></param>
        /// <param name="speedMultiplier"></param>
        /// <returns></returns>
        private bool HoldInteractFill(float deltaTime, float timeToHold, float speedMultiplier = 1f)
        {
            if (timeToHold == -1f)
            {
                return false;
            }
            holdFillAmount += deltaTime * speedMultiplier;
            if (holdFillAmount > timeToHold)
            {
                holdFillAmount = 0f;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Same as <see cref="InteractTrigger.HoldInteractNotFilled"/>, but modified to work with bots!
        /// </summary>
        private void HoldInteractNotFilled()
        {
            interactTrigger.holdingInteractEvent.Invoke(holdFillAmount / interactTrigger.timeToHold);
            if (!interactTrigger.specialCharacterAnimation && !interactTrigger.isLadder)
            {
                if (!interactTrigger.isBeingHeldByPlayer && !isBeingHeldByPlayer)
                {
                    interactTrigger.onInteractEarly.Invoke(null);
                }

                isBeingHeldByPlayer = true;
            }
        }

        /// <summary>
        /// Same as <see cref="PlayerControllerB.StopHoldInteractionOnTrigger"/>, but modified to work with bots
        /// </summary>
        public void StopHoldInteractionOnTrigger()
        {
            if (IsCompleted) return;

            IsCompleted = true;
            holdFillAmount = 0f;
            if (interactTrigger != null)
            {
                if (isBeingHeldByPlayer && interactTrigger.currentCooldownValue <= 0f)
                {
                    isBeingHeldByPlayer = false;
                    interactTrigger.onStopInteract.Invoke(null);
                }
            }
        }
    }
}
