using UnityEngine;

public class Mod_Droping : Module
{

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public Mod_ItemDroper.Drop drop;
    public Ex_ModData modData;
    public Chunk LastChunk; // 上一帧 item 所处的 chunk

    [Header("丢弃动画参数")]
    [Tooltip("垂直方向最大高度（与之前一致）")]
    public float arcHeight = 1f;

    public override void Awake()
    {
         _Data.ID = ModText.Drop;
    }

    public override void Load()
    {
        modData.ReadData(ref drop);
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
        float t = Mathf.Clamp01(drop.progressTime / drop.time);

        // 使用存储在drop中的控制点进行贝塞尔插值计算位置
        Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);

        // 垂直方向叠加正弦高度，形成抛物线效果
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

        // 更新物品位置和旋转
        drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
        drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

        // 更新 Chunk 归属
        UpdateChunkOwner(drop.item);

        // 检查动画是否完成
        if (t >= 1f)
        {
            drop.item.itemData.Stack.CanBePickedUp = true;
            drop = null; // 销毁droping
        }
    }

/// <summary>
/// 更新物品所属的 Chunk
/// </summary>
/// <param name="item">需要更新归属的物品</param>
private void UpdateChunkOwner(Item item)
{
    // 获取当前物品所在的 Chunk 坐标
    Vector2Int currentChunkPos = Chunk.GetChunkPosition(item.transform.position);
    string currentChunkKey = currentChunkPos.ToString();

    // 如果 LastChunk 为空或者需要切换 Chunk
    if (LastChunk == null || LastChunk.MapSave.MapPosition != currentChunkPos)
    {
        // 如果有旧的 Chunk，则从旧 Chunk 中移除物品
        if (LastChunk != null)
        {
            LastChunk.RemoveItem(item);
        }

        // 添加到新的 Chunk
        if (ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(currentChunkKey, out Chunk newChunk))
        {
            newChunk.AddItem(item);
            LastChunk = newChunk;
        }
        else
        {
            // 如果目标 Chunk 不存在，尝试获取最近的 Chunk

                Debug.LogError("无法找到合适的 Chunk 来放置物品");
                LastChunk = null;
            
        }
    }
}

    public override void Save()
    {
        modData.WriteData(drop);
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

        Mod_ItemDroper.Drop drop = new Mod_ItemDroper.Drop
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