using HarmonyLib;
using LethalBots.AI.AIStates;
using LethalBots.Utils;
using LethalBots.Utils.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager in charge of finding, caching, and synchronize clients with the terminal!<br/>
    /// This also manages the shopping stuff for bots as well!
    /// </summary>
    class TerminalManager : NetworkBehaviour
    {
        public static TerminalManager Instance { get; internal set; } = null!;

        private Terminal terminalScript = null!;

        private void Awake()
        {
            // Prevent multiple instances of TerminalManager
            if (Instance != null && Instance != this)
            {
                Destroy(Instance.gameObject); // Ok, for some reason this works.......I'm just not going to question it.......
            }

            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!base.NetworkManager.IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    // Destory Local manager
                    Destroy(Instance.gameObject);
                }
                Instance = this;
            }
        }

        /// <summary>
        /// Returns a cached instance of the ship terminal
        /// </summary>
        /// <returns></returns>
        public Terminal GetTerminal()
        {
            if (terminalScript == null)
            {
                terminalScript = Object.FindObjectOfType<Terminal>() ?? GameObject.Find("TerminalScript").GetComponent<Terminal>();
            }
            return terminalScript;
        }

        /// <summary>
        /// Helper function to make the bot have to type out a message!
        /// </summary>
        /// <param name="command"></param>
        /// <param name="clearPreviousText"></param>
        /// <returns></returns>
        public IEnumerator SendCommandToTerminal(string command, bool clearPreviousText = true)
        {
            command = command.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                yield break;
            }

            // Get the terminal instance
            Terminal ourTerminal = GetTerminal();
            if (ourTerminal == null) 
            {
                yield break;
            }

            // If there is previous text in the terminal, clear it if the bot wants to!
            if (clearPreviousText && ourTerminal.textAdded > 0)
            {
                for (int i = 0; i < ourTerminal.textAdded; i++)
                {
                    // Play a random keyboard sound effect for the backspace key!
                    RoundManager.PlayRandomClip(ourTerminal.terminalAudio, ourTerminal.keyboardClips);

                    // Wait for a random time between 0.05 and 0.1 seconds before pressing the backspace key
                    yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.1f));

                    // Remove the last character from the terminal's screen text
                    ourTerminal.screenText.text = ourTerminal.screenText.text.Substring(0, ourTerminal.screenText.text.Length - 1);
                }
            }

            // Use the terminal to play keyboard sound effects!
            for (int i = 0; i < command.Length; i++)
            {
                // If the terminal is null, we can't do anything!
                if (ourTerminal == null)
                {
                    yield break;
                }

                // Play a random keyboard sound effect for the character!
                RoundManager.PlayRandomClip(ourTerminal.terminalAudio, ourTerminal.keyboardClips);

                // Add the character to the terminal's screen text
                ourTerminal.screenText.text += command[i];

                // Wait for a random time between 0.05 and 0.3 seconds before typing the next character
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.3f));
            }

            // Play a random keyboard sound effect for the enter key!
            RoundManager.PlayRandomClip(ourTerminal.terminalAudio, ourTerminal.keyboardClips);

            // Send the command we just typed to the terminal!
            terminalScript.OnSubmit();
        }

        /// <summary>
        /// Carbon copy of <see cref="Terminal.OnSubmit"/>, but made to work with bots
        /// </summary>
        /// <param name="text">The text the bot on the terminal "sent"</param>
        /// <param name="ourTerminal">Optional parameter to use the given terminal instead of using <see cref="GetTerminal"/>'s value</param>
        [Obsolete("We now patch the base game so this is no longer needed. Use SendCommandToTerminal instead.")]
        public void OnSubmit(string text, Terminal? ourTerminal = null)
        {
            ourTerminal ??= GetTerminal();
            if (ourTerminal == null)
            {
                return;
            }

            if (ourTerminal.currentNode != null && ourTerminal.currentNode.acceptAnything)
            {
                ourTerminal.LoadNewNode(ourTerminal.currentNode.terminalOptions[0].result);
            }
            else
            {
                TerminalNode? terminalNode = ParsePlayerSentence(ourTerminal, text);
                if (terminalNode != null)
                {
                    if (terminalNode.buyRerouteToMoon == -2)
                    {
                        ourTerminal.totalCostOfItems = terminalNode.itemCost;
                    }
                    else if (terminalNode.itemCost != 0)
                    {
                        ourTerminal.totalCostOfItems = terminalNode.itemCost * ourTerminal.playerDefinedAmount;
                    }
                    if (terminalNode.buyItemIndex != -1 || (terminalNode.buyRerouteToMoon != -1 && terminalNode.buyRerouteToMoon != -2) || terminalNode.shipUnlockableID != -1 || terminalNode.buyVehicleIndex != -1)
                    {
                        ourTerminal.LoadNewNodeIfAffordable(terminalNode);
                    }
                    else if (terminalNode.creatureFileID != -1)
                    {
                        ourTerminal.AttemptLoadCreatureFileNode(terminalNode);
                    }
                    else if (terminalNode.storyLogFileID != -1)
                    {
                        ourTerminal.AttemptLoadStoryLogFileNode(terminalNode);
                    }
                    else
                    {
                        ourTerminal.LoadNewNode(terminalNode);
                    }
                }
            }
        }

        /// <summary>
        /// Carbon copy of <see cref="Terminal.ParsePlayerSentence"/>, but modifed for bots
        /// </summary>
        /// <remarks>
        /// WARNING: This may or may not work for custom commands from other mods, then again, the old system didn't work with other mods anyway....<br/>
        /// WARNING: This MUST be manually updated for every LC update that changes the terminal. That switch statement the base game uses could have more commands!
        /// </remarks>
        /// <param name="ourTerminal"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        private TerminalNode? ParsePlayerSentence(Terminal ourTerminal, string text)
        {
            // Start of copied code!
            ourTerminal.broadcastedCodeThisFrame = false;
            string s = ourTerminal.RemovePunctuation(text);
            string[] array = s.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            TerminalKeyword? terminalKeyword = null;
            if (ourTerminal.currentNode != null && ourTerminal.currentNode.overrideOptions)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    TerminalNode terminalNode = ourTerminal.ParseWordOverrideOptions(array[i], ourTerminal.currentNode.terminalOptions);
                    if (terminalNode != null)
                    {
                        return terminalNode;
                    }
                }
                return null;
            }
            if (array.Length > 1)
            {
                switch (array[0])
                {
                    case "switch":
                        {
                            int num = ourTerminal.CheckForPlayerNameCommand(array[0], array[1]);
                            if (num != -1)
                            {
                                StartOfRound.Instance.mapScreen.SwitchRadarTargetAndSync(num);
                                return ourTerminal.terminalNodes.specialNodes[20];
                            }
                            break;
                        }
                    case "flash":
                        {
                            int num = ourTerminal.CheckForPlayerNameCommand(array[0], array[1]);
                            if (num != -1)
                            {
                                StartOfRound.Instance.mapScreen.FlashRadarBooster(num);
                                return ourTerminal.terminalNodes.specialNodes[23];
                            }
                            if (StartOfRound.Instance.mapScreen.radarTargets[StartOfRound.Instance.mapScreen.targetTransformIndex].isNonPlayer)
                            {
                                StartOfRound.Instance.mapScreen.FlashRadarBooster(StartOfRound.Instance.mapScreen.targetTransformIndex);
                                return ourTerminal.terminalNodes.specialNodes[23];
                            }
                            break;
                        }
                    case "ping":
                        {
                            int num = ourTerminal.CheckForPlayerNameCommand(array[0], array[1]);
                            if (num != -1)
                            {
                                StartOfRound.Instance.mapScreen.PingRadarBooster(num);
                                return ourTerminal.terminalNodes.specialNodes[21];
                            }
                            break;
                        }
                    case "transmit":
                        {
                            SignalTranslator? signalTranslator = SingletonManager.SignalTranslator.Instance;
                            if (signalTranslator == null || !(Time.realtimeSinceStartup - signalTranslator.timeLastUsingSignalTranslator > 8f) || array.Length < 2)
                            {
                                break;
                            }
                            string message = s.Substring(8);
                            if (!string.IsNullOrEmpty(message))
                            {
                                if (!ourTerminal.IsServer)
                                {
                                    signalTranslator.timeLastUsingSignalTranslator = Time.realtimeSinceStartup;
                                }
                                HUDManager.Instance.UseSignalTranslatorServerRpc(message.Substring(0, Mathf.Min(message.Length, 10)));
                                return ourTerminal.terminalNodes.specialNodes[22];
                            }
                            break;
                        }
                }
            }
            terminalKeyword = ourTerminal.CheckForExactSentences(s);
            if (terminalKeyword != null)
            {
                if (terminalKeyword.accessTerminalObjects)
                {
                    ourTerminal.CallFunctionInAccessibleTerminalObject(terminalKeyword.word);
                    ourTerminal.PlayBroadcastCodeEffect();
                    return null;
                }
                if (terminalKeyword.specialKeywordResult != null)
                {
                    return terminalKeyword.specialKeywordResult;
                }
            }
            string value = Regex.Match(s, "\\d+").Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                ourTerminal.playerDefinedAmount = Mathf.Clamp(int.Parse(value), 0, 10);
            }
            else
            {
                ourTerminal.playerDefinedAmount = 1;
            }
            if (array.Length > 5)
            {
                return null;
            }
            TerminalKeyword? terminalKeyword2 = null;
            TerminalKeyword? terminalKeyword3 = null;
            bool flag = false;
            ourTerminal.hasGottenNoun = false;
            ourTerminal.hasGottenVerb = false;
            for (int j = 0; j < array.Length; j++)
            {
                terminalKeyword = ourTerminal.ParseWord(array[j]);
                if (terminalKeyword != null)
                {
                    Plugin.LogInfo("Parsed word: " + array[j]);
                    if (terminalKeyword.isVerb)
                    {
                        if (ourTerminal.hasGottenVerb)
                        {
                            continue;
                        }
                        ourTerminal.hasGottenVerb = true;
                        terminalKeyword2 = terminalKeyword;
                    }
                    else
                    {
                        if (ourTerminal.hasGottenNoun)
                        {
                            continue;
                        }
                        ourTerminal.hasGottenNoun = true;
                        terminalKeyword3 = terminalKeyword;
                        if (terminalKeyword.accessTerminalObjects)
                        {
                            ourTerminal.broadcastedCodeThisFrame = true;
                            ourTerminal.CallFunctionInAccessibleTerminalObject(terminalKeyword.word);
                            flag = true;
                        }
                    }
                    if (!flag && ourTerminal.hasGottenNoun && ourTerminal.hasGottenVerb)
                    {
                        break;
                    }
                }
                else
                {
                    Plugin.LogInfo("Could not parse word: " + array[j]);
                }
            }
            if (ourTerminal.broadcastedCodeThisFrame)
            {
                ourTerminal.PlayBroadcastCodeEffect();
                return ourTerminal.terminalNodes.specialNodes[19];
            }
            ourTerminal.hasGottenNoun = false;
            ourTerminal.hasGottenVerb = false;
            if (terminalKeyword3 == null)
            {
                return ourTerminal.terminalNodes.specialNodes[10];
            }
            if (terminalKeyword2 == null)
            {
                if (terminalKeyword3.defaultVerb == null)
                {
                    return ourTerminal.terminalNodes.specialNodes[11];
                }
                terminalKeyword2 = terminalKeyword3.defaultVerb;
            }
            for (int k = 0; k < terminalKeyword2.compatibleNouns.Length; k++)
            {
                if (terminalKeyword2.compatibleNouns[k].noun == terminalKeyword3)
                {
                    Plugin.LogInfo($"noun keyword: {terminalKeyword3.word} ; verb keyword: {terminalKeyword2.word} ; result null? : {terminalKeyword2.compatibleNouns[k].result == null}");
                    Plugin.LogInfo("result: " + terminalKeyword2.compatibleNouns[k].result.name);
                    return terminalKeyword2.compatibleNouns[k].result;
                }
            }
            return ourTerminal.terminalNodes.specialNodes[12];
        }
    }
}
