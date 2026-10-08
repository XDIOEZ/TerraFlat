using MemoryPack;
using NUnit.Framework.Interfaces;
using Org.BouncyCastle.Asn1.Cmp;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UltEvents;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using Newtonsoft.Json;
using FastCloner.Code;
using UnityEngine.Serialization;


[MemoryPackUnion(4, typeof(Data_GeneralItem))]//通用物品数据
[MemoryPackUnion(6, typeof(Data_Player))]//玩家数据
[MemoryPackUnion(8, typeof(Data_TileMap))]//瓦片地图数据
[MemoryPackUnion(9, typeof(BlockData))]//瓦片地图数据


[MemoryPackable]
[System.Serializable]
public abstract partial class ItemData
{
    [Tooltip("物品定义 ID：稳定标识，用于存档、配方和查找，不作为界面显示名")]
    [MemoryPackIgnore] public string IDName;

    [Tooltip("默认名称：来自定义的 gameName；界面按 ID 查询 RuntimeItemDefinition.DisplayName 获取当前语言名称")]
    [MemoryPackIgnore] public string GameName;

    [Tooltip("物品描述")]
    [TextArea]
    [MemoryPackIgnore]
    [JsonIgnore]
    [FastClonerIgnore]
    public string Description = "什么都没有描述";

    [Tooltip("物品耐久度")]
    [MemoryPackIgnore] public float Durability = 1;

    [Tooltip("物品耐久度")]
    [MemoryPackIgnore] public float MaxDurability = 1;

    [Tooltip("新版Tag系统_适配新版合成表")]
    [MemoryPackIgnore] public List<string> Tags = new();

    [Tooltip("物品堆叠信息")]
    [MemoryPackIgnore] public ItemStack Stack;

    [Tooltip("物品缩放")]
    [MemoryPackIgnore] public ItemTransform transform = new();

    #region 运行时物理反馈

    // 物理结果只在当前世界内存中回写，存档仍由现有位置与模块数据负责。
    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    public ItemPhysicsRuntimeState PhysicsState = new();

    #endregion

    [Tooltip("物品特殊数据")]
    [MemoryPackIgnore] public string ItemSpecialData;

    [Tooltip("此物品是否在手上?")]
    [MemoryPackIgnore] public bool inHand = false;

    [Tooltip("全局唯一标识")]
    [MemoryPackIgnore] public int Guid;
    [SerializeField, FormerlySerializedAs("ModuleDataDic"), MemoryPackIgnore, JsonIgnore]
    private ModuleDataCollection moduleData = new();

    [ShowInInspector, MemoryPackIgnore]
    public ModuleDataCollection ModuleDataDic
    {
        get { moduleData?.BindOwner(this); return moduleData; }
        set
        {
            if (ReferenceEquals(moduleData, value)) return;
            moduleData?.UnbindOwner(this);
            moduleData = value?.ForOwner(this);
            moduleData?.BindOwner(this);
            NotifyModuleStructureChanged();
        }
    }

    #region 运行态结构版本

    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    private uint moduleStructureVersion;

    [MemoryPackIgnore, JsonIgnore]
    public uint ModuleStructureVersion => moduleStructureVersion;

    [field: NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    public event Action<ItemData> ModuleStructureChanged;

    // 只有模块增删、替换与启用变化会使库存调度登记失效。
    public void NotifyModuleStructureChanged()
    {
        unchecked { moduleStructureVersion++; }
        ModuleStructureChanged?.Invoke(this);
    }

    // 临时模板移交独立集合时先解绑来源，避免默认实例被模块事件长期保活。
    public ModuleDataCollection DetachModuleData()
    {
        ModuleDataCollection detached = ModuleDataDic;
        ModuleDataDic = null;
        return detached;
    }

    #endregion

    // Unity 配置外壳不再进入存档，持久化统一使用实例快照。
    [Tooltip("实体所属的阵营/队伍 ID；为空时由运行时兼容规则推导")]
    [MemoryPackIgnore] public string FactionId = string.Empty;

    [Tooltip("制作材料赋予的耐久倍率；1 表示使用物品定义中的基础耐久")]
    [MemoryPackIgnore] public float CraftedDurabilityMultiplier = 1f;

    [Tooltip("物品实例当前的温度与含水率状态")]
    [MemoryPackIgnore] public ItemMatterState MatterState = new();

    #region 热量传导

    public const float DefaultHeatConductionRate = 1f; // 未在 JSON 配置时的基础热量传导速率(℃/s)

    [HideInInspector, JsonProperty("heatConductionRate")]
    [Tooltip("热量传导速率（℃/s）；由物品或玩家 JSON 配置，温差决定升温或降温方向")]
    [MemoryPackIgnore] public float HeatConductionRate = DefaultHeatConductionRate; // 实体自身的基础热量传导速率

    #endregion

    #region 定义与实例持久化边界

    [MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    public ItemSharedConfiguration SharedConfiguration { get; internal set; }

    [NonSerialized, MemoryPackIgnore, JsonIgnore]
    internal ModuleInstanceSnapshot[] PreservedModuleStates;

    [NonSerialized, MemoryPackIgnore, JsonIgnore]
    internal string[] PreservedAddedTags;

    [NonSerialized, MemoryPackIgnore, JsonIgnore]
    internal string[] PreservedRemovedTags;

    // 所有世界、库存与内嵌模块中的 ItemData 都由这个唯一属性写入实例状态。
    // 计算属性无需克隆标记；FastClonerIgnore 会调用 setter 写入 null。
    [JsonIgnore]
    public ItemInstanceSnapshot InstanceSnapshot
    {
        get => ItemSnapshotSerialization.Capture(this);
        set => (value ?? throw new InvalidOperationException("物品实例快照为空。")).RestoreColdTo(this);
    }

    #endregion

    //重写ToString方法，用于在控制台输出物品信息
    public override string ToString()
    {
        string str =
            $"物品定义 ID：{IDName}\n" +
            $"物品默认名称：{GameName}\n" +
            $"物品描述：{Description}\n" +
            $"物品重量：{Stack.Weight}kg\n" +
            $"物品体积：{Stack.Volume}L\n" +
            $"允许堆叠：{Stack.Stackable}\n" +
            $"物品耐久度：{Durability}\n" +
            $"是否可拾取：{Stack.CanBePickedUp}\n" +
            $"物品标签：{string.Join(", ", Tags)}\n" +
            $"物品堆叠信息：{Stack}\n" +
            $"物品特殊数据：{ItemSpecialData}\n" +
            $"全局唯一标识：{Guid}";
        return str;
    }

    public virtual int SyncData()
    {
        return 0;
    }

    public ModuleData GetModuleData_Frist(string moduleID)
    {
        foreach (var item in ModuleDataDic.Values)
        {
            if (item.ID == moduleID)
            {
                return item;
            }
        }
        Debug.LogError($"没有找到对应的模块({moduleID})数据!,检测ItemData中的Mods是否被初始化,检查mod是否被Save");
        return null;
    }

    public void AddAmount(float amount)
    {
        Stack.Amount += amount;
    }

    public void AddDurability(float amount)
    {
        Durability += amount;
        if (Durability > MaxDurability)
        {
            Durability = MaxDurability;
        }
    }

    #region 堆叠判定

    /// <summary>判断两个物品是否属于同一堆叠；空字符串与 null 视为同一份无特殊数据。</summary>
    public bool HasSameStackIdentity(ItemData other)
    {
        if (other == null || !string.Equals(IDName, other.IDName, StringComparison.Ordinal))
            return false;

        float ownDurabilityMultiplier = NormalizeCraftedDurabilityMultiplier(CraftedDurabilityMultiplier);
        float otherDurabilityMultiplier = NormalizeCraftedDurabilityMultiplier(other.CraftedDurabilityMultiplier);
        if (!Mathf.Approximately(ownDurabilityMultiplier, otherDurabilityMultiplier))
            return false;

        string ownSpecialData = string.IsNullOrEmpty(ItemSpecialData) ? string.Empty : ItemSpecialData;
        string otherSpecialData = string.IsNullOrEmpty(other.ItemSpecialData) ? string.Empty : other.ItemSpecialData;
        return string.Equals(ownSpecialData, otherSpecialData, StringComparison.Ordinal);
    }

    /// <summary>无效品质倍率按基础值处理，避免产生非法堆叠身份。</summary>
    private static float NormalizeCraftedDurabilityMultiplier(float multiplier)
    {
        return !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f
            ? multiplier
            : 1f;
    }

    /// <summary>判断两个物品是否允许合并进同一库存槽位。</summary>
    public bool CanStackWith(ItemData other)
    {
        return Stack != null && other?.Stack != null &&
               Stack.Stackable && other.Stack.Stackable &&
               HasSameStackIdentity(other);
    }

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class ItemMatterState
{
    public bool Initialized;
    public float TemperatureCelsius = 20f;
    public float Moisture;
    public bool IsBurning;
    public float CombustionElapsedSeconds;
}

/// <summary>Physics2D 回写给实体数据的瞬时结果，不承载战斗裁决。</summary>
public sealed class ItemPhysicsRuntimeState
{
    public Vector2 Velocity;
    public float AngularVelocity;
    public Vector2 LastContactPoint;
    public Vector2 LastContactNormal;
    public int LastContactItemGuid;
    public uint ContactVersion;
}

