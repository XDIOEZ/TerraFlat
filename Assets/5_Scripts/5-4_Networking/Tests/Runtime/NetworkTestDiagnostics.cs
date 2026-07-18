using System.Text;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Testing
{
    [DisallowMultipleComponent]
    public sealed class NetworkTestDiagnostics : MonoBehaviour
    {
        private float nextSnapshotTime;

        private void Update()
        {
            if (!NetworkServer.active || Time.realtimeSinceStartup < nextSnapshotTime)
                return;

            nextSnapshotTime = Time.realtimeSinceStartup + 1f;
            NetworkTestPlayer[] players = FindObjectsOfType<NetworkTestPlayer>();
            StringBuilder positions = new StringBuilder();

            foreach (NetworkTestPlayer player in players)
            {
                Vector3 position = player.transform.position;
                if (positions.Length > 0)
                    positions.Append("; ");
                positions.Append($"{player.netId}@({position.x:F2},{position.y:F2})");
            }

            Debug.Log($"[NET_TEST] ServerSnapshot connections={NetworkServer.connections.Count} players={players.Length} positions=[{positions}]");
        }
    }
}
