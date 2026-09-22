using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.Tilemaps;

/// <summary>
/// MOD 地块内容接入：definitionFiles.tiles 使用本体 schema，patchFiles 使用 tile:ID 目标。
/// 所有定义先在局部目录构建并校验，再发布；失败或卸载恢复原对象，不能残留数字 ID 或已销毁资源引用。
/// </summary>
public sealed partial class ModRuntimeManager
{
    #region 来源与所有权
    private List<PendingTileDefinition> pendingTileDefinitions = new();
    private List<(string Id, RuntimeTileDefinition Previous)> tileDefinitionChanges = new();
    private List<(string Id, TileBase Asset)> registeredTileAssets = new();

    private sealed class PendingTileDefinition
    {
        public ModPackage Package;
        public string File;
        public int Index;
        public TileDefinitionDto Definition;
    }

    private static bool IsTilePatchTarget(string target) =>
        target != null && target.StartsWith("tile:", StringComparison.Ordinal);

    /// <summary>记录 MOD TileBase 字典所有权，失败/卸载时不留下已销毁资源占用的键。</summary>
    private void RegisterModTileAsset(GameRes resources, string id, TileBase source)
    {
        TileBase asset = CloneAsset(source);
        RegisterUnique(resources.tileBaseDict, id, asset);
        registeredTileAssets.Add((id, asset));
    }

    /// <summary>复用 MOD 既有受限文件读取，不额外读取任意路径。</summary>
    private void QueueTileDefinitions(ModPackage package, string file, JObject document)
    {
        JToken token = document["tiles"];
        if (token == null) return;
        if (token is not JArray array)
            throw new InvalidDataException($"MOD {package.Manifest.Id} 的 tiles 必须是数组：{file}");
        for (int index = 0; index < array.Count; index++)
        {
            try
            {
                TileDefinitionDto dto = TileDefinitionFactory.DeserializeDefinition(array[index]);
                ValidateContentId(package.Manifest.Id, dto.Id);
                if (dto.RuntimeTileId < TileDefinitionFactory.FirstModRuntimeTileId)
                    throw new InvalidDataException($"新 MOD 地块 runtimeTileId 必须至少为 {TileDefinitionFactory.FirstModRuntimeTileId}，且在发布后保持不变。");
                pendingTileDefinitions.Add(new PendingTileDefinition
                { Package = package, File = file, Index = index, Definition = dto });
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"MOD {package.Manifest.Id} 地块定义无效：{file}#{index}：{error.Message}", error);
            }
        }
    }
    #endregion

    #region 构建与发布
    /// <summary>在资源、Item 和 Actor 开始使用地块前发布定义；跨目录引用由最终校验器统一验证。</summary>
    private void ProcessTileDefinitions(GameRes resources)
    {
        var resolved = new Dictionary<string, RuntimeTileDefinition>(resources.TileBlockDict, IdComparer);
        var changed = new HashSet<string>(IdComparer);
        foreach (PendingTileDefinition pending in pendingTileDefinitions)
        {
            TileDefinitionDto dto = pending.Definition;
            if (resolved.ContainsKey(dto.Id))
                throw new InvalidDataException($"重复 MOD 地块 {dto.Id}：{pending.File}#{pending.Index}；覆盖已有地块应使用 tile:ID Patch。");
            RuntimeTileDefinition definition = BuildModTile(resources, dto, pending.File);
            resolved.Add(dto.Id, definition);
            changed.Add(dto.Id);
            definitionInfos["tile:" + dto.Id] = new ModDefinitionInfo
            {
                Id = dto.Id,
                DeclaringModId = pending.Package.Manifest.Id,
                LastModifiedBy = pending.Package.Manifest.Id,
                SourceFile = pending.File,
                SourceIndex = pending.Index
            };
        }

        foreach (PendingPatchDocument pending in pendingPatchDocuments)
        {
            int index = 0;
            foreach (ModPatchOperation operation in pending.Document.Patches ?? new List<ModPatchOperation>())
            {
                int operationIndex = index++;
                if (!IsTilePatchTarget(operation?.Target)) continue;
                string id = operation.Target.Substring("tile:".Length);
                if (!resolved.TryGetValue(id, out RuntimeTileDefinition previous))
                {
                    if (operation.Optional) continue;
                    throw new InvalidDataException($"地块 Patch 找不到 {id}：{pending.File}#{operationIndex}");
                }
                JObject source = previous.CopySource();
                ApplyPatchOperation(source, operation, pending.File, operationIndex);
                TileDefinitionDto dto = TileDefinitionFactory.DeserializeDefinition(source);
                if (!string.Equals(dto.Id, previous.Id, StringComparison.Ordinal) || dto.RuntimeTileId != previous.RuntimeTileId)
                    throw new InvalidDataException($"地块 Patch 禁止改变 id 或 runtimeTileId：{pending.File}#{operationIndex}");
                resolved[id] = BuildModTile(resources, dto, pending.File);
                changed.Add(id);
                ModDefinitionInfo info = GetOrCreateDefinitionInfo("tile:" + id);
                info.LastModifiedBy = pending.Package.Manifest.Id;
                info.Patches.Add($"{pending.Package.Manifest.Id}:{pending.File}#{operationIndex}");
            }
        }

        TileDefinitionFactory.ValidateIdentities(resolved.Values);
        try
        {
            foreach (string id in changed.OrderBy(value => value, StringComparer.Ordinal))
            {
                resources.TileBlockDict.TryGetValue(id, out RuntimeTileDefinition previous);
                resources.RegisterTileDefinition(resolved[id], previous != null);
                tileDefinitionChanges.Add((id, previous));
            }
        }
        catch
        {
            UnloadTileDefinitions(resources);
            throw;
        }
    }

    /// <summary>与本体使用同一个构建器；资源键必须已由本体或有依赖关系的 MOD 注册。</summary>
    private static RuntimeTileDefinition BuildModTile(GameRes resources, TileDefinitionDto dto, string file)
    {
        try
        {
            return TileDefinitionFactory.Build(dto,
                id => resources.tileBaseDict.TryGetValue(id, out var tile) ? tile : null);
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"地块 {dto.Id} 构建失败（{file}）：{error.Message}", error);
        }
    }
    #endregion

    #region 失败与卸载
    /// <summary>恢复原地块定义对象，不重建 Behaviour；必须早于 MOD TileBase 资源销毁。</summary>
    private void UnloadTileDefinitions(GameRes resources)
    {
        for (int index = tileDefinitionChanges.Count - 1; index >= 0; index--)
        {
            var change = tileDefinitionChanges[index];
            resources?.UnregisterTileDefinition(change.Id);
            if (resources != null && change.Previous != null) resources.RegisterTileDefinition(change.Previous);
        }
        tileDefinitionChanges.Clear();
        pendingTileDefinitions.Clear();
        foreach (var entry in registeredTileAssets)
            if (resources != null && resources.tileBaseDict.TryGetValue(entry.Id, out var asset) && ReferenceEquals(asset, entry.Asset))
                resources.tileBaseDict.Remove(entry.Id);
        registeredTileAssets.Clear();
    }
    #endregion
}
