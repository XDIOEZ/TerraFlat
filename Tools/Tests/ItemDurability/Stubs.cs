// 只替代 Unity、序列化与质量监听外壳；耐久、拆分、合并、容量、整理均链接正式源码。
namespace MemoryPack
{
    public class MemoryPackableAttribute : Attribute { }
    public class MemoryPackIgnoreAttribute : Attribute { }
    public class MemoryPackConstructorAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class MemoryPackUnionAttribute(int tag, Type type) : Attribute { }
}
namespace FastCloner.Code { public class FastClonerIgnoreAttribute : Attribute { } }
namespace Newtonsoft.Json { public class JsonIgnoreAttribute : Attribute { } }
namespace Sirenix.OdinInspector
{
    public class ShowInInspectorAttribute : Attribute { }
    public class ReadOnlyAttribute : Attribute { }
}
namespace NUnit.Framework.Interfaces { }
namespace Org.BouncyCastle.Asn1.Cmp { }
namespace NaughtyAttributes { }
namespace UnityEditor { }
namespace UnityEngine.UI { }
namespace UnityEngine.Serialization
{
    public class FormerlySerializedAsAttribute(string name) : Attribute { }
}
namespace UnityEngine
{
    public class TooltipAttribute(string value) : Attribute { }
    public class TextAreaAttribute : Attribute { }
    public class SerializeField : Attribute { }
    public struct Vector2 { public float x, y; }
    public struct Vector3 { public static Vector3 zero => default; }
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) => throw new InvalidOperationException(value?.ToString());
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Floor(float value) => MathF.Floor(value);
        public static int FloorToInt(float value) => (int)MathF.Floor(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
        public static float Round(float value) => MathF.Round(value);
        public static float Abs(float value) => MathF.Abs(value);
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < 0.000001f;
    }
}
namespace UltEvents
{
    public class UltEvent<T>
    {
        private Action<T> handlers;
        public void Invoke(T value) => handlers?.Invoke(value);
        public static UltEvent<T> operator +(UltEvent<T> evt, Action<T> handler) { evt.handlers += handler; return evt; }
        public static UltEvent<T> operator -(UltEvent<T> evt, Action<T> handler) { evt.handlers -= handler; return evt; }
    }
    public class UltEvent<T, U>
    {
        public void Invoke(T first, U second) { }
    }
}
public class ItemStack
{
    public float Amount = 1, Weight = 1, Volume = 1;
    public bool Stackable = true, CanBePickedUp = true;
    public float CurrentWeight => Amount * Weight;
    public float CurrentVolume => Amount * Volume;
}
public class ItemTransform { }
public class ModuleData { public string ID; }
public class ModuleDataCollection : Dictionary<string, ModuleData>
{
    public void BindOwner(ItemData owner) { }
    public void UnbindOwner(ItemData owner) { }
    public ModuleDataCollection ForOwner(ItemData owner) => this;
}
public class ItemSharedConfiguration { }
public class ModuleInstanceSnapshot { }
public class ItemInstanceSnapshot
{
    public void RestoreColdTo(ItemData data) => throw new NotSupportedException("独立测试不模拟存档。");
}
public static class ItemSnapshotSerialization
{
    public static ItemInstanceSnapshot Capture(ItemData data) => throw new NotSupportedException("存档由正式 Lua 回归。");
}
public class Data_GeneralItem : ItemData { }
public class Data_Player : ItemData { }
public class Data_TileMap : ItemData { }
public class BlockData : ItemData { }
public abstract partial class ItemData
{
    private void BindMassStack() { }
    private void UnbindMassStack() { }
    private void NotifyMassChanged() { }
}
public partial class Inventory_Data
{
    private void RebindMassSlots() { }
    private void RebindMassSlot(ItemSlot slot) { }
}
public static class TagsTestExtensions
{
    public static bool ContainsTag(this List<string> tags, string tag) => tags.Contains(tag);
}
public static class ItemInstanceDataFactory
{
    // 测试不装配任何模块；这里只给正式库存拆分提供独立数据副本。
    public static ItemData CloneRuntime(ItemData item) => new Data_GeneralItem
    {
        IDName = item.IDName, Guid = item.Guid, GameName = item.GameName,
        Durability = item.Durability, MaxDurability = item.MaxDurability,
        CraftedDurabilityMultiplier = item.CraftedDurabilityMultiplier, ItemSpecialData = item.ItemSpecialData,
        Stack = new ItemStack { Amount = item.Stack.Amount, Stackable = item.Stack.Stackable,
            Weight = item.Stack.Weight, Volume = item.Stack.Volume, CanBePickedUp = item.Stack.CanBePickedUp },
        Tags = new List<string>(item.Tags)
    };
}
