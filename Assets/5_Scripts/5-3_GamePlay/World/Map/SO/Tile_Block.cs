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

    [Tooltip("格子建筑的切割、穿刺、劈砍、钝击防御。")]
    public CombatDefense DefenseValues = new CombatDefense();

    [Tooltip("None 表示任意武器可攻击；天然岩壁等资源地块可配置为 Pickaxe。")]
    public TileDamageToolKind RequiredTool = TileDamageToolKind.None;

    [Min(0f)]
    [Tooltip("通过工具限制后，任意有攻击力的武器对该建筑至少造成的伤害；0 表示不保底。")]
    public float MinimumWeaponDamage;

    public CombatImpactMaterial ImpactMaterial = CombatImpactMaterial.Default;

    [Tooltip("摧毁后掉落的 Item ID；留空则不掉落。")]
    public string DropItemId;

    [Min(0)]
    public int DropAmount;

    /// <summary>读取并校正格子建筑的四类防御。</summary>
    public CombatDefense ResolveDefense()
    {
        DefenseValues ??= new CombatDefense();
        DefenseValues.ClampNonNegative();
        return DefenseValues;
    }
}

/// <summary>
/// 旧 Prefab、群系和结构编辑器使用的地块 ID 引用壳。
/// 这里只序列化稳定 ID；数值、行为和 TileBase 引用统一解析 JSON 运行时定义，不能另存一份 SO 配置。
/// </summary>
[System.Serializable]
[CreateAssetMenu(menuName = "TileBlock/JSON Definition Reference", fileName = "Tile_Block")]
public class Tile_Block : ScriptableObject
{
    #region JSON 定义引用
    [Header("地块 JSON 的稳定 ID")]
    [Tooltip("仅作为旧编辑器和 Prefab 的引用；实际配置位于 GameConfig/Tiles。")]
    public string tileItemName;
    public string DefinitionId => string.IsNullOrWhiteSpace(tileItemName) ? name : tileItemName;
    public RuntimeTileDefinition Definition => ResolveDefinition();
    public string displayName => Definition.DisplayName;
    public TileData tileDataTemplate => Definition.TileDataTemplate;
    public TileBase TileBase => Definition.TileBase;
    public GroundTilePlacementRule groundPlacement => Definition.GroundPlacement;
    public GroundTileHarvestRule groundHarvest => Definition.GroundHarvest;
    public TileBuildingDamageProfile damageProfile => Definition.DamageProfile;
    public IReadOnlyList<TileBlockBehaviour> behaviours => Definition.Behaviours;

#if UNITY_EDITOR
    /// <summary>由编辑器 JSON 目录注入，运行时程序集不依赖 Editor 程序集。</summary>
    public static System.Func<string, RuntimeTileDefinition> EditorDefinitionResolver;
#endif

    /// <summary>不自动创建 GameRes；编辑器预览与真实资源会话严格分开。</summary>
    private RuntimeTileDefinition ResolveDefinition()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && EditorDefinitionResolver != null)
            return EditorDefinitionResolver(DefinitionId);
#endif
        RuntimeTileDefinition definition = GameRes.ExistingInstance?.GetTileBlock(DefinitionId);
        return definition ?? throw new System.InvalidOperationException($"地块 JSON 定义尚未加载：{DefinitionId}");
    }
    #endregion

    #region 旧调用兼容入口
    public virtual TileBase GetTileBaseAsset() => Definition.GetTileBaseAsset();

    /// <summary>旧地图转发到 JSON 创建的共享行为。</summary>
    public void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
        => Definition.OnEnter(item, tileData, map, receiver);

    public void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
        => Definition.OnExit(item, tileData, map, receiver);

    public void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
        => Definition.OnUpdate(item, tileData, map, receiver, deltaTime);
    #endregion
}
