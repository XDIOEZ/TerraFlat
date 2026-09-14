using UnityEngine;

/// <summary>
/// 世界物品的短期掉落轨迹模块：只负责抛物线运动与 Chunk 归属。
/// 轨迹结束后的水体判定统一交给 WorldItemWaterSystem，禁止在此维护浮沉、漂流或水下表现状态。
/// </summary>
public class Mod_Droping : Module
{
    #region 运行时归属

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public Mod_BaseDroper.Drop drop;
    public Ex_ModData modData;

    /// <summary>资源目录注册 Prefab 时使用的稳定掉落模块 ID，不依赖实例化后的 Awake。</summary>
    public override string CanonicalModuleId => ModText.Drop;

    public Chunk LastChunk; // 上一帧 item 所处的 chunk
    private bool usesLegacyChunkOwnership;

    /// <summary>只要掉落模块仍持有轨迹数据，物品就处于掉落阶段。</summary>
    public static bool IsDropInProgress(Item targetItem)
    {
        Mod_Droping dropping = targetItem?.itemMods?.GetMod_ByID<Mod_Droping>(ModText.Drop);
        return dropping?.drop != null &&
               !dropping.drop.waterFloating &&
               !dropping.drop.waterSinking;
    }

    #endregion

    #region 掉落轨迹

    [Header("丢弃动画参数")]
    [Tooltip("垂直方向最大高度（与之前一致）")]
    public float arcHeight = 1f;

    public override void Awake()
    {
        base.Awake();
    }

    public override void Load()
    {
        modData.ReadData(ref drop);
        BindDropItemReference();

        // 掉落物先尝试绑定新版 ChunkView。新区块窗口已启用时，即使当前画面尚未
        // 完成绑定也不能回退到旧 Chunk 查询，否则灌木死亡掉落会触发同步加载卡顿。
        bool attachedToWorldModel = item != null &&
            ItemWorldPlacement.TryAttachWorldModelTransientItem(item, item.transform.position);
        bool worldModelActive = ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive;
        usesLegacyChunkOwnership = !attachedToWorldModel && !worldModelActive;
        LastChunk = usesLegacyChunkOwnership && item != null
            ? item.GetComponentInParent<Chunk>()
            : null;

        if (drop != null && item?.itemData?.Stack != null)
            item.itemData.Stack.CanBePickedUp = false;

        // 旧存档曾把水体运行态保存在 Drop 中；加载后只把当前位置交给新的水系统重建。
        if (drop != null && item != null && (drop.waterSinking || drop.waterFloating))
        {
            item.itemData.Stack.CanBePickedUp = true;
            WorldItemWaterSystem.TryEnterWater(item, requirePickable: false);
            drop = null;
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (drop == null)
        {
            Module.REMOVEModFROMItem(item, _Data);
            return;
        }

        // 掉落模块挂在掉落物自身，优先绑定宿主，避免注册顺序导致 GUID 反查失败。
        if (drop.item == null)
        {
            BindDropItemReference();
            if (drop.item == null)
                return;
        }

        drop.progressTime += deltaTime;
        float duration = Mathf.Max(0.0001f, drop.time);
        float t = Mathf.Clamp01(drop.progressTime / duration);

        // Chunk 归属按地面轨迹计算。贝塞尔高度和 arcHeight 只是表现层高度，
        // 不能让物品在抛起时误切换到上方相邻 Chunk。
        Vector2 ownershipPos = WorldTopologyRuntime.NormalizePosition(
            Vector2.Lerp(drop.startPos, drop.endPos, t));

        // 区块画面可能正在分帧绑定；新版掉落在动画期间重试归属，
        // 仍只访问 WorldModel，不触发旧 Chunk 加载。
        if (!usesLegacyChunkOwnership &&
            drop.item.GetComponentInParent<ChunkNaturalItemRenderer>(true) == null)
        {
            ItemWorldPlacement.TryAttachWorldModelTransientItem(drop.item, ownershipPos);
        }

        Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
        pos = WorldTopologyRuntime.NormalizePosition(pos);
        drop.item.transform.position = new Vector3(pos.x, pos.y, 0f);
        drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

        bool hasTargetChunk = !usesLegacyChunkOwnership ||
            UpdateChunkOwner(drop.item, ownershipPos);

        if (t < 1f)
            return;

        if (usesLegacyChunkOwnership && !hasTargetChunk)
        {
            RequestTargetChunk(Chunk.GetChunkPosition(
                WorldTopologyRuntime.NormalizePosition(drop.endPos)));
            return;
        }

        if (usesLegacyChunkOwnership)
        {
            // 确保 Chunk 内的位置索引记录最终落点，而不是动画起点。
            LastChunk.AddItem(drop.item);
        }

        Item landedItem = drop.item;
        WorldItemWaterSystem.EntryResult waterResult =
            WorldItemWaterSystem.TryEnterWater(landedItem, requirePickable: false);
        if (waterResult == WorldItemWaterSystem.EntryResult.Transformed)
            return;

        drop = null;
        Module.REMOVEModFROMItem(item, _Data);

        if (waterResult == WorldItemWaterSystem.EntryResult.Activated)
            return;

        landedItem.itemData.Stack.CanBePickedUp = true;
        // 地形查询若正处于 ChunkView 分帧绑定空窗，统一世界入口会在后续帧补做最终落点检查。
        WorldItemWaterSystem.ScheduleSpawnCheck(landedItem);
    }

    #endregion

    #region 归属与存档

    /// <summary>绑定掉落模块所属的物品，并同步修正旧存档中的物品 GUID。</summary>
    private void BindDropItemReference()
    {
        if (drop == null || item == null)
            return;

        drop.item = item;
        if (item.itemData != null)
            drop.itemGuid = item.itemData.Guid;
    }

    /// <summary>更新物品所属的 Chunk。只有确认目标 Chunk 可用后才解除旧归属。</summary>
    private bool UpdateChunkOwner(Item targetItem, Vector2 ownershipPos)
    {
        if (targetItem == null)
            return false;

        Vector2Int currentChunkPos = Chunk.GetChunkPosition(ownershipPos);

        if (LastChunk == null)
            LastChunk = targetItem.GetComponentInParent<Chunk>();

        if (IsChunkAtPosition(LastChunk, currentChunkPos))
        {
            if (targetItem.itemData != null &&
                !LastChunk.RunTimeItems.ContainsKey(targetItem.itemData.Guid))
            {
                LastChunk.AddItem(targetItem);
            }

            return true;
        }

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null ||
            !chunkMgr.TryGetActiveChunkByPos(currentChunkPos, out Chunk newChunk))
        {
            return false;
        }

        LastChunk?.RemoveItem(targetItem);
        newChunk.AddItem(targetItem);
        LastChunk = newChunk;
        return true;
    }

    private static bool IsChunkAtPosition(Chunk chunk, Vector2Int chunkPos)
    {
        if (chunk == null)
            return false;

        Vector2Int ownerPos = chunk.MapSave?.MapPosition
            ?? Chunk.GetChunkPosition(chunk.transform.position);
        return ownerPos == chunkPos;
    }

    private void RequestTargetChunk(Vector2Int chunkPos)
    {
        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return;

        chunkMgr.RequestLoadChunk_By_Position(chunkPos);
    }

    public override void Save()
    {
        modData.WriteData(drop);
        item.itemData.ModuleDataDic[modData.Name] = modData;
    }

    #endregion

    #region 轨迹工具

    /// <summary>二阶贝塞尔曲线计算。</summary>
    public static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }

    /// <summary>创建直线运动控制点。</summary>
    public static Vector2 CreateLinearControlPoint(Vector2 startPos, Vector2 endPos)
    {
        return (startPos + endPos) * 0.5f;
    }

    /// <summary>创建抛物线运动控制点。</summary>
    public static Vector2 CreateParabolicControlPoint(
        Vector2 startPos,
        Vector2 endPos,
        float bezierOffset)
    {
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;
        return mid;
    }

    /// <summary>静态丢弃物品方法，供外部模块调用。</summary>
    public static void StaticDropItem_Pos(
        Item item,
        Vector2 startPos,
        Vector2 endPos,
        float time,
        bool isLinear = false,
        float bezierOffset = 1f,
        float arcHeight = 1f,
        float minRotationSpeed = 360f,
        float maxRotationSpeed = 1080f)
    {
        startPos = WorldTopologyRuntime.NormalizePosition(startPos);
        endPos = WorldTopologyRuntime.NearestImagePosition(startPos, endPos);
        item.transform.position = startPos;

        Vector2 controlPos = isLinear
            ? CreateLinearControlPoint(startPos, endPos)
            : CreateParabolicControlPoint(startPos, endPos, bezierOffset);

        Mod_BaseDroper.Drop drop = new Mod_BaseDroper.Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = controlPos,
            time = time,
            progressTime = 0f,
            rotationSpeed = Random.Range(minRotationSpeed, maxRotationSpeed),
            item = item
        };

        item.itemData.Stack.CanBePickedUp = false;
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight;
    }

    /// <summary>静态丢弃物品（在指定半径范围内随机位置）。</summary>
    public static void StaticDropItemInARange(
        Item item,
        Vector2 startPos,
        float radius,
        float time,
        bool isLinear = false,
        float bezierOffset = 1f,
        float arcHeight = 1f,
        float minRotationSpeed = 360f,
        float maxRotationSpeed = 1080f)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(
            item,
            startPos,
            endPos,
            time,
            isLinear,
            bezierOffset,
            arcHeight,
            minRotationSpeed,
            maxRotationSpeed);
    }

    #endregion
}
