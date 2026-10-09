using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>带世界纪元的掉落物句柄，不能在换世界后误操作复用的整数 ID。</summary>
public readonly struct DroppedItemHandle
{
    public readonly int Id;
    internal readonly uint Epoch;
    internal readonly Item Legacy;
    internal DroppedItemHandle(int id, uint epoch, Item legacy = null) { Id = id; Epoch = epoch; Legacy = legacy; }
    public bool IsValid => Legacy != null || (Id != 0 && Epoch == DroppedItemService.Epoch && DroppedItemService.Contains(Id));
}

/// <summary>掉落生成原因只描述通用来源，不承载具体物品业务。</summary>
public enum DroppedItemSpawnReason
{
    Generic = 0,
    PlayerDiscard = 1
}

/// <summary>完整 Item 掉落生成时向物品自身提供的通用上下文。</summary>
public readonly struct DroppedItemSpawnContext
{
    public readonly DroppedItemSpawnReason Reason;
    public readonly Item SourceItem;
    public readonly Vector2 Start;
    public readonly Vector2 End;
    public readonly float Duration;

    public DroppedItemSpawnContext(
        DroppedItemSpawnReason reason,
        Item sourceItem,
        Vector2 start,
        Vector2 end,
        float duration)
    {
        Reason = reason;
        SourceItem = sourceItem;
        Start = start;
        End = end;
        Duration = duration;
    }
}

/// <summary>需要感知自身被丢出的完整物品实现该接口，具体效果由物品自己维护。</summary>
public interface IDroppedItemSpawnContextReceiver
{
    void OnDroppedItemSpawned(in DroppedItemSpawnContext context);
}

/// <summary>特殊物理世界接管生成，通用掉落服务只负责入口与回滚句柄。</summary>
public interface IDroppedItemSpawnProvider
{
    bool CanHandle(in DroppedItemSpawnContext context);
    DroppedItemHandle SpawnDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale);
}

/// <summary>
/// 掉落态入口：passive 使用轻量 GameObject 模拟器，interactive 保留完整 Item。ItemData 只作轻量掉落物冷载荷，
/// 树木、矿石节点、安装中的建筑、手持物和战斗中的投射物仍由各自原系统管理。
/// </summary>
public static partial class DroppedItemService
{
    private const float StandardWorldDropScale = 0.5f;
    private const float DropPositionJitterRadius = 0.15f;
    private const float DropRotationJitterDegrees = 15f;

    private sealed class LegacyDropPlan
    {
        public Vector2 Start, End;
        public float Duration, Bezier, Arc, Spin;
    }

    private static DroppedItemRuntime runtime;
    private static GameSaveData ownerSave;
    private static string ownerWorld;
    private static readonly HashSet<Mod_ItemPicker> pickers = new();
    private static readonly Dictionary<Item, LegacyDropPlan> legacyPlans = new();
    private static readonly List<KeyValuePair<Item, LegacyDropPlan>> legacyScratch = new();
    private static readonly List<DropSpawnRegistration> spawnProviders = new();
    internal static uint Epoch { get; private set; } = 1;
    public static int Count => runtime?.Count ?? 0;
    public static int VisibleViewCount => runtime?.VisibleViewCount ?? 0;

    #region 外部物理世界生成入口
    private sealed class DropSpawnRegistration : IDisposable
    {
        public readonly IDroppedItemSpawnProvider Provider;
        private bool disposed;
        public DropSpawnRegistration(IDroppedItemSpawnProvider provider) => Provider = provider;
        public void Dispose()
        { if (disposed) return; disposed = true; spawnProviders.Remove(this); }
    }
    public static IDisposable RegisterSpawnProvider(IDroppedItemSpawnProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        var registration = new DropSpawnRegistration(provider);
        spawnProviders.Add(registration);
        return registration;
    }
    private static IDroppedItemSpawnProvider FindSpawnProvider(in DroppedItemSpawnContext context)
    {
        for (int i = spawnProviders.Count - 1; i >= 0; i--)
            if (spawnProviders[i].Provider.CanHandle(context)) return spawnProviders[i].Provider;
        return null;
    }
    private static bool TrySpawnWithProvider(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale,
        out DroppedItemHandle handle)
    {
        handle = default;
        IDroppedItemSpawnProvider provider = FindSpawnProvider(context);
        if (provider == null) return false;
        handle = provider.SpawnDrop(source, context, rotation, scale);
        try
        {
            if (!handle.IsValid) throw new InvalidOperationException("掉落生成来源没有返回有效的回滚句柄。");
            NotifySpawnContext(handle.Legacy, context);
            return true;
        }
        catch { Remove(handle); throw; }
    }
    #endregion
    #region 完整物品输送
    private static readonly List<Item> transportCandidates = new();
    private static readonly HashSet<Item> transportDedupe = new();
    private static readonly HashSet<int> transportedItems = new();
    private static uint transportStep = uint.MaxValue;

    /// <summary>完整 Item 的掉落物复用权威空间索引，同一轮跨带只能移动一次。</summary>
    public static void TransportItemBacked(MachineEntity belt, float seconds)
    {
        ItemMgr manager = ItemMgr.Instance;
        if (!GameNetwork.HasStateAuthority || manager == null || belt?.Definition.Transport == null) return;
        if (transportStep != MachineWorld.TransportStep) { transportStep = MachineWorld.TransportStep; transportedItems.Clear(); }
        Vector2 center = (Vector2)belt.Cell + Vector2.one * .5f;
        manager.QueryItemsInCircleNonAlloc(center, .72f, ~0, null, transportCandidates, transportDedupe);
        foreach (Item item in transportCandidates)
        {
            if (!IsLoosePickable(item) || item is Player || RuntimeAiEntityUtility.IsAiEntity(item) ||
                Mod_Droping.IsDropInProgress(item) || item.GetComponent<WorldItemWaterRuntime>()?.IsActive == true ||
                Mod_Building.TryReadBuildingData(item.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding ||
                MachineWorld.GetTransportAt(item.transform.position) != belt || !transportedItems.Add(item.itemData.Guid)) continue;
            MachineWorld.TryReceiveConveyorDrop(new DroppedItemTransferPort(item), item.transform.position, seconds, out bool terminal);
            if (terminal) continue;
            Vector2 position = MachineWorld.TransportGroundItem(item.transform.position, seconds);
            if (WorldTopologyRuntime.ShortestDelta(item.transform.position, position).sqrMagnitude < .00000001f) continue;
            Vector3 display = WorldTopologyRuntime.NearestImagePosition(item.transform.position, position);
            display.z = item.transform.position.z;
            Rigidbody2D body = item.GetComponent<Rigidbody2D>();
            if (body != null) { body.position = display; body.velocity = Vector2.zero; }
            item.transform.position = display;
            item.itemData.transform.position = new Vector3(position.x, position.y, display.z);
            manager.NotifyRuntimeItemMoved(item);
            ItemWorldPlacement.TryAttachWorldModelTransientItem(item, display);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
            WorldItemWaterSystem.TryEnterWater(item, requirePickable: true);
        }
        transportCandidates.Clear(); transportDedupe.Clear();
    }
    #endregion
    // 联机仍通过现有 Item 权威事务；轻量 GameObject 掉落物当前只在单机作为本地权威运行。
    public static bool UsesLightweightDrops => !GameNetwork.IsOnline;
    internal static bool Contains(int id) => runtime != null && runtime.Contains(id);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        runtime?.Dispose(); runtime = null; ownerSave = null; ownerWorld = null;
        pickers.Clear(); legacyPlans.Clear(); legacyScratch.Clear(); Epoch++;
        spawnProviders.Clear();
        forageLegacyCandidates.Clear(); forageLegacyDedupe.Clear();
        transportCandidates.Clear(); transportDedupe.Clear(); transportedItems.Clear(); transportStep = uint.MaxValue;
    }

    #region 生成与回收

    /// <summary>先成功创建掉落态，再由调用者提交库存扣减；passive 不实例化完整 Item。</summary>
    public static DroppedItemHandle Spawn(ItemData source, Vector2 position, Vector2? destination = null,
        float duration = 0f, float rotation = 0f, float bezierOffset = 1f,
        float arcHeight = 1f, float rotationSpeed = 720f, bool randomizeRotation = true,
        DroppedItemSpawnReason spawnReason = DroppedItemSpawnReason.Generic, Item sourceItem = null)
    {
        if (source?.Stack == null || source.Stack.Amount <= 0f ||
            float.IsNaN(source.Stack.Amount) || float.IsInfinity(source.Stack.Amount))
            throw new ArgumentException("掉落物必须具有有效的库存数量。", nameof(source));
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
            throw new ArgumentException("掉落时长必须是非负有限数。", nameof(duration));
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition))
            throw new InvalidOperationException($"找不到掉落物定义：{source.IDName}");
        if (definition.IsActor) throw new InvalidOperationException($"生物不是静态掉落物：{source.IDName}");
        // 掉落物外层尺寸统一为正常物品的一半，避免各入口继续沿用实体自身缩放。
        Vector3 finalScale = ResolveDefaultWorldDropScale(source);
        Vector2 spawnPosition = destination.HasValue
            ? position
            : position + UnityEngine.Random.insideUnitCircle * DropPositionJitterRadius;
        // 精确姿态入口可关闭二次抖动，避免覆盖调用方已经确定好的旋转。
        float finalRotation = randomizeRotation
            ? rotation + UnityEngine.Random.Range(-DropRotationJitterDegrees, DropRotationJitterDegrees)
            : rotation;
        var context = new DroppedItemSpawnContext(spawnReason, sourceItem, spawnPosition, destination ?? spawnPosition, duration);
        if (TrySpawnWithProvider(source, context, finalRotation, finalScale, out DroppedItemHandle supplied)) return supplied;
        if (!definition.UsesLightweightWorldDrop || !UsesLightweightDrops)
            return SpawnItemBacked(source, spawnPosition, destination, duration, finalRotation,
                bezierOffset, arcHeight, rotationSpeed, spawnReason, sourceItem);
        EnsureContext();
        ItemData payload = FastCloner.FastCloner.DeepClone(source);
        payload.inHand = false; payload.Stack.CanBePickedUp = true;
        RemoveLegacyDropData(payload);
        int id;
        do { id = Guid.NewGuid().GetHashCode() & int.MaxValue; } while (id == 0 || runtime.Contains(id));
        payload.Guid = id;
        Vector2 start = WorldTopologyRuntime.NormalizePosition(spawnPosition);
        Vector2 end = WorldTopologyRuntime.NearestImagePosition(start, destination ?? start);
        LightweightDroppedBody body = new()
        {
            Id = id, Position = start, Scale = new Unity.Mathematics.float2(finalScale.x, finalScale.y),
            Rotation = finalRotation, Amount = payload.Stack.Amount, Pickable = 0
        };
        LightweightDroppedFlight? flight = duration > 0f ? new LightweightDroppedFlight
        {
            Start = start, End = end, Control = (start + end) * 0.5f + Vector2.up * bezierOffset,
            Duration = Mathf.Max(0.0001f, duration), ArcHeight = arcHeight, RotationSpeed = rotationSpeed
        } : null;
        runtime.Add(payload, body, flight);
        return new DroppedItemHandle(id, Epoch);
    }

    /// <summary>所有世界掉落物统一使用 0.5 倍外层缩放。</summary>
    public static Vector3 ResolveDefaultWorldDropScale(ItemData source)
    {
        return Vector3.one * StandardWorldDropScale;
    }

    /// <summary>标准战利品产出；生物生成回到 AI 后端，不能把动物做成可入包的静态图标。</summary>
    public static DroppedItemHandle SpawnLoot(string itemId, Vector2 position, float amount = 1f,
        float radius = 1.2f, float duration = 0.5f, Vector2? destination = null,
        float bezierOffset = 1f, float arcHeight = 1f)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
            throw new ArgumentException("产出数量必须是正的有限数。", nameof(amount));
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
            throw new InvalidOperationException($"找不到战利品定义：{itemId}");
        if (definition.IsActor)
        {
            for (int i = 0; i < Mathf.FloorToInt(amount); i++)
            {
                if (!TrySpawnLootActor(itemId, position))
                    throw new InvalidOperationException($"AI 后端暂时无法生成战利品生物：{itemId}");
            }
            return default;
        }
        ItemData data = definition.CreateItemData(); data.Stack.Amount = amount;
        float scatterRadius = Mathf.Max(radius, DropPositionJitterRadius);
        Vector2 end = destination ?? (position + UnityEngine.Random.insideUnitCircle.normalized *
            UnityEngine.Random.Range(scatterRadius * 0.5f, scatterRadius));
        return Spawn(data, position, end, duration, bezierOffset: bezierOffset, arcHeight: arcHeight,
            rotationSpeed: UnityEngine.Random.Range(360f, 1080f));
    }

    /// <summary>每次尝试交付一个生物产出；占格或地形窗口暂不可用返回 false，调用方保留未提交数量。</summary>
    public static bool TrySpawnLootActor(string itemId, Vector2 position)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) || !definition.IsActor)
            throw new InvalidOperationException($"不是有效的生物战利品：{itemId}");
        if (AiRuntimeBackendService.UsesEntities(itemId))
        {
            var backend = AiRuntimeBackendService.Ecology;
            if (backend == null) return false;
            if (!backend.SupportsSpecies(itemId))
                throw new InvalidOperationException($"当前 ECS 后端未支持生物定义：{itemId}");
            return backend.TrySpawnEvent(itemId, position);
        }
        ItemMgr.Instance.InstantiateItem(itemId, position).Load();
        return true;
    }

    /// <summary>内容层只声明 passive/interactive；这里集中决定当前实例能否进入轻量掉落物模拟器。</summary>
    public static bool ShouldUseLightweightDrop(ItemData source)
    {
        if (!UsesLightweightDrops || source == null || string.IsNullOrWhiteSpace(source.IDName))
            return false;
        var context = new DroppedItemSpawnContext(DroppedItemSpawnReason.Generic, null, source.transform.position, source.transform.position, 0f);
        if (FindSpawnProvider(context) != null) return false;
        GameRes resources = GameRes.ExistingInstance;
        return resources != null &&
               resources.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition) &&
               definition.UsesLightweightWorldDrop;
    }

    /// <summary>回滚未提交的生成，或删除已被完全拾取的实体。</summary>
    public static void Remove(DroppedItemHandle handle)
    {
        if (handle.Legacy != null)
        {
            ItemMgr.Instance?.DespawnItem(handle.Legacy, saveData: false);
            return;
        }
        if (handle.Epoch == Epoch) runtime?.Remove(handle.Id);
    }

    /// <summary>只删除旧抛掷模块数据；容器、耐久、建筑快照等库存状态保持原样。</summary>
    internal static void RemoveLegacyDropData(ItemData data)
    {
        if (data.ModuleDataDic == null) return;
        List<string> removed = null;
        foreach (var pair in data.ModuleDataDic)
            if (pair.Value?.ID == ModText.Drop) (removed ??= new List<string>()).Add(pair.Key);
        if (removed != null) foreach (string key in removed) data.ModuleDataDic.Remove(key);
    }

    #endregion

    #region 旧生产者适配

    /// <summary>旧 MOD/拆建筑入口可在返回后继续写载荷；统一在本轮 Item Tick 后转换，不留下常驻 Item。</summary>
    public static bool ScheduleLegacyDrop(Item item, Vector2 start, Vector2 end, float duration,
        float bezierOffset = 1f, float arcHeight = 1f, float rotationSpeed = 720f)
    {
        if (item?.itemData?.Stack == null ||
            RuntimeAiEntityUtility.IsAiEntity(item))
            return false;
        var context = new DroppedItemSpawnContext(DroppedItemSpawnReason.Generic, item, start, end, duration);
        if (FindSpawnProvider(context) == null && !ShouldUseLightweightDrop(item.itemData)) return false;
        item.itemData.Stack.CanBePickedUp = false;
        legacyPlans[item] = new LegacyDropPlan
        { Start = start, End = end, Duration = duration, Bezier = bezierOffset, Arc = arcHeight, Spin = rotationSpeed };
        return true;
    }

    /// <summary>无轨迹的遗留生成入口也可移交；调用方必须先排除手持物、自然实体与附着投射物。</summary>
    public static bool TryConvertLooseItem(Item item)
    {
        if (item == null || item.PersistenceOwner != null || item.Owner != null || item.InHand || item.DestructionHandled || item.itemData == null ||
            legacyPlans.ContainsKey(item)) return false;
        if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) || definition.IsActor) return false;
        var context = new DroppedItemSpawnContext(DroppedItemSpawnReason.Generic, item, item.transform.position, item.transform.position, 0f);
        if (FindSpawnProvider(context) == null && !ShouldUseLightweightDrop(item.itemData)) return false;
        DroppedItemHandle handle = default;
        try
        {
            item.ModuleSave();
            handle = Spawn(item.itemData, item.transform.position,
                rotation: item.transform.eulerAngles.z, sourceItem: item);
            if (!handle.IsValid) return false;
            ItemMgr.Instance.DespawnItem(item, saveData: false);
            return true;
        }
        catch (Exception exception)
        {
            if (item != null && !item.DestructionHandled) Remove(handle);
            Debug.LogError($"[DroppedItems] 世界掉落转换失败：{exception}");
            return false;
        }
    }

    private static void ProcessLegacyPlans()
    {
        legacyScratch.Clear(); legacyScratch.AddRange(legacyPlans); legacyPlans.Clear();
        foreach (var pair in legacyScratch)
        {
            Item item = pair.Key; LegacyDropPlan plan = pair.Value;
            if (item == null || item.DestructionHandled) continue;
            DroppedItemHandle spawned = default;
            try
            {
                item.ModuleSave();
                spawned = Spawn(item.itemData, plan.Start, plan.End, plan.Duration,
                    item.transform.eulerAngles.z, plan.Bezier, plan.Arc, plan.Spin, sourceItem: item);
                ItemMgr.Instance.DespawnItem(item, saveData: false);
            }
            catch (Exception exception)
            {
                // 失败保留旧载荷，不销毁物品；错误必须可见，不能悄悄吞掉产出。
                if (item != null && !item.DestructionHandled) Remove(spawned);
                if (item != null && item.itemData?.Stack != null) item.itemData.Stack.CanBePickedUp = true;
                Debug.LogError($"[DroppedItems] 遗留掉落物转换失败，原物品已保留：{exception}");
            }
        }
        legacyScratch.Clear();
    }

    private static DroppedItemHandle SpawnItemBacked(ItemData source, Vector2 position, Vector2? destination,
        float duration, float rotation, float bezier, float arc, float spin,
        DroppedItemSpawnReason spawnReason, Item sourceItem)
    {
        ItemData data = FastCloner.FastCloner.DeepClone(source); data.inHand = false;
        data.Stack.CanBePickedUp = duration <= 0f;
        Item item = null;
        try
        {
            item = ItemMgr.Instance.InstantiateItem(data, position, Quaternion.Euler(0, 0, rotation),
                ResolveDefaultWorldDropScale(data));
            item.Load(); item.SetInHand(false);
            if (duration > 0f) Mod_BaseDroper.StaticDropItem_Pos(item, position, destination ?? position, duration,
                Mod_BaseDroper.MoveMode.BezierCurve, bezier, arc, spin, spin);
            NotifySpawnContext(item, new DroppedItemSpawnContext(
                spawnReason, sourceItem, position, destination ?? position, duration));
            return new DroppedItemHandle(item.itemData.Guid, 0, item);
        }
        catch
        {
            if (item != null) ItemMgr.Instance.DespawnItem(item, saveData: false);
            throw;
        }
    }

    /// <summary>只向完整掉落物分发通用上下文，掉落系统本身不识别任何具体物品类型。</summary>
    private static void NotifySpawnContext(Item item, in DroppedItemSpawnContext context)
    {
        if (item == null) return;

        MonoBehaviour[] components = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
            if (components[i] is IDroppedItemSpawnContextReceiver receiver)
                receiver.OnDroppedItemSpawned(context);
    }

    #endregion

    #region 世界生命周期与存档

    private static void EnsureContext()
    {
        GameSaveData save = SaveDataMgr.Instance.SaveData;
        Scene scene = SceneManager.GetActiveScene();
        if (save == null || !scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("掉落物世界尚未准备好。");
        // 维度使用各自的世界场景名，与 PlanetData/自然物持久化的地址约定一致。
        if (runtime != null && ReferenceEquals(ownerSave, save) && ownerWorld == scene.name) return;
        // 初次绑定时不能清掉本帧旧生产者刚登记的轨迹；只有明确退出才取消待转换请求。
        ReleaseWorld(clearPending: false); ownerSave = save; ownerWorld = scene.name;
        save.DroppedItems ??= new DroppedItemArchive();
        save.DroppedItems.Worlds.TryGetValue(ownerWorld, out List<DroppedItemSaveRecord> records);
        runtime = new DroppedItemRuntime(scene, records);
    }

    public static void RegisterPicker(Mod_ItemPicker picker) { if (picker != null) pickers.Add(picker); }
    public static void UnregisterPicker(Mod_ItemPicker picker) { pickers.Remove(picker); runtime?.ForgetPicker(picker); }

    /// <summary>由 ItemMgr 在正式世界门禁内集中驱动一次，而非为每个掉落物创建 MonoBehaviour。</summary>
    public static void Tick(float deltaTime)
    {
        ProcessLegacyPlans();
        if (!UsesLightweightDrops) return;
        EnsureContext();
        runtime.Tick(deltaTime, pickers);
    }

    /// <summary>在区块快照开始前结清本帧旧生产者，避免同一物品同时写入旧区块与轻量掉落物快照。</summary>
    public static void PrepareForSave()
    {
        if (!Application.isPlaying || SaveDataMgr.Instance.SaveData == null) return;
        ProcessLegacyPlans();
        if (!UsesLightweightDrops ||
            (runtime == null && legacyPlans.Count == 0 &&
             (ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive))) return;
        EnsureContext();
        WorldItemWaterSystem.ProcessPendingSpawnChecks();
    }

    public static void Present()
    {
        if (UsesLightweightDrops) runtime?.Present(Camera.main);
    }

    /// <summary>必须在退出保存之后释放；GameWorldExit 通知发生在保存之前，不能提前回收掉落物。</summary>
    public static void ReleaseWorld(bool capture = true, bool clearPending = true)
    {
        if (runtime != null)
        {
            if (capture && runtime.IsCreated && ownerSave != null)
            {
                ownerSave.DroppedItems ??= new DroppedItemArchive();
                ownerSave.DroppedItems.Worlds[ownerWorld] = runtime.Capture();
            }
            runtime.Dispose(); runtime = null;
        }
        ownerSave = null; ownerWorld = null; Epoch++;
        if (clearPending) { legacyPlans.Clear(); legacyScratch.Clear(); }
    }

    /// <summary>保存指定对象时只采集它自己的运行态；菜单预览、复制其他存档不会误写当前世界。</summary>
    public static byte[] CaptureArchive(GameSaveData save)
    {
        if (runtime != null && runtime.IsCreated && ReferenceEquals(ownerSave, save))
            save.DroppedItems.Worlds[ownerWorld] = runtime.Capture();
        return MemoryPackSerializer.Serialize(save.DroppedItems ?? new DroppedItemArchive());
    }

    public static void RestoreArchive(GameSaveData save, byte[] bytes)
    {
        DroppedItemArchive archive = bytes == null || bytes.Length == 0
            ? new DroppedItemArchive() : MemoryPackSerializer.Deserialize<DroppedItemArchive>(bytes);
        if (archive == null || archive.Version != 1 || archive.Worlds == null)
            throw new InvalidOperationException("掉落物快照版本不兼容或数据损坏。");
        save.DroppedItems = archive;
    }

    #endregion
}
