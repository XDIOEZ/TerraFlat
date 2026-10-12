using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>轻量掉落物低频推进温度与含水率；不为每个掉落物创建 Module 或 Update。</summary>
internal sealed partial class DroppedItemRuntime
{
    #region 分帧物质结算
    private const float MatterTickInterval = 1f;
    private const int MatterItemsPerStep = 64;
    private static readonly long MatterStepBudgetTicks = System.Math.Max(1L, Stopwatch.Frequency / 1000L);
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
            bool changed = WorldItemMatterRules.Advance(data, body.Position, seconds,
                liquidContact: body.WaterKind != 0 && !simulation.TryGetFlight(id, out _), out bool waterContact);
            changed |= ItemMatterRuntime.AdvanceCombustion(data, seconds, waterContact, out bool consumedAll);
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

            if (!ItemMatterRuntime.TryCreateSolidTransitionReplacement(data, out ItemData replacement))
                continue;
            if (!GameRes.ExistingInstance.TryGetItemDefinition(replacement.IDName, out RuntimeItemDefinition target) ||
                !target.UsesLightweightWorldDrop)
                continue;

            payloads[id] = replacement;
            visuals[id] = ResolveVisual(replacement);
            body.Amount = replacement.Stack.Amount;
            simulation.Set(body);
            UpdatePlacement(id);
        }
    }

    #endregion
}
