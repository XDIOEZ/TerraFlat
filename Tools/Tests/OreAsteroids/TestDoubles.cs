using Newtonsoft.Json;

#region 非目标环境替身
// 替身只提供编译所需的 Unity 值类型、资源入口和序列化标记，不实现被测业务。
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public sealed class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string path) where T : class => null; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
}
namespace MemoryPack
{
    public sealed class MemoryPackableAttribute : Attribute { }
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
}
public readonly struct WorldAddress
{
    public const string SurfaceDimensionId = "surface";
    public WorldAddress(string planet, string dimension) { }
}
public static class JsonConfigurationCache
{
    public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
}
#endregion
