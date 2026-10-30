using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 用文件和轻量类型代替 Unity 环境，配置与规则本身仍调用生产代码。
#region Unity 与序列化环境替身
namespace MemoryPack
{
    public sealed class MemoryPackableAttribute : Attribute { }
    public sealed class MemoryPackConstructorAttribute : Attribute { }
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
}
namespace UnityEngine
{
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class HideInInspector : Attribute { }
    public static class Mathf
    {
        public static float Max(float left, float right) => Math.Max(left, right);
        public static bool Approximately(float left, float right) => Math.Abs(left - right) < 0.000001f;
    }
    public static class Debug { public static void LogError(object value) { } }
}
namespace Sirenix.OdinInspector
{
    public sealed class ShowInInspectorAttribute : Attribute { }
    public sealed class LabelTextAttribute : Attribute { public LabelTextAttribute(string text) { } }
    public sealed class MinValueAttribute : Attribute { public MinValueAttribute(float value) { } }
}
namespace Unity.Mathematics
{
    public struct float4
    {
        public float x, y, z, w;
        public float4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static float4 zero => default;
    }
    public static class math
    {
        public static float max(float left, float right) => Math.Max(left, right);
        public static float min(float left, float right) => Math.Min(left, right);
    }
}
#endregion

#region 目录装载环境替身
public sealed class ItemDefinitionDto
{
    public string Id { get; set; }
    public bool Abstract { get; set; }
    public JObject Ecs { get; set; }
    public Dictionary<string, ItemModuleDefinitionDto> Modules { get; set; }
}
public sealed class ItemModuleDefinitionDto { public JObject Parameters { get; set; } }
public sealed class ResourceReloadContext { public void InCandidate(Action action) => action(); }
public static class AnimalBehaviorTreeNodeCatalog
{
    public static AnimalBehaviorSubtreeDefinition GetSharedSubtree(string name) => null;
}
public static partial class ActorDefinitionCatalogLoader
{
    public static string DefinitionsFolder, SharedPath;
    public static void ConfigureResourceReload(ResourceReloadContext candidate) { }
    public static List<ItemDefinitionDto> LoadBuiltInDefinitions()
    {
        var catalogs = Directory.GetFiles(DefinitionsFolder, "*.json")
            .Select(path => new JObject { ["schemaVersion"] = 1,
                ["items"] = JObject.Parse(File.ReadAllText(path))["actors"].DeepClone() }.ToString(Formatting.None));
        List<JObject> sources = ItemDefinitionCatalogLoader.ResolveDefinitionObjects(catalogs);
        ApplySharedBehaviorSubtrees(sources, LoadSharedBehaviorSubtreeFiles(SharedPath));
        return sources.Select(source => source.ToObject<ItemDefinitionDto>()).ToList();
    }

    public static JObject LoadSharedBehaviorSubtreeFiles(string indexPath)
        => ReadBehaviorSubtrees(ReadBehaviorSubtreeFilePaths(File.ReadAllText(indexPath)).Select(entry =>
            new KeyValuePair<string, string>(entry.Key, File.ReadAllText(Path.Combine(Path.GetDirectoryName(indexPath), entry.Value)))));
}
#endregion
