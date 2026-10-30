using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 温度回调与水冷顺序

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterSurvivalTemperatureCallbackProbe()
    {
        GameplayTestRuntimeProbe.Register("survival", "temperature_water_callback_order", CaptureTemperatureWaterCallbackOrder);
    }

    // 自有生物的水冷通知中改变体温，随后检查环境传热与目标清理。
    private static JObject CaptureTemperatureWaterCallbackOrder(Player player, JObject args)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player);
        var own = new ActorFixture(new Vector3(site.WorldCell.x + .5f, site.WorldCell.y + .5f));
        var result = new JObject();
        Mod_Temperature temperature = null;
        Action<float> listener = null;
        try
        {
            temperature = own.Actor.GetComponentInChildren<Mod_Temperature>(true)
                ?? throw new InvalidOperationException("正式生物没有体温模块。");
            if (!temperature.IsRuntimeLoaded) throw new InvalidOperationException("体温模块未加载。");
            float ambient;
            if (ItemEnvironmentSources.TryGet(own.Actor, out ItemEnvironmentSample localEnvironment))
                ambient = (float)localEnvironment.TemperatureCelsius;
            else if (!TemperatureMgr.Instance.TryGetAmbientTemperature(own.Actor.transform.position, out ambient))
                throw new InvalidOperationException("无法采样自有生物脚下的环境温度。");

            float initial = Mathf.Max(0f, ambient) + 100f;
            float injected = initial - 20f;
            temperature.SetTemperature(initial);
            temperature.SetWaterExposure(true, 10f, 0f, 5f);
            var events = new JArray();
            bool injectedFromCallback = false;
            listener = value =>
            {
                events.Add(value);
                if (injectedFromCallback) return;
                injectedFromCallback = true;
                temperature.SetTemperature(injected);
            };
            temperature.OnTemperatureChanged += listener;
            temperature.ModUpdate(.25f);
            temperature.OnTemperatureChanged -= listener;
            listener = null;
            result["initial"] = initial;
            result["injected"] = injected;
            result["ambient"] = temperature.Data.AmbientTemperature;
            result["events"] = events;
            result["afterCallback"] = temperature.Data.CurrentTemperature;
            result["expectedAfterCallback"] = ThermalRuntime.AdvanceAmbient(injected, temperature.Data.AmbientTemperature, .25f);

            temperature.SetTemperature(initial);
            temperature.ModUpdate(.25f);
            result["afterTargetCleared"] = temperature.Data.CurrentTemperature;
            result["expectedAfterTargetCleared"] = ThermalRuntime.AdvanceAmbient(initial, temperature.Data.AmbientTemperature, .25f);
            return result;
        }
        finally
        {
            if (temperature != null && listener != null) temperature.OnTemperatureChanged -= listener;
            own.Dispose();
            result["actorRemoved"] = ItemMgr.GetInstance()?.GetItemByGuid(own.Guid) == null;
        }
    }

    #endregion
}
