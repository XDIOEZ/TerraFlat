using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

/// <summary>
/// 地块数据与行为的显式工厂注册表。内建 ID 保持稳定，代码 MOD 可注册带命名空间的新 ID；
/// 工厂只在资源加载阶段执行，每个定义独立创建一组共享行为，不在角色移动时反射或 new。
/// Register 返回的租约由扩展持有，卸载扩展时释放，重复 ID 和未知 ID 均立即报错。
/// </summary>
public static class TileBehaviourRegistry
{
    #region 内建工厂
    private static readonly Dictionary<string, Func<JObject, TileData>> dataFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<JObject, TileBlockBehaviour>> behaviourFactories = new(StringComparer.Ordinal);

    static TileBehaviourRegistry()
    {
        RegisterData("universal", Create<TileData_Universal>);
        RegisterData("grass", Create<TileData_Grass>);
        RegisterData("farmland", Create<TileData_Farmland>);
        RegisterData("cellBuilding", Create<TileData_CellBuilding>);
        RegisterBehaviour("universal", Create<Tile_Universal>);
        RegisterBehaviour("grass", Create<Tile_Grass>);
        RegisterBehaviour("farmland", Create<Tile_Farmland>);
        RegisterBehaviour("ice", Create<Tile_Ice>);
        RegisterBehaviour("snow", Create<Tile_Snow>);
    }

    /// <summary>创建已知 C# 类型，再使用严格字段契约注入参数。</summary>
    private static T Create<T>(JObject parameters) where T : new()
    {
        var value = new T();
        TileDefinitionJson.Populate(parameters, value);
        return value;
    }
    #endregion

    #region 扩展入口
    public static IDisposable RegisterData(string id, Func<JObject, TileData> factory) => Register(dataFactories, id, factory);
    public static IDisposable RegisterBehaviour(string id, Func<JObject, TileBlockBehaviour> factory) => Register(behaviourFactories, id, factory);

    private static IDisposable Register<T>(Dictionary<string, Func<JObject, T>> registry, string id, Func<JObject, T> factory)
    {
        TileDefinitionFactory.ValidateId(id, "组件 type");
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (!registry.TryAdd(id, factory)) throw new InvalidOperationException($"重复地块组件 type：{id}");
        return new Registration<T>(registry, id, factory);
    }

    public static TileData BuildData(TileComponentDefinitionDto definition) => Build(dataFactories, definition, "数据");
    public static TileBlockBehaviour BuildBehaviour(TileComponentDefinitionDto definition) => Build(behaviourFactories, definition, "行为");

    private static T Build<T>(Dictionary<string, Func<JObject, T>> registry, TileComponentDefinitionDto definition, string kind) where T : class
    {
        if (definition == null || definition.Parameters == null || string.IsNullOrWhiteSpace(definition.Type))
            throw new InvalidDataException($"地块{kind}缺少 type 或 parameters。");
        if (!registry.TryGetValue(definition.Type, out Func<JObject, T> factory))
            throw new InvalidDataException($"未注册的地块{kind} type：{definition.Type}");
        T value = factory((JObject)definition.Parameters.DeepClone())
            ?? throw new InvalidDataException($"地块{kind}工厂返回空对象：{definition.Type}");
        TileDefinitionJson.ValidateFields(value, definition.Type);
        return value;
    }

    private sealed class Registration<T> : IDisposable
    {
        private Dictionary<string, Func<JObject, T>> registry;
        private readonly string id;
        private readonly Func<JObject, T> factory;
        public Registration(Dictionary<string, Func<JObject, T>> registry, string id, Func<JObject, T> factory)
        { this.registry = registry; this.id = id; this.factory = factory; }
        public void Dispose()
        {
            if (registry != null && registry.TryGetValue(id, out var current) && ReferenceEquals(current, factory)) registry.Remove(id);
            registry = null;
        }
    }
    #endregion
}
