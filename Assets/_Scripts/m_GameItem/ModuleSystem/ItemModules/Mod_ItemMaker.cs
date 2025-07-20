using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemMaker : Module
{
    public Ex_ModData modData;
    public override ModuleData Data { get => modData; set => modData = (Ex_ModData)value; }

    public List<Drop> drops = new List<Drop>();

    [Header("丢弃动画参数")]
    [Tooltip("二阶贝塞尔控制点相对于起点终点的偏移量")]
    public float bezierOffset = 1f;          // 控制点高度
    public float arcHeight = 1f;             // 垂直方向最大高度（与之前一致）

    /* ----------------------------------------------------------
     * 丢弃物品（自动随机终点）
     * ----------------------------------------------------------*/
    public void DropItem(Item item, Vector2 startPos, float radius, float time)
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
            itemGUID = item.Item_Data.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = mid,
            time = time,
            progressTime = 0f,
            item = item
        };

        drops.Add(drop);
        item.Item_Data.Stack.CanBePickedUp   = false;
    }

    /* ----------------------------------------------------------
     * 每帧更新
     * ----------------------------------------------------------*/
    public void Update()
    {
        if (!Data.isRunning) return;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];

            if (drop.item == null) { drops.RemoveAt(i); continue; }

            drop.progressTime += Time.deltaTime;
            float t = Mathf.Clamp01(drop.progressTime / drop.time);

            // 二阶贝塞尔插值
            Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
            // 垂直方向叠加正弦高度
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

            drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
            drop.item.transform.Rotate(Vector3.forward * 360f * Time.deltaTime);

            if (t >= 1f)
            {
                drops.RemoveAt(i);
                drop.item.Item_Data.Stack.CanBePickedUp = true;
            }
        }
    }

    /* ----------------------------------------------------------
     * 二阶贝塞尔曲线
     * ----------------------------------------------------------*/
    private Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
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

        // 通过 GUID 还原动画对象
        foreach (Drop drop in drops)
        {
            drop.item = RunTimeItemManager.Instance.GetItemByGuid(drop.itemGUID);
            // 如果场景里没有该物品，说明已销毁，直接移除
            if (drop.item == null)
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
        public int itemGUID;

        // 原来的 Vector2 改为 x 和 y 两个 float
        public float startX, startY;
        public float endX, endY;
        public float controlX, controlY;

        public float time;
        public float progressTime;
        [JsonIgnore]
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