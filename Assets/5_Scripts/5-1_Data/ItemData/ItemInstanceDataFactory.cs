using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;

/// <summary>当前定义共享的只读配置；ItemData 保留现有 Unity 与玩法字段作为适配外壳。</summary>
public sealed class ItemSharedConfiguration
{
    #region 共享配置

    public string DefinitionId { get; }
    public string GameName { get; }
    public string Description { get; }
    public float MaxDurability { get; }
    public float UnitWeight { get; }
    public float UnitVolume { get; }
    public bool Stackable { get; }
    public float HeatConductionRate { get; }
    public IReadOnlyList<string> Tags { get; }

    internal ItemSharedConfiguration(ItemData source)
    {
        DefinitionId = source.IDName;
        GameName = source.GameName;
        Description = source.Description;
        float multiplier = source.CraftedDurabilityMultiplier;
        MaxDurability = source.MaxDurability / (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f ? multiplier : 1f);
        UnitWeight = source.Stack?.Weight ?? 1f;
        UnitVolume = source.Stack?.Volume ?? 1f;
        Stackable = source.Stack?.Stackable ?? true;
        HeatConductionRate = source.HeatConductionRate;
        Tags = new ReadOnlyCollection<string>(source.Tags == null ? new List<string>() : new List<string>(source.Tags));
    }

    internal void BindTo(ItemData data)
    {
        data.SharedConfiguration = this;
        data.IDName = DefinitionId;
        data.GameName = GameName;
        data.Description = Description;
        data.MaxDurability = MaxDurability;
        data.HeatConductionRate = HeatConductionRate;
        data.Stack ??= new ItemStack();
        data.Stack.Weight = UnitWeight;
        data.Stack.Volume = UnitVolume;
        data.Stack.Stackable = Stackable;
        // 历史 Tags API 允许玩法临时添加标签，适配列表必须独立，不能污染共享定义。
        data.Tags = new List<string>(Tags);
    }

    #endregion
}

/// <summary>一次编译定义基线，生成时只创建实例外壳与必需的独立可变状态。</summary>
public static class ItemInstanceDataFactory
{
    #region 实例工厂

    public static Func<ItemData> Compile(ItemData templateData) => CompileCore(templateData, true);

    internal static Func<ItemData> CompileRuntimeCopy(ItemData source) => CompileCore(source, source?.SharedConfiguration != null);

    private static Func<ItemData> CompileCore(ItemData templateData, bool bindConfiguration)
    {
        if (templateData == null) throw new ArgumentNullException(nameof(templateData));
        ItemSharedConfiguration configuration = new(templateData);
        var initialState = new RuntimeDefaults(templateData);
        Func<ItemData> createShell = CompileShell(templateData);
        var moduleFactories = new List<Func<ModuleData>>();
        if (templateData.ModuleDataDic != null)
            foreach (KeyValuePair<string, ModuleData> pair in templateData.ModuleDataDic)
            {
                if (pair.Value == null) continue;
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key != pair.Value.StableName)
                    throw new InvalidDataException($"物品 {templateData.IDName} 的模块稳定名与索引不一致：{pair.Key}");
                moduleFactories.Add(ModuleRuntimeDataFactories.Compile(pair.Value));
            }

        return () =>
        {
            ItemData instance = createShell();
            configuration.BindTo(instance);
            initialState.ApplyTo(instance);
            if (!bindConfiguration) instance.SharedConfiguration = null;
            instance.ModuleDataDic = new Dictionary<string, ModuleData>(moduleFactories.Count, StringComparer.Ordinal);
            for (int i = 0; i < moduleFactories.Count; i++)
            {
                ModuleData module = moduleFactories[i]();
                instance.ModuleDataDic.Add(module.StableName, module);
            }
            // 内建默认状态直接创建，扩展模块可注册自己的纯内存工厂。
            ItemInstanceStateRestorationRegistry.Restore(instance);
            return instance;
        };
    }

    public static ItemData Create(ItemData templateData) => Compile(templateData)();

    /// <summary>复制独立运行态，保留持久身份；分堆或新生实体仍由调用方分配新 GUID。</summary>
    public static ItemData CloneRuntime(ItemData source) => CompileRuntimeCopy(source)();

    internal static ModuleData CloneModuleDefault(ModuleData source)
    {
        ModuleData clone = FastCloner.FastCloner.DeepClone<object>(source) as ModuleData;
        if (clone == null || clone.GetType() != source.GetType())
            throw new InvalidOperationException($"模块 {source.StableName} 的默认数据复制失败：期望 {source.GetType().Name}。");
        return clone;
    }

    private static Func<ItemData> CompileShell(ItemData source)
    {
        if (source.GetType() == typeof(Data_GeneralItem))
        {
            string code = ((Data_GeneralItem)source).code;
            return () => new Data_GeneralItem { code = code };
        }
        if (source.GetType() == typeof(BlockData)) return () => new BlockData();
        if (source.GetType() == typeof(Data_TileMap))
        {
            Func<Data_TileMap> createMap = ((Data_TileMap)source).CompileRuntimeMapFactory();
            return () => createMap();
        }
        if (source.GetType() != typeof(Data_Player))
            throw new InvalidDataException($"物品运行态类别尚未注册：{source.GetType().FullName}");

        var player = (Data_Player)source;
        var defaults = new Data_Player
        {
            CurrentSceneName = player.CurrentSceneName,
            hp = CloneHp(player.hp), defense = CloneDefense(player.defense),
            Speed = CloneGameValue(player.Speed) ?? new GameValue_float(), stamina = player.stamina,
            staminaMax = player.staminaMax, staminaRecoverySpeed = player.staminaRecoverySpeed,
            Name_User = player.Name_User, PlayerPov = player.PlayerPov,
            MaxCarryWeight = player.MaxCarryWeight, MaxCarryVolume = player.MaxCarryVolume,
            PerceptionRadiusMultiplier = player.PerceptionRadiusMultiplier
        };
        Func<Dictionary<string, Inventory_Data>> inventories = InventoryRuntimeDataFactory.CompileDictionary(player._inventoryData);
        return () => new Data_Player
        {
            CurrentSceneName = defaults.CurrentSceneName,
            hp = CloneHp(defaults.hp), defense = CloneDefense(defaults.defense),
            Speed = CloneGameValue(defaults.Speed), stamina = defaults.stamina,
            staminaMax = defaults.staminaMax, staminaRecoverySpeed = defaults.staminaRecoverySpeed,
            Name_User = defaults.Name_User, PlayerPov = defaults.PlayerPov,
            MaxCarryWeight = defaults.MaxCarryWeight, MaxCarryVolume = defaults.MaxCarryVolume,
            PerceptionRadiusMultiplier = defaults.PerceptionRadiusMultiplier,
            _inventoryData = inventories()
        };
    }

    internal static GameValue_float CloneGameValue(GameValue_float value) => value == null ? null : new GameValue_float
    {
        BaseValue = value.BaseValue, BaseAdditive = value.BaseAdditive,
        AdditiveModifier = value.AdditiveModifier, MultiplicativeModifier = value.MultiplicativeModifier,
        FinalAdditive = value.FinalAdditive
    };

    private static Hp CloneHp(Hp value) => value == null ? new Hp(30f) : new Hp(value.Value)
    {
        maxValue = value.maxValue,
        Weaknesses = value.Weaknesses == null ? new List<string>() : new List<string>(value.Weaknesses)
    };

    private static Defense CloneDefense(Defense value) => value == null ? new Defense(5f, 5f, 5f, 5f) :
        new Defense(value.Cutting, value.Piercing, value.Chopping, value.Blunt);

    /// <summary>原位恢复当前定义，库存槽位与手持实例继续共享同一数据引用。</summary>
    public static void ApplyCurrentDefinition(ItemData target, ItemData definitionDefaults)
    {
        if (target == null || definitionDefaults == null) throw new ArgumentNullException(nameof(target));
        if (ItemInstanceSnapshot.GetKind(target) != ItemInstanceSnapshot.GetKind(definitionDefaults))
            throw new InvalidOperationException($"物品定义的数据类别不一致：{target.IDName}");
        ItemInstanceSnapshot instanceState = ItemInstanceSnapshot.Capture(target);
        ItemSharedConfiguration configuration = definitionDefaults.SharedConfiguration ?? new ItemSharedConfiguration(definitionDefaults);
        configuration.BindTo(target);
        target.CraftedDurabilityMultiplier = 1f;
        target.ModuleDataDic = definitionDefaults.DetachModuleData();
        instanceState.RestoreTo(target);
    }

    #endregion

    #region 内存默认状态

    private sealed class RuntimeDefaults
    {
        private readonly int guid;
        private readonly float amount;
        private readonly bool canBePickedUp;
        private readonly float durabilityFraction;
        private readonly float craftedDurabilityMultiplier;
        private readonly string specialData;
        private readonly bool inHand;
        private readonly string factionId;
        private readonly ItemTransform transform;
        private readonly ItemMatterState matter;
        private readonly string[] tags;
        private readonly string[] addedTags;
        private readonly string[] removedTags;
        private readonly ModuleInstanceSnapshot[] preservedModules;

        public RuntimeDefaults(ItemData source)
        {
            guid = source.Guid;
            amount = source.Stack?.Amount ?? 1f;
            canBePickedUp = source.Stack?.CanBePickedUp ?? true;
            durabilityFraction = source.MaxDurability > 0f ? Mathf.Clamp01(source.Durability / source.MaxDurability) : 1f;
            float multiplier = source.CraftedDurabilityMultiplier;
            craftedDurabilityMultiplier = IsFinite(multiplier) && multiplier > 0f ? multiplier : 1f;
            specialData = source.ItemSpecialData;
            inHand = source.inHand;
            factionId = source.FactionId;
            transform = CloneTransform(source.transform);
            matter = CloneMatter(source.MatterState);
            tags = source.Tags?.ToArray() ?? Array.Empty<string>();
            addedTags = source.PreservedAddedTags == null ? null : (string[])source.PreservedAddedTags.Clone();
            removedTags = source.PreservedRemovedTags == null ? null : (string[])source.PreservedRemovedTags.Clone();
            preservedModules = source.PreservedModuleStates == null ? null : (ModuleInstanceSnapshot[])source.PreservedModuleStates.Clone();
            if (string.IsNullOrWhiteSpace(source.IDName) || !IsFinite(amount) || amount < 0f || !IsFinite(durabilityFraction) ||
                (transform != null && (!IsFinite(transform.position.x) || !IsFinite(transform.position.y) || !IsFinite(transform.position.z) ||
                    !IsFinite(transform.rotation.x) || !IsFinite(transform.rotation.y) || !IsFinite(transform.rotation.z) || !IsFinite(transform.rotation.w) ||
                    !IsFinite(transform.scale.x) || !IsFinite(transform.scale.y) || !IsFinite(transform.scale.z))))
                throw new InvalidDataException("物品运行态默认身份或数值无效。");
        }

        public void ApplyTo(ItemData target)
        {
            target.Guid = guid;
            target.Stack.Amount = amount;
            target.Stack.CanBePickedUp = canBePickedUp;
            target.CraftedDurabilityMultiplier = craftedDurabilityMultiplier;
            target.MaxDurability = Mathf.Max(0f, target.MaxDurability) * craftedDurabilityMultiplier;
            target.Durability = target.MaxDurability * durabilityFraction;
            target.ItemSpecialData = specialData;
            target.inHand = inHand;
            target.FactionId = factionId;
            if (transform != null) target.transform = CloneTransform(transform);
            target.MatterState = CloneMatter(matter);
            target.Tags.Clear();
            target.Tags.AddRange(tags);
            target.PreservedAddedTags = addedTags == null ? null : (string[])addedTags.Clone();
            target.PreservedRemovedTags = removedTags == null ? null : (string[])removedTags.Clone();
            // 快照对象只读，数组独立即可保留未加载的 MOD 负载。
            target.PreservedModuleStates = preservedModules == null ? null : (ModuleInstanceSnapshot[])preservedModules.Clone();
        }

        private static ItemTransform CloneTransform(ItemTransform value) => value == null ? null : new ItemTransform
        {
            position = value.position, rotation = value.rotation, scale = value.scale
        };

        private static ItemMatterState CloneMatter(ItemMatterState value) => value == null ? new ItemMatterState() : new ItemMatterState
        {
            Initialized = value.Initialized, TemperatureCelsius = value.TemperatureCelsius, Moisture = value.Moisture,
            IsBurning = value.IsBurning, CombustionElapsedSeconds = value.CombustionElapsedSeconds
        };

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    #endregion
}
