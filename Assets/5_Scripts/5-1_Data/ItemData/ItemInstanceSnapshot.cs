using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using UnityEngine;

/// <summary>脱离活跃对象的实例状态，定义配置和 Unity 引用不进入快照。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ItemInstanceSnapshot
{
    #region 冻结状态

    [MemoryPackInclude, MemoryPackOrder(0)] private ItemInstanceKind kind;
    [MemoryPackInclude, MemoryPackOrder(1)] private string definitionId;
    [MemoryPackInclude, MemoryPackOrder(2)] private int guid;
    [MemoryPackInclude, MemoryPackOrder(3)] private float amount;
    [MemoryPackInclude, MemoryPackOrder(4)] private bool canBePickedUp;
    [MemoryPackInclude, MemoryPackOrder(5)] private float durabilityFraction;
    [MemoryPackInclude, MemoryPackOrder(6)] private float craftedDurabilityMultiplier;
    [MemoryPackInclude, MemoryPackOrder(7)] private string specialData;
    [MemoryPackInclude, MemoryPackOrder(8)] private bool inHand;
    [MemoryPackInclude, MemoryPackOrder(9)] private string factionId;
    [MemoryPackInclude, MemoryPackOrder(10)] private bool hasTransform;
    [MemoryPackInclude, MemoryPackOrder(11)] private float positionX;
    [MemoryPackInclude, MemoryPackOrder(12)] private float positionY;
    [MemoryPackInclude, MemoryPackOrder(13)] private float positionZ;
    [MemoryPackInclude, MemoryPackOrder(14)] private float rotationX;
    [MemoryPackInclude, MemoryPackOrder(15)] private float rotationY;
    [MemoryPackInclude, MemoryPackOrder(16)] private float rotationZ;
    [MemoryPackInclude, MemoryPackOrder(17)] private float rotationW;
    [MemoryPackInclude, MemoryPackOrder(18)] private float scaleX;
    [MemoryPackInclude, MemoryPackOrder(19)] private float scaleY;
    [MemoryPackInclude, MemoryPackOrder(20)] private float scaleZ;
    [MemoryPackInclude, MemoryPackOrder(21)] private byte[] matterPayload;
    [MemoryPackInclude, MemoryPackOrder(22)] private ModuleInstanceSnapshot[] modules;
    [MemoryPackInclude, MemoryPackOrder(23)] private string[] addedTags;
    [MemoryPackInclude, MemoryPackOrder(24)] private string[] removedTags;
    [MemoryPackInclude, MemoryPackOrder(25)] private byte[] kindPayload;
    [MemoryPackInclude, MemoryPackOrder(26)] private float physicsVelocityX;
    [MemoryPackInclude, MemoryPackOrder(27)] private float physicsVelocityY;
    [MemoryPackInclude, MemoryPackOrder(28)] private float physicsAngularVelocity;
    [MemoryPackInclude, MemoryPackOrder(29)] private bool persistentWorldResident;

    [MemoryPackIgnore] public string DefinitionId => definitionId;
    [MemoryPackIgnore] public int Guid => guid;
    [MemoryPackIgnore] public ItemInstanceKind Kind => kind;

    #endregion

    #region 捕获与恢复

    // 外层记录持有实时数量、耐久和物质时，内部载荷保持确定的稳定基线。
    public void ResetSeparatelyPersistedQuantityDurabilityAndMatterState()
    {
        amount = 1f;
        canBePickedUp = true;
        durabilityFraction = 1f;
        matterPayload = ItemSnapshotSerialization.SerializePayload(ItemMatterSnapshot.Capture(null));
    }

    public static ItemInstanceSnapshot Capture(ItemData data, bool includeTransform = true,
        bool publicPlayerState = false, Func<string, string> publicSpecialData = null,
        Func<ModuleData, bool, ModuleInstanceSnapshot> captureModule = null)
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
            matterPayload = ItemSnapshotSerialization.SerializePayload(ItemMatterSnapshot.Capture(data.MatterState)),
            physicsAngularVelocity = includeTransform && !data.inHand ? data.PhysicsState?.AngularVelocity ?? 0f : 0f,
            persistentWorldResident = includeTransform && !data.inHand && data.PhysicsState?.PersistentWorldResident == true
        };
        if (snapshot.hasTransform)
        {
            Vector3 position = data.transform.position;
            Quaternion rotation = data.transform.rotation;
            Vector3 scale = data.transform.scale;
            snapshot.positionX = position.x; snapshot.positionY = position.y; snapshot.positionZ = position.z;
            snapshot.rotationX = rotation.x; snapshot.rotationY = rotation.y; snapshot.rotationZ = rotation.z; snapshot.rotationW = rotation.w;
            snapshot.scaleX = scale.x; snapshot.scaleY = scale.y; snapshot.scaleZ = scale.z;
        }
        if (includeTransform && !data.inHand && data.PhysicsState != null)
        {
            snapshot.physicsVelocityX = data.PhysicsState.Velocity.x;
            snapshot.physicsVelocityY = data.PhysicsState.Velocity.y;
        }

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
                captured[pair.Key] = captureModule == null
                    ? ModuleInstanceSnapshot.Capture(module, publicPlayerState)
                    : captureModule(module, publicPlayerState);
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
        current.MatterState = MemoryPackSerializer.Deserialize<ItemMatterSnapshot>(matterPayload)?.Restore() ?? new ItemMatterState();
        if (current.Stack != null)
        {
            current.Stack.Amount = amount;
            current.Stack.CanBePickedUp = canBePickedUp;
        }
        if (hasTransform && !preserveTransform)
            current.transform = new ItemTransform
            {
                position = new Vector3(positionX, positionY, positionZ),
                rotation = new Quaternion(rotationX, rotationY, rotationZ, rotationW),
                scale = new Vector3(scaleX, scaleY, scaleZ)
            };
        if (!preserveTransform)
            current.PhysicsState = new ItemPhysicsRuntimeState
            { Velocity = new Vector2(physicsVelocityX, physicsVelocityY), AngularVelocity = physicsAngularVelocity, PersistentWorldResident = persistentWorldResident };
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
            !IsFinite(physicsVelocityX) || !IsFinite(physicsVelocityY) || !IsFinite(physicsAngularVelocity) ||
            (kind != ItemInstanceKind.Block && kindPayload == null) ||
            (hasTransform && (!IsFinite(positionX) || !IsFinite(positionY) || !IsFinite(positionZ) ||
                !IsFinite(rotationX) || !IsFinite(rotationY) || !IsFinite(rotationZ) || !IsFinite(rotationW) ||
                !IsFinite(scaleX) || !IsFinite(scaleY) || !IsFinite(scaleZ))))
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

public enum ItemInstanceKind : byte { General = 0, Player = 1, TileMap = 2, Block = 3 }

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ItemMatterSnapshot
{
    #region 物质存档字段
    [MemoryPackOrder(0)] public bool Initialized;
    [MemoryPackOrder(1)] public float? TemperatureCelsius;
    [MemoryPackOrder(2)] public float Moisture;
    [MemoryPackOrder(3)] public bool IsBurning;
    [MemoryPackOrder(4)] public float CombustionElapsedSeconds;

    public static ItemMatterSnapshot Capture(ItemMatterState state) => state == null ? new ItemMatterSnapshot() : new ItemMatterSnapshot
    {
        Initialized = state.Initialized, TemperatureCelsius = state.TemperatureCelsius, Moisture = state.Moisture,
        IsBurning = state.IsBurning, CombustionElapsedSeconds = state.CombustionElapsedSeconds
    };
    public ItemMatterState Restore() => new ItemMatterState
    {
        Initialized = Initialized, TemperatureCelsius = TemperatureCelsius ?? 20f, Moisture = Moisture,
        IsBurning = IsBurning, CombustionElapsedSeconds = CombustionElapsedSeconds
    };
    #endregion
}

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

    public static bool IsPublicPlayerStateCapture => publicSpecialDataFilter != null;

    public static IDisposable BeginPrivatePlayerState()
    {
        var scope = new CaptureScope(publicSpecialDataFilter);
        publicSpecialDataFilter = null;
        return scope;
    }

    public static IDisposable BeginPublicPlayerState(Func<string, string> specialDataFilter)
    {
        if (specialDataFilter == null) throw new ArgumentNullException(nameof(specialDataFilter));
        var scope = new CaptureScope(publicSpecialDataFilter);
        publicSpecialDataFilter = specialDataFilter;
        return scope;
    }

    public static ItemInstanceSnapshot Capture(ItemData data, bool includeTransform = true,
        Func<ModuleData, bool, ModuleInstanceSnapshot> captureModule = null) => ItemInstanceSnapshot.Capture(data, includeTransform,
        publicPlayerState: data is Data_Player && publicSpecialDataFilter != null,
        publicSpecialData: publicSpecialDataFilter, captureModule: captureModule);

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
[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class PlayerInstanceSnapshot
{
    [MemoryPackOrder(0)] public string SceneName;
    [MemoryPackOrder(1)] public bool HasHp;
    [MemoryPackOrder(2)] public float HpValue;
    [MemoryPackOrder(3)] public float HpMaximum;
    [MemoryPackOrder(4)] public List<string> HpWeaknesses;
    [MemoryPackOrder(5)] public bool HasDefense;
    [MemoryPackOrder(6)] public float DefenseCutting;
    [MemoryPackOrder(7)] public float DefensePiercing;
    [MemoryPackOrder(8)] public float DefenseChopping;
    [MemoryPackOrder(9)] public float DefenseBlunt;
    [MemoryPackOrder(10)] public bool HasSpeed;
    [MemoryPackOrder(11)] public float SpeedBase;
    [MemoryPackOrder(12)] public float SpeedBaseAdditive;
    [MemoryPackOrder(13)] public float SpeedAdditiveModifier;
    [MemoryPackOrder(14)] public float? SpeedMultiplicativeModifier;
    [MemoryPackOrder(15)] public float SpeedFinalAdditive;
    [MemoryPackOrder(16)] public float Stamina;
    [MemoryPackOrder(17)] public float StaminaMax;
    [MemoryPackOrder(18)] public float StaminaRecoverySpeed;
    [MemoryPackOrder(19)] public string UserName;
    [MemoryPackOrder(20)] public float PlayerPov;
    [MemoryPackOrder(21)] public float MaxCarryWeight;
    [MemoryPackOrder(22)] public float MaxCarryVolume;
    [MemoryPackOrder(23)] public float? PerceptionRadiusMultiplier;
    [MemoryPackOrder(24)] public Dictionary<string, InventoryInstanceSnapshot> Inventories;

    public static byte[] Capture(Data_Player data, bool publicState)
    {
        return ItemSnapshotSerialization.SerializePayload(new PlayerInstanceSnapshot
        {
            SceneName = data.CurrentSceneName,
            HasHp = data.hp != null, HpValue = data.hp?.Value ?? 0f, HpMaximum = data.hp?.maxValue ?? 0f,
            HpWeaknesses = data.hp?.Weaknesses == null ? null : new List<string>(data.hp.Weaknesses),
            HasDefense = data.defense != null, DefenseCutting = data.defense?.Cutting ?? 0f,
            DefensePiercing = data.defense?.Piercing ?? 0f, DefenseChopping = data.defense?.Chopping ?? 0f,
            DefenseBlunt = data.defense?.Blunt ?? 0f,
            HasSpeed = data.Speed != null, SpeedBase = data.Speed?.BaseValue ?? 0f,
            SpeedBaseAdditive = data.Speed?.BaseAdditive ?? 0f, SpeedAdditiveModifier = data.Speed?.AdditiveModifier ?? 0f,
            SpeedMultiplicativeModifier = data.Speed?.MultiplicativeModifier ?? 1f,
            SpeedFinalAdditive = data.Speed?.FinalAdditive ?? 0f,
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
        if (state.HasHp)
        {
            target.hp.maxValue = state.HpMaximum;
            target.hp.Weaknesses = state.HpWeaknesses ?? new List<string>();
        }
        target.defense ??= new Defense();
        if (state.HasDefense)
        {
            target.defense.Cutting = state.DefenseCutting;
            target.defense.Piercing = state.DefensePiercing;
            target.defense.Chopping = state.DefenseChopping;
            target.defense.Blunt = state.DefenseBlunt;
        }
        target.Speed ??= new GameValue_float();
        if (state.HasSpeed)
        {
            target.Speed.BaseValue = state.SpeedBase;
            target.Speed.BaseAdditive = state.SpeedBaseAdditive;
            target.Speed.AdditiveModifier = state.SpeedAdditiveModifier;
            target.Speed.MultiplicativeModifier = state.SpeedMultiplicativeModifier ?? 1f;
            target.Speed.FinalAdditive = state.SpeedFinalAdditive;
        }
        target.stamina = state.Stamina;
        target.staminaMax = state.StaminaMax;
        target.staminaRecoverySpeed = state.StaminaRecoverySpeed;
        target.Name_User = state.UserName;
        target.PlayerPov = state.PlayerPov;
        target.MaxCarryWeight = state.MaxCarryWeight;
        target.MaxCarryVolume = state.MaxCarryVolume;
        target.PerceptionRadiusMultiplier = state.PerceptionRadiusMultiplier ?? 1f;
        if (state.Inventories != null)
            InventoryInstanceSnapshot.RestoreDictionary(state.Inventories, target._inventoryData ??= new());
        if (state.HasHp) target.hp.Value = state.HpValue;
    }
}
