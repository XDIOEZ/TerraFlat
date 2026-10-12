using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>陈列结果只保存稳定物品身份与普通数值，直接进入测试 JSON。</summary>
[Serializable]
public sealed class GameplayTestExhibit
{
    #region 陈列记录

    public string id;
    public string displayName;
    public int row;
    public int column;
    public float x;
    public float y;
    public int guid;
    public string backend;
    public bool success;
    public string error = string.Empty;

    #endregion
}

/// <summary>真实世界上的物品陈列厅，复用地形、资源实体、机器与掉落物的正式入口。</summary>
public sealed class GameplayTestShowroom : IDisposable
{
    #region 配置与运行状态

    private const int MinimumSlotSize = 8;
    private const float TerrainTimeoutSeconds = 90f;
    private const string ProfileMarker = "testing.showroom";
    private readonly List<GameplayTestExhibit> exhibits = new();
    private readonly List<ChunkLease> leases = new();
    private readonly List<Item> items = new();
    private readonly List<MachineEntity> machines = new();
    private readonly List<DroppedItemHandle> drops = new();
    private readonly List<ResourceExhibit> resources = new();
    private readonly CancellationTokenSource cancellation = new();
    private string worldName;
    private string bodyId;
    private bool configured;
    private bool building;
    private bool disposed;
    private int slotSize = MinimumSlotSize;

    private sealed class ResourceExhibit
    {
        public NaturalEntityHandle Handle;
        public RuntimeWorldAddress Address;
        public ChunkTilemapRenderer Owner;
    }

    public int ExpectedCount { get; private set; }
    public int PlacedCount { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsTerrainReady { get; private set; }
    public IReadOnlyList<GameplayTestExhibit> Exhibits => exhibits;

    #endregion

    #region 测试世界生成规则

    // 正式进入前登记，出生搜索与全部后续区块共用同一份测试地形规则。
    public void Configure(PlanetData planet)
    {
        if (disposed) throw new ObjectDisposedException(nameof(GameplayTestShowroom));
        if (configured) throw new InvalidOperationException("陈列厅已经配置，不能重复进入另一个世界。");
        if (planet == null || string.IsNullOrWhiteSpace(planet.Name))
            throw new ArgumentException("陈列厅必须指定有效的测试世界。", nameof(planet));
        worldName = planet.Name;
        bodyId = string.IsNullOrWhiteSpace(planet.BodyId) ? "earth" : planet.BodyId;
        if (GameRes.ExistingInstance != null)
            slotSize = ResolveSlotSize(CollectDefinitions(GameRes.ExistingInstance));
        WorldGenerationRuntimeHooks.BeforeWorldModelGeneration += ConfigureProfile;
        WorldGenerationRuntimeHooks.WorldModelTerrainDecorator += ApplyTerrain;
        configured = true;
    }

    private ChunkGenerationProfileSnapshot ConfigureProfile(ChunkGenerationProfileSnapshot profile)
    {
        PlanetData active = null;
        bool activeTestWorld = SaveDataMgr.Instance != null &&
            SaveDataMgr.Instance.TryGetActivePlanetData(out active) &&
            string.Equals(active.Name, worldName, StringComparison.Ordinal);
        if (!activeTestWorld && !string.Equals(GameManager.Instance?.ReadyPlanetData?.Name,
                worldName, StringComparison.Ordinal)) return profile;
        // 先合入真实星体参数，再写测试覆盖，避免后续冻结把水文开关重新打开。
        profile = FlatWorld.Spaceflight.SpaceCatalog.LoadDefault().ApplySurfaceProfile(
            activeTestWorld ? active.BodyId : bodyId, profile);
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in profile.NumericParameters) numbers.Add(pair.Key, pair.Value);
        numbers[ProfileMarker] = 1d;
        numbers["testing.slotSize"] = slotSize;
        numbers["river.enabled"] = 0d;
        numbers["lake.basin.enabled"] = 0d;
        numbers["lake.lava.enabled"] = 0d;
        numbers["structure.enabled"] = 0d;
        numbers["cave.portal.enabled"] = 0d;
        numbers["cave.portal.shrub.enabled"] = 0d;
        numbers["biome.snow.regions.enabled"] = 0d;
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in profile.TextParameters) texts.Add(pair.Key, pair.Value);
        texts["testing.worldName"] = worldName;
        return profile.WithGenerationConfiguration(numbers, texts, Array.Empty<CaveResourceRuleSnapshot>())
            .WithEcology(0d, Array.Empty<EcologySpawnRuleSnapshot>());
    }

    private static void ApplyTerrain(ChunkGenerationRequest request, ChunkTerrainBuffer terrain,
        CancellationToken token)
    {
        if (!request.Profile.NumericParameters.TryGetValue(ProfileMarker, out double marker) || marker <= 0d)
            return;
        int size = request.Profile.NumericParameters.TryGetValue("testing.slotSize", out double value)
            ? Math.Max(MinimumSlotSize, (int)value) : MinimumSlotSize;
        int baseTile = request.Profile.Settings.GroundTileId;
        int alternateTile = request.Profile.Settings.StoneTileId;
        if (baseTile <= 0 || alternateTile <= 0)
            throw new InvalidOperationException("陈列厅缺少正式地面地块定义。");
        for (int y = 0; y < terrain.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < terrain.Width; x++)
            {
                int worldX = request.Address.ChunkOrigin.X + x;
                int worldY = request.Address.ChunkOrigin.Y + y;
                int squareX = (int)Math.Floor(worldX / (double)size);
                int squareY = (int)Math.Floor(worldY / (double)size);
                int tile = ((squareX + squareY) & 1) == 0 ? baseTile : alternateTile;
                terrain.SetCell(x, y, new TerrainCell(tile, 0, 0, (int)SurfaceBiomeKind.Grassland,
                    1000, TerrainCellFlags.Walkable));
                terrain.SetLiquid(x, y, 0, 0f);
                terrain.SetGrass(x, y, ChunkTerrainData.GrassEmpty);
                terrain.SetEnvironmentValue("height", x, y, .5f);
                terrain.SetEnvironmentValue("temperature", x, y, .5f);
                terrain.SetEnvironmentValue("temperature.celsius", x, y, 26f);
                terrain.SetEnvironmentValue("precipitation", x, y, 0f);
                terrain.SetEnvironmentValue("basePrecipitation", x, y, 0f);
                terrain.SetEnvironmentValue("moisture", x, y, .35f);
                terrain.SetEnvironmentValue("airHumidity", x, y, .35f);
                terrain.SetEnvironmentValue("windX", x, y, 0f);
                terrain.SetEnvironmentValue("windY", x, y, 0f);
                terrain.SetEnvironmentValue("snow.depth", x, y, 0f);
            }
        }
    }

    #endregion

    #region 陈列目录与场地准备

    /// <summary>专项回归只申请玩家所在区块，不花时间准备全部物品陈列。</summary>
    public IEnumerator PrepareMinimal(Vector3 spawn, Action<string, string, bool, string> record)
    {
        if (disposed || !configured || building) throw new InvalidOperationException("最小测试地形状态无效。");
        building = true;
        ChunkMgr manager = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("测试区块管理器尚未就绪。");
        ChunkGenerationProfileSnapshot profile = manager.ActiveGenerationProfile
            ?? throw new InvalidOperationException("测试区块生成配置尚未就绪。");
        RuntimeWorldAddress address = manager.ResolveWorldAddress((Vector2)spawn);
        leases.Add(manager.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (DimensionManager.Instance != null)
            seed = DimensionManager.Instance.GetActiveGenerationSeed(seed);
        Task<ChunkRuntime> task = manager.RequestChunkDataAsync(address, seed, profile, cancellation.Token);
        float started = Time.realtimeSinceStartup;
        while (!disposed && !task.IsCompleted && Time.realtimeSinceStartup - started < TerrainTimeoutSeconds)
            yield return null;
        IsTerrainReady = !disposed && task.Status == TaskStatus.RanToCompletion &&
                         task.Result?.DataStatus == ChunkDataStatus.Ready && task.Result.Terrain != null;
        record("showroom.terrain", address.ToString(), IsTerrainReady,
            IsTerrainReady ? "玩家所在真实区块就绪。" :
                task.IsFaulted ? task.Exception.GetBaseException().ToString() : "最小测试地形加载失败或超时。");
    }

    public IEnumerator Build(PlanetData planet, Action<string, string, bool, string> record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (disposed || !configured || building)
            throw new InvalidOperationException("陈列厅未配置、已释放或正在构建。");
        if (planet == null || !string.Equals(planet.Name, worldName, StringComparison.Ordinal))
            throw new InvalidOperationException("陈列厅构建的星球与配置世界不一致。");
        building = true;
        GameRes catalog = GameRes.ExistingInstance ?? throw new InvalidOperationException("物品目录尚未就绪。");
        ChunkMgr manager = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("区块管理器尚未就绪。");
        Player player = FindLocalPlayer();
        Vector3 spawn = player.itemData.transform.position;
        List<RuntimeItemDefinition> definitions = CollectDefinitions(catalog);
        ExpectedCount = definitions.Count;
        slotSize = ResolveSlotSize(definitions);
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ExpectedCount)));
        int originX = Mathf.FloorToInt(spawn.x) + slotSize;
        int originY = Mathf.FloorToInt(spawn.y) + slotSize;
        for (int i = 0; i < definitions.Count; i++)
        {
            exhibits.Add(new GameplayTestExhibit
            {
                id = definitions[i].Id, displayName = definitions[i].SourceDisplayName,
                row = i / columns, column = i % columns,
                x = originX + (i % columns) * slotSize + .5f,
                y = originY + (i / columns) * slotSize + .5f
            });
        }
        record("showroom", "catalog", ExpectedCount > 0,
            $"非生物物品 {ExpectedCount} 种，{columns} 列，陈列格间隔 {slotSize} 格；玩家出生点保持不变。");
        yield return PrepareTerrain(manager, spawn, originX, originY, columns, record);
        for (int i = 0; i < definitions.Count && !disposed; i++)
        {
            GameplayTestExhibit exhibit = exhibits[i];
            try
            {
                RuntimeWorldAddress address = manager.ResolveWorldAddress(new Vector2(exhibit.x, exhibit.y));
                if (!manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                    chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
                    throw new InvalidOperationException("陈列格的真实地形尚未就绪。");
                SpawnExhibit(definitions[i], exhibit, address);
                exhibit.success = true;
                PlacedCount++;
            }
            catch (Exception exception)
            {
                exhibit.error = exception.ToString();
                Debug.LogException(exception);
            }
            record("showroom.item", exhibit.id, exhibit.success,
                exhibit.success ? $"{exhibit.backend} ({exhibit.x}, {exhibit.y})" : exhibit.error);
            if ((i & 7) == 7) yield return null;
        }
        UpdatePresentation();
        IsComplete = !disposed && IsTerrainReady && ExpectedCount > 0 && PlacedCount == ExpectedCount;
        record("showroom", "complete", IsComplete, $"实际放置 {PlacedCount}/{ExpectedCount} 件。");
    }

    private IEnumerator PrepareTerrain(ChunkMgr manager, Vector3 spawn, int originX, int originY,
        int columns, Action<string, string, bool, string> record)
    {
        ChunkGenerationProfileSnapshot profile = manager.ActiveGenerationProfile;
        if (profile == null) throw new InvalidOperationException("正式世界生成配置尚未冻结。");
        var targets = new Dictionary<RuntimeWorldAddress, Task<ChunkRuntime>>();
        int rows = Math.Max(1, (ExpectedCount + columns - 1) / columns);
        int minX = Mathf.FloorToInt(spawn.x) - slotSize;
        int minY = Mathf.FloorToInt(spawn.y) - slotSize;
        int maxX = originX + columns * slotSize;
        int maxY = originY + rows * slotSize;
        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (DimensionManager.Instance != null)
            seed = DimensionManager.Instance.GetActiveGenerationSeed(seed);
        RuntimeWorldAddress first = manager.ResolveWorldAddress(new Vector2(minX, minY));
        for (int y = first.ChunkOrigin.Y; y <= maxY; y += profile.Height)
        for (int x = first.ChunkOrigin.X; x <= maxX; x += profile.Width)
        {
            RuntimeWorldAddress address = manager.ResolveWorldAddress(new Vector2(x, y));
            if (targets.ContainsKey(address)) continue;
            leases.Add(manager.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
            targets.Add(address, manager.RequestChunkDataAsync(address, seed, profile, cancellation.Token));
        }
        float started = Time.realtimeSinceStartup;
        while (!disposed && !AllFinished(targets) && Time.realtimeSinceStartup - started < TerrainTimeoutSeconds)
            yield return null;
        bool ready = !disposed;
        foreach (var target in targets)
        {
            Task<ChunkRuntime> task = target.Value;
            bool success = task.Status == TaskStatus.RanToCompletion &&
                task.Result?.DataStatus == ChunkDataStatus.Ready && task.Result.Terrain != null;
            ready &= success;
            if (!success)
                record("showroom.terrain", target.Key.ToString(), false,
                    task.IsFaulted ? task.Exception.GetBaseException().ToString() : "场地生成取消或超过 90 秒。");
        }
        IsTerrainReady = ready;
        record("showroom", "terrain", ready, $"真实场地包含 {targets.Count} 个区块。");
    }

    private static bool AllFinished(Dictionary<RuntimeWorldAddress, Task<ChunkRuntime>> targets)
    {
        foreach (Task<ChunkRuntime> task in targets.Values)
            if (!task.IsCompleted) return false;
        return true;
    }

    private static List<RuntimeItemDefinition> CollectDefinitions(GameRes catalog)
    {
        var definitions = new List<RuntimeItemDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeItemDefinition definition in catalog.ItemDefinitions.Values)
            if (definition != null && !definition.IsActor && ids.Add(definition.Id)) definitions.Add(definition);
        definitions.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return definitions;
    }

    private static int ResolveSlotSize(List<RuntimeItemDefinition> definitions)
    {
        int size = MinimumSlotSize;
        foreach (RuntimeItemDefinition definition in definitions)
        foreach (GridCellOffset cell in definition.WorldGridOccupancy)
            size = Math.Max(size, Math.Max(Math.Abs(cell.X), Math.Abs(cell.Y)) * 2 + 4);
        return size;
    }

    private static Player FindLocalPlayer()
    {
        Player player = ItemMgr.GetInstance()?.User_Player;
        if (player != null && player.IsLocalProfile) return player;
        throw new InvalidOperationException("真实本地玩家尚未进入测试世界。");
    }

    #endregion

    #region 正式实体后端

    private void SpawnExhibit(RuntimeItemDefinition definition, GameplayTestExhibit exhibit,
        RuntimeWorldAddress address)
    {
        ItemData data = definition.CreateItemData();
        data.Guid = ItemMgr.GetInstance().GenerateGuid();
        data.transform ??= new ItemTransform();
        data.transform.position = new Vector3(exhibit.x, exhibit.y);
        data.transform.rotation = Quaternion.identity;
        data.transform.scale = Vector3.one;
        data.inHand = false;
        exhibit.guid = data.Guid;
        if (definition.UsesResourceEntities)
        {
            exhibit.backend = "resource";
            if (!NaturalEntityEcsService.TryRegister(definition, data.Guid, data.transform.position,
                    26f, 0f, address.DimensionId, data, out NaturalEntityHandle handle))
                throw new InvalidOperationException("资源实体未能创建，禁止退回普通物品。");
            resources.Add(new ResourceExhibit { Handle = handle, Address = address });
            return;
        }
        if (MachineWorld.OwnsWorldItem(data))
        {
            exhibit.backend = "machine";
            MachineEntity machine = MachineWorld.SpawnGenerated(data, data.transform.position)
                ?? throw new InvalidOperationException("正式机器生成入口没有返回实体。");
            machines.Add(machine);
            return;
        }
        if (data.Stack?.CanBePickedUp == true)
        {
            exhibit.backend = definition.UsesLightweightWorldDrop ? "drop.passive" : "drop.interactive";
            data.Stack.Amount = 1f;
            DroppedItemHandle handle = DroppedItemService.Spawn(data, data.transform.position,
                destination: data.transform.position, randomizeRotation: false);
            if (!handle.IsValid) throw new InvalidOperationException("正式掉落物入口没有返回有效实体。");
            exhibit.guid = handle.Legacy != null ? handle.Legacy.itemData.Guid : handle.Id;
            drops.Add(handle);
            return;
        }
        exhibit.backend = "item";
        Item item = null;
        try
        {
            item = ItemMgr.GetInstance().InstantiateItem(data, data.transform.position,
                Quaternion.identity, Vector3.one);
            if (item == null) throw new InvalidOperationException("正式物品入口没有返回实例。");
            item.Load();
            if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) &&
                building.Role == BuildingRole.PlacedBuilding)
                item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.SetAsInstalled();
            items.Add(item);
        }
        catch
        {
            if (item != null) ItemMgr.GetInstance().DespawnItem(item, false);
            throw;
        }
    }

    // 玩家走进远处展区时接续真实资源表现，显示卸载不会删掉陈列数据。
    public void UpdatePresentation()
    {
        if (disposed || resources.Count == 0 || ChunkMgr.ExistingInstance == null) return;
        foreach (ResourceExhibit resource in resources)
        {
            ChunkTilemapRenderer owner = null;
            if (ChunkMgr.ExistingInstance.TryGetRuntimeChunkPresentationView(resource.Address, out ChunkView view) &&
                view.IsBaseTerrainPresented)
                owner = view.GetComponentInChildren<ChunkTilemapRenderer>(true);
            if (ReferenceEquals(resource.Owner, owner)) continue;
            resource.Owner = owner;
            try { NaturalEntityEcsService.BindPresentation(resource.Handle, owner); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }

    #endregion

    #region 世界退出清理

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (configured)
        {
            WorldGenerationRuntimeHooks.BeforeWorldModelGeneration -= ConfigureProfile;
            WorldGenerationRuntimeHooks.WorldModelTerrainDecorator -= ApplyTerrain;
        }
        cancellation.Cancel();
        foreach (ResourceExhibit resource in resources)
            Cleanup(() => NaturalEntityEcsService.Remove(resource.Handle));
        foreach (DroppedItemHandle drop in drops) Cleanup(() => DroppedItemService.Remove(drop));
        foreach (Item item in items)
            if (item != null && ItemMgr.GetInstance() != null)
                Cleanup(() => ItemMgr.GetInstance().DespawnItem(item, false));
        foreach (MachineEntity machine in machines)
            Cleanup(() => { if (ReferenceEquals(MachineWorld.GetById(machine.Id), machine)) MachineWorld.Remove(machine.Id); });
        foreach (ChunkLease lease in leases) Cleanup(lease.Dispose);
        cancellation.Dispose();
        resources.Clear(); drops.Clear(); items.Clear(); machines.Clear(); leases.Clear();
    }

    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    #endregion
}
