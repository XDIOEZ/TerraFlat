using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemDroper : Module
{
    /// <summary>
    /// 物品移动模式
    /// </summary>
    public enum MoveMode
    {
        BezierCurve,    // 贝塞尔曲线（抛物线）
        StraightLine    // 直线运动
    }
    
    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    [Tooltip("当前正在进行的物品丢弃列表")]
    public List<Drop> drops = new List<Drop>();

    [Header("丢弃动画参数")]
    [Tooltip("默认移动模式")]
    public MoveMode defaultMoveMode = MoveMode.BezierCurve;
    
    [Tooltip("二阶贝塞尔控制点相对于起点终点的偏移量")]
    public float bezierOffset = 1f;          // 控制点高度
    
    [Tooltip("垂直方向最大高度（与之前一致）")]
    public float arcHeight = 1f;             // 垂直方向最大高度（与之前一致）

    /* ----------------------------------------------------------
     * 丢弃物品（自动随机终点）
     * ----------------------------------------------------------*/
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.ItemDorper;
        }
    }

    [Tooltip("丢弃物品（自动随机终点）")]
    public void DropItem_Range(Item item, Vector2 startPos, float radius, float time)
    {
        item.transform.position = startPos;

        // 随机终点
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;

        // 根据移动模式计算控制点
        Vector2 controlPos = CalculateControlPoint(startPos, endPos, defaultMoveMode);

        Drop drop = new Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = controlPos,
            time = time,
            progressTime = 0f,
            item = item
        };

        item.itemData.Stack.CanBePickedUp = false;
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight; // 传递弧高参数
        drops.Add(drop);
    }

    /// <summary>
    /// 丢弃物品（指定起点与终点）
    /// </summary>
    [Tooltip("丢弃物品（指定起点与终点）")]
    public void DropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time)
    {
        StaticDropItem_Pos(item, startPos, endPos, time, defaultMoveMode);
    }

    /// <summary>
    /// 丢弃物品（在指定半径范围内随机位置）
    /// </summary>
    [Tooltip("丢弃物品（在指定半径范围内随机位置")]
    public static void DropItemInARange(Item item, Vector2 startPos,float radius, float time)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(item, startPos, endPos, time);
    }

    /// <summary>
    /// 【静态方法】供外部模块（如Mod_DeathLoot）调用，复用掉落动画逻辑
    /// </summary>
    [Tooltip("静态丢弃物品方法，供外部模块调用")]
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time, MoveMode mode = MoveMode.BezierCurve, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        item.transform.position = startPos;

        // 根据移动模式计算控制点
        Vector2 controlPos = CalculateControlPoint(startPos, endPos, mode, bezierOffset);

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
    /// 根据移动模式计算控制点
    /// </summary>
    /// <param name="startPos">起点</param>
    /// <param name="endPos">终点</param>
    /// <param name="mode">移动模式</param>
    /// <param name="bezierOffset">贝塞尔偏移量</param>
    /// <returns>控制点位置</returns>
    private static Vector2 CalculateControlPoint(Vector2 startPos, Vector2 endPos, MoveMode mode, float bezierOffset = 1f)
    {
        Vector2 mid = (startPos + endPos) * 0.5f;
        
        switch (mode)
        {
            case MoveMode.StraightLine:
                // 直线模式，控制点就在中点上，形成直线
                return mid;
            case MoveMode.BezierCurve:
            default:
                // 贝塞尔曲线模式，控制点向上偏移
                mid.y += bezierOffset;
                return mid;
        }
    }
    
    /* ----------------------------------------------------------
     * 每帧更新
     * ----------------------------------------------------------*/
    [Tooltip("每帧更新丢弃动画")]
    public override void ModUpdate(float deltaTime)
    {
        if (!_Data.isRunning) return;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];

            if (drop.item == null) { drops.RemoveAt(i); continue; }

            drop.progressTime += deltaTime;
            float t = Mathf.Clamp01(drop.progressTime / drop.time);

            // 使用存储在drop中的控制点进行贝塞尔插值
            Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
            // 垂直方向叠加正弦高度
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

            drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
            drop.item.transform.Rotate(Vector3.forward * 360f * deltaTime);

            if (t >= 1f)
            {
                drops.RemoveAt(i);
                drop.item.itemData.Stack.CanBePickedUp = true;
            }
        }
    }

    /* ----------------------------------------------------------
     * 二阶贝塞尔曲线
     * ----------------------------------------------------------*/
    [Tooltip("计算二阶贝塞尔曲线上的点")]
    private static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }

    /* ----------------------------------------------------------
     * 序列化/反序列化
     * ----------------------------------------------------------*/
    [Tooltip("加载丢弃数据")]
    public override void Load()
    {
        modData.ReadData(ref drops);

        // 临时列表，记录需要移除的 drop
        List<Drop> toRemove = new List<Drop>();

        foreach (Drop drop in drops)
        {
            drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
            if (drop.item == null)
                toRemove.Add(drop); // 延迟移除
        }

        // 统一移除
        foreach (Drop drop in toRemove)
        {
            drops.Remove(drop);
        }
    }

    [Tooltip("保存丢弃数据")]
    public override void Save()
    {
        modData.WriteData(drops);
    }

    /* ----------------------------------------------------------
     * 数据结构
     * ----------------------------------------------------------*/
    [System.Serializable]
    public class Drop
    {
        [Tooltip("物品的唯一标识符")]
        public int itemGuid;

        // 原来的 Vector2 改为 x 和 y 两个 float
        [Tooltip("起点X坐标")]
        public float startX, startY;
        
        [Tooltip("终点X坐标")]
        public float endX, endY;
        
        [Tooltip("控制点X坐标")]
        public float controlX, controlY;

        [Tooltip("总时间")]
        public float time;
        
        [Tooltip("已进行的时间")]
        public float progressTime;
        
        [Tooltip("每秒旋转角度（单位：度）")]
        public float rotationSpeed = 360f;
        
        [JsonIgnore]
        [ShowInInspector]
        [System.NonSerialized] 
        [Tooltip("物品引用")]
        public Item item;

        // 属性封装，方便使用 Vector2 接口
        [JsonIgnore]
        public Vector2 startPos
        {
            get => new Vector2(startX, startY);
            set { startX = value.x; startY = value.y; }
        }
        
        [JsonIgnore]
        public Vector2 endPos
        {
            get => new Vector2(endX, endY);
            set { endX = value.x; endY = value.y; }
        }
        
        [JsonIgnore]
        public Vector2 controlPos
        {
            get => new Vector2(controlX, controlY);
            set { controlX = value.x; controlY = value.y; }
        }
    }
}