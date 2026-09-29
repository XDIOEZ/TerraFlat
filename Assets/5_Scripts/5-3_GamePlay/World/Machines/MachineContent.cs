using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>模块 Prefab 仅作资源期配置模板；编译机器定义不实例化任何模块 GameObject。</summary>
public sealed class MachineContent
{
    #region 只读内容
    public RuntimeItemDefinition Definition { get; }
    private readonly List<MachineModuleConfiguration> modules = new();

    public MachineContent(RuntimeItemDefinition definition, GameRes resources = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        resources = resources != null ? resources : GameRes.ExistingInstance;
        if (resources == null) throw new InvalidOperationException("机器配置需要已加载的资源目录。");
        foreach (RuntimeItemModuleDefinition entry in definition.ModuleDefinitions)
        {
            if (!entry.Enabled) continue;
            GameObject prefab = resources.GetPrefab(entry.PrefabId, false);
            Module authoring = prefab != null ? prefab.GetComponentInChildren<Module>(true) : null;
            if (authoring == null)
                throw new InvalidOperationException("机器配置模块未加载或缺少 Module：" + entry.PrefabId);
            modules.Add(new MachineModuleConfiguration(entry, authoring));
        }
    }

    public MachineModuleConfiguration Find<T>() where T : Module
    {
        foreach (MachineModuleConfiguration module in modules)
            if (module.Authoring is T) return module;
        return null;
    }

    public bool Has<T>() where T : Module => Find<T>() != null;
    public bool Has(Type type)
    {
        foreach (MachineModuleConfiguration module in modules)
            if (type.IsInstanceOfType(module.Authoring)) return true;
        return false;
    }
    #endregion
}

public sealed class MachineModuleConfiguration
{
    #region 配置投影
    public RuntimeItemModuleDefinition Declaration { get; }
    public Module Authoring { get; }
    public JObject Parameters { get; }

    public MachineModuleConfiguration(RuntimeItemModuleDefinition declaration, Module authoring)
    {
        Declaration = declaration;
        Authoring = authoring;
        Parameters = string.IsNullOrWhiteSpace(declaration.ParametersJson)
            ? new JObject() : JObject.Parse(declaration.ParametersJson);
    }

    public T Value<T>(string field, T fallback)
        => Parameters.TryGetValue(field, StringComparison.Ordinal, out JToken token)
            ? token.ToObject<T>() : fallback;

    /// <summary>先复制配置数据再叠加 JSON，绝不写回共享 Prefab。</summary>
    public T Data<T>(string field, T fallback) where T : class
    {
        T copy = MachinePersistence.Clone(fallback);
        if (Parameters[field] is JObject patch)
            JsonConvert.PopulateObject(patch.ToString(Formatting.None), copy);
        return copy;
    }
    #endregion
}

/// <summary>便携设施继续共享原有模块数据，落地模拟不需要实例化这些模块。</summary>
public static class MachineModuleState
{
    #region 便携状态
    public static Ex_ModData_MemoryPackable Binary(ItemData snapshot, string id)
    {
        Ex_ModData_MemoryPackable found = null;
        if (snapshot?.ModuleDataDic != null)
            foreach (ModuleData entry in snapshot.ModuleDataDic.Values)
            {
                if (entry?.ID != id) continue;
                if (found != null || entry is not Ex_ModData_MemoryPackable binary)
                    throw new InvalidOperationException("机器模块状态类型错误或重复：" + id);
                found = binary;
            }
        return found ?? throw new InvalidOperationException("机器快照缺少模块状态：" + id);
    }

    public static T Read<T>(ItemData snapshot, string id) where T : class
    {
        var module = Binary(snapshot, id);
        return module.BitData?.Length > 0 ? module.GetData<T>() : null;
    }

    public static void Write<T>(ItemData snapshot, string id, T state)
        => Binary(snapshot, id).WriteData(state);
    #endregion
}
