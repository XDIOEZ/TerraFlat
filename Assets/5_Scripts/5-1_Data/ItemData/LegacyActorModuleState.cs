using MemoryPack;
using System.Linq;

/// <summary>旧发布格式的有限迁移载荷，模块定义就绪后移交给当前能力。</summary>
public sealed class LegacyActorModuleState
{
    #region 兼容载荷
    public const string AttributesKey = "legacy.player.attributes";
    public const string InventoriesKey = "legacy.player.inventories";
    public float CameraPov;
    public float MaxCarryWeight;
    public float MaxCarryVolume;
    public float TargetPerceptionMultiplier = 1f;

    public static ModuleData FindPendingModule(ItemData data, string key)
    {
        if (data.ModuleDataDic.TryGetValue(key, out var module)) return module;
        var state = data.PreservedModuleStates?.FirstOrDefault(value => value.StableName == key);
        if (state == null) return null;
        module = state.CreateColdData();
        state.RestoreTo(module);
        return module;
    }
    public static void RemovePendingModule(ItemData data, string key)
    {
        data.ModuleDataDic.Remove(key);
        if (data.PreservedModuleStates != null)
            data.PreservedModuleStates = data.PreservedModuleStates.Where(value => value.StableName != key).ToArray();
    }
    #endregion
}

// MemoryPack Union 6 仅用于解码正式旧载荷，不注册为任何运行时角色或定义。
[MemoryPackable]
internal partial class LegacyPlayerDataEnvelope : Data_GeneralItem { }
