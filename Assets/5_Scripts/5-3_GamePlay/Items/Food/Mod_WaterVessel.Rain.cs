using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class Mod_WaterVessel
{
    #region 落地开口容器自动接雨

    public bool CollectsRainwater; // 只有朝上开口的容器通过内容配置启用接雨。
    public float RainwaterIntervalSeconds = VesselRainwaterCollection.DefaultIntervalSeconds;
    private float rainSampleClock;
    private const float RainSampleIntervalSeconds = 1f;

    // 仅权威端给落地且开口朝上的容器接当地雨水，背包和手持实例不参与。
    private void TickRainwaterCollection()
    {
        if (!IsRuntimeLoaded || !Enabled || !CollectsRainwater || !GameNetwork.HasStateAuthority ||
            item == null || item.DestructionHandled || item.InHand || item.Owner != null ||
            item.gameObject.scene != SceneManager.GetActiveScene() ||
            Vector3.Dot(item.transform.up, Vector3.up) < .99f)
        {
            rainSampleClock = 0f;
            return;
        }

        float space = MixedLiquidContents.GetReceivableServings(Data, Capacity, VesselRainwaterCollection.PureWaterLiquidId, float.MaxValue);
        if (space < 1f)
        {
            Data.RainwaterProgress = 0d;
            rainSampleClock = 0f;
            return;
        }

        rainSampleClock += Time.deltaTime;
        if (rainSampleClock < RainSampleIntervalSeconds)
            return;
        float seconds = rainSampleClock;
        rainSampleClock = 0f;

        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (weather == null ||
            !weather.TryGetPrecipitationAt(item.transform.position, out float rain, out _) ||
            rain <= 0f ||
            !ContaminationSystem.TryGetNormalizedLoad(item.transform.position, out float contamination) ||
            !TemperatureMgr.Instance.TryGetAmbientTemperature(item.transform.position, out float temperature))
            return;

        string liquidId = VesselRainwaterCollection.ResolveLiquidId(contamination);
        if (liquidId == null || ResolveLiquidDefinition(liquidId, false) == null)
            return;
        space = MixedLiquidContents.GetReceivableServings(Data, Capacity, liquidId, float.MaxValue);

        int collected = VesselRainwaterCollection.Advance(ref Data.RainwaterProgress, seconds, rain,
            space, RainwaterIntervalSeconds);
        if (collected <= 0)
            return;

        LiquidVesselOperations.AddState(this, liquidId, collected, temperature);
        CommitVessel();
    }

    #endregion
}
