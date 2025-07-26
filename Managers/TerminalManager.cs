using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LethalBots.Managers
{
    /// <summary>
    /// Manager in charge of finding, caching, and synchronize clients with the terminal!
    /// </summary>
    class TerminalManager : NetworkBehaviour
    {
        public static TerminalManager Instance { get; internal set; } = null!;

        private Terminal? terminalScript = null;

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

        public Terminal GetTerminal()
        {
            if (terminalScript == null)
            {
                terminalScript = GameObject.Find("TerminalScript").GetComponent<Terminal>();
            }
            return terminalScript;
        }
    }
}
