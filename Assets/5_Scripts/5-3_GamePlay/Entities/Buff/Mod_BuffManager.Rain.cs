using System;
using FlatWorld.Networking;

public partial class Mod_BuffManager
{
    #region 生物淋雨潮湿

    private RainWetnessClock rainStackClock;

    private void ResetRainStackClock() => rainStackClock.Reset();

    /// <summary>在生物自身的权威 Tick 上采样当地降雨，不从玩家、相机或雨粒子回调施加 Buff。</summary>
    private bool AdvanceRainWetness(float deltaTime)
    {
        Item receiver = buffReceiver != null ? buffReceiver : item;
        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || receiver == null || receiver.DestructionHandled ||
            !receiver.isActiveAndEnabled || weather == null ||
            !weather.TryGetPrecipitationAt(receiver.transform.position, out float intensity, out _) ||
            intensity <= 0f)
        {
            ResetRainStackClock();
            return false;
        }

        GameRes resources = GameRes.Instance;
        if (resources == null || !IsRainWetnessActor(receiver, resources))
        {
            ResetRainStackClock();
            return false;
        }

        Mod_DamageReceiver health = receiver.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health == null || health.Hp <= 0f)
        {
            ResetRainStackClock();
            return false;
        }

        BuffDefinition definition = resources.GetBuffDefinition(WetBuffIds.Wet);
        if (definition == null || definition.RainStackIntervalSeconds <= 0f || definition.RainMaxStacks <= 0 ||
            definition.RainReferenceIntensity <= 0f || intensity <= 0f || float.IsNaN(intensity) || float.IsInfinity(intensity))
        {
            ResetRainStackClock();
            return false;
        }

        int completed = rainStackClock.Advance(deltaTime, intensity, definition.RainStackIntervalSeconds,
            definition.RainReferenceIntensity, definition.RainMaxStacks);
        int available = Math.Max(0, Math.Min(definition.MaxStacks, definition.RainMaxStacks) - GetBuffStacks(WetBuffIds.Wet));
        int added = Math.Min(available, completed);
        if (added > 0)
            AddBuff(WetBuffIds.Wet, added);

        // 持续淋雨时保持已有潮湿，避免 30 秒自然消退抵消 60 秒叠层；停雨后恢复原消退。
        return true;
    }

    private static bool IsRainWetnessActor(Item receiver, GameRes resources)
    {
        return receiver is Player ||
            (receiver.itemData != null && resources.TryGetItemDefinition(receiver.itemData.IDName,
                out RuntimeItemDefinition definition) && definition.IsActor);
    }

    #endregion
}
