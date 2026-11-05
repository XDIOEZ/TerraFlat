using System;
using System.Runtime.CompilerServices;
using MemoryPack;

/// <summary>档案只保存名称和最后提交的世界地址，角色数值由通用物品及模块持有。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ActorProfileMetadata
{
    #region 档案元数据
    [MemoryPackOrder(0)] public string DisplayName = string.Empty;
    [MemoryPackOrder(1)] public string WorldKey = "地球";
    #endregion
}

public static class ActorProfileMetadataStore
{
    #region 冷角色与档案元数据关联
    private static readonly ConditionalWeakTable<ItemData, ActorProfileMetadata> metadata = new();

    public static ActorProfileMetadata GetActorProfileMetadata(this ItemData actorData)
        => metadata.GetValue(actorData, _ => new ActorProfileMetadata());

    public static void BindActorProfileMetadata(ItemData actorData, ActorProfileMetadata value)
    {
        metadata.Remove(actorData);
        metadata.Add(actorData, value ?? new ActorProfileMetadata());
    }

    public static Data_GeneralItem CreateActorData(Data_GeneralItem actorData, string displayName = null, string worldKey = null)
    {
        var value = actorData.GetActorProfileMetadata();
        if (displayName != null) value.DisplayName = displayName;
        if (worldKey != null) value.WorldKey = worldKey;
        return actorData;
    }
    #endregion
}

/// <summary>SQLite v2 将档案外壳与通用角色快照封装为独立记录。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ActorProfileSnapshot
{
    #region 独立档案记录
    [MemoryPackOrder(0)] public ItemInstanceSnapshot Actor;
    [MemoryPackOrder(1)] public ActorProfileMetadata Metadata;

    public static ActorProfileSnapshot Capture(ItemData data) => new()
    {
        Actor = ItemSnapshotSerialization.Capture(data),
        Metadata = new ActorProfileMetadata
        {
            DisplayName = data.GetActorProfileMetadata().DisplayName,
            WorldKey = data.GetActorProfileMetadata().WorldKey
        }
    };

    public ItemData RestoreActorData()
    {
        ItemData result = Actor.CreateColdData();
        ActorProfileMetadataStore.BindActorProfileMetadata(result, Metadata);
        return result;
    }
    #endregion
}
