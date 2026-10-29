using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// ChunkMgr 的生态配置与状态存档扩展。
/// 纯生成器只负责返回确定性基线；这里负责读取冻结配置、捕获主线程 Item 状态以及维护删除差量。
/// </summary>
public partial class ChunkMgr
{
    /// <summary>自然生成点被实际移除后的通知；图层观察者按地址和 GUID 增量刷新。</summary>
    public event Action<RuntimeWorldAddress, int> NaturalItemRemoved;

    #region 世界生成配置冻结

    private readonly WorldGenerationProfileCache activeWorldGenerationCache = new();
    private readonly WorldGenerationProfileCache portalSourceGenerationCache = new();
    private ChunkGenerationProfileSnapshot pairedCaveSource, pairedSurfaceSource, pairedGenerationProfile;
    private DeterministicChunkGenerator pairedGenerator;
    private ChunkGenerationTopologySnapshot pairedTopology;
    private string pairedDimension;
    private int pairedSeed;

    // 订阅者可以依赖外部可变状态，有 Hook 时直接走正式构建链。
    internal static ChunkGenerationProfileSnapshot GetCachedWorldGenerationProfile(
        WorldGenerationProfileCache cache, PlanetData planet, int seed,
        out ChunkGenerationProfileSnapshot currentTileCatalog)
    {
        if (WorldGenerationRuntimeHooks.HasProfileSubscribers)
        {
            cache.Clear();
            return CreateFrozenWorldGenerationProfile(planet, out currentTileCatalog);
        }

        var save = SaveDataMgr.Instance?.SaveData;
        var resources = GameRes.ExistingInstance;
        DimensionManager.Instance.TryGetDefinitionForWorldKey(planet.Name, out DimensionDefinition definition);
        var inputs = new WorldGenerationProfileCache.Inputs(planet, save,
            FlatWorld.Spaceflight.SpaceCatalog.LoadDefault(), resources, resources?.ResourceReloadVersion ?? 0,
            NaturalGenerationRuleCatalogService.Catalog, definition, seed,
            save?.UsesFrozenWorldGenerationConfiguration != false, GameNetwork.HasStateAuthority);
        if (cache.TryGet(inputs, out var profile, out currentTileCatalog)) return profile;

        EcologyWorldSaveData before = planet.Ecology;
        int beforeVersion = before?.DataVersion ?? 0;
        string beforeProfile = before?.ProfileId;
        bool beforeHasConfiguration = before?.HasConfiguration ?? false;
        profile = CreateFrozenWorldGenerationProfile(planet, out currentTileCatalog);
        // 首次冻结后的下一次调用需先恢复存档配置，再保存可复用的结果。
        if (ReferenceEquals(before, planet.Ecology) && beforeVersion == (planet.Ecology?.DataVersion ?? 0) &&
            beforeProfile == planet.Ecology?.ProfileId &&
            beforeHasConfiguration == (planet.Ecology?.HasConfiguration ?? false))
            cache.Store(inputs, profile, currentTileCatalog);
        else
            cache.Clear();
        return profile;
    }

    // 冷采样、出生查询与正式流送共用世界实例的最终生成配置。
    public static ChunkGenerationProfileSnapshot CreateFrozenWorldGenerationProfile(PlanetData planet) =>
        CreateFrozenWorldGenerationProfile(planet, out _);

    private static ChunkGenerationProfileSnapshot CreateFrozenWorldGenerationProfile(PlanetData planet,
        out ChunkGenerationProfileSnapshot currentTileCatalog)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        DimensionManager.Instance.TryGetDefinitionForWorldKey(planet.Name, out DimensionDefinition definition);
        string presetId = string.IsNullOrWhiteSpace(planet.GenerationPresetId)
            ? "surface.default" : planet.GenerationPresetId;
        ChunkGenerationProfileSnapshot profile = FlatWorld.Spaceflight.SpaceCatalog.LoadDefault()
            .CreateGenerationProfile(presetId);
        profile = ApplyWorldChunkSize(profile, planet);
        profile = ApplyWorldSpatialDistanceScale(profile, planet);
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        currentTileCatalog = profile;
        profile = ApplyPersistedEcologyConfiguration(profile, planet);
        // 实例覆盖优先于冻结预设，入口连接取世界定义而不从地形算法推断。
        var numbers = new Dictionary<string, double>(profile.NumericParameters, StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(profile.TextParameters, StringComparer.Ordinal);
        if (planet.GenerationNumericOverrides != null)
            foreach (var pair in planet.GenerationNumericOverrides) numbers[pair.Key] = pair.Value;
        if (planet.GenerationTextOverrides != null)
            foreach (var pair in planet.GenerationTextOverrides) texts[pair.Key] = pair.Value;
        if (definition != null)
        {
            numbers["cave.portal.pairedExit"] = definition.PairPortalsWithTargetWorld ? 1d : 0d;
            texts["cave.portal.itemId"] = definition.PortalItemId;
            texts["cave.portal.targetDimensionId"] = definition.PortalTargetDimensionId;
        }
        profile = profile.WithGenerationConfiguration(numbers, texts, null);
        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        return profile.WithNumericParameter("cave.portal.baseSeed", seed == 0 ? 1 : seed);
    }

    // 冻结配置按预设身份隔离，切换预设不覆盖区块和自然物差量。
    private static ChunkGenerationProfileSnapshot ApplyPersistedEcologyConfiguration(
        ChunkGenerationProfileSnapshot profile, PlanetData planet)
    {
        if (SaveDataMgr.Instance?.SaveData?.UsesFrozenWorldGenerationConfiguration == false)
            return profile;
        planet.Ecology ??= new EcologyWorldSaveData();
        planet.Ecology.EnsureCompatibleVersion();
        if (!planet.Ecology.HasConfiguration || planet.Ecology.ProfileId != profile.ProfileId)
        {
            if (GameNetwork.HasStateAuthority) planet.Ecology.CaptureConfiguration(profile);
            return profile;
        }
        if (planet.Ecology.TryApplyGenerationConfiguration(profile, out var restored)) profile = restored;
        profile = profile.WithEcology(planet.Ecology.GlobalMultiplier, planet.Ecology.CreateRuleSnapshots());
        planet.Ecology.ConfigurationFingerprint = profile.EcologyFingerprint;
        planet.Ecology.Generation.GenerationFingerprint = profile.GenerationFingerprint;
        return profile;
    }

    /// <summary>
    /// 矿洞 Profile 注入冻结后的地表生成参考。
    /// 传送门与高度影响都以地表参数、种子和拓扑为唯一真源，不读取已生成或已加载的地表区块，
    /// 从而保证任意区块加载顺序下都能复算同一份地表结果。
    /// </summary>
    private ChunkGenerationProfileSnapshot AttachCavePortalPairing(
        ChunkGenerationProfileSnapshot profile, int baseSeed)
    {
        DimensionManager dimensionManager = DimensionManager.Instance;
        if (profile == null || !dimensionManager.TryGetDefinitionForWorldKey(
                SaveDataMgr.Instance?.GetCurrentPlanetData()?.Name, out DimensionDefinition definition) ||
            !definition.PairPortalsWithTargetWorld)
            return profile;

        string sourceDimensionId = definition.PortalTargetDimensionId;
        PlanetData sourcePlanet = GetPortalSourcePlanetData(dimensionManager, sourceDimensionId);
        if (sourcePlanet == null) return profile;
        ChunkGenerationProfileSnapshot sourceProfile = GetCachedWorldGenerationProfile(
            portalSourceGenerationCache, sourcePlanet, baseSeed, out _);
        int sourceSeed = dimensionManager.GetGenerationSeedForDimension(baseSeed, sourceDimensionId);
        ChunkGenerationTopologySnapshot topology = ResolveGenerationTopology(sourcePlanet);
        if (ReferenceEquals(profile, pairedCaveSource) && ReferenceEquals(sourceProfile, pairedSurfaceSource) &&
            ReferenceEquals(runtimeGenerator, pairedGenerator) && sourceSeed == pairedSeed &&
            sourceDimensionId == pairedDimension && topology.IsWrapped == pairedTopology.IsWrapped &&
            topology.Min.Equals(pairedTopology.Min) && topology.Span.Equals(pairedTopology.Span))
            return pairedGenerationProfile;

        ChunkGenerationProfileSnapshot caveSourceProfile = profile;
        ChunkGenerationSettingsSnapshot sourceSettings = sourceProfile.Settings;
        // 两端共享门户候选格，地形和空间倍率仍由各自世界配置决定。
        var numbers = new Dictionary<string, double>(profile.NumericParameters, StringComparer.Ordinal)
        {
            ["cave.portal.enabled"] = sourceSettings.CavePortalEnabled ? 1d : 0d,
            ["cave.portal.chunkChance"] = sourceSettings.CavePortalChunkChance,
            ["cave.portal.safeRadius"] = sourceSettings.CavePortalSafeRadius / profile.Settings.WorldSpatialDistanceScale,
            ["cave.portal.chunkWidth"] = sourceSettings.CavePortalChunkWidth > 0 ? sourceSettings.CavePortalChunkWidth : sourceProfile.Width,
            ["cave.portal.chunkHeight"] = sourceSettings.CavePortalChunkHeight > 0 ? sourceSettings.CavePortalChunkHeight : sourceProfile.Height,
            ["cave.portal.seedSalt"] = sourceSettings.CavePortalSeedSalt,
            ["cave.portal.baseSeed"] = baseSeed
        };
        profile = profile.WithGenerationConfiguration(numbers, null, null);

        var pairing = new CavePortalPairingSnapshot(
            sourceDimensionId,
            sourceSeed,
            sourceProfile,
            topology, runtimeGenerator);
        pairedCaveSource = caveSourceProfile; pairedSurfaceSource = sourceProfile; pairedGenerator = runtimeGenerator;
        pairedSeed = sourceSeed; pairedDimension = sourceDimensionId; pairedTopology = topology;
        return pairedGenerationProfile = profile.WithCavePortalPairing(pairing);
    }

    /// <summary>从当前星球根取目标维度数据；缺失时让 Profile 按默认无限世界配置生成。</summary>
    private static PlanetData GetPortalSourcePlanetData(DimensionManager dimensionManager,
        string sourceDimensionId)
    {
        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr?.SaveData?.PlanetData_Dict == null || dimensionManager == null ||
            !dimensionManager.ActiveAddress.IsValid)
        {
            return null;
        }

        WorldAddress sourceAddress = dimensionManager.ActiveAddress.WithDimension(
            sourceDimensionId);
        return saveDataMgr.SaveData.PlanetData_Dict.TryGetValue(sourceAddress.WorldKey,
            out PlanetData sourcePlanet)
            ? sourcePlanet
            : null;
    }

    /// <summary>读取当前世界已经冻结的生态配置指纹，供联机生成设置校验使用。</summary>
    public ulong GetActiveEcologyFingerprint()
    {
        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr?.SaveData?.UsesFrozenWorldGenerationConfiguration != false &&
            saveDataMgr.TryGetActivePlanetData(out PlanetData planet) &&
            planet.Ecology != null && planet.Ecology.HasConfiguration)
        {
            return planet.Ecology.ConfigurationFingerprint;
        }

        return ActiveGenerationProfile?.EcologyFingerprint ?? 0UL;
    }

    /// <summary>返回当前完整纯生成 Profile 指纹，包含洞穴布局、矿脉与天然传送门参数。</summary>
    public ulong GetActiveGenerationFingerprint()
    {
        return ActiveGenerationProfile?.GenerationFingerprint ?? 0UL;
    }

    /// <summary>在主机发送世界快照前确保当前地表生态配置已经冻结。</summary>
    public void EnsureActiveEcologyConfiguration()
    {
        if (!GameNetwork.HasStateAuthority || SaveDataMgr.Instance == null ||
            !SaveDataMgr.Instance.TryGetActivePlanetData(out _))
        {
            return;
        }

        PrepareActiveGenerationSnapshot(out _);
    }

    #endregion

    #region 生态 Item 状态存档

    /// <summary>保存当前仍在表现窗口中的自然物状态；只有状态权威端可以写入。</summary>
    public void CaptureRuntimeNaturalItemStates()
    {
        if (!GameNetwork.HasStateAuthority || activeRuntimeBindings == null)
            return;

        independentNaturalEntities?.CaptureAll();

        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
            binding?.View?.CaptureNaturalItemState();
    }

    /// <summary>自动保存专用的自然物分帧快照，先复制 View 列表再跨帧遍历。</summary>
    public IEnumerator CaptureRuntimeNaturalItemStatesCoroutine(AutoSaveWorkBudget budget = null)
    {
        if (!GameNetwork.HasStateAuthority || activeRuntimeBindings == null)
            yield break;

        independentNaturalEntities?.CaptureAll();

        budget ??= new AutoSaveWorkBudget();
        List<ChunkView> views = new List<ChunkView>();
        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
        {
            ChunkView view = binding?.View;
            if (view != null)
                views.Add(view);
        }

        for (int i = 0; i < views.Count; i++)
        {
            ChunkView view = views[i];
            if (view != null)
            {
                IEnumerator captureRoutine = view.CaptureNaturalItemStateCoroutine(budget);
                while (captureRoutine.MoveNext())
                    yield return captureRoutine.Current;
            }

            // 空区块也计入预算，避免没有自然物时一次扫完全部表现窗口。
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }
    }

    /// <summary>读取某个自然物的状态覆盖；没有覆盖时返回 false。</summary>
    public bool TryGetNaturalItemOverride(RuntimeWorldAddress address, int guid,
        out ItemData itemData)
    {
        itemData = null;
        if (guid == 0 || SaveDataMgr.Instance == null ||
            !SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet) ||
            planet.Ecology == null)
        {
            return false;
        }

        return planet.Ecology.TryGetChangedItem(
            address.ChunkOrigin.X, address.ChunkOrigin.Y, guid, out itemData);
    }

    /// <summary>判断自然物是否已被采集或销毁。</summary>
    public bool IsNaturalItemRemoved(RuntimeWorldAddress address, int guid)
    {
        if (guid == 0 || SaveDataMgr.Instance == null ||
            !SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet) ||
            planet.Ecology == null)
        {
            return false;
        }

        return planet.Ecology.IsRemoved(address.ChunkOrigin.X, address.ChunkOrigin.Y, guid);
    }

    /// <summary>记录自然物删除；客户端只等待现有 Item 网络同步，不写本地生态删除列表。</summary>
    public void MarkNaturalItemRemoved(RuntimeWorldAddress address, int guid)
    {
        TryRemoveNaturalItem(address, guid);
    }

    /// <summary>一次性写入自然物删除差量；已经移除或世界不可写时不重复提交。</summary>
    public bool TryRemoveNaturalItem(RuntimeWorldAddress address, int guid)
        => TryCommitNaturalItemRemoval(address, guid, false);

    internal bool TryRemoveNaturalItemForWorldTransfer(RuntimeWorldAddress address, int guid)
        => TryCommitNaturalItemRemoval(address, guid, true);

    private bool TryCommitNaturalItemRemoval(RuntimeWorldAddress address, int guid, bool allowReserved)
    {
        if (!GameNetwork.HasStateAuthority || guid == 0 || SaveDataMgr.Instance == null)
            return false;

        // 普通删除不能抢占正在交接或采集的天然来源。
        if (!allowReserved && NaturalEntityEcsService.IsGeneratedNaturalEntitySourceReserved(
            GameplayWorldRegistry.ExecutionWorldKey, address.ChunkOrigin.X, address.ChunkOrigin.Y, guid)) return false;

        if (!SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet))
            return false;

        planet.Ecology ??= new EcologyWorldSaveData();
        if (planet.Ecology.IsRemoved(address.ChunkOrigin.X, address.ChunkOrigin.Y, guid))
            return false;
        planet.Ecology.MarkRemoved(address.ChunkOrigin.X, address.ChunkOrigin.Y, guid);
        NotifyNaturalItemRemovedToWorldOwners(address, guid);
        return true;
    }

    internal void NotifyNaturalItemRemovedToWorldOwners(RuntimeWorldAddress address, int guid)
    {
        // 已提交删除后逐个通知，单个监听异常不能阻断其他归属清理。
        if (NaturalItemRemoved != null)
            foreach (Action<RuntimeWorldAddress, int> callback in NaturalItemRemoved.GetInvocationList())
                try { callback(address, guid); }
                catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
    }

    /// <summary>保存一个自然物的 ItemData 状态覆盖。</summary>
    public void CaptureNaturalItemState(RuntimeWorldAddress address, ItemData itemData)
    {
        if (!GameNetwork.HasStateAuthority || itemData == null || itemData.Guid == 0 ||
            SaveDataMgr.Instance == null)
            return;

        if (!SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet))
            return;

        planet.Ecology ??= new EcologyWorldSaveData();
        planet.Ecology.CaptureChangedItem(
            address.ChunkOrigin.X, address.ChunkOrigin.Y, itemData);
    }

    /// <summary>卸载捕获保持每个自然物的正式快照，最后集中写入同一区块差量。</summary>
    public void CaptureNaturalItemStates(RuntimeWorldAddress address, IReadOnlyList<ItemData> items)
    {
        if (!GameNetwork.HasStateAuthority || items == null || items.Count == 0 ||
            SaveDataMgr.Instance == null || !SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet))
            return;
        planet.Ecology ??= new EcologyWorldSaveData();
        planet.Ecology.CaptureChangedItems(address.ChunkOrigin.X, address.ChunkOrigin.Y, items);
    }

    #endregion
}
