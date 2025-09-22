using Sirenix.Reflection.Editor;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Mod_ItemDroper;

public class Mod_Droping : Module
{
    public override ModuleData _Data { get =>modData; set => modData = (Ex_ModData)value; }
    public Mod_ItemDroper.Drop drop;
    public Ex_ModData modData;

    [Header("丢弃动画参数")]
    [Tooltip("二阶贝塞尔控制点相对于起点终点的偏移量")]
    public float bezierOffset = 1f;          // 控制点高度
    public float arcHeight = 1f;             // 垂直方向最大高度（与之前一致）
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Drop;
        }
    }
    public override void Load()
    {
        modData.ReadData(ref drop);
      
    }
public override void ModUpdate(float deltaTime)
{
    //检测droping是否为空
    //如果为空自动销毁模块本身
    
    if (drop.item == null)
    {
        drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
        if(drop.item == null)
        {
            Debug.LogError("丢弃物品丢失");
            drop.item = item;
            return;
        }
    }

    drop.progressTime += deltaTime;
    float t = Mathf.Clamp01(drop.progressTime / drop.time);

    // 二阶贝塞尔插值
    Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
    // 垂直方向叠加正弦高度
    pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

    drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
    // 使用Drop中的旋转速度代替固定的旋转速度
    drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

    //TODO 归纳到对应Chunk中
    ChunkMgr.Instance.UpdateItem_ChunkOwner(drop.item);

    if (t >= 1f)
    {
        drop.item.itemData.Stack.CanBePickedUp = true;
        drop = null;//销毁droping
    }
    if (drop == null)
    {
        Module.REMOVEModFROMItem(item, _Data);
        return;
    }
}

    public override void Save()
    {
        modData.WriteData(drop);
    }
    private Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }
}
