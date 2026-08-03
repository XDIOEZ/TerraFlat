using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum TileDamageToolKind
{
    None,
    Pickaxe,
    Axe,
    Hammer
}

[System.Serializable]
public sealed class TileBuildingDamageProfile
{
    [Tooltip("开启后，此 Tile 可由格子建筑伤害系统扣血和摧毁。")]
    public bool Damageable;

    [Min(1f)]
    public float MaxHealth = 100f;

    [Min(0f)]
    public float Defense;

    [Tooltip("None 表示不限制工具类型。石墙/岩壁应配置为 Pickaxe。")]
    public TileDamageToolKind RequiredTool = TileDamageToolKind.None;

    [Tooltip("开启后，攻击至少要命中一项弱点标签才会造成伤害。")]
    public bool RequireWeaknessMatch;

    public List<DamageType> Weakness = new List<DamageType>();

    public CombatImpactMaterial ImpactMaterial = CombatImpactMaterial.Default;

    [Tooltip("摧毁后掉落的 Item ID；留空则不掉落。")]
    public string DropItemId;

    [Min(0)]
    public int DropAmount;
}

/// <summary>
/// 地块逻辑 ScriptableObject
/// 负责描述「踩在某个 Tile 上时」的进入 / 退出 / 持续效果接口。
/// 通过组合 TileBlockBehaviour，而不是继承本类，来实现具体逻辑。
/// </summary>
[System.Serializable]
[CreateAssetMenu(menuName = "TileBlock/Block", fileName = "Tile_Block")]
public class Tile_Block : ScriptableObject
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

    [Header("格子建筑伤害")]
    public TileBuildingDamageProfile damageProfile = new TileBuildingDamageProfile();

    [Header("逻辑行为列表（组合方式")]
    [Tooltip("按顺序执行的地块逻辑行为列表，通过 SerializeReference 支持多态，多种逻辑可以叠加生效")]
    [SerializeReference]
    public List<TileBlockBehaviour> behaviours = new List<TileBlockBehaviour>();

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
    public void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (behaviours == null || behaviours.Count == 0)
            return;

        for (int i = 0; i < behaviours.Count; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            b.OnEnter(item, tileData, map, receiver);
        }
    }

    /// <summary>
    /// 离开该地块时调用
    /// </summary>
    public void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (behaviours == null || behaviours.Count == 0)
            return;

        for (int i = 0; i < behaviours.Count; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            b.OnExit(item, tileData, map, receiver);
        }
    }

    /// <summary>
    /// 每帧在该地块上时调用（可选）
    /// </summary>
    public void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        if (behaviours == null || behaviours.Count == 0)
            return;

        for (int i = 0; i < behaviours.Count; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            b.OnUpdate(item, tileData, map, receiver, deltaTime);
        }
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
