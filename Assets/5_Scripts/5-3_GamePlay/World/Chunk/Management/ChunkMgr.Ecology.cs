using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// ChunkMgr 的生态配置与状态存档扩展。
/// 纯生成器只负责返回确定性基线；这里负责读取冻结配置、捕获主线程 Item 状态以及维护删除差量。
/// </summary>
public partial class ChunkMgr
{
    #region 配置冻结

    /// <summary>把当前 Profile 的生态配置冻结到 PlanetData，并恢复已保存配置。</summary>
    private static ChunkGenerationProfileSnapshot ApplyPersistedEcologyConfiguration(
        ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            return profile;

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr == null || !saveDataMgr.TryGetActivePlanetData(out PlanetData planet))
            return profile;

        return ApplyPersistedEcologyConfiguration(profile, planet);
    }

    /// <summary>按指定维度世界的数据恢复 Profile；矿洞复核地表入口时也复用同一份冻结配置。</summary>
    private static ChunkGenerationProfileSnapshot ApplyPersistedEcologyConfiguration(
        ChunkGenerationProfileSnapshot profile, PlanetData planet)
    {
        if (profile == null || planet == null)
            return profile;

        // 完整 Profile 同样需要冻结：矿洞的房间/隧道、矿脉和入口参数不能随 SO 后续改动重排。
        profile = ApplyPersistedGenerationConfiguration(profile, planet);

        if (profile.Settings.Mode != ChunkGenerationMode.Surface)
            return profile;

        if (!planet.Ecology.HasConfiguration)
        {
            // 客户端等待主机快照提供冻结配置，不在本地写入生态状态。
            if (GameNetwork.HasStateAuthority)
                planet.Ecology.CaptureConfiguration(profile);
            return profile;
        }

        ChunkGenerationProfileSnapshot persistedProfile = profile.WithEcology(
            planet.Ecology.GlobalMultiplier,
            planet.Ecology.CreateRuleSnapshots());
        // 同步冻结规则指纹，供联机校验。
        planet.Ecology.ConfigurationFingerprint = persistedProfile.EcologyFingerprint;
        return persistedProfile;
    }

    /// <summary>只恢复完整生成参数；地表配对复核不需要重建生态 Item，却必须读取相同地形和结构配置。</summary>
    private static ChunkGenerationProfileSnapshot ApplyPersistedGenerationConfiguration(
        ChunkGenerationProfileSnapshot profile, PlanetData planet)
    {
        if (profile == null || planet == null)
            return profile;

        planet.Ecology ??= new EcologyWorldSaveData();
        if (!planet.Ecology.HasGenerationConfiguration ||
            !planet.Ecology.Generation.Matches(profile))
        {
            if (GameNetwork.HasStateAuthority)
                planet.Ecology.CaptureGenerationConfiguration(profile);
            return profile;
        }

        if (!planet.Ecology.TryApplyGenerationConfiguration(profile,
                out ChunkGenerationProfileSnapshot restoredProfile))
        {
            return profile;
        }

        planet.Ecology.Generation.GenerationFingerprint = restoredProfile.GenerationFingerprint;
        return restoredProfile;
    }

    /// <summary>
    /// 矿洞 Profile 注入冻结后的地表生成参考。
    /// 传送门与高度影响都以地表参数、种子和拓扑为唯一真源，不读取已生成或已加载的地表区块，
    /// 从而保证任意区块加载顺序下都能复算同一份地表结果。
    /// </summary>
    private static ChunkGenerationProfileSnapshot AttachCavePortalPairing(
        ChunkGenerationProfileSnapshot profile, int baseSeed)
    {
        if (profile == null || profile.Settings.Mode != ChunkGenerationMode.Cave)
            return profile;

        DimensionManager dimensionManager = DimensionManager.Instance;
        string sourceDimensionId = profile.Settings.CavePortalTargetDimensionId;
        ChunkGenerationProfileSO sourceAsset = dimensionManager?.GetGenerationProfile(
            sourceDimensionId);
        if (sourceAsset == null)
            return profile;

        PlanetData sourcePlanet = GetPortalSourcePlanetData(dimensionManager, sourceDimensionId);
        ChunkGenerationProfileSnapshot sourceProfile = ApplyWorldCoordinateScale(
            sourceAsset.CreateSnapshot(), sourcePlanet);
        sourceProfile = ApplyPersistedEcologyConfiguration(sourceProfile, sourcePlanet);
        sourceProfile = sourceProfile.WithNumericParameter("cave.portal.baseSeed", baseSeed);
        if (sourceProfile.Settings.Mode != ChunkGenerationMode.Surface)
            return profile;

        ChunkGenerationSettingsSnapshot sourceSettings = sourceProfile.Settings;
        // 即使旧矿洞存档还保留较早的入口参数，也要以它对应地表的冻结参数为准。
        profile = profile
            .WithNumericParameter("world.coordinateScale", sourceSettings.WorldCoordinateScale)
            .WithNumericParameter("cave.portal.enabled", sourceSettings.CavePortalEnabled ? 1d : 0d)
            .WithNumericParameter("cave.portal.chunkChance", sourceSettings.CavePortalChunkChance)
            .WithNumericParameter("cave.portal.safeRadius", sourceSettings.CavePortalSafeRadius)
            .WithNumericParameter("cave.portal.chunkWidth",
                sourceSettings.CavePortalChunkWidth > 0
                    ? sourceSettings.CavePortalChunkWidth
                    : sourceProfile.Width)
            .WithNumericParameter("cave.portal.chunkHeight",
                sourceSettings.CavePortalChunkHeight > 0
                    ? sourceSettings.CavePortalChunkHeight
                    : sourceProfile.Height)
            .WithNumericParameter("cave.portal.seedSalt", sourceSettings.CavePortalSeedSalt)
            .WithNumericParameter("cave.portal.baseSeed", baseSeed);

        int sourceSeed = dimensionManager != null
            ? dimensionManager.GetGenerationSeedForDimension(baseSeed, sourceDimensionId)
            : baseSeed;
        var pairing = new CavePortalPairingSnapshot(
            sourceDimensionId,
            sourceSeed,
            sourceProfile,
            ResolveGenerationTopology(sourcePlanet));
        return profile.WithCavePortalPairing(pairing);
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
        if (SaveDataMgr.Instance != null &&
            SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet) &&
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

        ChunkGenerationProfileSO profileAsset = DimensionManager.Instance?.GetActiveGenerationProfile();
        if (profileAsset == null)
            return;

        ChunkGenerationProfileSnapshot profile = ApplyWorldCoordinateScale(
            profileAsset.CreateSnapshot());
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        profile = ApplyPersistedEcologyConfiguration(profile);
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (baseSeed == 0)
            baseSeed = 1;
        profile = profile.WithNumericParameter("cave.portal.baseSeed", baseSeed);
        activeGenerationSnapshot = AttachCavePortalPairing(profile, baseSeed);
    }

    #endregion

    #region 生态 Item 状态存档

    /// <summary>保存当前仍在表现窗口中的自然物状态；只有状态权威端可以写入。</summary>
    public void CaptureRuntimeNaturalItemStates()
    {
        if (!GameNetwork.HasStateAuthority || activeRuntimeBindings == null)
            return;

        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
            binding?.View?.CaptureNaturalItemState();
    }

    /// <summary>自动保存专用的自然物分帧快照，先复制 View 列表再跨帧遍历。</summary>
    public IEnumerator CaptureRuntimeNaturalItemStatesCoroutine()
    {
        if (!GameNetwork.HasStateAuthority || activeRuntimeBindings == null)
            yield break;

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
            if (view == null)
                continue;

            IEnumerator captureRoutine = view.CaptureNaturalItemStateCoroutine();
            while (captureRoutine.MoveNext())
                yield return captureRoutine.Current;
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
        if (!GameNetwork.HasStateAuthority || guid == 0 || SaveDataMgr.Instance == null)
            return;

        if (!SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet))
            return;

        planet.Ecology ??= new EcologyWorldSaveData();
        planet.Ecology.MarkRemoved(address.ChunkOrigin.X, address.ChunkOrigin.Y, guid);
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

    #endregion
}
