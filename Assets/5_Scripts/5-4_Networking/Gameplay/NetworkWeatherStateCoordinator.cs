using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 服务器广播 WeatherMgr 的权威阶段状态，客户端仅负责应用表现与本地只读环境数据。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWeatherStateCoordinator : MonoBehaviour
    {
        private bool serverStarted;
        private bool clientStarted;
        private bool requestedInitialState;

#region 生命周期

        public void StartServerSide()
        {
            if (serverStarted)
                return;

            NetworkServer.RegisterHandler<NetworkWeatherStateRequest>(OnServerStateRequest, false);
            WeatherMgr.AuthoritativeWeatherStateChanged += HandleAuthoritativeWeatherStateChanged;
            serverStarted = true;
        }

        public void StartClientSide()
        {
            if (clientStarted)
                return;

            NetworkClient.RegisterHandler<NetworkWeatherStateMessage>(OnClientWeatherState, false);
            requestedInitialState = false;
            clientStarted = true;
        }

        public void StopServerSide()
        {
            if (!serverStarted)
                return;

            NetworkServer.UnregisterHandler<NetworkWeatherStateRequest>();
            WeatherMgr.AuthoritativeWeatherStateChanged -= HandleAuthoritativeWeatherStateChanged;
            serverStarted = false;
        }

        public void StopClientSide()
        {
            if (!clientStarted)
                return;

            NetworkClient.UnregisterHandler<NetworkWeatherStateMessage>();
            requestedInitialState = false;
            clientStarted = false;
        }

        private void Update()
        {
            if (!clientStarted || requestedInitialState || !NetworkClient.active || NetworkServer.active)
                return;
            if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
                return;
            if (SaveDataMgr.Instance?.Active_PlanetData == null)
                return;

            requestedInitialState = true;
            NetworkClient.Send(new NetworkWeatherStateRequest());
        }

#endregion

#region 服务端

        private void OnServerStateRequest(
            NetworkConnectionToClient connection,
            NetworkWeatherStateRequest request)
        {
            if (connection == null || WeatherMgr.Instance == null)
                return;

            connection.Send(CreateMessage(WeatherMgr.Instance.CaptureWeatherState()));
        }

        private void HandleAuthoritativeWeatherStateChanged(WeatherStateSnapshot snapshot)
        {
            if (!NetworkServer.active)
                return;

            NetworkServer.SendToAll(CreateMessage(snapshot));
        }

        private static NetworkWeatherStateMessage CreateMessage(WeatherStateSnapshot snapshot)
        {
            return new NetworkWeatherStateMessage
            {
                PlanetName = snapshot.PlanetName,
                Weather = snapshot.Weather,
                Phase = snapshot.Phase,
                Intensity = snapshot.Intensity,
                WindStrength = snapshot.WindStrength,
                PhaseStartedTotalTime = snapshot.PhaseStartedTotalTime,
                PhaseEndTotalTime = snapshot.PhaseEndTotalTime,
                NextWeatherEventTotalTime = snapshot.NextWeatherEventTotalTime,
                RandomCursor = snapshot.RandomCursor,
                EventSequence = snapshot.EventSequence,
                DataVersion = snapshot.DataVersion
            };
        }

#endregion

#region 客户端

        private static void OnClientWeatherState(NetworkWeatherStateMessage message)
        {
            if (NetworkServer.active)
                return;

            WeatherMgr.Instance.ApplyReplicatedWeatherState(
                message.PlanetName,
                message.Weather,
                message.Phase,
                message.Intensity,
                message.WindStrength,
                message.PhaseStartedTotalTime,
                message.PhaseEndTotalTime,
                message.NextWeatherEventTotalTime,
                message.RandomCursor,
                message.EventSequence,
                message.DataVersion);
        }

#endregion
    }
}
