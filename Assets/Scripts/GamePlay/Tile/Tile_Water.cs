using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 水体地块逻辑 ScriptableObject
/// 负责处理进入 / 离开水地块时的特效与 Buff 效果。
/// </summary>
[CreateAssetMenu(menuName = "TileBlock/Water", fileName = "Tile_Water")]
public class Tile_Water : Tile_Block
{
    [Header("进入水体时附加的 Buff 列表")]
    public List<Buff_Data> BuffInfo = new List<Buff_Data>();

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;

        // 入水特效
        if (validItem)
        {
            GameObject effectObj = VisualEffectManager.Instance.PlayEffect(
                owner: item.transform,
                effectName: "入水特效",
                parent: item.transform
            );

            if (effectObj != null)
            {
                Transform fxTransform = effectObj.transform;
                Vector3 pos = fxTransform.localPosition;

                // 通过 TileData_Water 的深度调整特效位置
                TileData_Water water = tileData as TileData_Water;
                float depthValue = water != null ? water.DeepValue.Value : 0f;

                pos.y = Mathf.Lerp(-0.7f, 0f, depthValue);
                pos.x = 0f;
                fxTransform.localPosition = pos;
            }
        }

        // Buff 添加逻辑
        if (!validItem || buffManager == null || BuffInfo == null || BuffInfo.Count == 0)
            return;

        foreach (Buff_Data buffData in BuffInfo)
        {
            if (buffData == null)
                continue;

            buffManager.AddBuffRuntime(buffData, item);
        }
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        // 停止入水特效
        VisualEffectManager.Instance.StopOwnerEffect(
            owner: item.transform,
            effectName: "入水特效"
        );

        // 移除 Buff
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null || BuffInfo == null)
            return;

        foreach (Buff_Data buffData in BuffInfo)
        {
            if (buffData == null)
                continue;

            if (buffManager.HasBuff(buffData.buff_ID))
            {
                buffManager.RemoveBuff(buffData.buff_ID);
            }
        }
    }

    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        // 需要在水中持续生效的逻辑可以写在这里（例如持续减速 / 掉血）
    }
}
