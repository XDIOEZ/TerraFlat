using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using UnityEngine;

/// <summary>脱离活跃对象的实例状态，定义配置和 Unity 引用不进入快照。</summary>
[MemoryPackable]
public sealed partial class ItemInstanceSnapshot
{
    #region 冻结状态

    [MemoryPackInclude] private ItemInstanceKind kind;
    [MemoryPackInclude] private string definitionId;
    [MemoryPackInclude] private int guid;
    [MemoryPackInclude] private float amount;
    [MemoryPackInclude] private bool canBePickedUp;
    [MemoryPackInclude] private float durabilityFraction;
    [MemoryPackInclude] private float craftedDurabilityMultiplier;
    [MemoryPackInclude] private string specialData;
    [MemoryPackInclude] private bool inHand;
    [MemoryPackInclude] private string factionId;
    [MemoryPackInclude] private bool hasTransform;
    [MemoryPackInclude] private Vector3 position;
    [MemoryPackInclude] private Quaternion rotation;
    [MemoryPackInclude] private Vector3 scale;
    [MemoryPackInclude] private byte[] matterPayload;
    [MemoryPackInclude] private ModuleInstanceSnapshot[] modules;
    [MemoryPackInclude] private string[] addedTags;
    [MemoryPackInclude] private string[] removedTags;
    [MemoryPackInclude] private byte[] kindPayload;

    [MemoryPackIgnore] public string DefinitionId => definitionId;
    [MemoryPackIgnore] public int Guid => guid;
    [MemoryPackIgnore] public ItemInstanceKind Kind => kind;

    #endregion

    #region 捕获与恢复

    public static ItemInstanceSnapshot Capture(ItemData data, bool includeTransform = true,
        bool publicPlayerState = false, Func<string, string> publicSpecialData = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var snapshot = new ItemInstanceSnapshot
        {
            kind = GetKind(data), definitionId = data.IDName, guid = data.Guid,
            amount = data.Stack?.Amount ?? 1f, canBePickedUp = data.Stack?.CanBePickedUp ?? true,
            durabilityFraction = data.MaxDurability > 0f ? Mathf.Clamp01(data.Durability / data.MaxDurability) : 1f,
            craftedDurabilityMultiplier = NormalizeMultiplier(data.CraftedDurabilityMultiplier),
            specialData = publicPlayerState && publicSpecialData != null
                ? publicSpecialData(data.ItemSpecialData) : data.ItemSpecialData,
            inHand = data.inHand, factionId = data.FactionId,
            hasTransform = includeTransform && data.transform != null,
            position = includeTransform && data.transform != null ? data.transform.position : Vector3.zero,
            rotation = includeTransform && data.transform != null ? data.transform.rotation : Quaternion.identity,
            scale = includeTransform && data.transform != null ? data.transform.scale : Vector3.one,
            matterPayload = ItemSnapshotSerialization.SerializePayload(data.MatterState ?? new ItemMatterState())
        };

        var captured = new SortedDictionary<string, ModuleInstanceSnapshot>(StringComparer.Ordinal);
        if (data.PreservedModuleStates != null)
            foreach (ModuleInstanceSnapshot state in data.PreservedModuleStates)
                if (state != null) captured[state.StableName] = state;
        if (data.ModuleDataDic != null)
            foreach (KeyValuePair<string, ModuleData> pair in data.ModuleDataDic)
            {
                ModuleData module = pair.Value;
                if (module == null) continue;
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key != module.StableName)
                    throw new InvalidDataException($"物品 {data.IDName} 的模块稳定名与索引不一致：{pair.Key}");
                captured[pair.Key] = ModuleInstanceSnapshot.Capture(module, publicPlayerState);
            }
        snapshot.modules = new ModuleInstanceSnapshot[captured.Count];
        captured.Values.CopyTo(snapshot.modules, 0);
        snapshot.CaptureTagOverrides(data);

        switch (data)
        {
            case Data_GeneralItem general:
                snapshot.kindPayload = ItemSnapshotSerialization.SerializePayload(general.code);
                break;
            case Data_Player player:
                snapshot.kindPayload = PlayerInstanceSnapshot.Capture(player, publicPlayerState);
                break;
            case Data_TileMap map:
                snapshot.kindPayload = map.CaptureMapInstanceState();
                break;
        }
        return snapshot;
    }

    /// <summary>只恢复实例值，模块集合及静态配置始终由目标当前定义决定。</summary>
    public void RestoreTo(ItemData current, bool preserveTransform = false, bool preserveGuid = false)
    {
        Validate();
        if (current == null || GetKind(current) != kind)
            throw new InvalidDataException("物品实例快照与目标数据类别不一致。");

        float oldMultiplier = NormalizeMultiplier(current.CraftedDurabilityMultiplier);
        float baseDurability = current.SharedConfiguration?.MaxDurability ?? current.MaxDurability / oldMultiplier;
        current.CraftedDurabilityMultiplier = craftedDurabilityMultiplier;
        current.MaxDurability = Mathf.Max(0f, baseDurability) * craftedDurabilityMultiplier;
        current.Durability = current.MaxDurability * durabilityFraction;
        if (!preserveGuid) current.Guid = guid;
        current.ItemSpecialData = specialData;
        current.inHand = inHand;
        current.FactionId = factionId;
        current.MatterState = MemoryPackSerializer.Deserialize<ItemMatterState>(matterPayload) ?? new ItemMatterState();
        if (current.Stack != null)
        {
            current.Stack.Amount = amount;
            current.Stack.CanBePickedUp = canBePickedUp;
        }
        if (hasTransform && !preserveTransform)
            current.transform = new ItemTransform { position = position, rotation = rotation, scale = scale };
        RestoreTagOverrides(current);

        var unbound = new List<ModuleInstanceSnapshot>();
        foreach (ModuleInstanceSnapshot state in modules ?? Array.Empty<ModuleInstanceSnapshot>())
        {
            if (current.ModuleDataDic != null && current.ModuleDataDic.TryGetValue(state.StableName, out ModuleData target) &&
                state.CanRestoreTo(target))
                state.RestoreTo(target);
            else
                unbound.Add(state);
        }
        // 未加载的扩展状态继续保存，但不能凭快照创建当前定义已删除的模块。
        current.PreservedModuleStates = unbound.Count == 0 ? null : unbound.ToArray();
        ItemInstanceStateRestorationRegistry.Restore(current);
        RestoreKindState(current);
    }

    public ItemData CreateColdData()
    {
        ItemData data = CreateData(kind);
        RestoreColdTo(data);
        return data;
    }

    internal void RestoreColdTo(ItemData data)
    {
        Validate();
        if (GetKind(data) != kind) throw new InvalidDataException("持久化物品类别与快照不一致。");
        data.IDName = definitionId;
        data.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        data.Stack = new ItemStack();
        data.CraftedDurabilityMultiplier = 1f;
        data.MaxDurability = 1f;
        foreach (ModuleInstanceSnapshot state in modules ?? Array.Empty<ModuleInstanceSnapshot>())
        {
            ModuleData cold = state.CreateColdData();
            if (cold != null) data.ModuleDataDic.Add(state.StableName, cold);
        }
        RestoreTo(data);
    }

    private void RestoreKindState(ItemData data)
    {
        switch (data)
        {
            case Data_GeneralItem general:
                general.code = kindPayload == null ? null : MemoryPackSerializer.Deserialize<string>(kindPayload);
                break;
            case Data_Player player:
                PlayerInstanceSnapshot.Restore(kindPayload, player);
                break;
            case Data_TileMap map:
                map.RestoreMapInstanceState(kindPayload);
                break;
        }
    }

    private void CaptureTagOverrides(ItemData data)
    {
        if (data.SharedConfiguration == null)
        {
            addedTags = data.PreservedAddedTags;
            removedTags = data.PreservedRemovedTags;
            return;
        }
        var baseline = new HashSet<string>(data.SharedConfiguration.Tags, StringComparer.Ordinal);
        var actual = data.Tags == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(data.Tags, StringComparer.Ordinal);
        var added = new List<string>();
        foreach (string tag in actual) if (!baseline.Contains(tag)) added.Add(tag);
        var removed = new List<string>();
        foreach (string tag in baseline) if (!actual.Contains(tag)) removed.Add(tag);
        added.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);
        addedTags = added.Count == 0 ? null : added.ToArray();
        removedTags = removed.Count == 0 ? null : removed.ToArray();
    }

    private void RestoreTagOverrides(ItemData data)
    {
        data.Tags ??= new List<string>();
        if (data.SharedConfiguration != null)
        {
            data.Tags.Clear();
            foreach (string tag in data.SharedConfiguration.Tags) data.Tags.Add(tag);
        }
        else if (data.PreservedAddedTags != null)
            foreach (string tag in data.PreservedAddedTags) data.Tags.Remove(tag);
        foreach (string tag in removedTags ?? Array.Empty<string>()) data.Tags.Remove(tag);
        foreach (string tag in addedTags ?? Array.Empty<string>())
            if (!data.Tags.Contains(tag)) data.Tags.Add(tag);
        data.PreservedAddedTags = addedTags;
        data.PreservedRemovedTags = removedTags;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(definitionId) || !Enum.IsDefined(typeof(ItemInstanceKind), kind) ||
            !IsFinite(amount) || amount < 0f || !IsFinite(durabilityFraction) || durabilityFraction < 0f || durabilityFraction > 1f ||
            !IsFinite(craftedDurabilityMultiplier) || craftedDurabilityMultiplier <= 0f || matterPayload == null ||
            (kind != ItemInstanceKind.Block && kindPayload == null) ||
            (hasTransform && (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w) ||
                !IsFinite(scale.x) || !IsFinite(scale.y) || !IsFinite(scale.z))))
            throw new InvalidDataException("物品实例快照身份或数值无效。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModuleInstanceSnapshot state in modules ?? Array.Empty<ModuleInstanceSnapshot>())
            if (state == null || !state.IsValid || !names.Add(state.StableName))
                throw new InvalidDataException("物品实例快照包含无效或重复的稳定模块名。");
    }

    public static ItemInstanceKind GetKind(ItemData data) => data switch
    {
        Data_GeneralItem => ItemInstanceKind.General,
        Data_Player => ItemInstanceKind.Player,
        Data_TileMap => ItemInstanceKind.TileMap,
        BlockData => ItemInstanceKind.Block,
        _ => throw new InvalidDataException($"物品数据类别尚未注册：{data?.GetType().FullName}")
    };

    internal static ItemData CreateData(ItemInstanceKind kind) => kind switch
    {
        ItemInstanceKind.General => new Data_GeneralItem(),
        ItemInstanceKind.Player => new Data_Player(),
        ItemInstanceKind.TileMap => new Data_TileMap(),
        ItemInstanceKind.Block => new BlockData(),
        _ => throw new InvalidDataException("物品快照类别尚未注册。")
    };

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static float NormalizeMultiplier(float value) => IsFinite(value) && value > 0f ? value : 1f;

    #endregion
}

public enum ItemInstanceKind : byte { General, Player, TileMap, Block }

/// <summary>公开世界快照使用独立的捕获策略，策略只作用于同步序列化调用所在的线程。</summary>
public static class ItemSnapshotSerialization
{
    #region 独立负载序列化

    /// <summary>快照捕获可嵌套在外层序列化中，每份负载独占缓冲和引用状态。</summary>
    public static byte[] SerializePayload<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var state = MemoryPackWriterOptionalStatePool.Rent(null);
        var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref buffer, state);
        MemoryPackSerializer.Serialize(ref writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    #endregion

    #region 序列化捕获策略

    [ThreadStatic] private static Func<string, string> publicSpecialDataFilter;

    public static IDisposable BeginPublicPlayerState(Func<string, string> specialDataFilter)
    {
        if (specialDataFilter == null) throw new ArgumentNullException(nameof(specialDataFilter));
        var scope = new CaptureScope(publicSpecialDataFilter);
        publicSpecialDataFilter = specialDataFilter;
        return scope;
    }

    internal static ItemInstanceSnapshot Capture(ItemData data) => ItemInstanceSnapshot.Capture(data,
        publicPlayerState: data is Data_Player && publicSpecialDataFilter != null,
        publicSpecialData: publicSpecialDataFilter);

    private sealed class CaptureScope : IDisposable
    {
        private readonly Func<string, string> previous;
        private bool disposed;
        public CaptureScope(Func<string, string> previous) => this.previous = previous;
        public void Dispose()
        {
            if (disposed) return;
            publicSpecialDataFilter = previous;
            disposed = true;
        }
    }

    #endregion
}

/// <summary>角色属性属于角色进度；私有库存由公开网络快照排除。</summary>
[MemoryPackable]
internal sealed partial class PlayerInstanceSnapshot
{
    public string SceneName;
    public Hp Hp;
    public Defense Defense;
    public GameValue_float Speed;
    public float Stamina;
    public float StaminaMax;
    public float StaminaRecoverySpeed;
    public string UserName;
    public float PlayerPov;
    public float MaxCarryWeight;
    public float MaxCarryVolume;
    public float PerceptionRadiusMultiplier;
    public Dictionary<string, InventoryInstanceSnapshot> Inventories;

    public static byte[] Capture(Data_Player data, bool publicState)
    {
        return ItemSnapshotSerialization.SerializePayload(new PlayerInstanceSnapshot
        {
            SceneName = data.CurrentSceneName, Hp = data.hp, Defense = data.defense, Speed = data.Speed,
            Stamina = data.stamina, StaminaMax = data.staminaMax, StaminaRecoverySpeed = data.staminaRecoverySpeed,
            UserName = data.Name_User, PlayerPov = data.PlayerPov, MaxCarryWeight = data.MaxCarryWeight,
            MaxCarryVolume = data.MaxCarryVolume, PerceptionRadiusMultiplier = data.PerceptionRadiusMultiplier,
            Inventories = publicState ? null : InventoryInstanceSnapshot.CaptureDictionary(data._inventoryData)
        });
    }

    public static void Restore(byte[] payload, Data_Player target)
    {
        if (payload == null) return;
        PlayerInstanceSnapshot state = MemoryPackSerializer.Deserialize<PlayerInstanceSnapshot>(payload);
        if (state == null) throw new InvalidDataException("角色实例状态为空。");
        target.CurrentSceneName = state.SceneName;
        target.hp ??= new Hp(30f);
        if (state.Hp != null)
        {
            target.hp.maxValue = state.Hp.maxValue;
            target.hp.Weaknesses = state.Hp.Weaknesses ?? new List<string>();
        }
        target.defense ??= new Defense();
        if (state.Defense != null)
        {
            target.defense.Cutting = state.Defense.Cutting;
            target.defense.Piercing = state.Defense.Piercing;
            target.defense.Chopping = state.Defense.Chopping;
            target.defense.Blunt = state.Defense.Blunt;
        }
        target.Speed ??= new GameValue_float();
        if (state.Speed != null)
        {
            target.Speed.BaseValue = state.Speed.BaseValue;
            target.Speed.BaseAdditive = state.Speed.BaseAdditive;
            target.Speed.AdditiveModifier = state.Speed.AdditiveModifier;
            target.Speed.MultiplicativeModifier = state.Speed.MultiplicativeModifier;
            target.Speed.FinalAdditive = state.Speed.FinalAdditive;
        }
        target.stamina = state.Stamina;
        target.staminaMax = state.StaminaMax;
        target.staminaRecoverySpeed = state.StaminaRecoverySpeed;
        target.Name_User = state.UserName;
        target.PlayerPov = state.PlayerPov;
        target.MaxCarryWeight = state.MaxCarryWeight;
        target.MaxCarryVolume = state.MaxCarryVolume;
        target.PerceptionRadiusMultiplier = state.PerceptionRadiusMultiplier;
        if (state.Inventories != null)
            InventoryInstanceSnapshot.RestoreDictionary(state.Inventories, target._inventoryData ??= new());
        if (state.Hp != null) target.hp.Value = state.Hp.Value;
    }
}
