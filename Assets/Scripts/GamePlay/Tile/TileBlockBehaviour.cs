using UnityEngine;

/// <summary>
/// 地块逻辑行为基类，通过组合到 Tile_Block 中使用。
/// 具体地块（如水、草、通用等）的进入 / 退出 / 持续效果实现应继承此类。
/// （普通可序列化类，不再继承 ScriptableObject）
/// </summary>
[System.Serializable]
public abstract class TileBlockBehaviour
{
    /// <summary>
    /// 进入该地块时调用
    /// </summary>
    public virtual void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
    }

    /// <summary>
    /// 离开该地块时调用
    /// </summary>
    public virtual void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
    }

    /// <summary>
    /// 每帧在该地块上时调用（可选）
    /// </summary>
    public virtual void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
    }
}
