using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>轻量掉落物低频推进温度与含水率；不为每个掉落物创建 Module 或 Update。</summary>
internal sealed partial class DroppedItemRuntime
{
    #region 分帧物质结算
    private const float MatterTickInterval = 1f;
    private const int MatterItemsPerStep = 64;
    private static readonly long MatterStepBudgetTicks = System.Math.Max(1L, Stopwatch.Frequency / 1000L);
    private static readonly ProfilerMarker MatterEnvironmentMarker = new("DroppedItems.Matter.Environment");
    private static readonly ProfilerMarker MatterCombustionMarker = new("DroppedItems.Matter.Combustion");
    private static readonly ProfilerMarker MatterTransitionMarker = new("DroppedItems.Matter.Transition");
    private readonly List<int> matterScratch = new();
    private readonly Dictionary<int, double> lastMatterTimes = new();
    private double matterTime;
    private double nextMatterSweepTime = MatterTickInterval;
    private int matterCursor;

    private void TickMatter(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        matterTime += deltaTime;
        if (matterCursor >= matterScratch.Count)
        {
            if (matterTime < nextMatterSweepTime) return;
            matterScratch.Clear();
            matterScratch.AddRange(simulation.Ids);
            matterCursor = 0;
            nextMatterSweepTime = matterTime + MatterTickInterval;
        }
        long deadline = Stopwatch.GetTimestamp() + MatterStepBudgetTicks;
        // 每个物品按自己的实际间隔补算，分帧不会少算温度或燃烧时间。
        for (int processed = 0; matterCursor < matterScratch.Count && processed < MatterItemsPerStep &&
             Stopwatch.GetTimestamp() < deadline; processed++)
        {
            int id = matterScratch[matterCursor++];
            if (!simulation.Contains(id) || !payloads.TryGetValue(id, out ItemData data)) continue;
            if (!lastMatterTimes.TryGetValue(id, out double lastTime)) lastTime = matterTime;
            float seconds = (float)(matterTime - lastTime);
            lastMatterTimes[id] = matterTime;
            if (seconds <= 0f) continue;
            LightweightDroppedBody body = simulation.Get(id);
            // 物态规则只消费实体当前余量，冷载荷的旧数量不能把已拾取的物品加回地面。
            data.Stack.Amount = body.Amount;
            bool changed, waterContact, consumedAll;
            // 分开记录单件环境、燃烧和物态替换，定位时间预算无法截断的长调用。
            using (MatterEnvironmentMarker.Auto())
            {
                WorldLiquidSettings contact = null;
                if (body.WaterKind != 0 && !simulation.TryGetFlight(id, out _) &&
                    TrySampleEnvironmentTerrain(body.Position, out RuntimeTerrainTileSample terrain) &&
                    WorldLiquidSourceResolver.TryResolve(terrain, out WorldLiquidSourceTarget source))
                    contact = source.Liquid.WorldWater;
                changed = WorldItemMatterRules.Advance(data, body.Position, seconds, contact, out waterContact);
            }
            // 满足材料转化时先保全整堆数量，再交给新材料自己的燃烧规则。
            if (TryTransitionMatter(id, data, ref body))
                continue;
            using (MatterCombustionMarker.Auto())
                changed |= ItemMatterRuntime.AdvanceCombustion(data, seconds, waterContact,
                    ItemEnvironmentSources.CanSustainCombustion(matterWorldKey, body.Position), out consumedAll);
            if (changed)
                presentation.Changed(id);
            if (consumedAll)
            {
                Remove(id);
                continue;
            }
            if (!Mathf.Approximately(body.Amount, data.Stack.Amount))
            {
                body.Amount = data.Stack.Amount;
                simulation.Set(body);
                UpdatePlacement(id);
            }

        }
    }

    private bool TryTransitionMatter(int id, ItemData data, ref LightweightDroppedBody body)
    {
        using (MatterTransitionMarker.Auto())
        {
            if (!ItemMatterRuntime.TryCreateSolidTransitionOutputs(data, out IReadOnlyList<ItemData> outputs)) return false;
            ItemData replacement = outputs[0];
            if (!GameRes.ExistingInstance.TryGetItemDefinition(replacement.IDName, out RuntimeItemDefinition target) ||
                !target.UsesLightweightWorldDrop) return false;
            var replacementVisual = ResolveVisual(replacement);
            var originalVisual = visuals[id];
            LightweightDroppedBody originalBody = body;
            var spawned = new List<DroppedItemHandle>();
            try
            {
                // 多产物先全部生成，任一生成失败就回收，源堆保持原样。
                for (int index = 1; index < outputs.Count; index++)
                {
                    DroppedItemHandle handle = DroppedItemService.Spawn(outputs[index], body.Position, body.Position);
                    if (!handle.IsValid) throw new System.InvalidOperationException("材料转化产物未成功落地。");
                    spawned.Add(handle);
                }
                replacement.Guid = data.Guid;
                replacement.inHand = data.inHand;
                payloads[id] = replacement;
                visuals[id] = replacementVisual;
                body.Amount = replacement.Stack.Amount;
                simulation.Set(body);
                UpdatePlacement(id);
                presentation.Changed(id);
                return true;
            }
            catch
            {
                try
                {
                    payloads[id] = data;
                    visuals[id] = originalVisual;
                    body = originalBody;
                    simulation.Set(body);
                    UpdatePlacement(id);
                    presentation.Changed(id);
                }
                finally
                {
                    foreach (DroppedItemHandle handle in spawned) DroppedItemService.Remove(handle);
                }
                throw;
            }
        }
    }

    #endregion
}
