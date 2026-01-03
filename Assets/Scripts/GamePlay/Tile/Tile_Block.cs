using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 抽象地块逻辑 ScriptableObject
/// 负责描述「踩在某个 Tile 上时」的进入 / 退出 / 持续效果接口。
/// 后续具体地块（如水、草）可以继承本类实现自己的逻辑。
/// </summary>
[System.Serializable]
public abstract class Tile_Block : ScriptableObject
{
    [Header("标识配置")]
    [Tooltip("用于和 TileData.Name_ItemName 对应，例如：TileItem_Water")]
    public string tileItemName;

    [Tooltip("策划可读的显示名称，仅用于编辑器显示")] 
    public string displayName;

    [Header("TileData 初始模板（用于生成地图数据）")]
    [Tooltip("作为该地块的默认数据模板，生成地图或放置方块时会从这里拷贝一份运行时 TileData")] 
    [SerializeReference]
    public TileData tileDataTemplate;

        [Header("对应的 Unity TileBase 资源")]
    public TileBase TileBase;

    /// <summary>
    /// 创建一份运行时使用的 TileData 拷贝
    /// </summary>
    public virtual TileData CreateRuntimeTileData()
    {
        // 使用 TileData 自带的 Clone 手写拷贝，避免通用深拷贝插件的额外开销
        return tileDataTemplate != null ? tileDataTemplate.Clone() : null;
    }

    /// <summary>
    /// 获取用于渲染到 Tilemap 上的 Unity TileBase（默认无，子类可重写）
    /// </summary>
    public virtual UnityEngine.Tilemaps.TileBase GetTileBaseAsset()
    {
        return TileBase;
    }

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

    /// <summary>
    /// 在编辑器中自动校正 TileData 模板的 ID 和 Name，避免手动填写
    /// </summary>
    private void OnValidate()
    {
        if (tileDataTemplate == null)
            return;

        // 优先使用 tileItemName，未填写则退回到 SO 资源名
        string keyName = !string.IsNullOrEmpty(tileItemName) ? tileItemName : name;
        if (string.IsNullOrEmpty(keyName))
            return;

        // 始终同步 Name，供 TileEffectReceiver 通过 GameRes.GetTileBlock 查找
        tileDataTemplate.Name = keyName;

        // 仅在 ID 为空时填充，避免覆盖那些有特殊含义（如 TileBase 名称）的配置
        if (string.IsNullOrEmpty(tileDataTemplate.ID))
        {
            tileDataTemplate.ID = keyName;
        }
    }
}
