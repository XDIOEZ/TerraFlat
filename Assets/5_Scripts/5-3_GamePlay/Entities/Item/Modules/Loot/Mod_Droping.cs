using UnityEngine;

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

	#endregion

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

        // 掉落物先尝试绑定新版 ChunkView。新区块窗口已启用时，即使当前画面尚未
        // 完成绑定也不能回退到旧 Chunk 查询，否则灌木死亡掉落会触发同步加载卡顿。
        bool attachedToWorldModel = item != null &&
            ItemWorldPlacement.TryAttachWorldModelDrop(item, item.transform.position);
        bool worldModelActive = ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive;
        usesLegacyChunkOwnership = !attachedToWorldModel && !worldModelActive;
        LastChunk = usesLegacyChunkOwnership && item != null
            ? item.GetComponentInParent<Chunk>()
            : null;
        item.itemData.Stack.CanBePickedUp = false;
    }

    public override void ModUpdate(float deltaTime)
    {
        // 检测droping是否为空，如果为空自动销毁模块本身
        if (drop == null)
        {
            Module.REMOVEModFROMItem(item, _Data);
            return;
        }

        // 检查物品是否为空，如果为空则尝试重新获取
        if (drop.item == null)
        {
            drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
            if (drop.item == null)
            {
                Debug.LogError("丢弃物品丢失");
                drop.item = item;
                return;
            }
        }

        // 更新进度时间并计算插值参数
        drop.progressTime += deltaTime;
        float duration = Mathf.Max(0.0001f, drop.time);
        float t = Mathf.Clamp01(drop.progressTime / duration);

        // Chunk 归属按地面轨迹计算。贝塞尔高度和 arcHeight 只是表现层高度，
        // 不能让物品在抛起时误切换到上方相邻 Chunk。
        Vector2 ownershipPos = WorldTopologyRuntime.NormalizePosition(Vector2.Lerp(drop.startPos, drop.endPos, t));

        // 区块画面可能正在分帧绑定；新版掉落在动画期间重试归属，
        // 仍只访问 WorldModel，不触发旧 Chunk 加载。
        if (!usesLegacyChunkOwnership &&
            drop.item.GetComponentInParent<ChunkNaturalItemRenderer>(true) == null)
        {
            ItemWorldPlacement.TryAttachWorldModelDrop(drop.item, ownershipPos);
        }

        // 使用存储在drop中的控制点进行贝塞尔插值计算位置
        Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);

        // 垂直方向叠加正弦高度，形成抛物线效果
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

        // 更新物品位置和旋转
        pos = WorldTopologyRuntime.NormalizePosition(pos);
        drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
        drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

        // 旧 Chunk 物品仍同步归属；新版生态物品保持在 NaturalItems 下，不触发旧区块加载。
        bool hasTargetChunk = !usesLegacyChunkOwnership ||
            UpdateChunkOwner(drop.item, ownershipPos);

        // 检查动画是否完成
        if (t >= 1f)
        {
            if (usesLegacyChunkOwnership && !hasTargetChunk)
            {
                RequestTargetChunk(Chunk.GetChunkPosition(WorldTopologyRuntime.NormalizePosition(drop.endPos)));
                return;
            }

            if (usesLegacyChunkOwnership)
            {
                // 确保 Chunk 内的位置索引记录的是最终落点，而不是动画起点。
                LastChunk.AddItem(drop.item);
            }
            drop.item.itemData.Stack.CanBePickedUp = true;
            drop = null; // 销毁droping
        }
    }

    /// <summary>
    /// 更新物品所属的 Chunk。只有确认目标 Chunk 可用后才解除旧归属。
    /// </summary>
    private bool UpdateChunkOwner(Item targetItem, Vector2 ownershipPos)
    {
        if (targetItem == null)
            return false;

        Vector2Int currentChunkPos = Chunk.GetChunkPosition(ownershipPos);

        // 新掉落物可能已被 ItemMgr 挂到 Chunk 下，但 LastChunk 尚未初始化。
        if (LastChunk == null)
            LastChunk = targetItem.GetComponentInParent<Chunk>();

        if (IsChunkAtPosition(LastChunk, currentChunkPos))
        {
            // 显式 parent 实例化不会自动写入 Chunk 的运行时字典，这里补齐一次。
            if (targetItem.itemData != null && !LastChunk.RunTimeItems.ContainsKey(targetItem.itemData.Guid))
                LastChunk.AddItem(targetItem);

            return true;
        }

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null || !chunkMgr.TryGetActiveChunkByPos(currentChunkPos, out Chunk newChunk))
            return false;

        // 先确认新 Chunk，再从旧 Chunk 移除，避免加载边缘出现无归属物品。
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

        // ChunkMgr 内部会对相同坐标的请求去重；每帧重试可避免加载队列
        // 因场景切换或快速移动被清空后，掉落物永久停留在等待状态。
        chunkMgr.RequestLoadChunk_By_Position(chunkPos);
    }

    public override void Save()
    {
        modData.WriteData(drop);
        item.itemData.ModuleDataDic[modData.Name] = modData;
    }

    /// <summary>
    /// 二阶贝塞尔曲线计算
    /// </summary>
    /// <param name="p0">起点</param>
    /// <param name="p1">控制点</param>
    /// <param name="p2">终点</param>
    /// <param name="t">插值参数(0-1)</param>
    /// <returns>插值位置</returns>
    public static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }
    
    /// <summary>
    /// 创建直线运动的控制点（三点共线实现直线移动）
    /// </summary>
    /// <param name="startPos">起点</param>
    /// <param name="endPos">终点</param>
    /// <returns>控制点位置</returns>
    public static Vector2 CreateLinearControlPoint(Vector2 startPos, Vector2 endPos)
    {
        // 控制点设为起点和终点的中点，实现直线移动
        return (startPos + endPos) * 0.5f;
    }
    
    /// <summary>
    /// 创建抛物线运动的控制点
    /// </summary>
    /// <param name="startPos">起点</param>
    /// <param name="endPos">终点</param>
    /// <param name="bezierOffset">控制点垂直偏移量</param>
    /// <returns>控制点位置</returns>
    public static Vector2 CreateParabolicControlPoint(Vector2 startPos, Vector2 endPos, float bezierOffset)
    {
        // 计算二阶贝塞尔控制点：中点向上偏移
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;
        return mid;
    }
    
    /// <summary>
    /// 静态丢弃物品方法，供外部模块调用
    /// </summary>
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        startPos = WorldTopologyRuntime.NormalizePosition(startPos);
        endPos = WorldTopologyRuntime.NearestImagePosition(startPos, endPos);
        item.transform.position = startPos;

        // 根据是否直线运动计算控制点
        Vector2 controlPos;
        if (isLinear)
        {
            controlPos = CreateLinearControlPoint(startPos, endPos);
        }
        else
        {
            controlPos = CreateParabolicControlPoint(startPos, endPos, bezierOffset);
        }

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
        
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight; // 传递弧高参数
        item.itemData.Stack.CanBePickedUp = false;
    }
    
    /// <summary>
    /// 静态丢弃物品（在指定半径范围内随机位置）
    /// </summary>
    public static void StaticDropItemInARange(Item item, Vector2 startPos, float radius, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(item, startPos, endPos, time, isLinear, bezierOffset, arcHeight, minRotationSpeed, maxRotationSpeed);
    }
}
