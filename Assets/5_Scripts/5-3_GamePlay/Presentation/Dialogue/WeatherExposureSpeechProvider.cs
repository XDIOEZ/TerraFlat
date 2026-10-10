using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 读取角色所在地天气并贡献自言自语 Facts，实时雨冷由统一环境温度处理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherExposureSpeechProvider : MonoBehaviour, ICharacterSpeechContextContributor
    {
#region 配置

        [Header("暴露检测")]
        [SerializeField, Min(0.5f)] private float heatSourceRadius = 6f;
        [SerializeField, Min(0.1f)] private float scanInterval = 0.5f;
        [SerializeField] private LayerMask heatSourceLayerMask = ~0;

        [Header("体温影响")]
        [SerializeField] private float heatSourceAmbientBonus = 10f;
        [SerializeField, Min(1f)] private float heatRecoverySpeedMultiplier = 5f;

#endregion

#region 运行时状态

        private Item actorItem;
        private Mod_Temperature temperature;
        private float nextScanAt;
        private float localRainIntensity;

        public bool IsRainExposed { get; private set; }
        public bool HasNearbyHeatSource { get; private set; }
        public int ContextOrder => 150;

#endregion

#region 生命周期

        private void Update()
        {
            if (Time.unscaledTime < nextScanAt)
                return;

            RefreshExposureState();
        }

        private void OnDisable()
        {
            ResetRuntimeTemperatureModifiers();
            IsRainExposed = false;
            HasNearbyHeatSource = false;
        }

#endregion

#region Fact 贡献

        public void Contribute(CharacterSpeechContext context)
        {
            if (Time.unscaledTime >= nextScanAt)
                RefreshExposureState();

            WeatherMgr weatherManager = WeatherMgr.ExistingInstance;
            var localWeather = default(FlatWorld.WorldModel.RegionalWeatherSnapshot);
            bool hasWeather = weatherManager != null && weatherManager.TryGetWeatherAt(transform.position, out localWeather);
            WeatherType weather = hasWeather ? WeatherMgr.DescribeWeather(in localWeather) : WeatherType.Clear;
            WeatherPhase phase = hasWeather ? WeatherMgr.DescribeWeatherPhase(in localWeather) : WeatherPhase.Clear;
            float intensity = !hasWeather ? 0f : weather switch
            {
                WeatherType.Fog => localWeather.FogDensity,
                WeatherType.Cloudy => localWeather.CloudCoverage,
                _ => localWeather.PrecipitationIntensity
            };

            context.SetFact(CharacterSpeechFacts.WeatherType, weather.ToString());
            context.SetFact(CharacterSpeechFacts.WeatherPhase, phase.ToString());
            context.SetFact(
                CharacterSpeechFacts.WeatherIntensity,
                intensity.ToString("0.000", CultureInfo.InvariantCulture));
            context.SetFact(CharacterSpeechFacts.WeatherIsRaining, (hasWeather && localWeather.PrecipitationIntensity > 0f).ToString());
            bool isSnowing = hasWeather && weatherManager.TryGetPrecipitationAt(transform.position, out _, out float snowIntensity) &&
                snowIntensity > 0f;
            context.SetFact(CharacterSpeechFacts.WeatherIsSnowing, isSnowing.ToString());
            context.SetFact(CharacterSpeechFacts.WeatherIsExposed, IsRainExposed.ToString());
            context.SetFact(CharacterSpeechFacts.WeatherHasHeatSource, HasNearbyHeatSource.ToString());
            context.SetFact(
                CharacterSpeechFacts.WeatherRemainingSeconds,
                (hasWeather ? localWeather.RemainingSeconds : 0f).ToString("0.0", CultureInfo.InvariantCulture));
        }

#endregion

#region 暴露与恢复

        private void RefreshExposureState()
        {
            nextScanAt = Time.unscaledTime + Mathf.Max(0.1f, scanInterval);
            WeatherMgr weather = WeatherMgr.ExistingInstance;
            localRainIntensity = weather != null && weather.TryGetPrecipitationAt(transform.position, out float rainIntensity, out _)
                ? rainIntensity : 0f;
            HasNearbyHeatSource = FindNearbyIgnitedHeatSource();
            IsRainExposed = localRainIntensity > 0f && !HasNearbyHeatSource;

            if (!TryResolveTemperature())
                return;

            if (HasNearbyHeatSource)
            {
                temperature.Data.RuntimeAmbientOffset = heatSourceAmbientBonus;
                temperature.Data.RuntimeChangeSpeedMultiplier = heatRecoverySpeedMultiplier;
                return;
            }

            ResetRuntimeTemperatureModifiers();
        }

        private bool FindNearbyIgnitedHeatSource()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position,
                heatSourceRadius,
                heatSourceLayerMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Item nearbyItem = GameplayPhysics2D.ResolveComponent<Item>(hits[i]);
                if (nearbyItem == null || nearbyItem == actorItem || nearbyItem.itemMods == null)
                    continue;

                Mod_Fuel fuel = nearbyItem.itemMods.GetMod_ByID<Mod_Fuel>(ModText.Fuel);
                if (fuel == null || !fuel.HasFuel())
                    continue;

                Mod_Combustion combustion =
                    nearbyItem.itemMods.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId);
                if (combustion != null ? combustion.IsActivelyBurning : fuel.GetIgnitedState())
                    return true;
            }

            return false;
        }

        private bool TryResolveTemperature()
        {
            if (temperature != null && temperature.Data != null)
                return true;

            actorItem ??= GetComponentInParent<Item>();
            if (actorItem == null || !actorItem.IsInitialized || actorItem.itemMods == null)
                return false;

            temperature = actorItem.itemMods.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
            return temperature != null && temperature.Data != null;
        }

        private void ResetRuntimeTemperatureModifiers()
        {
            if (!TryResolveTemperature())
                return;

            temperature.Data.RuntimeAmbientOffset = 0f;
            temperature.Data.RuntimeChangeSpeedMultiplier = 1f;
        }

#endregion
    }
}
