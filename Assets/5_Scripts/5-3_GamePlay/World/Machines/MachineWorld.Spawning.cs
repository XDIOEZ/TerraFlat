using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 世界生成与恢复
    /// <summary>世界生成直接交付数据实体；稳定 GUID 防止区块重载重复生成，已拆除的生成设施不会复活。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MachineEntity SpawnGenerated(ItemData snapshot, Vector3 position)
    {
        if (!GameNetwork.HasStateAuthority) return null;
        EnsureScope();
        if (snapshot == null || snapshot.Guid == 0 || !OwnsWorldItem(snapshot))
            throw new ArgumentException("生成机器必须带有效的落地定义和稳定 GUID。", nameof(snapshot));
        if (nodes.TryGetValue(snapshot.Guid, out MachineEntity existing)) return existing;
        if (owner?.Mechanical?.RemovedGenerated.TryGetValue(worldKey, out var removed) == true && removed.Contains(snapshot.Guid)) return null;
        ItemData candidate = FastCloner.FastCloner.DeepClone(snapshot);
        candidate.transform ??= new ItemTransform();
        Vector2Int cell = CellOf(position);
        candidate.transform.position = new Vector3(cell.x + .5f, cell.y + .5f, position.z);
        candidate.inHand = false;
        Mod_Building.SetInstalledDataState(candidate);
        MachineState state = ReadMachineState(candidate);
        state.Generated = true;
        WriteMachineState(candidate, state);
        return RestoreMachine(candidate);
    }

    /// <summary>存档恢复与世界生成共用数据入口；不借用临时 Item 初始化机器。</summary>
    public static MachineEntity RestoreMachine(ItemData snapshot)
    {
        if (!GameNetwork.HasStateAuthority) throw new InvalidOperationException("只有权威端可以恢复机器。");
        EnsureScope();
        if (snapshot == null || snapshot.Guid == 0 || !OwnsWorldItem(snapshot)) throw new ArgumentException("机器快照无效。");
        if (nodes.TryGetValue(snapshot.Guid, out var existing)) return existing;
        if (GetAt(CellOf(snapshot.transform.position), MachineCatalog.Get(snapshot.IDName).Layer) != null)
            throw new InvalidOperationException("恢复机器的占地已经被其它实体使用。");
        RestoreDescriptor(FastCloner.FastCloner.DeepClone(snapshot));
        MachineEntity entity = nodes[snapshot.Guid];
        try { WakeNode(entity); }
        catch
        {
            DisposeProcessor(entity);
            nodes.Remove(entity.Id);
            dirty = true;
            throw;
        }
        dirty = true;
        BuildingOccupancyRegistry.NotifyMechanicalChanged(entity.Cell);
        CellChanged?.Invoke(entity.Cell);
        NodeStateChanged?.Invoke(entity);
        return entity;
    }

    private static void RememberGeneratedRemoval(MachineEntity entity)
    {
        MachineState state = entity.State ?? ReadMachineState(entity.Snapshot);
        if (!state.Generated || owner == null) return;
        owner.Mechanical ??= new MachineArchive();
        if (!owner.Mechanical.RemovedGenerated.TryGetValue(worldKey, out var removed))
            owner.Mechanical.RemovedGenerated[worldKey] = removed = new HashSet<int>();
        removed.Add(entity.Id);
    }
    #endregion
}
