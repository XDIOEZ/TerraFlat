using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>本机听者附近的自然声景；只持有树叶和河流两条聚合循环。</summary>
public sealed class NaturalAmbientAudioPresenter : IDisposable
{
    #region 声景状态
    public const string FoliageCueId = "ambient.foliage.rustle.loop";
    public const string RiverCueId = "ambient.river.flow.loop";
    public const string FoliageSourceTag = "NatureAudio.Foliage";

    private sealed class AmbientLoop
    {
        public AudioCue Cue;
        public AudioHandle Handle;
        public float Volume;
        public Vector2 Position;
    }

    private sealed class RiverChunk
    {
        public ChunkTerrainData Terrain;
        public RiverAmbientAudioChunkIndex Index;
        public long Revision;
    }

    private readonly AmbientLoop foliage = new(), river = new();
    private readonly Dictionary<RuntimeWorldAddress, RiverChunk> riverChunks = new();
    private readonly HashSet<RuntimeWorldAddress> desiredChunks = new();
    private readonly List<RuntimeWorldAddress> removedChunks = new();
    private WorldRuntime world;
    private ChunkMgr manager;
    private string worldKey;
    private IDisposable committed, evicted;
    private RuntimeWorldAddress? listenerChunk;
    private Vector2Int? listenerCell;
    private ulong spatialRevision;
    private bool riverDirty, windowDirty;
    private int nearbyFoliageCount;
    private float riverStrength;
    private Vector2 foliagePosition, riverPosition;

    public float PresentedFoliageVolume => foliage.Volume;
    public float PresentedRiverVolume => river.Volume;
    public int ActiveLoopCount => (foliage.Handle.IsPlaying ? 1 : 0) + (river.Handle.IsPlaying ? 1 : 0);
    #endregion

    #region 本机世界与音量推进
    public void Advance(Player player, float deltaTime)
    {
        if (player == null || !player.IsLocalProfile || !player.isActiveAndEnabled ||
            !GameplayWorldRegistry.TryGet(GameplayWorldRegistry.GetItemWorldKey(player), out var context) ||
            context.Chunks == null || context.Chunks.IsWorldRuntimeShuttingDown || context.Chunks.WorldRuntime == null)
        { Dispose(); return; }

        if (!ReferenceEquals(world, context.Chunks.WorldRuntime) || manager != context.Chunks)
            BindWorld(context);
        using var scope = worldKey != GameplayWorldRegistry.ExecutionWorldKey ? GameplayWorldRegistry.UseWorld(worldKey) : null;
        if (deltaTime <= 0f) return;

        Vector2 listener = WorldLocalPresentation.ToLogical(player.transform.position);
        RuntimeWorldAddress address = manager.ResolveWorldAddress(listener);
        if (!listenerChunk.HasValue || listenerChunk.Value != address || windowDirty)
        {
            RefreshNearbyRiverChunks(address);
            listenerChunk = address;
            windowDirty = false;
        }

        Vector2Int cell = Vector2Int.FloorToInt(listener);
        bool moved = !listenerCell.HasValue || listenerCell.Value != cell;
        if (moved || spatialRevision != NaturalEntityEcsService.SpatialRevision)
        {
            nearbyFoliageCount = NaturalEntityEcsService.FindNearestTaggedAmbientSource(listener,
                foliage.Cue != null ? foliage.Cue.MaxDistance : 0f, FoliageSourceTag, out foliagePosition);
            spatialRevision = NaturalEntityEcsService.SpatialRevision;
        }
        foreach (RiverChunk chunk in riverChunks.Values)
        {
            if (chunk.Revision == chunk.Index.Revision) continue;
            chunk.Revision = chunk.Index.Revision;
            riverDirty = true;
        }
        if (moved || riverDirty) SelectNearestRiver(listener);
        listenerCell = cell;

        float wind = 0f;
        WeatherMgr weather = WeatherMgr.GetForWorld(worldKey);
        if (nearbyFoliageCount > 0 && weather != null && weather.TryGetWindBlendAt(foliagePosition, out var blend))
            wind = math.length(blend.Wind);
        AdvanceLoop(foliage, foliagePosition, ResolveFoliageVolume(nearbyFoliageCount, wind), player, deltaTime);
        AdvanceLoop(river, riverPosition, riverStrength, player, deltaTime);
    }

    // 没有叶簇或风时静音，密度和风强分别平滑增强，不制造与场景无关的底噪。
    public static float ResolveFoliageVolume(int nearbySourceCount, float windStrength) =>
        Mathf.Sqrt(Mathf.InverseLerp(0.03f, 0.8f, windStrength)) * Mathf.Clamp01(nearbySourceCount / 4f);

    private static void AdvanceLoop(AmbientLoop loop, Vector2 position, float target, Player player, float deltaTime)
    {
        loop.Volume = Mathf.MoveTowards(loop.Volume, target, deltaTime / 1.2f);
        if (loop.Volume <= 0.0001f || loop.Cue == null)
        { loop.Handle.Stop(); loop.Handle = AudioHandle.Invalid; return; }
        if (target > 0f) loop.Position = position;
        Vector2 presented = WorldLocalPresentation.ProjectPosition(loop.Position, player.transform.position);
        AudioService service = AudioService.Instance;
        if (!loop.Handle.IsPlaying)
            loop.Handle = service.Play(loop.Cue, AudioPlayOptions.At(presented, loop.Volume));
        service.SetWorldPosition(loop.Handle, presented);
        service.SetVolumeScale(loop.Handle, loop.Volume);
    }
    #endregion

    #region 河流候选与流送增量
    private void BindWorld(GameplayWorldRuntime context)
    {
        Dispose();
        manager = context.Chunks;
        world = manager.WorldRuntime;
        worldKey = context.WorldKey;
        AudioService.Instance.TryGetCue(FoliageCueId, out foliage.Cue);
        AudioService.Instance.TryGetCue(RiverCueId, out river.Cue);
        committed = world.Events.Subscribe<ChunkCommitted>(change =>
        { if (desiredChunks.Contains(change.Address)) windowDirty = true; });
        evicted = world.Events.Subscribe<ChunkEvicted>(change => RemoveRiverChunk(change.Address));
        spatialRevision = ulong.MaxValue;
        windowDirty = riverDirty = true;
    }

    private static bool IsWaterContactLiquid(string id) => GameRes.ExistingInstance != null &&
        GameRes.ExistingInstance.TryGetLiquidDefinition(id, out LiquidDefinition liquid) && liquid.WorldWater?.WaterContact == true;

    private void RefreshNearbyRiverChunks(RuntimeWorldAddress center)
    {
        desiredChunks.Clear();
        int width = manager.ActiveGenerationProfile.Width, height = manager.ActiveGenerationProfile.Height;
        float range = river.Cue != null ? river.Cue.MaxDistance : 0f;
        int reachX = Mathf.CeilToInt(range / width), reachY = Mathf.CeilToInt(range / height);
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        for (int y = -reachY; y <= reachY; y++)
        for (int x = -reachX; x <= reachX; x++)
        {
            var address = new RuntimeWorldAddress(center.DimensionId, new Int2(
                topology.NormalizeX(center.ChunkOrigin.X + x * width), topology.NormalizeY(center.ChunkOrigin.Y + y * height)));
            if (!desiredChunks.Add(address)) continue;
            if (!world.TryGetChunk(address, out ChunkRuntime chunk) || chunk.DataStatus != ChunkDataStatus.Ready ||
                chunk.Terrain == null || chunk.Terrain.IsDisposed) continue;
            if (riverChunks.TryGetValue(address, out RiverChunk current) && ReferenceEquals(current.Terrain, chunk.Terrain)) continue;
            RemoveRiverChunk(address);
            riverChunks.Add(address, new RiverChunk { Terrain = chunk.Terrain,
                Index = new RiverAmbientAudioChunkIndex(chunk.Terrain, IsWaterContactLiquid) });
            riverDirty = true;
        }
        removedChunks.Clear();
        foreach (RuntimeWorldAddress address in riverChunks.Keys)
            if (!desiredChunks.Contains(address)) removedChunks.Add(address);
        foreach (RuntimeWorldAddress address in removedChunks) RemoveRiverChunk(address);
    }

    private void SelectNearestRiver(Vector2 listener)
    {
        riverStrength = 0f;
        float nearest = river.Cue != null ? river.Cue.MaxDistance : 0f;
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        foreach (var pair in riverChunks)
        {
            Vector2 origin = new(pair.Key.ChunkOrigin.X, pair.Key.ChunkOrigin.Y);
            float2 delta = topology.ShortestDelta(new float2(origin.x, origin.y), new float2(listener.x, listener.y));
            Vector2 localListener = new(delta.x, delta.y);
            if (!pair.Value.Index.TryFindNearest(localListener, nearest, out Vector2 localPosition, out float strength)) continue;
            nearest = Vector2.Distance(localListener, localPosition);
            riverPosition = origin + localPosition;
            riverStrength = strength;
        }
        riverDirty = false;
    }

    private void RemoveRiverChunk(RuntimeWorldAddress address)
    {
        if (!riverChunks.Remove(address, out RiverChunk chunk)) return;
        chunk.Index.Dispose();
        riverDirty = true;
    }
    #endregion

    #region 生命周期清理
    public void Dispose()
    {
        foliage.Handle.Stop(); river.Handle.Stop();
        foliage.Handle = river.Handle = AudioHandle.Invalid;
        foliage.Volume = river.Volume = 0f;
        foliage.Cue = river.Cue = null;
        committed?.Dispose(); evicted?.Dispose();
        committed = evicted = null;
        foreach (RiverChunk chunk in riverChunks.Values) chunk.Index.Dispose();
        riverChunks.Clear();
        desiredChunks.Clear(); removedChunks.Clear();
        listenerChunk = null; listenerCell = null;
        world = null; manager = null; worldKey = null;
        nearbyFoliageCount = 0; riverStrength = 0f;
    }
    #endregion
}
