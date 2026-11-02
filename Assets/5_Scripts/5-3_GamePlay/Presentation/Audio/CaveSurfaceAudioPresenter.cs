using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using Unity.Mathematics;
using UnityEngine;

/// <summary>把地表的天气、自然声和空间音效经开放洞口传入本机玩家所在的矿洞。</summary>
public sealed class CaveSurfaceAudioPresenter : IDisposable
{
    #region 洞口路由与声音状态

    private const float WeatherSoundRange = 48f;
    private const float WeatherSampleInterval = 0.25f;
    private const float WeatherSilenceThreshold = 0.001f;
    private const float ThunderSpeed = 64f;
    private const float PortalRefreshDistance = 4f;
    private const float PortalSearchRange = 512f;
    private const int MaximumNearbyPortals = 16;
    private readonly List<CaveExitLink> savedPortals = new();
    private readonly List<AudioPortalLink> audioPortals = new();
    private readonly float[] audioPortalDistanceSq = new float[MaximumNearbyPortals];
    private readonly List<PendingThunder> pendingThunder = new();
    private readonly NaturalAmbientAudioPresenter surfaceNature = new();
    private WeatherMgr.RainAudioState rain;
    private WeatherMgr.WindAudioState wind;
    private WeatherMgr surfaceWeather;
    private AudioHandle rainHandle, windHandle, galeHandle;
    private GameSaveData cachedSave;
    private string listenerWorldKey, surfaceWorldKey;
    private long cachedRevision = -1;
    private Player localPlayer;
    private float sampleElapsed;
    private float sampledRain, sampledWind;
    private bool hasRainSample;
    private Vector2 sampledEntrance;
    private Vector2 routingCenter;
    private bool hasRoutingCenter;

    private readonly struct PendingThunder
    {
        public readonly Vector2 Position;
        public readonly float Strength;
        public readonly float Distance;
        public readonly float PlayTime;
        public PendingThunder(Vector2 position, float strength, float distance, float playTime)
        {
            Position = position;
            Strength = strength;
            Distance = distance;
            PlayTime = playTime;
        }
    }

    #endregion

    #region 本机听者与地表声场

    public void Advance(Player player, float deltaTime)
    {
        if (player == null || !player.IsLocalProfile || !player.isActiveAndEnabled)
        {
            if (listenerWorldKey != null) Dispose();
            return;
        }

        string currentWorld = GameplayWorldRegistry.GetItemWorldKey(player);
        if (string.IsNullOrEmpty(currentWorld))
        {
            Dispose();
            return;
        }

        WorldAddress current = WorldAddress.FromWorldKey(currentWorld);
        AudioService audio = AudioService.Instance;
        if (current.DimensionId != WorldAddress.CaveDimensionId)
        {
            ReleaseSurfaceSound();
            if (listenerWorldKey != currentWorld)
            {
                audio.SetWorldAcousticContext(currentWorld, null, null);
                listenerWorldKey = currentWorld;
            }
            return;
        }

        GameSaveData save = SaveDataMgr.GetExistingInstance()?.SaveData;
        long revision = DimensionTravelProgressStore.GetCaveExitLightingRevision(save);
        if (listenerWorldKey != currentWorld || !ReferenceEquals(cachedSave, save) || cachedRevision != revision)
        {
            ReleaseSurfaceSound();
            savedPortals.Clear();
            audioPortals.Clear();
            DimensionTravelProgressStore.CollectOpenCaveExitLinks(save, current, savedPortals);
            surfaceWorldKey = current.WithDimension(WorldAddress.SurfaceDimensionId).WorldKey;
            listenerWorldKey = currentWorld;
            cachedRevision = revision;
            cachedSave = save;
            localPlayer = player;
            sampleElapsed = WeatherSampleInterval;
            hasRoutingCenter = false;
        }

        localPlayer = player;

        Vector2 listenerPosition = WorldLocalPresentation.ToLogical(player.transform.position);
        if (!hasRoutingCenter ||
            WorldTopologyRuntime.ShortestDelta(listenerPosition, routingCenter).sqrMagnitude >= PortalRefreshDistance * PortalRefreshDistance)
            RefreshNearbyPortals(audio, currentWorld, listenerPosition);
        int nearest = -1;
        float nearestDistance = WeatherSoundRange;
        for (int i = 0; i < audioPortals.Count; i++)
        {
            float distance = WorldTopologyRuntime.ShortestDelta(listenerPosition, audioPortals[i].CavePosition).magnitude;
            if (distance >= nearestDistance) continue;
            nearest = i;
            nearestDistance = distance;
        }
        if (nearest < 0)
        {
            ReleaseSurfaceSound();
            return;
        }

        Vector2 entrance = audioPortals[nearest].SurfacePosition;
        if (entrance != sampledEntrance)
        {
            StopWeatherLoops();
            rain.Reset();
            wind.Reset();
            sampledEntrance = entrance;
            sampleElapsed = WeatherSampleInterval;
        }
        BindSurfaceWeather();
        if (GameplayWorldRegistry.TryGet(surfaceWorldKey, out _))
            surfaceNature.AdvanceFromSurfacePortal(player, deltaTime, surfaceWorldKey, entrance);
        else
            surfaceNature.Dispose();

        sampleElapsed += Mathf.Max(0f, deltaTime);
        if (sampleElapsed >= WeatherSampleInterval)
        {
            sampleElapsed = 0f;
            SampleSurfaceWeather(entrance);
        }
        rain.Advance(hasRainSample, sampledRain, deltaTime, 12f);
        wind.Advance(sampledWind, deltaTime, 3f);
        UpdateWeatherLoop(AudioEventIds.WeatherRainLoop, rain.Volume, entrance, ref rainHandle);
        UpdateWeatherLoop(AudioEventIds.WeatherWindLoop, wind.NormalVolume, entrance, ref windHandle);
        UpdateWeatherLoop(AudioEventIds.WeatherWindGaleLoop, wind.GaleVolume, entrance, ref galeHandle);
        PlayPendingThunder();
    }

    // 每移动四格最多重选十六个附近洞口，避免每个声音遍历整份存档。
    private void RefreshNearbyPortals(AudioService audio, string currentWorld, Vector2 listenerPosition)
    {
        audioPortals.Clear();
        float maxDistanceSq = PortalSearchRange * PortalSearchRange;
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        float2 origin = new(listenerPosition.x, listenerPosition.y);
        for (int i = 0; i < savedPortals.Count; i++)
        {
            CaveExitLink candidate = savedPortals[i];
            float distanceSq = topology.SqrDistance(origin,
                new float2(candidate.CavePosition.x, candidate.CavePosition.y));
            if (distanceSq >= maxDistanceSq) continue;
            int insertIndex = 0;
            while (insertIndex < audioPortals.Count && audioPortalDistanceSq[insertIndex] <= distanceSq)
                insertIndex++;
            if (insertIndex >= MaximumNearbyPortals) continue;
            int lastIndex = Mathf.Min(audioPortals.Count, MaximumNearbyPortals - 1);
            for (int j = lastIndex; j > insertIndex; j--)
                audioPortalDistanceSq[j] = audioPortalDistanceSq[j - 1];
            audioPortalDistanceSq[insertIndex] = distanceSq;
            if (audioPortals.Count == MaximumNearbyPortals)
                audioPortals.RemoveAt(MaximumNearbyPortals - 1);
            audioPortals.Insert(insertIndex, new AudioPortalLink(candidate.SurfacePosition, candidate.CavePosition));
        }
        audio.SetWorldAcousticContext(currentWorld, surfaceWorldKey, audioPortals);
        routingCenter = listenerPosition;
        hasRoutingCenter = true;
    }

    private void BindSurfaceWeather()
    {
        WeatherMgr next = null;
        if (GameplayWorldRegistry.TryGet(surfaceWorldKey, out _))
        {
            using var scope = GameplayWorldRegistry.UseWorld(surfaceWorldKey);
            next = WeatherMgr.GetForWorld(surfaceWorldKey);
        }
        if (surfaceWeather == next) return;
        if (surfaceWeather != null)
            surfaceWeather.LightningOccurred -= OnSurfaceLightning;
        surfaceWeather = next;
        if (surfaceWeather != null)
            surfaceWeather.LightningOccurred += OnSurfaceLightning;
    }

    private void SampleSurfaceWeather(Vector2 entrance)
    {
        hasRainSample = false;
        sampledWind = 0f;
        if (surfaceWeather == null || !GameplayWorldRegistry.TryGet(surfaceWorldKey, out _)) return;
        using var scope = GameplayWorldRegistry.UseWorld(surfaceWorldKey);
        hasRainSample = surfaceWeather.TryGetPrecipitationAt(entrance, out sampledRain, out _);
        if (surfaceWeather.TryGetWindBlendAt(entrance, out var blend))
            sampledWind = Mathf.Clamp01(Unity.Mathematics.math.length(blend.Wind));
    }

    private void UpdateWeatherLoop(string cueId, float volume, Vector2 entrance, ref AudioHandle handle)
    {
        if (volume <= WeatherSilenceThreshold)
        {
            handle.Stop();
            handle = AudioHandle.Invalid;
            return;
        }
        Vector3 position = WorldLocalPresentation.ProjectPosition(entrance, localPlayer.transform.position);
        AudioService audio = AudioService.Instance;
        if (!handle.IsPlaying)
        {
            AudioPlayOptions options = AudioPlayOptions.At(position, volume);
            options.SourceWorldKey = surfaceWorldKey;
            options.ForceSpatial = true;
            options.SpatialMaxDistanceOverride = WeatherSoundRange;
            options.OverrideLoop = true;
            options.Loop = true;
            handle = audio.Play(cueId, options);
        }
        audio.SetWorldPosition(handle, position);
        audio.SetVolumeScale(handle, volume);
    }

    #endregion

    #region 洞外雷声

    private void OnSurfaceLightning(RegionalLightningEvent lightning)
    {
        if (localPlayer == null || lightning.WorldKey != surfaceWorldKey || pendingThunder.Count >= 8)
            return;
        Vector2 listener = WorldLocalPresentation.ToLogical(localPlayer.transform.position);
        float distance = float.PositiveInfinity;
        for (int i = 0; i < audioPortals.Count; i++)
            distance = Mathf.Min(distance,
                WorldTopologyRuntime.ShortestDelta(lightning.Position, audioPortals[i].SurfacePosition).magnitude +
                WorldTopologyRuntime.ShortestDelta(listener, audioPortals[i].CavePosition).magnitude);
        if (distance >= 192f || lightning.Strength <= 0f) return;
        pendingThunder.Add(new PendingThunder(lightning.Position, lightning.Strength, distance,
            Time.unscaledTime + distance / ThunderSpeed));
    }

    private void PlayPendingThunder()
    {
        for (int i = pendingThunder.Count - 1; i >= 0; i--)
        {
            PendingThunder thunder = pendingThunder[i];
            if (Time.unscaledTime < thunder.PlayTime) continue;
            pendingThunder.RemoveAt(i);
            if (localPlayer == null) continue;
            AudioPlayOptions options = AudioPlayOptions.At(
                WorldLocalPresentation.ProjectPosition(thunder.Position, localPlayer.transform.position),
                Mathf.Clamp01(thunder.Strength));
            options.SourceWorldKey = surfaceWorldKey;
            AudioService.Instance.Play(
                thunder.Distance <= 64f
                    ? AudioEventIds.WeatherThunder : AudioEventIds.WeatherThunderFar, options);
        }
    }

    #endregion

    #region 退出及资源回收

    private void StopWeatherLoops()
    {
        rainHandle.Stop(0.25f);
        windHandle.Stop(0.25f);
        galeHandle.Stop(0.25f);
        rainHandle = windHandle = galeHandle = AudioHandle.Invalid;
    }

    private void ReleaseSurfaceSound()
    {
        StopWeatherLoops();
        surfaceNature.Dispose();
        if (surfaceWeather != null)
            surfaceWeather.LightningOccurred -= OnSurfaceLightning;
        surfaceWeather = null;
        localPlayer = null;
        pendingThunder.Clear();
        rain.Reset();
        wind.Reset();
        sampledRain = sampledWind = 0f;
        hasRainSample = false;
        sampledEntrance = default;
    }

    public void Dispose()
    {
        bool ownsAcousticContext = listenerWorldKey != null;
        ReleaseSurfaceSound();
        savedPortals.Clear();
        audioPortals.Clear();
        listenerWorldKey = surfaceWorldKey = null;
        cachedSave = null;
        cachedRevision = -1;
        hasRoutingCenter = false;
        if (ownsAcousticContext && AudioService.HasInstance)
            AudioService.Instance.SetWorldAcousticContext(null, null, null);
    }

    #endregion
}
