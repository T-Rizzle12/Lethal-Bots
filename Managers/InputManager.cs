using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Patches.NpcPatches;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalBots.Managers
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; } = null!;

        private void Awake()
        {
            Instance = this;
            AddEventHandlers();
        }

        private void AddEventHandlers()
        {
            Plugin.InputActionsInstance.LeadBot.performed += LeadBot_performed;
            Plugin.InputActionsInstance.DropItem.performed += GiveTakeItem_performed;
            //Plugin.InputActionsInstance.GrabIntern.performed += GrabIntern_performed;
            //Plugin.InputActionsInstance.ReleaseInterns.performed += ReleaseInterns_performed;
            Plugin.InputActionsInstance.ChangeSuitBot.performed += ChangeSuitLethalBot_performed;
        }

        public void RemoveEventHandlers()
        {
            Plugin.InputActionsInstance.LeadBot.performed -= LeadBot_performed;
            Plugin.InputActionsInstance.DropItem.performed -= GiveTakeItem_performed;
            //Plugin.InputActionsInstance.GrabIntern.performed -= GrabIntern_performed;
            //Plugin.InputActionsInstance.ReleaseInterns.performed -= ReleaseInterns_performed;
            Plugin.InputActionsInstance.ChangeSuitBot.performed -= ChangeSuitLethalBot_performed;
        }

        #region Tips display

        public string GetKeyAction(InputAction inputAction)
        {
            int bindingIndex;
            if (StartOfRound.Instance.localPlayerUsingController)
            {
                // Gamepad
                bindingIndex = inputAction.GetBindingIndex(InputBinding.MaskByGroup("Gamepad"));
            }
            else
            {
                // kbm
                bindingIndex = inputAction.GetBindingIndex(InputBinding.MaskByGroup("KeyboardAndMouse"));
            }
            return inputAction.GetBindingDisplayString(bindingIndex);
        }

        public void AddLethalBotsControlTip(HUDManager hudManager)
        {
            int index = -1;
            for (int i = 0; i < hudManager.controlTipLines.Length - 1; i++)
            {
                TextMeshProUGUI textMeshProUGUI = hudManager.controlTipLines[i + 1];
                if (textMeshProUGUI != null && textMeshProUGUI.enabled && string.IsNullOrWhiteSpace(textMeshProUGUI.text))
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
            {
                index = hudManager.controlTipLines.Length - 1;
            }

            if (LethalBotManager.Instance.IsLocalPlayerNextToChillLethalBots())
            {
                WriteControlTipLine(hudManager.controlTipLines[index], Const.TOOLTIP_MAKE_BOT_LOOK, GetKeyAction(Plugin.InputActionsInstance.MakeBotLookAtPosition));
            }
        }

        private void WriteControlTipLine(TextMeshProUGUI line, string textToAdd, string keyAction)
        {
            if (!IsStringPresent(line.text, textToAdd))
            {
                if (!string.IsNullOrWhiteSpace(line.text))
                {
                    line.text += "\n";
                }
                line.text += string.Format(textToAdd, keyAction);
            }
        }

        private bool IsStringPresent(string stringCurrent, string stringToAdd)
        {
            string[] splits = stringCurrent.Split(new string[] { "[", "]\n" }, System.StringSplitOptions.None);
            foreach (string split in splits)
            {
                if (string.IsNullOrWhiteSpace(split))
                {
                    continue;
                }

                if (stringToAdd.Contains(split.Trim()))
                {
                    return true;
                }
            }

            return false;
        }

        public void UpdateControlTip()
        {
            string[] currentControlTipLines = { };
            if (HUDManager.Instance.controlTipLines != null
                && HUDManager.Instance.controlTipLines.Length > 0)
            {
                currentControlTipLines = HUDManager.Instance.controlTipLines.Select(i => i.text).ToArray();
            }

            HUDManager.Instance.ChangeControlTipMultiple(currentControlTipLines);
        }

        #endregion

        #region Event handlers

        private bool IsPerformedValid(PlayerControllerB localPlayer)
        {
            if (!localPlayer.IsOwner
                || localPlayer.isPlayerDead
                || !localPlayer.isPlayerControlled)
            {
                return false;
            }

            if (localPlayer.isGrabbingObjectAnimation
                || localPlayer.isTypingChat
                || localPlayer.inTerminalMenu
                || localPlayer.IsInspectingItem)
            {
                return false;
            }
            if (localPlayer.inAnimationWithEnemy != null)
            {
                return false;
            }
            if (localPlayer.jetpackControls || localPlayer.disablingJetpackControls)
            {
                return false;
            }
            if (StartOfRound.Instance.suckingPlayersOutOfShip)
            {
                return false;
            }

            if (localPlayer.hoveringOverTrigger != null)
            {
                if (localPlayer.hoveringOverTrigger.holdInteraction
                    || !PlayerControllerBPatch.InteractTriggerUseConditionsMet_ReversePatch(localPlayer))
                {
                    return false;
                }
            }

            return true;
        }

        private void LeadBot_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (!IsPerformedValid(localPlayer))
            {
                return;
            }

            // Use of interact key to assign lethalBot to player
            Ray interactRay = new Ray(localPlayer.gameplayCamera.transform.position, localPlayer.gameplayCamera.transform.forward);
            RaycastHit[] raycastHits = Physics.RaycastAll(interactRay, localPlayer.grabDistance, Const.PLAYER_MASK);
            foreach (RaycastHit hit in raycastHits)
            {
                if (hit.collider.tag != "Player")
                {
                    continue;
                }

                PlayerControllerB player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (player == null)
                {
                    continue;
                }
                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(player);
                if (lethalBot == null
                    || lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }

                EnumAIStates currentBotState = lethalBot.State.GetAIState();
                if (lethalBot.OwnerClientId != localPlayer.actualClientId 
                    || !lethalBot.IsFollowingTargetPlayer())
                {
                    if (lethalBot.IsInSpecialAnimation())
                    {
                        HUDManager.Instance.DisplayTip("Bot is busy!", "If the bot is on the terminal, you can type 'hop off the terminal' to get them to hop off for a few seconds.");
                        return;
                    }

                    lethalBot.SyncAssignTargetAndSetMovingTo(localPlayer);

                    if (Plugin.Config.ChangeSuitAutoBehaviour.Value)
                    {
                        lethalBot.ChangeSuitLethalBotServerRpc(player.playerClientId, localPlayer.currentSuitID);
                    }
                }
                else if (currentBotState != EnumAIStates.SearchingForScrap)
                {
                    lethalBot.State = new SearchingForScrapState(lethalBot.State);
                }

                //HUDManager.Instance.ClearControlTips();
                //HUDManager.Instance.ChangeControlTipMultiple(new string[] { Const.TOOLTIPS_ORDER_1 });
                return;
            }
        }

        private void GiveTakeItem_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (!IsPerformedValid(localPlayer))
            {
                return;
            }

            // Make an lethalBot drop his object
            Ray interactRay = new Ray(localPlayer.gameplayCamera.transform.position, localPlayer.gameplayCamera.transform.forward);
            RaycastHit[] raycastHits = Physics.RaycastAll(interactRay, localPlayer.grabDistance, Const.PLAYER_MASK);
            foreach (RaycastHit hit in raycastHits)
            {
                if (hit.collider.tag != "Player")
                {
                    continue;
                }

                PlayerControllerB lethalBotController = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (lethalBotController == null)
                {
                    continue;
                }
                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI(lethalBotController);
                if (lethalBot == null
                    || lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }

                // To cut Discard_performed from triggering after this input
                AccessTools.Field(typeof(PlayerControllerB), "timeSinceSwitchingSlots").SetValue(localPlayer, 0f);

                if (!lethalBot.AreHandsFree())
                {
                    // Bot drop item
                    lethalBot.DropItem();
                }
                // If we still have stuff in our inventory,
                // we should swap to it in case the player wants us to drop it!
                else if (lethalBot.HasSomethingInInventory())
                {
                    for (int i = 0; i < lethalBot.NpcController.Npc.ItemSlots.Length; i++)
                    {
                        if (lethalBot.NpcController.Npc.ItemSlots[i] != null)
                        {
                            lethalBot.SwitchItemSlotsAndSync(i);
                        }
                    }
                }
                /*else if (localPlayer.currentlyHeldObjectServer != null)
                 {
                     // Intern take item from player hands
                     GrabbableObject grabbableObject = localPlayer.currentlyHeldObjectServer;
                     lethalBot.GiveItemToInternServerRpc(localPlayer.playerClientId, grabbableObject.NetworkObject);
                 }*/

                return;
            }
        }

        /*private void GrabIntern_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (!IsPerformedValid(localPlayer))
            {
                return;
            }

            Ray interactRay = new Ray(localPlayer.gameplayCamera.transform.position, localPlayer.gameplayCamera.transform.forward);
            RaycastHit[] raycastHits = Physics.RaycastAll(interactRay, localPlayer.grabDistance, Const.PLAYER_MASK);
            foreach (RaycastHit hit in raycastHits)
            {
                if (hit.collider.tag != "Player")
                {
                    continue;
                }

                PlayerControllerB player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (player == null)
                {
                    continue;
                }
                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI((int)player.playerClientId);
                if (lethalBot == null
                    || lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }

                lethalBot.SyncAssignTargetAndSetMovingTo(localPlayer);
                // Grab lethalBot
                lethalBot.GrabLethalBotServerRpc(localPlayer.playerClientId);

                UpdateControlTip();
                return;
            }
        }

        private void ReleaseInterns_performed(InputAction.CallbackContext obj)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (!IsPerformedValid(localPlayer))
            {
                return;
            }

            // No lethalBot in interact range
            // Check if we hold interns
            LethalBotAI[] internsAIsHoldByPlayer = LethalBotManager.Instance.GetInternsAiHoldByPlayer((int)localPlayer.playerClientId);
            if (internsAIsHoldByPlayer.Length > 0)
            {
                for (int i = 0; i < internsAIsHoldByPlayer.Length; i++)
                {
                    internsAIsHoldByPlayer[i].SyncReleaseLethalBot(localPlayer);
                }
            }

            HUDManager.Instance.ClearControlTips();
        }*/

        private void ChangeSuitLethalBot_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (!IsPerformedValid(localPlayer))
            {
                return;
            }

            // Use of change suit key to change suit of lethalBot
            Ray interactRay = new Ray(localPlayer.gameplayCamera.transform.position, localPlayer.gameplayCamera.transform.forward);
            RaycastHit[] raycastHits = Physics.RaycastAll(interactRay, localPlayer.grabDistance, Const.PLAYER_MASK);
            foreach (RaycastHit hit in raycastHits)
            {
                if (hit.collider.tag != "Player")
                {
                    continue;
                }

                PlayerControllerB player = hit.collider.gameObject.GetComponent<PlayerControllerB>();
                if (player == null)
                {
                    continue;
                }
                LethalBotAI? lethalBot = LethalBotManager.Instance.GetLethalBotAI((int)player.playerClientId);
                if (lethalBot == null 
                    || lethalBot.IsSpawningAnimationRunning())
                {
                    continue;
                }


                lethalBot.ChangeSuitLethalBotServerRpc(lethalBot.NpcController.Npc.playerClientId, localPlayer.currentSuitID);

                return;
            }
        }

        #endregion
    }
}
