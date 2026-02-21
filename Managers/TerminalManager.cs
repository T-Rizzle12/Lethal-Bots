using HarmonyLib;
using LethalBots.AI.AIStates;
using LethalBots.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager in charge of finding, caching, and synchronize clients with the terminal!<br/>
    /// This also manages the shopping stuff for bots as well!
    /// </summary>
    class TerminalManager : NetworkBehaviour
    {
        public static TerminalManager Instance { get; internal set; } = null!;

        private Terminal? terminalScript = null;

        #region Reflection

        private static readonly MethodInfo loadNewNodeIfAffordableMethod = AccessTools.Method(typeof(Terminal), "LoadNewNodeIfAffordable");

        private static readonly MethodInfo attemptLoadCreatureFileNodeMethod = AccessTools.Method(typeof(Terminal), "AttemptLoadCreatureFileNode");

        private static readonly MethodInfo parseWordOverrideOptionsMethod = AccessTools.Method(typeof(Terminal), "ParseWordOverrideOptions");

        private static readonly MethodInfo checkForPlayerNameCommandMethod = AccessTools.Method(typeof(Terminal), "CheckForPlayerNameCommand");

        private static readonly MethodInfo checkForExactSentencesMethod = AccessTools.Method(typeof(Terminal), "CheckForExactSentences");

        private static readonly MethodInfo callFunctionInAccessibleTerminalObjectMethod = AccessTools.Method(typeof(Terminal), "CallFunctionInAccessibleTerminalObject");

        private static readonly MethodInfo playBroadcastCodeEffectMethod = AccessTools.Method(typeof(Terminal), "PlayBroadcastCodeEffect");

        private static readonly MethodInfo parseWordMethod = AccessTools.Method(typeof(Terminal), "ParseWord");

        #endregion

        private void Awake()
        {
            // Prevent multiple instances of TerminalManager
            if (Instance != null && Instance != this)
            {
                if (Instance.IsSpawned && Instance.IsServer)
                {
                    Instance.NetworkObject.Despawn(destroy: true);
                }
                else
                {
                    Destroy(Instance.gameObject);
                }
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
                terminalScript = GameObject.Find("TerminalScript").GetComponent<Terminal>();
            }
            return terminalScript;
        }

        private static string RemovePunctuation(string s)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (char c in s)
            {
                if (!char.IsPunctuation(c))
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().ToLower();
        }

        /// <summary>
        /// Carbon copy of <see cref="Terminal.OnSubmit"/>, but made to work with bots
        /// </summary>
        /// <param name="text">The text the bot on the terminal "sent"</param>
        /// <param name="ourTerminal">Optional parameter to use the given terminal instead of using <see cref="GetTerminal"/>'s value</param>
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
                    ref int totalCostOfItems = ref PatchesUtil.totalCostOfItemsField.Invoke(ourTerminal);
                    if (terminalNode.buyRerouteToMoon == -2)
                    {
                        totalCostOfItems = terminalNode.itemCost;
                    }
                    else if (terminalNode.itemCost != 0)
                    {
                        totalCostOfItems = terminalNode.itemCost * ourTerminal.playerDefinedAmount;
                    }
                    if (terminalNode.buyItemIndex != -1 || (terminalNode.buyRerouteToMoon != -1 && terminalNode.buyRerouteToMoon != -2) || terminalNode.shipUnlockableID != -1 || terminalNode.buyVehicleIndex != -1)
                    {
                        loadNewNodeIfAffordableMethod.Invoke(ourTerminal, new object[] { terminalNode });
                    }
                    else if (terminalNode.creatureFileID != -1)
                    {
                        attemptLoadCreatureFileNodeMethod.Invoke(ourTerminal, new object[] { terminalNode });
                    }
                    else if (terminalNode.storyLogFileID != -1)
                    {
                        attemptLoadCreatureFileNodeMethod.Invoke(ourTerminal, new object[] { terminalNode });
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
        /// WARMING: This MUST be manually updated for every LC update that changes the terminal. That switch statement the base game uses could have more commands!
        /// </remarks>
        /// <param name="text"></param>
        /// <returns></returns>
        private TerminalNode? ParsePlayerSentence(Terminal ourTerminal, string text)
        {
            // Reflection setup!
            ref bool broadcastedCodeThisFrame = ref PatchesUtil.broadcastedCodeThisFrameField.Invoke(ourTerminal);
            ref bool hasGottenNoun = ref PatchesUtil.hasGottenNounField.Invoke(ourTerminal);
            ref bool hasGottenVerb = ref PatchesUtil.hasGottenVerbField.Invoke(ourTerminal);

            // Start of copied code!
            broadcastedCodeThisFrame = false;
            string s = RemovePunctuation(text);
            string[] array = s.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            TerminalKeyword? terminalKeyword = null;
            if (ourTerminal.currentNode != null && ourTerminal.currentNode.overrideOptions)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    TerminalNode terminalNode = (TerminalNode)parseWordOverrideOptionsMethod.Invoke(ourTerminal, new object[] { array[i], ourTerminal.currentNode.terminalOptions } );
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
                            int num = (int)checkForPlayerNameCommandMethod.Invoke(ourTerminal, new object[] { array[0], array[1] });
                            if (num != -1)
                            {
                                StartOfRound.Instance.mapScreen.SwitchRadarTargetAndSync(num);
                                return ourTerminal.terminalNodes.specialNodes[20];
                            }
                            break;
                        }
                    case "flash":
                        {
                            int num = (int)checkForPlayerNameCommandMethod.Invoke(ourTerminal, new object[] { array[0], array[1] });
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
                            int num = (int)checkForPlayerNameCommandMethod.Invoke(ourTerminal, new object[] { array[0], array[1] });
                            if (num != -1)
                            {
                                StartOfRound.Instance.mapScreen.PingRadarBooster(num);
                                return ourTerminal.terminalNodes.specialNodes[21];
                            }
                            break;
                        }
                    case "transmit":
                        {
                            SignalTranslator? signalTranslator = MissionControlState.SignalTranslator;
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
            terminalKeyword = (TerminalKeyword)checkForExactSentencesMethod.Invoke(ourTerminal, new object[] { s });
            if (terminalKeyword != null)
            {
                if (terminalKeyword.accessTerminalObjects)
                {
                    callFunctionInAccessibleTerminalObjectMethod.Invoke(ourTerminal, new object[] { terminalKeyword.word });
                    playBroadcastCodeEffectMethod.Invoke(ourTerminal, null);
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
            new List<TerminalKeyword>();
            bool flag = false;
            hasGottenNoun = false;
            hasGottenVerb = false;
            for (int j = 0; j < array.Length; j++)
            {
                terminalKeyword = (TerminalKeyword)parseWordMethod.Invoke(ourTerminal, new object[] { array[j] });
                if (terminalKeyword != null)
                {
                    Plugin.LogInfo("Parsed word: " + array[j]);
                    if (terminalKeyword.isVerb)
                    {
                        if (hasGottenVerb)
                        {
                            continue;
                        }
                        hasGottenVerb = true;
                        terminalKeyword2 = terminalKeyword;
                    }
                    else
                    {
                        if (hasGottenNoun)
                        {
                            continue;
                        }
                        hasGottenNoun = true;
                        terminalKeyword3 = terminalKeyword;
                        if (terminalKeyword.accessTerminalObjects)
                        {
                            broadcastedCodeThisFrame = true;
                            callFunctionInAccessibleTerminalObjectMethod.Invoke(ourTerminal, new object[] { terminalKeyword.word });
                            flag = true;
                        }
                    }
                    if (!flag && hasGottenNoun && hasGottenVerb)
                    {
                        break;
                    }
                }
                else
                {
                    Plugin.LogInfo("Could not parse word: " + array[j]);
                }
            }
            if (broadcastedCodeThisFrame)
            {
                playBroadcastCodeEffectMethod.Invoke(ourTerminal, null);
                return ourTerminal.terminalNodes.specialNodes[19];
            }
            hasGottenNoun = false;
            hasGottenVerb = false;
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
