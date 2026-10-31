using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEngine;

/// <summary>只追踪本工具实际迁移的 Addressables 源资源，过期时恢复原组和原地址。</summary>
internal static class FlatWorldSpriteAtlasSourceEntryOwnership
{
    #region 持久归属清单

    private const string GeneratedRoot = "Assets/6_Art/Generated/FlatWorldSpriteAtlases/";
    private const string GroupPrefix = "FlatWorldSpriteAtlas_";
    private const string ManifestPath = "ProjectSettings/FlatWorldSpriteAtlasSourceEntries.json";

    [Serializable]
    private sealed class OwnershipManifest
    {
        public int version = 1;
        public SourceOwnership[] sources = Array.Empty<SourceOwnership>();
    }

    [Serializable]
    private sealed class SourceOwnership
    {
        public string guid;
        public string sourcePath;
        public string originalGroup;
        public string originalAddress;
        public string managedGroup;
        public string managedAddress;
        public string previousManagedGroup;
    }

    internal readonly struct Result
    {
        public readonly int Managed;
        public readonly int Migrated;
        public readonly int Restored;

        internal Result(int managed, int migrated, int restored)
        { Managed = managed; Migrated = migrated; Restored = restored; }
    }

    private static OwnershipManifest ReadManifest()
    {
        if (!File.Exists(ManifestPath)) return new OwnershipManifest();
        OwnershipManifest document;
        try { document = JsonUtility.FromJson<OwnershipManifest>(File.ReadAllText(ManifestPath, Encoding.UTF8)); }
        catch (Exception error) { throw new BuildFailedException("源资源归属清单损坏，请恢复备份后再构建：" + error); }
        if (document == null || document.version != 1 || document.sources == null ||
            document.sources.Any(source => source == null || string.IsNullOrWhiteSpace(source.guid) ||
                string.IsNullOrWhiteSpace(source.sourcePath) || string.IsNullOrWhiteSpace(source.originalGroup) ||
                string.IsNullOrWhiteSpace(source.originalAddress) || string.IsNullOrWhiteSpace(source.managedGroup) ||
                string.IsNullOrWhiteSpace(source.managedAddress) ||
                !source.managedGroup.StartsWith(GroupPrefix, StringComparison.Ordinal)) ||
            document.sources.GroupBy(source => source.guid, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new BuildFailedException("源资源归属清单版本、归属或唯一性无效：" + ManifestPath);
        return document;
    }

    private static void SaveManifest(IEnumerable<SourceOwnership> source)
    {
        var manifest = new OwnershipManifest
        {
            sources = source.OrderBy(entry => entry.guid, StringComparer.Ordinal).ToArray()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
        string staging = ManifestPath + ".pending";
        File.WriteAllText(staging, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
        if (File.Exists(ManifestPath)) File.Replace(staging, ManifestPath, null);
        else File.Move(staging, ManifestPath);
    }

    #endregion

    #region 活跃源迁移与失活回滚

    /// <summary>预检全部来源后写入可恢复清单，之后才执行源资源分组改动。</summary>
    internal static Result Reconcile(AddressableAssetSettings settings,
        IReadOnlyDictionary<string, (string Path, string Group)> active,
        Action<string, string, string> moveEntry)
    {
        OwnershipManifest manifest = ReadManifest();
        var owned = manifest.sources.ToDictionary(source => source.guid, StringComparer.Ordinal);
        var operations = new List<(string Path, string Group, string Address, bool Restore)>();
        int restored = 0;
        int migrated = 0;

        foreach (SourceOwnership source in manifest.sources)
        {
            AddressableAssetEntry current = settings.FindAssetEntry(source.guid);
            if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(source.guid)))
            {
                settings.RemoveAssetEntry(source.guid, false);
                owned.Remove(source.guid);
                continue;
            }
            if (current == null || current.parentGroup == null)
            {
                if (active.ContainsKey(source.guid))
                    throw new BuildFailedException("原受管源资源的 Addressables 记录已经消失：" + source.sourcePath);
                owned.Remove(source.guid);
                continue;
            }
            bool atManaged = string.Equals(current.parentGroup.Name, source.managedGroup, StringComparison.Ordinal) &&
                string.Equals(current.address, source.managedAddress, StringComparison.Ordinal);
            bool atOriginal = string.Equals(current.parentGroup.Name, source.originalGroup, StringComparison.Ordinal) &&
                string.Equals(current.address, source.originalAddress, StringComparison.Ordinal);
            bool atPrevious = !string.IsNullOrEmpty(source.previousManagedGroup) &&
                string.Equals(current.parentGroup.Name, source.previousManagedGroup, StringComparison.Ordinal) &&
                string.Equals(current.address, source.managedAddress, StringComparison.Ordinal);
            if (!atManaged && !atOriginal && !atPrevious)
                throw new BuildFailedException("源资源的组或地址已被外部编辑，停止自动迁移：" + source.sourcePath);
            if (!active.TryGetValue(source.guid, out var required))
            {
                if (atManaged || atPrevious)
                {
                    if (settings.FindGroup(source.originalGroup) == null)
                        throw new BuildFailedException("无法恢复旧源资源：原 Addressables 组已不存在：" + source.originalGroup);
                    string currentPath = AssetDatabase.GUIDToAssetPath(source.guid);
                    if (string.IsNullOrWhiteSpace(currentPath))
                        throw new BuildFailedException("旧源资源 GUID 无法解析，拒绝自动恢复：" + source.guid);
                    operations.Add((currentPath, source.originalGroup, source.originalAddress, true));
                    restored++;
                }
                owned.Remove(source.guid);
                continue;
            }
            if (atOriginal || atPrevious || !string.Equals(source.managedGroup, required.Group, StringComparison.Ordinal))
            {
                operations.Add((required.Path, required.Group, source.managedAddress, false));
                migrated++;
            }
            if (!atOriginal && !string.Equals(source.managedGroup, required.Group, StringComparison.Ordinal))
                source.previousManagedGroup = source.managedGroup;
            source.sourcePath = required.Path;
            source.managedGroup = required.Group;
        }

        foreach (var required in active)
        {
            if (owned.ContainsKey(required.Key)) continue;
            AddressableAssetEntry current = settings.FindAssetEntry(required.Key);
            if (current?.parentGroup == null) continue; // 隐式依赖不能被改成工具拥有的显式条目。
            if (current.parentGroup.Name.StartsWith(GroupPrefix, StringComparison.Ordinal))
                throw new BuildFailedException("旧源资源在图集组中，但无可追溯原组记录，拒绝猜测恢复位置：" + required.Value.Path);
            SourceOwnership source = new()
            {
                guid = required.Key, sourcePath = required.Value.Path,
                originalGroup = current.parentGroup.Name, originalAddress = current.address,
                managedGroup = required.Value.Group, managedAddress = current.address
            };
            owned.Add(source.guid, source);
            operations.Add((required.Value.Path, required.Value.Group, source.managedAddress, false));
            migrated++;
        }

        // 不清理未知来源，若旧图集组还有无清单的源资源则明确阻止产出带陈旧依赖的包。
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null || !group.Name.StartsWith(GroupPrefix, StringComparison.Ordinal)) continue;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry.AssetPath.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (!manifest.sources.Any(source => string.Equals(source.guid, entry.guid, StringComparison.Ordinal)))
                    throw new BuildFailedException("图集组中存在无归属清单的源资源，请人工核对：" + entry.AssetPath);
            }
        }

        // 先落盘迁移意图；中途失败时重新执行仍能识别原始状态与应回退来源。
        SaveManifest(owned.Values.Concat(manifest.sources.Where(source => !owned.ContainsKey(source.guid))));
        foreach (var operation in operations)
            moveEntry(operation.Path, operation.Group, operation.Address);
        foreach (SourceOwnership source in owned.Values)
        {
            AddressableAssetEntry entry = settings.FindAssetEntry(source.guid);
            if (entry?.parentGroup == null || entry.parentGroup.Name != source.managedGroup ||
                entry.address != source.managedAddress)
                throw new BuildFailedException("源资源迁移结果不符合归属清单：" + source.sourcePath);
        }
        foreach (var operation in operations.Where(value => value.Restore))
        {
            string guid = AssetDatabase.AssetPathToGUID(operation.Path);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            if (entry?.parentGroup == null || entry.parentGroup.Name != operation.Group ||
                entry.address != operation.Address)
                throw new BuildFailedException("失活源资源未恢复到原分组和地址：" + operation.Path);
        }
        foreach (SourceOwnership source in owned.Values) source.previousManagedGroup = null;
        SaveManifest(owned.Values);
        return new Result(owned.Count, migrated, restored);
    }

    #endregion
}
