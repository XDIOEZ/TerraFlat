using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 读取权威天气并贡献自言自语 Facts；同时在权威端结算雨中暴露降温与火源恢复。
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
        [SerializeField] private float lightRainAmbientPenalty = -8f;
        [SerializeField] private float heavyRainAmbientPenalty = -16f;
        [SerializeField, Min(1f)] private float rainCoolingSpeedMultiplier = 4f;
        [SerializeField] private float heatSourceAmbientBonus = 10f;
        [SerializeField, Min(1f)] private float heatRecoverySpeedMultiplier = 5f;

#endregion

#region 运行时状态

        private Item actorItem;
        private Mod_Temperature temperature;
        private float nextScanAt;

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

            WeatherMgr weatherManager = WeatherMgr.Instance;
            WeatherType weather = weatherManager.CurrentWeather;
            WeatherPhase phase = weatherManager.CurrentWeatherPhase;
            float intensity = weatherManager.CurrentWeatherIntensity;

            context.SetFact(CharacterSpeechFacts.WeatherType, weather.ToString());
            context.SetFact(CharacterSpeechFacts.WeatherPhase, phase.ToString());
            context.SetFact(
                CharacterSpeechFacts.WeatherIntensity,
                intensity.ToString("0.000", CultureInfo.InvariantCulture));
            context.SetFact(CharacterSpeechFacts.WeatherIsRaining, weatherManager.IsRaining().ToString());
            context.SetFact(CharacterSpeechFacts.WeatherIsExposed, IsRainExposed.ToString());
            context.SetFact(CharacterSpeechFacts.WeatherHasHeatSource, HasNearbyHeatSource.ToString());
            context.SetFact(
                CharacterSpeechFacts.WeatherRemainingSeconds,
                weatherManager.CurrentWeatherRemainingTime.ToString("0.0", CultureInfo.InvariantCulture));
        }

#endregion

#region 暴露与恢复

        private void RefreshExposureState()
        {
            nextScanAt = Time.unscaledTime + Mathf.Max(0.1f, scanInterval);
            bool raining = WeatherMgr.Instance.IsRaining();
            HasNearbyHeatSource = FindNearbyIgnitedHeatSource();
            IsRainExposed = raining && !HasNearbyHeatSource;

            if (!TryResolveTemperature())
                return;

            if (HasNearbyHeatSource)
            {
                temperature.Data.RuntimeAmbientOffset = heatSourceAmbientBonus;
                temperature.Data.RuntimeChangeSpeedMultiplier = heatRecoverySpeedMultiplier;
                return;
            }

            if (IsRainExposed)
            {
                float intensity = WeatherMgr.Instance.CurrentWeatherIntensity;
                temperature.Data.RuntimeAmbientOffset = Mathf.Lerp(
                    lightRainAmbientPenalty,
                    heavyRainAmbientPenalty,
                    intensity);
                temperature.Data.RuntimeChangeSpeedMultiplier = rainCoolingSpeedMultiplier;
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
                Item nearbyItem = hits[i] != null ? hits[i].GetComponentInParent<Item>() : null;
                if (nearbyItem == null || nearbyItem == actorItem || nearbyItem.itemMods == null)
                    continue;

                Mod_Fuel fuel = nearbyItem.itemMods.GetMod_ByID<Mod_Fuel>(ModText.Fuel);
                if (fuel != null && fuel.GetIgnitedState() && fuel.HasFuel())
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
