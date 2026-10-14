using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>生物、导航、生存、手工制作和地形生态现场只使用正式隔离世界及自有夹具。</summary>
public static class GameplayTestActorWorldRuntimeProbe
{
    #region 现场操作登记
    private static readonly Dictionary<string, IDisposable> fixtures = new(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntimeOperations() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorOperations() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.RegisterAsync("ai", "advance_navigation", CaptureAdvance);
        GameplayTestRuntimeProbe.Register("ai", "cleanup", (_, _) => Release("ai"));
        GameplayTestRuntimeProbe.Register("survival", "temperature_conditions", CaptureTemperatureConditions);
        GameplayTestRuntimeProbe.Register("crafting", "handcraft_clicks", CaptureHandcraft);
        GameplayTestRuntimeProbe.Register("crafting", "cleanup", (_, _) => Release("crafting"));
        GameplayTestRuntimeProbe.Register("terrain", "cover_resource_lifecycle", CaptureTerrainEcology);
        GameplayTestRuntimeProbe.Register("terrain", "cleanup", (_, _) => Release("terrain"));
    }

    private static void Retain(string system, IDisposable fixture)
    {
        try
        {
            if (fixtures.ContainsKey(system)) throw new InvalidOperationException("上一个自有现场尚未收尾：" + system);
            GameplayTestRuntimeProbe.RegisterCleanup(new Cleanup(() =>
            {
                fixture.Dispose();
                if (fixtures.TryGetValue(system, out var current) && ReferenceEquals(current, fixture)) fixtures.Remove(system);
            }));
            fixtures.Add(system, fixture);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private static JObject Release(string system)
    {
        bool existed = fixtures.TryGetValue(system, out var fixture);
        fixture?.Dispose();
        fixtures.Remove(system);
        var result = new JObject { ["hadFixture"] = existed, ["released"] = !fixtures.ContainsKey(system) };
        if (fixture is ActorFixture actor) result["actorRemoved"] = ItemMgr.GetInstance()?.GetItemByGuid(actor.Guid) == null;
        if (fixture is CraftingFixture crafting)
        {
            result["inputEmpty"] = crafting.Table.inputInventory.Data.itemSlots.All(slot => slot.itemData == null);
            result["outputEmpty"] = crafting.Table.outputInventory.Data.itemSlots.All(slot => slot.itemData == null);
            result["panelClosed"] = !crafting.Table.IsPanelOpen;
        }
        if (fixture is TerrainFixture terrain)
        {
            result["terrainRestored"] = terrain.Sample.Terrain.GetCell(terrain.Sample.LocalCell.x, terrain.Sample.LocalCell.y).Equals(terrain.Original);
            result["layersRestored"] = GroundTileLayerSystem.Capture(terrain.Sample.Terrain, terrain.Sample.LocalCell).Covers.Count == terrain.OriginalLayers.Covers.Count;
            result["resourceRemoved"] = !NaturalEntityEcsService.Contains(terrain.Resource);
            result["grassRestored"] = terrain.Sample.Terrain.GetGrass(terrain.Sample.LocalCell.x, terrain.Sample.LocalCell.y) == terrain.OriginalLayers.SurfaceGrass;
        }
        return result;
    }

    private sealed class Cleanup : IDisposable
    {
        private Action cleanup;
        public Cleanup(Action cleanup) => this.cleanup = cleanup;
        public void Dispose()
        {
            if (cleanup == null) return;
            cleanup();
            cleanup = null;
        }
    }
    #endregion

    #region 自有生物与真实空地
    private sealed class ActorFixture : IDisposable
    {
        public Item Actor { get; private set; }
        public readonly int Guid;
        private bool disposed;

        public ActorFixture(Vector3 position, string species = "Zombie")
        {
            if (AiRuntimeBackendService.UsesEntities(species))
                throw new InvalidOperationException("本案例要求正式 GameObject 生物，不变更物种后端路由。");
            if (!AiRuntimeBackendService.TrySpawnDirect(species, WorldLocalPresentation.ProjectPosition(position), 0, out int guid))
                throw new InvalidOperationException("正式生物入口未生成测试物种：" + species);
            Guid = guid;
            Actor = ItemMgr.Instance.GetItemByGuid(guid);
            if (Actor == null)
            {
                AiRuntimeBackendService.TryDespawnActor(guid);
                throw new InvalidOperationException("当前测试要求正式 GameObject 生物后端。");
            }
        }

        public void Reload(ItemData snapshot, Vector3 position)
        {
            if (Actor == null || !ReferenceEquals(ItemMgr.Instance.GetItemByGuid(Guid), Actor))
                throw new InvalidOperationException("生物身份已变化，拒绝替换其他实例。");
            if (!AiRuntimeBackendService.TryDespawnActor(Guid)) throw new InvalidOperationException("自有生物卸载失败。");
            Actor = ItemMgr.Instance.InstantiateItem(snapshot, WorldLocalPresentation.ProjectPosition(position));
            if (Actor == null) throw new InvalidOperationException("生物存档快照未能恢复正式实例。");
            if (!Actor.IsInitialized) Actor.Load();
        }

        public void Dispose()
        {
            if (disposed) return;
            Item current = ItemMgr.GetInstance()?.GetItemByGuid(Guid);
            if (current != null && !ReferenceEquals(current, Actor))
                throw new InvalidOperationException("自有生物 GUID 被其他实例占用，拒绝删除。");
            if (current != null && !AiRuntimeBackendService.TryDespawnActor(Guid))
                throw new InvalidOperationException("自有测试生物清理失败。");
            disposed = true;
        }
    }

    private static RuntimeTerrainTileSample FindOpenCell(Player player, bool requireLine = false)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        ChunkMgr chunks = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("真实区块管理器未就绪。");
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        if (navigation == null || !navigation.IsNavigationReady) throw new InvalidOperationException("正式运行态导航网格未就绪。");
        Vector2Int origin = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(player.transform.position));
        for (int radius = 3; radius <= 8; radius++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
            Vector2 center = (Vector2)WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(x, y)) + Vector2.one * .5f;
            if (!chunks.TryGetRuntimeTerrainTile(center, out var sample) || !FarmlandSystem.IsOpen(sample) ||
                FarmlandSystem.HasWorldPlant(sample.WorldCell) || sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y) > 0f ||
                !navigation.TryGetCell(center, out _, out bool walkable) || !walkable ||
                !chunks.TryGetRuntimeChunkView(sample.Address, out var view) || view == null || !view.gameObject.activeInHierarchy)
                continue;
            if (!requireLine) return sample;
            bool openLine = true;
            for (int step = 1; step <= 3; step++)
            {
                Vector2 destination = WorldTopologyRuntime.NormalizePosition(center + Vector2.right * step);
                if (!chunks.TryGetRuntimeTerrainTile(destination, out var next) || !FarmlandSystem.IsOpen(next) ||
                    FarmlandSystem.HasWorldPlant(next.WorldCell) || next.Terrain.GetLiquidDepth(next.LocalCell.x, next.LocalCell.y) > 0f ||
                    !navigation.TryGetCell(destination, out _, out bool nextWalkable) || !nextWalkable)
                { openLine = false; break; }
            }
            if (openLine && navigation.HasGridLineOfSight(center, WorldTopologyRuntime.NormalizePosition(center + Vector2.right * 3))) return sample;
        }
        throw new InvalidOperationException("附近没有可用于真实测试的已加载空地；不会替换现有建筑、植物或液体。");
    }

    private static JObject Position(Vector3 value) => new() { ["x"] = value.x, ["y"] = value.y };
    #endregion

    #region AI 正式命令与异步导航
    private static IEnumerator CaptureAdvance(Player player, JObject args, Action<JObject> completed)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player, true);
        Vector3 start = new(site.WorldCell.x + .5f, site.WorldCell.y + .5f);
        Vector3 destination = WorldTopologyRuntime.NormalizePosition(start + Vector3.right * 3f);
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        ActorFixture own = null;
        int pathRequest = 0;
        bool retained = false;
        try
        {
            WorldNavigationPathResult limitResult = default;
            bool received = false;
            pathRequest = navigation.RequestPath(start, destination, value => { limitResult = value; received = true; }, 1);
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (!received && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (!received) throw new TimeoutException("真实异步导航没有在预算内返回代价拒绝结果。");
            pathRequest = 0;

            own = new ActorFixture(start);
            Retain("ai", own);
            // 自有生物与测试玩家同阵营，隔离近距离玩家触发的正式避让状态。
            if (!FactionRelationService.TrySetFactionId(own.Actor, FactionRelationService.GetFactionId(player)))
                throw new InvalidOperationException("自有推进生物无法设置测试阵营。");
            bool playerThreatExcluded = !FactionRelationService.CanAttack(own.Actor, player);
            AI_Wolf ai = own.Actor.GetComponentInChildren<AI_Wolf>(true)
                ?? throw new InvalidOperationException("正式测试生物没有推进状态机。");
            Mod_Mover_AI mover = own.Actor.GetComponentInChildren<Mod_Mover_AI>(true)
                ?? throw new InvalidOperationException("正式测试生物缺少实际移动模块。");
            WorldNavigationAgent agent = own.Actor.GetComponent<WorldNavigationAgent>()
                ?? throw new InvalidOperationException("正式移动模块没有绑定导航代理。");
            bool commandAccepted = AiRuntimeBackendService.TrySetAdvanceCommand(own.Guid,
                new AIAdvanceCommand(0, WorldLocalPresentation.ProjectPosition(destination), .55f, false));
            bool commandStored = ai.HasAdvanceCommand;
            bool acceptedPath = false, pendingPath = false, reached = false, stayedWalkable = true;
            float maximumDisplacement = 0f, maximumSpeed = 0f;
            var results = new HashSet<string>(StringComparer.Ordinal);
            deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (commandAccepted && Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (own.Actor == null || !AiRuntimeBackendService.TryGetActor(own.Guid, out Vector3 position, out bool alive) || !alive)
                    throw new InvalidOperationException("推进中的自有生物已失效或死亡。");
                position = WorldLocalPresentation.ToLogical(own.Actor.transform.position);
                maximumDisplacement = Mathf.Max(maximumDisplacement, WorldTopologyRuntime.ShortestDelta(start, position).magnitude);
                maximumSpeed = Mathf.Max(maximumSpeed, agent.Velocity.magnitude);
                results.Add(agent.DestinationResult.ToString());
                acceptedPath |= agent.HasPath || agent.DestinationResult == WorldNavigationDestinationResult.Accepted ||
                    agent.DestinationResult == WorldNavigationDestinationResult.Reached;
                pendingPath |= agent.PathPending;
                stayedWalkable &= navigation.TryGetCell(position, out _, out bool walkable) && walkable;
                reached = WorldTopologyRuntime.ShortestDelta(position, destination).magnitude <= .65f && !ai.HasAdvanceCommand;
                if (reached) break;
                yield return null;
            }
            Vector3 end = WorldLocalPresentation.ToLogical(own.Actor.transform.position);
            bool cancelled = AiRuntimeBackendService.TryClearAdvanceCommand(own.Guid);
            var sample = new JObject
            {
                ["actorGuid"] = own.Guid, ["actorInitialized"] = own.Actor.IsInitialized,
                ["aiLoaded"] = ai.IsRuntimeLoaded, ["moverLoaded"] = mover.IsRuntimeLoaded,
                ["playerThreatExcluded"] = playerThreatExcluded,
                ["commandAccepted"] = commandAccepted, ["commandStored"] = commandStored,
                ["pathAccepted"] = acceptedPath, ["pathPendingObserved"] = pendingPath,
                ["results"] = new JArray(results.OrderBy(value => value, StringComparer.Ordinal)),
                ["reached"] = reached, ["commandClearedAtArrival"] = reached,
                ["maximumDisplacement"] = maximumDisplacement, ["maximumSpeed"] = maximumSpeed,
                ["stayedWalkable"] = stayedWalkable, ["start"] = Position(start), ["end"] = Position(end),
                ["distanceToGoal"] = WorldTopologyRuntime.ShortestDelta(end, destination).magnitude,
                ["cancelAccepted"] = cancelled, ["hasDestinationAfterCancel"] = mover.HasActiveDestination,
                ["hasPathAfterCancel"] = agent.HasPath, ["pendingAfterCancel"] = agent.PathPending,
                ["limitCallbackReceived"] = received, ["limitAccepted"] = limitResult.Success,
                ["limitRejectedByCost"] = limitResult.RejectedByPathCost,
                ["limitTotalCost"] = limitResult.TotalCost, ["gridRevision"] = navigation.GridRevision,
                ["cleanupRegistered"] = true
            };
            retained = true;
            completed(sample);
        }
        finally
        {
            if (pathRequest != 0) navigation.CancelPath(pathRequest);
            if (!retained) own?.Dispose();
        }
    }
    #endregion

    #region 真实角色体温缓冲与疾病生命周期
    private static BuffInstance FindSourceCondition(Mod_BuffManager manager, string source)
    {
        foreach (BuffInstance buff in manager.ActiveBuffs.Values)
            if (buff != null && buff.SourceKey == source)
                return buff;
        return null;
    }

    private static JObject CaptureTemperatureConditions(Player player, JObject args)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player);
        Vector3 center = new(site.WorldCell.x + .5f, site.WorldCell.y + .5f);
        var result = new JObject();
        var own = new ActorFixture(center);
        try
        {
            Mod_Temperature temperature = own.Actor.GetComponentInChildren<Mod_Temperature>(true)
                ?? throw new InvalidOperationException("正式生物没有体温模块。");
            Mod_BuffManager buffs = own.Actor.GetComponentInChildren<Mod_BuffManager>(true)
                ?? throw new InvalidOperationException("正式生物没有 Buff 模块。");
            if (!temperature.IsRuntimeLoaded || !buffs.IsRuntimeLoaded)
                throw new InvalidOperationException("测试必须采样已加载的真实生物模块。");
            float originalMaximumBuffer = temperature.Data.DangerTemperatureBufferSeconds;
            temperature.Data.RemainingTemperatureBufferSeconds = .5f;
            temperature.SetTemperature(temperature.SafeTemperatureMin - 50f);
            // 只加速自有生物的一次正式结算，不跳转全局世界时间或重新计算疾病规则。
            temperature.ModUpdate(.25f);
            result["beforeExhaustion"] = temperature.Data.RemainingTemperatureBufferSeconds;
            result["coldBeforeExhaustion"] = FindSourceCondition(buffs, "temperature:hypothermia") != null;
            temperature.ModUpdate(.25f);
            result["afterExhaustion"] = temperature.Data.RemainingTemperatureBufferSeconds;
            BuffInstance coldCondition = FindSourceCondition(buffs, "temperature:hypothermia");
            result["coldAfterExhaustion"] = coldCondition != null;
            result["coldConditionSourceKey"] = coldCondition?.SourceKey;
            result["coldConditionDefinitionId"] = coldCondition?.DefinitionId;
            result["hotWhileCold"] = FindSourceCondition(buffs, "temperature:heatstroke") != null;
            result["coldTemperature"] = temperature.Data.CurrentTemperature;
            result["safeMinimum"] = temperature.SafeTemperatureMin;
            result["actualAmbient"] = temperature.Data.AmbientTemperature;

            temperature.SetTemperature(temperature.SafeTemperatureMax + 50f);
            temperature.ModUpdate(.25f);
            BuffInstance hotCondition = FindSourceCondition(buffs, "temperature:heatstroke");
            result["hotAfterSwitch"] = hotCondition != null;
            result["hotConditionSourceKey"] = hotCondition?.SourceKey;
            result["hotConditionDefinitionId"] = hotCondition?.DefinitionId;
            result["coldAfterSwitch"] = FindSourceCondition(buffs, "temperature:hypothermia") != null;
            result["hotTemperature"] = temperature.Data.CurrentTemperature;
            result["safeMaximum"] = temperature.SafeTemperatureMax;
            result["bufferAfterSwitch"] = temperature.Data.RemainingTemperatureBufferSeconds;
            temperature.SetTemperature((temperature.SafeTemperatureMin + temperature.SafeTemperatureMax) * .5f);
            temperature.ModUpdate(.5f);
            result["coldAfterSafe"] = FindSourceCondition(buffs, "temperature:hypothermia") != null;
            result["hotAfterSafe"] = FindSourceCondition(buffs, "temperature:heatstroke") != null;
            result["safeTemperature"] = temperature.Data.CurrentTemperature;
            result["bufferAfterSafe"] = temperature.Data.RemainingTemperatureBufferSeconds;
            result["maximumBuffer"] = originalMaximumBuffer;
            result["actorInitialized"] = own.Actor.IsInitialized;
            result["temperatureLoaded"] = temperature.IsRuntimeLoaded;
            result["buffsLoaded"] = buffs.IsRuntimeLoaded;
            own.Actor.ModuleSave();
            ItemData snapshot = FastCloner.FastCloner.DeepClone(own.Actor.itemData);
            own.Reload(snapshot, center);
            Mod_Temperature restored = own.Actor.GetComponentInChildren<Mod_Temperature>(true);
            result["restoredInitialized"] = own.Actor.IsInitialized;
            result["restoredGuid"] = own.Actor.itemData.Guid;
            result["expectedGuid"] = own.Guid;
            result["restoredBuffer"] = restored?.Data.RemainingTemperatureBufferSeconds;
            result["restoredMaximumBuffer"] = restored?.Data.DangerTemperatureBufferSeconds;
            return result;
        }
        finally
        {
            own.Dispose();
            result["actorRemoved"] = ItemMgr.GetInstance()?.GetItemByGuid(own.Guid) == null;
        }
    }
    #endregion

    #region 正式手工面板按键与原子扣料
    private sealed class CraftingFixture : IDisposable
    {
        public readonly Mod_HandCraftTable Table;
        private readonly bool wasOpen;
        private readonly Inventory inputTarget, outputTarget;
        private bool disposed;

        public CraftingFixture(Player player)
        {
            Table = player.GetComponentInChildren<Mod_HandCraftTable>(true)
                ?? throw new InvalidOperationException("真实玩家没有手工制作模块。");
            if (!Table.IsRuntimeLoaded || Table.inputInventory?.Data?.itemSlots == null || Table.outputInventory?.Data?.itemSlots == null)
                throw new InvalidOperationException("正式玩家的手工制作库存未加载。");
            if (Table.inputInventory.Data.itemSlots.Any(slot => slot.itemData != null) || Table.outputInventory.Data.itemSlots.Any(slot => slot.itemData != null))
                throw new InvalidOperationException("手工制作现场要求输入与输出为空；拒绝覆盖先前案例的物品。");
            wasOpen = Table.IsPanelOpen;
            inputTarget = Table.inputInventory.DefaultTarget_Inventory;
            outputTarget = Table.outputInventory.DefaultTarget_Inventory;
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearInventory(Table.inputInventory);
            ClearInventory(Table.outputInventory);
            if (!wasOpen && Table.basePanel != null) Table.I_ClosePanel();
            Table.inputInventory.DefaultTarget_Inventory = inputTarget;
            Table.outputInventory.DefaultTarget_Inventory = outputTarget;
            disposed = true;
        }
    }

    private static void ClearInventory(Inventory inventory)
    {
        foreach (ItemSlot slot in inventory.Data.itemSlots)
        {
            ItemData value = slot.itemData;
            if (value != null && !inventory.Data.TrySetSlotItemAmount(slot, value, 0f))
                throw new InvalidOperationException("自有制作槽位未能通过正式库存事务清理。");
        }
    }

    private static float Count(Inventory inventory, string id) => inventory.Data.itemSlots
        .Where(slot => slot.itemData?.IDName == id).Sum(slot => slot.itemData.Stack.Amount);

    private static JObject CaptureHandcraft(Player player, JObject args)
    {
        var own = new CraftingFixture(player);
        Retain("crafting", own);
        Mod_HandCraftTable table = own.Table;
        GameRes resources = GameRes.ExistingInstance;
        RuntimeRecipe recipe = resources.GetRecipe("core:藤蔓=绳子")
            ?? throw new InvalidOperationException("正式手工配方目录缺少藤蔓制绳配方。");
        table.I_ShowPanel();
        if (table.workButton == null) throw new InvalidOperationException("正式手工面板没有合成按钮。");
        ItemData material = resources.CreateItemData("Twine");
        material.Stack.Amount = 3f;
        bool inserted = table.inputInventory.Data.TryAddItem(material);
        var result = new JObject
        {
            ["materialInserted"] = inserted, ["panelOpen"] = table.IsPanelOpen,
            ["recipeId"] = recipe.Id, ["buttonInteractable"] = table.workButton.interactable,
            ["inputBefore"] = Count(table.inputInventory, "Twine"), ["outputBefore"] = Count(table.outputInventory, "Rope")
        };
        int required = table.GetRequiredClickCount();
        for (int click = 1; click < required; click++) table.workButton.onClick.Invoke();
        result["requiredClicks"] = required;
        result["inputBeforeFinalClick"] = Count(table.inputInventory, "Twine");
        result["outputBeforeFinalClick"] = Count(table.outputInventory, "Rope");
        table.workButton.onClick.Invoke();
        result["inputAfterCraft"] = Count(table.inputInventory, "Twine");
        result["outputAfterCraft"] = Count(table.outputInventory, "Rope");
        result["expectedOutput"] = GameDifficultyService.ScaleCount(1,
            GameDifficultyService.Current.Production.CraftingOutputMultiplier, 1);

        ClearInventory(table.outputInventory);
        foreach (ItemSlot slot in table.outputInventory.Data.itemSlots)
        {
            ItemData blocker = resources.CreateItemData("Ore_Stone");
            if (!table.outputInventory.Data.TrySetSlotItemAmount(slot, blocker, 1f))
                throw new InvalidOperationException("无法准备自有满输出槽位。");
        }
        ItemData secondMaterial = resources.CreateItemData("Twine");
        secondMaterial.Stack.Amount = 3f;
        result["secondMaterialsInserted"] = table.inputInventory.Data.TryAddItem(secondMaterial);
        float inputBeforeFull = Count(table.inputInventory, "Twine");
        float blockerBefore = Count(table.outputInventory, "Ore_Stone");
        for (int click = 0; click < required; click++) table.workButton.onClick.Invoke();
        result["inputBeforeFull"] = inputBeforeFull;
        result["inputAfterFull"] = Count(table.inputInventory, "Twine");
        result["blockerBefore"] = blockerBefore;
        result["blockerAfter"] = Count(table.outputInventory, "Ore_Stone");
        result["outputAfterFull"] = Count(table.outputInventory, "Rope");
        result["fullOutputSlots"] = table.outputInventory.Data.itemSlots.Count;
        result["fullButtonInteractable"] = table.workButton.interactable;
        result["cleanupRegistered"] = true;
        return result;
    }
    #endregion

    #region 真实地表覆盖与自然实体占地
    private sealed class TerrainFixture : IDisposable
    {
        public readonly RuntimeTerrainTileSample Sample;
        public readonly TerrainCell Original;
        public readonly GroundLayerCellSaveData OriginalLayers;
        public readonly float OriginalDamage, OriginalLiquidDepth;
        public NaturalEntityHandle Resource;
        private readonly Player player;
        private readonly IDisposable lease;
        private readonly GrassCellState? grassDelta;
        private bool disposed;

        public TerrainFixture(Player player, RuntimeTerrainTileSample sample)
        {
            this.player = player;
            Sample = sample;
            Original = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
            OriginalLayers = GroundTileLayerSystem.Capture(sample.Terrain, sample.LocalCell);
            if (OriginalLayers.Covers.Count != 0 || OriginalLayers.ExcavatedLayers != 0)
                throw new InvalidOperationException("地形生态测试要求天然空地，拒绝覆盖先前案例的地层修改。");
            OriginalDamage = GroundTileHarvestSystem.ReadProgress(sample);
            OriginalLiquidDepth = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
            grassDelta = SaveDataMgr.TestExistingInstance.CaptureTestRuntimeGrassDelta(player, sample);
            lease = ChunkMgr.ExistingInstance.AcquireChunkLease(sample.Address, ChunkLeaseKind.Simulation);
        }

        public void Dispose()
        {
            if (disposed) return;
            try
            {
                NaturalEntityEcsService.Remove(Resource);
                if (Sample.Terrain == null || Sample.Terrain.IsDisposed) throw new InvalidOperationException("测试地表已卸载，无法清理。");
                TerrainCell current = Sample.Terrain.GetCell(Sample.LocalCell.x, Sample.LocalCell.y);
                if (current.BlockingTileId != Original.BlockingTileId || current.BackTileId != Original.BackTileId ||
                    TerrainSupportLayer.GetTileId(Sample.Terrain, Sample.LocalCell.x, Sample.LocalCell.y) != 0)
                    throw new InvalidOperationException("测试空地出现其他来源的建筑，拒绝覆盖。");
                GroundTileLayerSystem.Restore(Sample.Terrain, OriginalLayers);
                Sample.Terrain.SetCell(Sample.LocalCell.x, Sample.LocalCell.y, Original);
                Sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, Sample.LocalCell.x, Sample.LocalCell.y, OriginalDamage);
                SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
                saves.RecordGroundLayerCell(Sample);
                if (!saves.RestoreTestRuntimeGrassDelta(player, Sample, grassDelta))
                    throw new InvalidOperationException("测试空地草差量未能恢复。");
                if (!ChunkMgr.ExistingInstance.TryGetChunkRuntime(Sample.Address, out var chunk))
                    throw new InvalidOperationException("自有地形区块不再存在。");
                saves.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, Sample.WorldCell, Sample.LocalCell,
                    Original.GroundTileId, FarmlandSystem.GetBlockId(Original.GroundTileId)));
                WorldNavigationManager.ExistingInstance?.UpdateChunkRuntimeCell(chunk, Sample.LocalCell.x, Sample.LocalCell.y);
                disposed = true;
            }
            finally { lease.Dispose(); }
        }
    }

    private static JObject CaptureTerrainEcology(Player player, JObject args)
    {
        RuntimeTerrainTileSample sample = FindOpenCell(player);
        var own = new TerrainFixture(player, sample);
        Retain("terrain", own);
        GameRes resources = GameRes.ExistingInstance;
        if (!resources.TryGetTileDefinition("Tile_Dirt", out var soil) || !resources.TryGetTileDefinition("Tile_Grass", out var grass))
            throw new InvalidOperationException("正式地块目录缺少 Tile_Dirt 或 Tile_Grass 测试定义。");
        Vector3 center = new(sample.WorldCell.x + .5f, sample.WorldCell.y + .5f);
        bool first = GroundTileLayerSystem.TryApplyPlayerCover(sample, soil, out string firstReason);
        GroundLayerCellSaveData one = GroundTileLayerSystem.Capture(sample.Terrain, sample.LocalCell);
        bool second = GroundTileLayerSystem.TryApplyPlayerCover(sample, grass, out string secondReason);
        GroundLayerCellSaveData two = GroundTileLayerSystem.Capture(sample.Terrain, sample.LocalCell);
        SaveDataMgr.TestExistingInstance.TryGetChunkDelta(SceneManager.GetActiveScene().name,
            new Vector2Int(sample.Address.ChunkOrigin.X, sample.Address.ChunkOrigin.Y).ToString(), out var delta);
        GroundLayerCellSaveData saved = delta?.GroundLayerCells.FirstOrDefault(value => value.LocalPosition == sample.LocalCell);
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        bool walkableBefore = navigation.TryGetCell(center, out _, out bool before) && before;
        RuntimeItemDefinition resourceDefinition = resources.ItemDefinitions["Tree_Pine"];
        ItemData resourceData = resources.CreateItemData(resourceDefinition.Id);
        resourceData.Guid = ItemMgr.Instance.GenerateGuid();
        resourceData.transform ??= new ItemTransform();
        resourceData.transform.position = center;
        bool registered = NaturalEntityEcsService.TryRegister(resourceDefinition, resourceData.Guid, center,
            26f, 0f, sample.Address.DimensionId, resourceData, out own.Resource);
        if (!registered) throw new InvalidOperationException("真实自然资源 ECS 入口没有建立自有树木。");
        if (ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(sample.Address, out var view))
            NaturalEntityEcsService.BindPresentation(own.Resource, view.GetComponentInChildren<ChunkTilemapRenderer>(true));
        bool readBlocked = navigation.TryGetCell(center, out _, out bool blockedWalkable);
        bool captured = NaturalEntityEcsService.TryCapture(own.Resource, out ItemData snapshot);
        bool removed = NaturalEntityEcsService.TryTakeSnapshotAndRemove(own.Resource, out ItemData removedSnapshot);
        bool walkableAfter = navigation.TryGetCell(center, out _, out bool after) && after;
        bool remains = NaturalEntityEcsService.Contains(own.Resource);
        bool restoredEntity = NaturalEntityEcsService.TryRegister(resourceDefinition, resourceData.Guid, center,
            26f, 0f, sample.Address.DimensionId, removedSnapshot, out own.Resource);
        if (restoredEntity && view != null)
            NaturalEntityEcsService.BindPresentation(own.Resource, view.GetComponentInChildren<ChunkTilemapRenderer>(true));
        bool recaptured = NaturalEntityEcsService.TryCapture(own.Resource, out ItemData recovered);
        return new JObject
        {
            ["firstCoverAccepted"] = first, ["firstReason"] = firstReason,
            ["secondCoverAccepted"] = second, ["secondReason"] = secondReason,
            ["originalCoverCount"] = own.OriginalLayers.Covers.Count,
            ["oneCoverCount"] = one.Covers.Count, ["twoCoverCount"] = two.Covers.Count,
            ["bottomGround"] = two.Covers.FirstOrDefault()?.UnderlyingCell.GroundTileId,
            ["expectedBottomGround"] = own.Original.GroundTileId,
            ["topUnderlyingGround"] = two.Covers.LastOrDefault()?.UnderlyingCell.GroundTileId,
            ["expectedTopUnderlyingGround"] = soil.RuntimeTileId,
            ["currentGround"] = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId,
            ["expectedCurrentGround"] = grass.RuntimeTileId,
            ["savedCoverCount"] = saved?.Covers.Count,
            ["liquidBefore"] = own.OriginalLiquidDepth,
            ["liquidAfter"] = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y),
            ["entityRegistered"] = registered, ["entityCaptured"] = captured,
            ["entityGuid"] = snapshot?.Guid, ["expectedGuid"] = resourceData.Guid,
            ["entityId"] = snapshot?.IDName, ["walkableBefore"] = walkableBefore,
            ["blockedCellRead"] = readBlocked, ["walkableWithTree"] = blockedWalkable,
            ["entityRemoved"] = removed, ["entityRemainsAfterRemoval"] = remains,
            ["walkableAfterRemoval"] = walkableAfter, ["removedGuid"] = removedSnapshot?.Guid,
            ["entityRestored"] = restoredEntity, ["entityRecaptured"] = recaptured,
            ["restoredGuid"] = recovered?.Guid, ["restoredId"] = recovered?.IDName,
            ["cleanupRegistered"] = true
        };
    }
    #endregion
}
