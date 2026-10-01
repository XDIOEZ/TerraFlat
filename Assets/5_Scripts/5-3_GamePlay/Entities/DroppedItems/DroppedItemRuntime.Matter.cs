using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;

/// <summary>轻量掉落物低频推进温度与含水率；不为每个掉落物创建 Module 或 Update。</summary>
internal sealed partial class DroppedItemRuntime
{
    private const float MatterTickInterval = 1f;
    private readonly List<int> matterScratch = new();
    private float matterClock;

    private void TickMatter(float deltaTime)
    {
        matterClock += deltaTime;
        if (matterClock < MatterTickInterval) return;
        float seconds = matterClock;
        matterClock = 0f;

        matterScratch.Clear();
        matterScratch.AddRange(simulation.Ids);
        foreach (int id in matterScratch)
        {
            if (!simulation.Contains(id) || !payloads.TryGetValue(id, out ItemData data)) continue;
            LightweightDroppedBody body = simulation.Get(id);
            float ambient = TemperatureMgr.DefaultAmbientTemperature;
            TemperatureMgr temperature = TemperatureMgr.Instance;
            if (temperature != null)
                temperature.TryGetAmbientTemperature(body.Position, out ambient);

            bool changed = ItemMatterRuntime.Advance(
                data, ambient, 1f, seconds, submerged: body.WaterKind != 0);
            if (changed)
                presentation.Changed(id);

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
}
