using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemDroper : Module
{
    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    public List<Drop> drops = new List<Drop>();

    [Header("丢弃动画参数")]
    [Tooltip("二阶贝塞尔控制点相对于起点终点的偏移量")]
    public float bezierOffset = 1f;          // 控制点高度
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

    public void DropItem_Range(Item item, Vector2 startPos, float radius, float time)
    {
        item.transform.position = startPos;

        // 随机终点
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;

        // 计算二阶贝塞尔控制点：起点-终点的中点向上偏移
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;

        Drop drop = new Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = mid,
            time = time,
            progressTime = 0f,
            item = item
        };

        item.itemData.Stack.CanBePickedUp = false;
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.drop = drop;
        drops.Add(drop);
    }

    /// <summary>
    /// 丢弃物品（指定起点与终点）
    /// </summary>
    public void DropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time)
    {
        StaticDropItem_Pos(item, startPos, endPos, time);
    }

    /// <summary>
    /// 【静态方法】供外部模块（如Mod_DeathLoot）调用，复用掉落动画逻辑
    /// </summary>
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time,float bezierOffset = 1f, float arcHeight = 1f)
    {
        item.transform.position = startPos;

        // 计算二阶贝塞尔控制点：中点向上偏移（使用模块的bezierOffset）
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;

        Mod_ItemDroper.Drop drop = new Mod_ItemDroper.Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = mid,
            time = time,
            progressTime = 0f,
            item = item
        };
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.drop = drop;
        item.itemData.Stack.CanBePickedUp = false;

        itemDrop.drop = drop;
    }


    /* ----------------------------------------------------------
     * 每帧更新
     * ----------------------------------------------------------*/
    public override void Action(float deltaTime)
    {
        if (!_Data.isRunning) return;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];

            if (drop.item == null) { drops.RemoveAt(i); continue; }

            drop.progressTime += deltaTime;
            float t = Mathf.Clamp01(drop.progressTime / drop.time);

            // 二阶贝塞尔插值
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
    private static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }

    /* ----------------------------------------------------------
     * 序列化/反序列化
     * ----------------------------------------------------------*/
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
        public int itemGuid;

        // 原来的 Vector2 改为 x 和 y 两个 float
        public float startX, startY;
        public float endX, endY;
        public float controlX, controlY;

        public float time;
        public float progressTime;
        [JsonIgnore]
        [ShowInInspector]
        [System.NonSerialized] public Item item;

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