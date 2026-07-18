using System;
using System.Collections;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Networking.Testing
{
    [DisallowMultipleComponent]
    public sealed class NetworkTestAutoLauncher : MonoBehaviour
    {
        private float quitAt = -1f;

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            NetworkTestRuntime.AutoMove = HasFlag(args, "-networkAutoMove");

            if (TryGetValue(args, "-networkExitAfter", out string exitAfterText) &&
                float.TryParse(exitAfterText, out float exitAfter) && exitAfter > 0f)
            {
                quitAt = Time.realtimeSinceStartup + exitAfter;
            }

            if (!TryGetValue(args, "-networkRole", out string role))
                yield break;

            string address = TryGetValue(args, "-networkAddress", out string configuredAddress)
                ? configuredAddress
                : "127.0.0.1";
            ushort port = TryGetValue(args, "-networkPort", out string portText) && ushort.TryParse(portText, out ushort configuredPort)
                ? configuredPort
                : (ushort)7777;

            yield return null;

            INetworkSession session = GameNetwork.Session;
            NetworkStartResult result;
            switch (role.ToLowerInvariant())
            {
                case "host":
                    result = session.StartHost(port);
                    break;
                case "server":
                    result = session.StartServer(port);
                    break;
                case "client":
                    result = session.StartClient(address, port);
                    break;
                default:
                    Debug.LogError($"[NET_TEST] Unknown network role: {role}");
                    yield break;
            }

            Debug.Log($"[NET_TEST] Auto launch role={role} address={address} port={port} success={result.Success} error={result.Error}");
        }

        private void Update()
        {
            if (quitAt > 0f && Time.realtimeSinceStartup >= quitAt)
            {
                Debug.Log("[NET_TEST] Auto test time elapsed; quitting.");
                Application.Quit(0);
                quitAt = -1f;
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            return Array.Exists(args, value => string.Equals(value, flag, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetValue(string[] args, string key, out string value)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i + 1];
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
