using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Tilemaps;

/// <summary>
/// 资源加载阶段创建的共享地块定义，不是 ScriptableObject，也不是每格实例。
/// 每个定义只创建一组 Behaviour；角色计时、Buff 层数和单格状态仍由角色运行器与世界模型持有。
/// 模板只读使用，创建单格快照必须调用 CreateTileData，禁止修改共享模板。
/// </summary>
public sealed class RuntimeTileDefinition
{
    #region 定义
    public string Id { get; }
    public int RuntimeTileId { get; }
    public string DisplayName { get; }
    public string TileAssetId { get; }
    public TileBase TileBase { get; }
    public TileData TileDataTemplate { get; }
    public IReadOnlyList<TileBlockBehaviour> Behaviours { get; }
    public TileBuildingDamageProfile DamageProfile { get; }
    public GroundTilePlacementRule GroundPlacement { get; }
    private readonly JObject source;

    internal RuntimeTileDefinition(TileDefinitionDto dto, TileBase tile, TileData template,
        List<TileBlockBehaviour> behaviours, JObject source)
    {
        Id = dto.Id;
        RuntimeTileId = dto.RuntimeTileId;
        DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id : dto.DisplayName;
        TileAssetId = dto.TileAsset;
        TileBase = tile;
        TileDataTemplate = template;
        Behaviours = behaviours.AsReadOnly();
        DamageProfile = dto.DamageProfile;
        GroundPlacement = dto.GroundPlacement;
        this.source = (JObject)source.DeepClone();
    }

    /// <summary>编辑器与 MOD Patch 获取独立配置副本，不暴露目录内部的可变 JSON。</summary>
    public JObject CopySource() => (JObject)source.DeepClone();

    /// <summary>创建独立地块数据；行为实例不随格子或角色复制。</summary>
    public TileData CreateTileData() => TileDataTemplate.Clone();

    // 保留旧消费代码常用名称，实际数据全部来自本运行时定义。
    public string name => Id;
    public string tileItemName => Id;
    public string displayName => DisplayName;
    public TileData tileDataTemplate => TileDataTemplate;
    public IReadOnlyList<TileBlockBehaviour> behaviours => Behaviours;
    public TileBuildingDamageProfile damageProfile => DamageProfile;
    public GroundTilePlacementRule groundPlacement => GroundPlacement;
    public TileBase GetTileBaseAsset() => TileBase;
    #endregion

    #region 稳定行为入口
    /// <summary>按 JSON 顺序执行进入行为。</summary>
    public void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        for (int i = 0; i < Behaviours.Count; i++)
            Behaviours[i].OnEnter(item, tileData, map, receiver);
    }

    /// <summary>按 JSON 顺序执行离开行为。</summary>
    public void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        for (int i = 0; i < Behaviours.Count; i++)
            Behaviours[i].OnExit(item, tileData, map, receiver);
    }

    /// <summary>共享行为读取调用方上下文，不在此创建对象或解析 JSON。</summary>
    public void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        for (int i = 0; i < Behaviours.Count; i++)
            Behaviours[i].OnUpdate(item, tileData, map, receiver, deltaTime);
    }
    #endregion
}
