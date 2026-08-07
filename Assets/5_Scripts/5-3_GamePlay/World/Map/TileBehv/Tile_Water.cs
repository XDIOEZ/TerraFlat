using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 水体地块逻辑行为
/// 负责处理进入 / 离开水地块时的特效与 Buff 效果。
/// 作为 TileBlockBehaviour 的具体实现，通过组合到 Tile_Block 中使用。
/// </summary>
[System.Serializable]
public class Tile_Water : TileBlockBehaviour
{
    [Header("进入水体时附加的 Buff 列表")]
    public List<string> BuffInfo = new List<string>();

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;
        // 入水效果改为：根据水深修改 Shader 的 _BodyClip，实现下半身剔除插值
        if (validItem)
        {
            // 通过 TileData_Water 的深度(0-1)，直接作为身体剔除比例使用：
            // 0 = 完全不剔除，1 = 完全从脚到底部剔除
            TileData_Water water = tileData as TileData_Water;

            float depthValue = water != null ? Mathf.Clamp01(water.deepValue) : 0f;

            // 对物体及子物体的 Renderer 应用 PropertyBlock，避免改动共享材质
            var renderers = item.GetComponentsInChildren<Renderer>();
            
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);

                // 使用 Sprite-Lit-Master.shader 中的 _BodyClip 控制下半身剔除
                block.SetFloat("_BodyClip", depthValue);

                r.SetPropertyBlock(block);
            }
        }

        // Buff 添加逻辑
        if (!validItem || buffManager == null || BuffInfo == null || BuffInfo.Count == 0)
            return;

        foreach (string buffId in BuffInfo)
        {
            if (string.IsNullOrWhiteSpace(buffId))
                continue;

            buffManager.AddBuff(buffId);
        }
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
        // 退出水面时，重置 Shader 中的身体剔除参数
        var renderers = item.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);

            // 恢复为 0，显示整个人物
            block.SetFloat("_BodyClip", 0f);

            r.SetPropertyBlock(block);
        }

        // 移除 Buff
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null || BuffInfo == null)
            return;

        foreach (string buffId in BuffInfo)
        {
            if (string.IsNullOrWhiteSpace(buffId))
                continue;

            if (buffManager.HasBuff(buffId))
                buffManager.RemoveBuff(buffId);
        }
    }

    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        // 需要在水中持续生效的逻辑可以写在这里（例如持续减速 / 掉血）
    }
}
