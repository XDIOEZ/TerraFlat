using System;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>按游戏时钟自然消失的物品能力，不依赖食物、燃烧或死亡掉落。</summary>
public sealed class Mod_TimedDespawn : Module
{
    #region 定义与世界物品生命周期
    public const string ModuleId = "Item.TimedDespawn";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 1f;
    public Ex_ModData_MemoryPackable State = new() { ModuleId = ModuleId };
    public override ModuleData _Data { get => State; set => State = (Ex_ModData_MemoryPackable)value; }
    [Min(0.01f)] public float LifetimeGameSeconds = 1440f;

    protected override void OnLoad()
    {
        if (GameNetwork.HasStateAuthority)
            TimedDespawnRules.IsExpired(State, LifetimeGameSeconds, item.gameObject.scene.name);
    }

    // 时钟每次推进已写回正式载荷，保存时不能从外壳缓存覆盖库存中的新状态。
    protected override void OnSave() { }

    public override void ModUpdate(float deltaTime)
    {
        // 手持物的库存拥有数量；世界外壳只负责没有库存宿主的物品。
        if (!GameNetwork.HasStateAuthority || item == null || item.Owner != null || item.DestructionHandled)
            return;
        if (TimedDespawnRules.IsExpired(State, LifetimeGameSeconds, item.gameObject.scene.name))
            item.DestroySelf();
    }
    #endregion
}

[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial struct TimedDespawnState
{
    [MemoryPackOrder(0)] public bool Initialized;
    [MemoryPackOrder(1)] public double ElapsedGameSeconds;
    [MemoryPackOrder(2)] public double LastGameSeconds;
    [MemoryPackOrder(3)] public string ClockScene;
}

/// <summary>库存、完整物品和轻量掉落物共用的计时规则；状态沿用正式模块快照。</summary>
public static class TimedDespawnRules
{
    #region 游戏时钟与持久状态
    public static bool Advance(ref TimedDespawnState state, double now, string clockScene, float lifetime)
    {
        if (!double.IsFinite(now) || now < 0d || string.IsNullOrEmpty(clockScene) ||
            !float.IsFinite(lifetime) || lifetime <= 0f)
            return false;
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.ElapsedGameSeconds = 0d;
        }
        else if (string.Equals(state.ClockScene, clockScene, StringComparison.Ordinal))
        {
            // 暂停不推进，跳时补算；时钟倒退不增加寿命，跨世界不混用两个时间原点。
            state.ElapsedGameSeconds += Math.Max(0d, now - state.LastGameSeconds);
        }
        state.LastGameSeconds = now;
        state.ClockScene = clockScene;
        return state.ElapsedGameSeconds >= lifetime;
    }

    public static bool IsExpired(Ex_ModData_MemoryPackable module, float lifetime, string worldKey)
    {
        DayTimeSystem clock = DayTimeSystem.GetInstance();
        if (module == null || !module.Enabled || clock == null ||
            !clock.TryGetResolvedTimeData(worldKey, out string clockScene, out TimeData time))
            return false;
        return IsExpiredAt(module, lifetime, clockScene, time.GetTotalGameTimeSeconds());
    }

    internal static bool IsExpiredAt(Ex_ModData_MemoryPackable module, float lifetime, string clockScene, double now)
    {
        if (module == null || !module.Enabled) return false;
        TimedDespawnState state = module.ReadRuntimeState<TimedDespawnState>();
        TimedDespawnState previous = state;
        bool expired = Advance(ref state, now, clockScene, lifetime);
        if (previous.Initialized != state.Initialized || previous.LastGameSeconds != state.LastGameSeconds ||
            previous.ElapsedGameSeconds != state.ElapsedGameSeconds || previous.ClockScene != state.ClockScene)
            module.WriteRuntimeState(state);
        return expired;
    }
    #endregion

    #region 定义与轻量掉落适配
    private sealed class DefinitionSettings
    {
        public string StableName;
        public float Lifetime;
        public DefinitionSettings(RuntimeItemDefinition definition)
        {
            foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            {
                if (!module.Enabled || module.ModuleId != Mod_TimedDespawn.ModuleId) continue;
                StableName = module.StableName;
                Lifetime = ReadLifetime(module);
                break;
            }
        }
    }
    private static readonly ConditionalWeakTable<RuntimeItemDefinition, DefinitionSettings> settings = new();
    private static readonly ConditionalWeakTable<RuntimeItemDefinition, DefinitionSettings>.CreateValueCallback createSettings = definition => new(definition);

    internal static float ReadLifetime(RuntimeItemModuleDefinition definition)
    {
        float lifetime = string.IsNullOrWhiteSpace(definition?.ParametersJson) ? 1440f :
            (float?)JObject.Parse(definition.ParametersJson)[nameof(Mod_TimedDespawn.LifetimeGameSeconds)] ?? 1440f;
        if (!float.IsFinite(lifetime) || lifetime <= 0f)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "自然消失寿命必须是正的有限游戏秒数。");
        return lifetime;
    }

    public static bool IsWorldItemExpired(ItemData data, string worldKey)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || data?.ModuleDataDic == null || resources == null ||
            !resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)) return false;
        DefinitionSettings config = settings.GetValue(definition, createSettings);
        return config.StableName != null && data.ModuleDataDic.TryGetValue(config.StableName, out ModuleData module) &&
            module is Ex_ModData_MemoryPackable state && state.ModuleId == Mod_TimedDespawn.ModuleId &&
            IsExpired(state, config.Lifetime, worldKey);
    }

    internal static bool RemoveExpiredSlot(Inventory_Data inventory, ItemSlot slot, ItemData expected)
    {
        // 使用正式库存事务清空到期堆叠，不触发死亡、燃烧产物或另造草木灰。
        return expected != null && ReferenceEquals(slot?.itemData, expected) &&
            inventory != null && inventory.TrySetSlotItemAmount(slot, expected, 0f);
    }
    #endregion
}

public sealed class TimedDespawnInventoryRule : IModuleDataRule
{
    #region 库存纯数据调度
    private readonly float lifetime;
    public TimedDespawnInventoryRule(RuntimeItemModuleDefinition definition) => lifetime = TimedDespawnRules.ReadLifetime(definition);
    public bool CanStep(ModuleDataTickContext context) => GameNetwork.HasStateAuthority &&
        context.ModuleData is Ex_ModData_MemoryPackable && context.InventoryData != null &&
        context.ItemData != null && ReferenceEquals(context.Slot?.itemData, context.ItemData);
    public void Step(ModuleDataTickContext context)
    {
        if (!CanStep(context) || !context.ModuleData.Enabled) return;
        string world = context.Owner != null ? context.Owner.gameObject.scene.name : GameplayWorldRegistry.ExecutionWorldKey;
        if (TimedDespawnRules.IsExpired((Ex_ModData_MemoryPackable)context.ModuleData, lifetime, world))
            TimedDespawnRules.RemoveExpiredSlot(context.InventoryData, context.Slot, context.ItemData);
    }
    #endregion
}
