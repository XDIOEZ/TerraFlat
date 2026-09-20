using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>地块运行时目录；字符串身份用于配置和 MOD，稳定整数身份用于世界格子和表现映射。</summary>
public partial class GameRes
{
    #region 地块目录
    private readonly Dictionary<int, RuntimeTileDefinition> tileDefinitionsByNumber = new();

    /// <summary>全部校验成功后发布本体目录；本方法只在无活动世界的资源加载阶段调用。</summary>
    public void ReplaceTileDefinitions(IReadOnlyList<RuntimeTileDefinition> definitions)
    {
        TileDefinitionFactory.ValidateIdentities(definitions);
        ClearTileDefinitions();
        foreach (RuntimeTileDefinition definition in definitions) RegisterTileDefinition(definition);
    }

    /// <summary>注册 MOD 地块或显式替换同身份定义，禁止替换过程中改变稳定数字编号。</summary>
    public void RegisterTileDefinition(RuntimeTileDefinition definition, bool replaceExisting = false)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        TileBlockDict.TryGetValue(definition.Id, out RuntimeTileDefinition previous);
        if (previous != null && !replaceExisting) throw new InvalidDataException($"重复地块 ID：{definition.Id}");
        if (previous != null && previous.RuntimeTileId != definition.RuntimeTileId)
            throw new InvalidDataException($"地块 {definition.Id} 的稳定 runtimeTileId 不能在覆盖时改变。");
        if (definition.RuntimeTileId > 0 && tileDefinitionsByNumber.TryGetValue(definition.RuntimeTileId, out var other) &&
            !string.Equals(other.Id, definition.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"地块数字 ID {definition.RuntimeTileId} 已属于 {other.Id}，不能分配给 {definition.Id}。");
        TileBlockDict[definition.Id] = definition;
        if (definition.RuntimeTileId > 0) tileDefinitionsByNumber[definition.RuntimeTileId] = definition;
    }

    /// <summary>MOD 卸载与失败回滚使用，不留下额外的数字映射。</summary>
    internal void UnregisterTileDefinition(string id)
    {
        if (!TileBlockDict.TryGetValue(id, out RuntimeTileDefinition definition)) return;
        TileBlockDict.Remove(id);
        if (definition.RuntimeTileId > 0) tileDefinitionsByNumber.Remove(definition.RuntimeTileId);
    }

    /// <summary>不创建单例、不访问存档的数字地块查询。</summary>
    public bool TryGetTileDefinition(int tileId, out RuntimeTileDefinition definition) =>
        tileDefinitionsByNumber.TryGetValue(tileId, out definition);

    /// <summary>配置、建筑与 MOD 共用的稳定字符串查询。</summary>
    public bool TryGetTileDefinition(string id, out RuntimeTileDefinition definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(id) && TileBlockDict.TryGetValue(id, out definition);
    }

    /// <summary>资源卸载先清空所有派生地块索引，再释放底层 TileBase 资源。</summary>
    private void ClearTileDefinitions()
    {
        TileBlockDict.Clear();
        tileDefinitionsByNumber.Clear();
    }
    #endregion
}
