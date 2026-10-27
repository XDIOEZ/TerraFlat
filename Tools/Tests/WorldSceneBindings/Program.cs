using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 世界与场景绑定回归
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var bindings = new WorldSceneBindingIndex();
        object first = new(), replacement = new(), second = new();
        const int sceneA = 101, sceneB = 202;
        const string worldA = "world:earth", worldB = "world:ship";
        Require(!bindings.TryResolveWorldKeyForScene(sceneA, null, out _), "未绑定的场景不应有世界。");
        Require(bindings.BindWorldToScene(sceneA, worldA, first), "首次绑定失败。");
        Require(bindings.TryResolveWorldKeyForScene(sceneA, null, out string single) && single == worldA,
            "世界身份不能正确解析，与场景标识无关。");
        Require(!bindings.BindWorldToScene(sceneA, worldA, first), "重复绑定改变了归属。");
        Require(bindings.BindWorldToScene(sceneA, worldB, second), "同场景第二世界绑定失败。");
        Require(!bindings.TryResolveWorldKeyForScene(sceneA, null, out _), "多世界场景不能猜测归属。");
        Require(bindings.TryResolveWorldKeyForScene(sceneA, worldA, out string explicitA) && explicitA == worldA &&
                bindings.TryResolveWorldKeyForScene(sceneA, worldB, out string explicitB) && explicitB == worldB,
            "明确上下文没有选中对应的世界。");
        Require(!bindings.TryResolveWorldKeyForScene(sceneA, "world:missing", out _), "错误的明确上下文不能回退。");
        Require(bindings.BindWorldToScene(sceneA, worldA, replacement), "新拥有者没有接替旧绑定。");
        Require(!bindings.UnbindWorldFromScene(sceneA, worldA, first) &&
                !bindings.TryResolveWorldKeyForOwner(sceneA, worldA, first, out _) &&
                bindings.TryResolveWorldKeyForOwner(sceneA, worldA, replacement, out string ownerKey) && ownerKey == worldA,
            "旧拥有者清理新绑定或沿用已失效的世界上下文。");
        Require(bindings.UnbindWorldFromScene(sceneA, worldA, replacement), "新拥有者未能解绑。");
        Require(bindings.TryResolveWorldKeyForScene(sceneA, null, out single) && single == worldB,
            "移除其他世界后唯一世界没有恢复可查询。");
        Require(bindings.BindWorldToScene(sceneB, worldB, second), "世界未能绑定第二个场景。");
        Require(bindings.UnbindWorldFromScene(sceneA, worldB, second) &&
                !bindings.HasWorldsForScene(sceneA) && bindings.HasWorldsForScene(sceneB),
            "解绑一个场景破坏了同世界的其他场景。");

        var identities = new RuntimeWorldIdentityIndex();
        int firstId = identities.GetOrCreateRuntimeWorldIdentity(worldA);
        int secondId = identities.GetOrCreateRuntimeWorldIdentity(worldB);
        Require(firstId > 0 && secondId > 0 && firstId != secondId &&
                identities.GetOrCreateRuntimeWorldIdentity(new string(worldA.ToCharArray())) == firstId,
            "世界整数身份不唯一或不稳定。");
        var allocated = new HashSet<int> { firstId, secondId };
        for (int i = 0; i < 4096; i++)
            Require(allocated.Add(identities.GetOrCreateRuntimeWorldIdentity("world:" + i)), "不同世界身份冲突。");

        for (int i = 0; i < 32; i++)
        {
            bindings.TryResolveWorldKeyForScene(sceneB, null, out _);
            identities.TryGetRuntimeWorldIdentity(worldA, out _);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool stable = true;
        for (int i = 0; i < 8192; i++)
            stable &= bindings.TryResolveWorldKeyForScene(sceneB, null, out _) &&
                      identities.TryGetRuntimeWorldIdentity(worldA, out _);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(stable && bytes == 0, $"稳定查询分配了 {bytes} 字节。");

        var memberships = new WorldItemOwnershipIndex<object>(identities);
        object itemA = new(), itemB = new();
        Require(memberships.AssignItemToWorld(itemA, worldA) && memberships.AssignItemToWorld(itemB, worldB) &&
                memberships.GetWorldItemCount(worldA) == 1 && memberships.GetWorldItemCount(worldB) == 1,
            "世界没有独立登记物品。");
        Require(memberships.TryGetOwnership(itemA, out var firstOwner) && firstOwner.WorldIdentity == firstId,
            "物品反向索引没有返回世界身份。");
        ulong firstRevision = firstOwner.Revision;
        ulong worldARevision = memberships.GetWorldOwnershipRevision(worldA);
        object unrelated = new();
        memberships.AssignItemToWorld(unrelated, worldB);
        Require(memberships.GetWorldOwnershipRevision(worldA) == worldARevision,
            "其他世界的物品变化不应使本世界感知缓存失效。");
        memberships.DetachItem(unrelated);
        ulong beforeDuplicate = memberships.Revision;
        Require(!memberships.AssignItemToWorld(itemA, worldA) && memberships.Revision == beforeDuplicate,
            "重复登记不应改变归属版本。");
        Require(memberships.AssignItemToWorld(itemA, worldB) && memberships.GetWorldItemCount(worldA) == 0 &&
                memberships.GetWorldItemCount(worldB) == 2 &&
                memberships.AssignItemToWorld(itemA, worldA) &&
                memberships.TryGetOwnership(itemA, out var returnedOwner) && returnedOwner.Revision > firstRevision,
            "跨世界往返应移动集合并使旧查询结果失效。");
        for (int i = 0; i < 32; i++) memberships.TryGetOwnership(itemA, out _);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8192; i++) stable &= memberships.TryGetOwnership(itemA, out _);
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(stable && bytes == 0, $"物品归属查询分配了 {bytes} 字节。");
        Require(memberships.DetachItem(itemA) && !memberships.TryGetOwnership(itemA, out _) &&
                memberships.GetWorldItemCount(worldA) == 0, "离开世界后归属记录未清理。");
        memberships.Clear();
        Require(!memberships.TryGetOwnership(itemB, out _) && memberships.GetWorldItemCount(worldB) == 0,
            "清空世界没有释放物品归属。");

        identities.Clear();
        Require(!identities.TryGetRuntimeWorldIdentity(worldA, out _) &&
                identities.GetOrCreateRuntimeWorldIdentity(worldA) > secondId,
            "清理后的世界身份被旧任务复用。");
        bindings.Clear();
        Require(!bindings.HasWorldsForScene(sceneB), "清理场景绑定失败。");
        bindings.Clear();
        Require(!bindings.HasWorldsForScene(sceneB), "空绑定重复清理改变了归属。");
        Console.WriteLine("World bindings and item ownership: all checks passed; stable lookups allocated 0 bytes.");
    }
    #endregion
}
