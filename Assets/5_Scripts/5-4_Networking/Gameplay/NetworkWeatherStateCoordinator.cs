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
        private string requestedWorld;
        private uint requestedRealm;
        private RegionalWeatherSettings regionalObserverSettings;
        private readonly Dictionary<Vector2Int, NetworkRegionalWeatherStateMessage> pendingRegions = new();
        private readonly List<Vector2Int> pendingRegionKeys = new();

#region 生命周期

        public void StartServerSide()
        {
            if (serverStarted)
                return;

            regionalObserverSettings = ChunkWeatherConfiguration.Load();
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
            pendingRegions.Clear();
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
            if (!NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint realm))
                return;

            string world = GameplayWorldRegistry.LocalPresentationWorldKey;
            if (!string.Equals(requestedWorld, world, System.StringComparison.Ordinal) || requestedRealm != realm)
            {
                requestedWorld = world; requestedRealm = realm; requestedInitialState = false;
                pendingRegions.Clear(); ResetWeatherSurfaceClient();
            }
            if (!requestedInitialState)
            {
                requestedInitialState = true;
                NetworkClient.Send(new NetworkWeatherStateRequest { RealmRevision = realm });
            }
            MaintainPendingRegionalWeather();
            MaintainWeatherSurfaceClient();
        }

#endregion

#region 世界消息路由
        // 请求世界来自连接绑定角色，客户端不能借天气请求读取其它维度。
        private static bool TryGetConnectionWorld(NetworkConnectionToClient connection, out string world)
        {
            world = null;
            var player = connection?.identity != null ? connection.identity.GetComponent<NetworkWorldPlayer>() : null;
            if (connection?.isAuthenticated != true || !connection.isReady || player == null || player.IsWorldTransferPending ||
                !player.TryGetSpaceObserver(out var observer) || observer.InSpace ||
                player.RealmRevision != observer.RealmRevision || string.IsNullOrEmpty(observer.WorldKey)) return false;
            world = observer.WorldKey;
            return true;
        }
        private static bool CanApplyLocalWorld(string world, uint realm) =>
            NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint currentRealm) && currentRealm == realm &&
            string.Equals(world, GameplayWorldRegistry.LocalPresentationWorldKey, System.StringComparison.Ordinal) &&
            GameplayWorldRegistry.TryGet(world, out _);

        private bool IsRegionalWeatherObserved(NetworkWorldPlayer player, string world, Vector2Int region)
        {
            if (player?.TryGetSpaceObserver(out var observer) != true || observer.InSpace || observer.WorldKey != world) return false;
            int distance = Mathf.Max(4, player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 4);
            return NetworkItemStateCoordinator.IsChunkInsideObserverWindow(world, region, observer.Position.LocalMeters.ToVector2(),
                distance + regionalObserverSettings.RegionSizeInChunks);
        }

        private void SendWeatherToConnection<T>(NetworkConnectionToClient connection, T message) where T : struct, NetworkMessage
        {
            if (!TryGetConnectionWorld(connection, out string world) ||
                connection.identity?.GetComponent<NetworkWorldPlayer>() is not NetworkWorldPlayer player) return;
            if (message is NetworkWeatherStateMessage weather && weather.WorldKey == world)
            { weather.RealmRevision = player.RealmRevision; connection.Send(weather); }
            else if (message is NetworkRegionalWeatherStateMessage region && region.WorldKey == world &&
                IsRegionalWeatherObserved(player, world, new Vector2Int(region.RegionX, region.RegionY)))
            { region.RealmRevision = player.RealmRevision; connection.Send(region); }
        }

        private void SendToWorld<T>(string world, T message) where T : struct, NetworkMessage
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != null && connection is not LocalConnectionToClient &&
                    TryGetConnectionWorld(connection, out string observerWorld) &&
                    string.Equals(world, observerWorld, System.StringComparison.Ordinal))
                    SendWeatherToConnection(connection, message);
        }
#endregion

#region 服务端

        private void OnServerStateRequest(
            NetworkConnectionToClient connection,
            NetworkWeatherStateRequest request)
        {
            if (!TryGetConnectionWorld(connection, out string world) || !GameplayWorldRegistry.TryGet(world, out _) ||
                connection.identity.GetComponent<NetworkWorldPlayer>().RealmRevision != request.RealmRevision) return;
            using var worldScope = GameplayWorldRegistry.UseWorld(world);
            WeatherMgr weather = WeatherMgr.GetForWorld(world);
            if (weather == null) return;
            SendWeatherToConnection(connection, CreateMessage(weather.CaptureWeatherState()));
            IReadOnlyList<RegionalWeatherStateSnapshot> regions = weather.CaptureRegionalWeatherStates();
            for (int i = 0; i < regions.Count; i++)
                SendWeatherToConnection(connection, CreateRegionalMessage(regions[i], true));
        }

        private void HandleAuthoritativeWeatherStateChanged(WeatherStateSnapshot snapshot)
        {
            if (!NetworkServer.active)
                return;

            SendToWorld(snapshot.WorldKey, CreateMessage(snapshot));
        }

        private void HandleAuthoritativeRegionalWeatherStateChanged(RegionalWeatherStateSnapshot snapshot)
        {
            if (!NetworkServer.active)
                return;

            SendToWorld(snapshot.WorldKey, CreateRegionalMessage(snapshot, false));
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
                LightningCellX = snapshot.LightningCell.X,
                LightningCellY = snapshot.LightningCell.Y,
                LightningStrength = snapshot.LightningStrength,
                RemainingSeconds = snapshot.RemainingSeconds,
                InitialState = initialState
            };
        }

        private static NetworkWeatherStateMessage CreateMessage(WeatherStateSnapshot snapshot)
        {
            return new NetworkWeatherStateMessage
            {
                WorldKey = snapshot.WorldKey,
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

            if (!CanApplyLocalWorld(message.WorldKey, message.RealmRevision)) return;
            using var worldScope = GameplayWorldRegistry.UseWorld(message.WorldKey);
            WeatherMgr.GetForWorld(message.WorldKey)?.ApplyReplicatedWeatherState(
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

        private void OnClientRegionalWeatherState(NetworkRegionalWeatherStateMessage message)
        {
            if (NetworkServer.active || !CanApplyLocalWorld(message.WorldKey, message.RealmRevision)) return;
            var region = new Vector2Int(message.RegionX, message.RegionY);
            if (!WeatherMgr.HasRegionalPersistenceLease(message.WorldKey, region)) return;
            if (!SaveDataMgr.Instance.IsPersistedWeatherRegionReady(message.WorldKey, region))
            {
                if (!pendingRegions.TryGetValue(region, out var old) || message.Revision > old.Revision ||
                    message.Revision == old.Revision && message.SampleTime > old.SampleTime)
                    pendingRegions[region] = message;
                return;
            }
            ApplyClientRegionalWeatherMessage(message);
        }

        // 区域读盘未完成时只保留最新天气，持有区块退出后立即丢弃。
        private void MaintainPendingRegionalWeather()
        {
            pendingRegionKeys.Clear();
            pendingRegionKeys.AddRange(pendingRegions.Keys);
            foreach (Vector2Int region in pendingRegionKeys)
            {
                NetworkRegionalWeatherStateMessage message = pendingRegions[region];
                if (!CanApplyLocalWorld(message.WorldKey, message.RealmRevision) ||
                    !WeatherMgr.HasRegionalPersistenceLease(message.WorldKey, region))
                { pendingRegions.Remove(region); continue; }
                if (!SaveDataMgr.Instance.IsPersistedWeatherRegionReady(message.WorldKey, region)) continue;
                pendingRegions.Remove(region);
                ApplyClientRegionalWeatherMessage(message);
            }
        }

        private static void ApplyClientRegionalWeatherMessage(NetworkRegionalWeatherStateMessage message)
        {
            using var worldScope = GameplayWorldRegistry.UseWorld(message.WorldKey);
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
                new Int2(message.LightningCellX, message.LightningCellY),
                message.LightningStrength,
                message.RemainingSeconds);
            WeatherMgr.GetForWorld(message.WorldKey)?.ApplyReplicatedRegionalWeatherState(snapshot, message.InitialState);
        }

#endregion
    }
}
