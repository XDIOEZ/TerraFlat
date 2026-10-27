#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestWorldPhysicsProxyRuntimeProbe
{
    #region 原生触发的准确能力接收与复用清理
    private sealed class TriggerCounter
    {
        internal int Enter, Stay, Exit, InvalidIdentity;
        internal int Total => Enter + Stay + Exit;
    }

    // 用自有数据能力接收真实原生事件，回调不补造碰撞或重算进入离开规则。
    private static JObject CaptureResourceProxyNativeTriggers(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        using var worldScope = GameplayWorldRegistry.UseWorld(world);
        const string prefab = "probe:world-physics-trigger";
        const string moduleSlot = "probe:trigger-receiver";
        Item visitor = null;
        var counters = new Dictionary<NaturalEntityHandle, TriggerCounter>();
        bool Compile(RuntimeItemModuleDefinition module, out ResourceEntityCapability capability, out string reason)
        {
            reason = null;
            capability = new ResourceEntityCapability(context =>
            {
                var counter = new TriggerCounter();
                context.SetState(counter);
                counters.Add(context.Handle, counter);
            }, physicsTrigger: (context, contact) =>
            {
                TriggerCounter counter = context.GetState<TriggerCounter>();
                if (counter == null) throw new InvalidOperationException("触发接收者丢失了原数据能力状态。");
                if (visitor == null || !WorldPhysicsService2D.TryResolveCollider(contact.OtherCollider, out var other) ||
                    other.Item != visitor) return;
                bool exact = context.Handle.IsValid && context.Definition.Id == "Tree_Pine" &&
                    context.Module.StableName == moduleSlot && contact.ReceiverModule == moduleSlot &&
                    contact.RegionId == context.Definition.Physics.Regions[0].Id && contact.Purpose == WorldPhysicsPurpose2D.Trigger &&
                    GameplayWorldRegistry.ExecutionWorldKey == world && other.World.WorldKey == world &&
                    NaturalEntityEcsService.TryGetTargetObservation(context.Handle, out var target) &&
                    target.Target.TargetGuid == context.Snapshot.Guid;
                if (!exact) counter.InvalidIdentity++;
                switch (contact.Phase)
                {
                    case WorldPhysicsTriggerPhase2D.Enter: counter.Enter++; break;
                    case WorldPhysicsTriggerPhase2D.Stay: counter.Stay++; break;
                    case WorldPhysicsTriggerPhase2D.Exit: counter.Exit++; break;
                }
            });
            return true;
        }
        IDisposable registration = ResourceEntityCapabilityRegistry.Register(prefab, Compile);
        own.CleanupActions.Add(registration.Dispose);
        RuntimeItemDefinition Install(string regionId)
        {
            JObject physics = PhysicsJson("WorldFixed");
            physics["regions"] = new JArray(new JObject
            {
                ["id"] = regionId, ["purposes"] = "Trigger", ["module"] = moduleSlot,
                ["collider"] = new JObject { ["type"] = "CircleCollider2D", ["radius"] = 1f }
            });
            return own.InstallPhysicsDefinition("Tree_Pine", physics, source =>
                source.Modules[moduleSlot] = new ItemModuleDefinitionDto
                { Prefab = prefab, Id = "probe:world-physics-trigger", Enabled = true, Data = new JObject(), Parameters = new JObject() });
        }
        Vector2 point = WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + new Vector2(-5f, -5f);
        NaturalEntityHandle first = own.SpawnResource(Install("contact:first"), point);
        WorldPhysicsEntityProxy2D firstProxy = RequireProxy(first);
        Collider2D firstRegion = firstProxy.Colliders.Single();
        int firstShapeId = firstRegion.GetInstanceID();
        own.InstallPhysicsDefinition("StoneMortar", PhysicsJson("IndependentDynamic"));
        visitor = own.SpawnItem("StoneMortar", point + Vector2.left * 3f);
        WorldPhysicsService2D service = firstProxy.Owner;
        if (!service.TryGetItemProxy(visitor, out WorldPhysicsEntityProxy2D visitorProxy))
            throw new InvalidOperationException("触发回归的正式移动Item未领取代理。");
        void CrossNativeTrigger(Collider2D region)
        {
            Physics2D.SyncTransforms();
            Bounds bounds = region.bounds;
            Vector2 center = bounds.center;
            float distance = bounds.extents.x + 1f;
            visitorProxy.SetPose(new WorldPhysicsEntityPose2D(center + Vector2.left * distance,
                0f, Vector2.right * 3f, 0f));
            int steps = Mathf.CeilToInt((distance * 2f + 1f) / (3f * .02f));
            for (int i = 0; i < steps; i++) service.Simulate(.02f);
        }
        CrossNativeTrigger(firstRegion);
        TriggerCounter firstCounter = counters[first];
        int oldCount = firstCounter.Total;
        NaturalEntityEcsService.TryTakeSnapshotAndRemove(first, out _);
        NaturalEntityHandle second = own.SpawnResource(Install("contact:second"), point);
        WorldPhysicsEntityProxy2D secondProxy = RequireProxy(second);
        Collider2D secondRegion = secondProxy.Colliders.Single();
        CrossNativeTrigger(secondRegion);
        TriggerCounter secondCounter = counters[second];
        return new JObject
        {
            ["firstEnter"] = firstCounter.Enter, ["firstStay"] = firstCounter.Stay, ["firstExit"] = firstCounter.Exit,
            ["secondEnter"] = secondCounter.Enter, ["secondStay"] = secondCounter.Stay, ["secondExit"] = secondCounter.Exit,
            ["invalidIdentity"] = firstCounter.InvalidIdentity + secondCounter.InvalidIdentity,
            ["samePooledRegion"] = secondRegion.GetInstanceID() == firstShapeId,
            ["oldLeaseReleased"] = !firstProxy.IsValid, ["oldReceiverUnchanged"] = firstCounter.Total == oldCount,
            ["newHandle"] = second != first
        };
    }
    #endregion
}
#endif
