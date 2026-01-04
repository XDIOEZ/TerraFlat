using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用地块逻辑行为
/// - 主要用于快速创建「没有特殊逻辑」或「只需要简单 Buff/特效」的通用地块行为类。
/// - 参考 Tile_Water 的结构，但不绑定水深等特殊含义。
/// 作为 TileBlockBehaviour 的具体实现，通过组合到 Tile_Block 中使用。
/// </summary>
[System.Serializable]
public class Tile_Universal : TileBlockBehaviour
{
    [Header("进入该地块时播放的特效名称(可选)")]
    [Tooltip("留空则不播放特效，例如：\"入水特效\"、\"踩到草特效\" 等")]
    public string enterEffectName;

    [Header("离开该地块时需要停止的特效名称(可选)")]
    [Tooltip("通常与进入时的特效同名；留空则不处理特效停止逻辑")]
    public string exitEffectName;

    [Header("进入地块时附加的 Buff 列表(可选)")]
    public List<Buff_Data> BuffInfo = new List<Buff_Data>();

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;

        // 通用进入特效（如果配置了名称）
        if (validItem && !string.IsNullOrEmpty(enterEffectName))
        {
            GameObject effectObj = VisualEffectManager.Instance.PlayEffect(
                owner: item.transform,
                effectName: enterEffectName,
                parent: item.transform
            );

            // 通用效果：默认不做位移偏移，直接挂在角色上即可
            // 如有需要，可在具体地块子类或策划侧通过特效本身做位置调整。
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

        // 停止特效（如果配置了名称）
        if (!string.IsNullOrEmpty(exitEffectName))
        {
            VisualEffectManager.Instance.StopOwnerEffect(
                owner: item.transform,
                effectName: exitEffectName
            );
        }
        else if (!string.IsNullOrEmpty(enterEffectName))
        {
            // 如果未单独配置退出特效名，则默认尝试停止进入特效名
            VisualEffectManager.Instance.StopOwnerEffect(
                owner: item.transform,
                effectName: enterEffectName
            );
        }

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
        // 通用地块默认不做持续效果，如需要可以在这里扩展：
        // 例如：持续减速、持续掉血、持续获得某种状态等。
    }
}
