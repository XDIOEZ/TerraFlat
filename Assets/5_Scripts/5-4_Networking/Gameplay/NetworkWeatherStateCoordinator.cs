using System.Collections.Generic;
using FlatWorld.WorldModel;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 服务器广播 WeatherMgr 的权威状态，客户端仅应用表现与只读环境反馈。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class NetworkWeatherStateCoordinator : MonoBehaviour
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
            WeatherMgr.AuthoritativeRegionalWeatherStateChanged += HandleAuthoritativeRegionalWeatherStateChanged;
            StartWeatherSurfaceServer();
            serverStarted = true;
        }

        public void StartClientSide()
        {
            if (clientStarted)
                return;

            NetworkClient.RegisterHandler<NetworkWeatherStateMessage>(OnClientWeatherState, false);
            NetworkClient.RegisterHandler<NetworkRegionalWeatherStateMessage>(OnClientRegionalWeatherState, false);
            StartWeatherSurfaceClient();
            requestedInitialState = false;
            clientStarted = true;
        }

        public void StopServerSide()
        {
            if (!serverStarted)
                return;

            NetworkServer.UnregisterHandler<NetworkWeatherStateRequest>();
            WeatherMgr.AuthoritativeWeatherStateChanged -= HandleAuthoritativeWeatherStateChanged;
            WeatherMgr.AuthoritativeRegionalWeatherStateChanged -= HandleAuthoritativeRegionalWeatherStateChanged;
            StopWeatherSurfaceServer();
            serverStarted = false;
        }

        public void StopClientSide()
        {
            if (!clientStarted)
                return;

            NetworkClient.UnregisterHandler<NetworkWeatherStateMessage>();
            NetworkClient.UnregisterHandler<NetworkRegionalWeatherStateMessage>();
            StopWeatherSurfaceClient();
            requestedInitialState = false;
            clientStarted = false;
        }

        private void Update()
        {
            if (!clientStarted || !NetworkClient.active || NetworkServer.active)
                return;
            if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
                return;
            if (SaveDataMgr.Instance?.Active_PlanetData == null)
                return;

            if (!requestedInitialState)
            {
                requestedInitialState = true;
                NetworkClient.Send(new NetworkWeatherStateRequest());
            }
            MaintainWeatherSurfaceClient();
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
            IReadOnlyList<RegionalWeatherStateSnapshot> regions = WeatherMgr.Instance.CaptureRegionalWeatherStates();
            for (int i = 0; i < regions.Count; i++)
                connection.Send(CreateRegionalMessage(regions[i], true));
        }

        private void HandleAuthoritativeWeatherStateChanged(WeatherStateSnapshot snapshot)
        {
            if (!NetworkServer.active)
                return;

            NetworkServer.SendToAll(CreateMessage(snapshot));
        }

        private void HandleAuthoritativeRegionalWeatherStateChanged(RegionalWeatherStateSnapshot snapshot)
        {
            if (!NetworkServer.active)
                return;

            NetworkServer.SendToAll(CreateRegionalMessage(snapshot, false));
        }

        private static NetworkRegionalWeatherStateMessage CreateRegionalMessage(
            RegionalWeatherStateSnapshot snapshot,
            bool initialState)
        {
            return new NetworkRegionalWeatherStateMessage
            {
                WorldKey = snapshot.WorldKey,
                RegionX = snapshot.Region.X,
                RegionY = snapshot.Region.Y,
                SampleTime = snapshot.SampleTime,
                Revision = snapshot.Revision,
                AirHumidity = snapshot.AirHumidity,
                HumidityDeficit = snapshot.HumidityDeficit,
                CoolingOffsetCelsius = snapshot.CoolingOffsetCelsius,
                RainIntensity = snapshot.RainIntensity,
                SnowIntensity = snapshot.SnowIntensity,
                PrecipitationIntensity = snapshot.PrecipitationIntensity,
                WindX = snapshot.WindX,
                WindY = snapshot.WindY,
                WindStrength = snapshot.WindStrength,
                CloudCoverage = snapshot.CloudCoverage,
                FogDensity = snapshot.FogDensity,
                LightningActivity = snapshot.LightningActivity,
                LightningSequence = snapshot.LightningSequence,
                RemainingSeconds = snapshot.RemainingSeconds,
                InitialState = initialState
            };
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

        private static void OnClientRegionalWeatherState(NetworkRegionalWeatherStateMessage message)
        {
            if (NetworkServer.active)
                return;

            RegionalWeatherStateSnapshot snapshot = new RegionalWeatherStateSnapshot(
                message.WorldKey,
                new Int2(message.RegionX, message.RegionY),
                message.SampleTime,
                message.Revision,
                message.AirHumidity,
                message.HumidityDeficit,
                message.CoolingOffsetCelsius,
                message.RainIntensity,
                message.SnowIntensity,
                message.PrecipitationIntensity,
                message.WindX,
                message.WindY,
                message.WindStrength,
                message.CloudCoverage,
                message.FogDensity,
                message.LightningActivity,
                message.LightningSequence,
                message.RemainingSeconds);
            WeatherMgr.Instance.ApplyReplicatedRegionalWeatherState(snapshot, message.InitialState);
        }

#endregion
    }
}
