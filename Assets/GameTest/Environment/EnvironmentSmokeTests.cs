using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Environment
{
    /// <summary>环境基础冒烟测试：保护时间、天气、温度与雨效入口。</summary>
    public sealed class EnvironmentSmokeTests
    {
        [Test]
        [Category("Environment.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Time/DayTimeSystem.cs", "DayTimeSystem");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/WeatherMgr.cs", "WeatherMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/TemperatureMgr.cs", "TemperatureMgr");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Weather/RainEffect.prefab");
        }

        [Test]
        [Category("Environment.Smoke")]
        public void SerializableTimeDataPreservesTotalDays()
        {
            var source = new TimeData
            {
                CurrentTime = 321f,
                DayLength = 1440f,
                TimeScaleModifier = 12f,
                TotalDays = 17
            };

            TimeData restored = new SerializableTimeData(source).ToTimeData();

            Assert.That(restored.TotalDays, Is.EqualTo(17));
            Assert.That(restored.CurrentTime, Is.EqualTo(321f));
            Assert.That(restored.GetTotalGameTime(), Is.EqualTo(17 * 1440f + 321f));
        }

        [Test]
        [Category("Environment.Weather")]
        public void FixedSeedProducesDeterministicWeatherTimeline()
        {
            RainEventScheduleConfig config = CreateFastWeatherConfig();
            PlanetData left = new PlanetData();
            PlanetData right = new PlanetData();

            int leftTransitions = WeatherEventScheduler.Advance(left, 0f, 20f, 24680, config);
            int rightTransitions = WeatherEventScheduler.Advance(right, 0f, 20f, 24680, config);

            Assert.That(leftTransitions, Is.EqualTo(rightTransitions));
            Assert.That(left.WeatherPhase, Is.EqualTo(right.WeatherPhase));
            Assert.That(left.CurrentWeather, Is.EqualTo(right.CurrentWeather));
            Assert.That(left.WeatherIntensity, Is.EqualTo(right.WeatherIntensity));
            Assert.That(left.WeatherPhaseEndTotalTime, Is.EqualTo(right.WeatherPhaseEndTotalTime));
            Assert.That(left.NextWeatherEventTotalTime, Is.EqualTo(right.NextWeatherEventTotalTime));
            Assert.That(left.WeatherRandomCursor, Is.EqualTo(right.WeatherRandomCursor));
            Assert.That(left.WeatherEventSequence, Is.GreaterThan(0));
        }

        [Test]
        [Category("Environment.Weather")]
        public void TimeJumpCrossesEveryBoundaryWithoutDuplicateTransition()
        {
            RainEventScheduleConfig config = CreateFastWeatherConfig();
            PlanetData planet = new PlanetData();

            int transitions = WeatherEventScheduler.Advance(planet, 0f, 8f, 13579, config);
            int duplicateTransitions = WeatherEventScheduler.Advance(planet, 8f, 8f, 13579, config);

            Assert.That(transitions, Is.EqualTo(8));
            Assert.That(duplicateTransitions, Is.Zero);
            Assert.That(planet.WeatherEventSequence, Is.EqualTo(2));
            Assert.That(WeatherEventScheduler.GetCurrentBoundary(planet), Is.GreaterThan(8f));
        }

        private static RainEventScheduleConfig CreateFastWeatherConfig()
        {
            return new RainEventScheduleConfig
            {
                MinClearInterval = 1f,
                MaxClearInterval = 1f,
                ForecastDuration = 1f,
                RainStartingDuration = 1f,
                MinSteadyRainDuration = 1f,
                MaxSteadyRainDuration = 1f,
                HeavyRainDuration = 1f,
                RainEndingDuration = 1f,
                RecoveryDuration = 1f
            };
        }
    }
}
