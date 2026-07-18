using FlatWorld.Networking;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Testing
{
    [DisallowMultipleComponent]
    public sealed class NetworkTestHud : MonoBehaviour
    {
        private string serverAddress = "127.0.0.1";
        private string portText = "7777";
        private string feedback = "Ready";

        private void OnGUI()
        {
            INetworkSession session = GameNetwork.Session;

            GUILayout.BeginArea(new Rect(16f, 16f, 340f, 310f), GUI.skin.box);
            GUILayout.Label("FlatWorld Mirror Network Test");
            GUILayout.Label($"Role: {session.Role}    State: {session.State}");

            if (NetworkServer.active)
                GUILayout.Label($"Server connections: {NetworkServer.connections.Count}");
            if (NetworkClient.active)
                GUILayout.Label($"Client connected: {NetworkClient.isConnected}");

            GUILayout.Space(8f);

            if (session.State == NetworkSessionState.Offline)
            {
                GUILayout.Label("Server address");
                serverAddress = GUILayout.TextField(serverAddress);
                GUILayout.Label("UDP port");
                portText = GUILayout.TextField(portText);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host"))
                    ApplyStartResult(session.StartHost(ParsePort()));
                if (GUILayout.Button("Client"))
                    ApplyStartResult(session.StartClient(serverAddress, ParsePort()));
                if (GUILayout.Button("Server"))
                    ApplyStartResult(session.StartServer(ParsePort()));
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("Stop"))
            {
                session.Stop();
                feedback = "Stopping...";
            }

            GUILayout.Space(8f);
            GUILayout.Label(feedback);
            GUILayout.Label("Move local player: WASD / Arrow Keys");
            GUILayout.Label("Open a second build and connect to 127.0.0.1.");
            GUILayout.EndArea();
        }

        private ushort ParsePort()
        {
            return ushort.TryParse(portText, out ushort port) ? port : (ushort)7777;
        }

        private void ApplyStartResult(NetworkStartResult result)
        {
            feedback = result.Success ? "Start requested" : result.Error;
        }
    }
}
