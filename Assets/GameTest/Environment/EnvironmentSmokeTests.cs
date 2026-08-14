using System.IO;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Environment
{
    /// <summary>环境基础冒烟测试：保护时间、天气、温度与雨效入口。</summary>
    public sealed class EnvironmentSmokeTests
    {

        [Test]
        [Category("Environment.Weather")]
        public void WeatherManagerDefersTimeSystemUntilWorldEntry()
        {
            string source = File.ReadAllText("Assets/5_Scripts/5-3_GamePlay/World/Environment/WeatherMgr.cs");
            int startIndex = source.IndexOf("private void Start()");
            int destroyIndex = source.IndexOf("protected override void OnDestroy()", startIndex);

            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(destroyIndex, Is.GreaterThan(startIndex));
            Assert.That(source.Substring(startIndex, destroyIndex - startIndex), Does.Not.Contain("TimeSystem"));
        }

        [Test]
        [Category("Environment.Weather")]
        public void CaveSuppressesWeatherAndStopsWeatherClockSubscription()
        {
            DimensionDefinition surface = DimensionDefinition.CreateSurface();
            DimensionDefinition cave = DimensionDefinition.CreateCave();
            Assert.That(WeatherMgr.IsWeatherSuppressedInDimension(surface), Is.False);
            Assert.That(WeatherMgr.IsWeatherSuppressedInDimension(cave), Is.True);

            string source = File.ReadAllText("Assets/5_Scripts/5-3_GamePlay/World/Environment/WeatherMgr.cs");
            int lifecycleIndex = source.IndexOf("private void ApplyGameWorldLifecycleState(bool isActive)");
            int updateIndex = source.IndexOf("private void Update()", lifecycleIndex);
            Assert.That(lifecycleIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(updateIndex, Is.GreaterThan(lifecycleIndex));
            Assert.That(source.Substring(lifecycleIndex, updateIndex - lifecycleIndex),
                Does.Contain("ShutdownWeatherEventSystem();"));
        }

        [Test]
        [Category("Environment.Smoke")]
        [Category("Smoke")]
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

            int leftTransitions = WeatherEventScheduler.Advance(left, 0f, 20f, 10f, 24680, config);
            int rightTransitions = WeatherEventScheduler.Advance(right, 0f, 20f, 10f, 24680, config);

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

            int transitions = WeatherEventScheduler.Advance(planet, 0f, 20f, 10f, 13579, config);
            int duplicateTransitions = WeatherEventScheduler.Advance(planet, 20f, 20f, 10f, 13579, config);

            Assert.That(transitions, Is.EqualTo(8));
            Assert.That(duplicateTransitions, Is.Zero);
            Assert.That(planet.WeatherEventSequence, Is.EqualTo(2));
            Assert.That(WeatherEventScheduler.GetCurrentBoundary(planet), Is.GreaterThan(20f));
        }

        [Test]
        [Category("Environment.Weather")]
        public void ClearWeatherRollsExactlyOncePerDay()
        {
            RainEventScheduleConfig config = CreateFastWeatherConfig();
            config.DailyRainChance = 0f;
            PlanetData planet = new PlanetData();

            int transitions = WeatherEventScheduler.Advance(planet, 0f, 50f, 10f, 97531, config);

            Assert.That(transitions, Is.EqualTo(5));
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.Clear));
            Assert.That(planet.WeatherEventSequence, Is.Zero);
            Assert.That(planet.WeatherRandomCursor, Is.EqualTo(5));
            Assert.That(planet.NextWeatherEventTotalTime, Is.EqualTo(60f));
        }

        [Test]
        [Category("Environment.Weather")]
        public void RainDurationUsesConfiguredFractionOfDay()
        {
            const float dayLength = 100f;
            RainEventScheduleConfig config = CreateFastWeatherConfig();
            config.MinRainDurationDays = 0.5f;
            config.MaxRainDurationDays = 1f;
            PlanetData planet = new PlanetData();

            WeatherEventScheduler.Advance(planet, 0f, dayLength, dayLength, 86420, config);
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.Forecast));

            float boundary = planet.WeatherPhaseEndTotalTime;
            WeatherEventScheduler.Advance(planet, dayLength, boundary, dayLength, 86420, config);
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.RainStarting));
            float rainStart = planet.WeatherPhaseStartedTotalTime;

            float previousBoundary = boundary;
            boundary = planet.WeatherPhaseEndTotalTime;
            WeatherEventScheduler.Advance(planet, previousBoundary, boundary, dayLength, 86420, config);
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.RainSteady));

            previousBoundary = boundary;
            boundary = planet.WeatherPhaseEndTotalTime;
            WeatherEventScheduler.Advance(planet, previousBoundary, boundary, dayLength, 86420, config);
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.RainHeavy));

            previousBoundary = boundary;
            boundary = planet.WeatherPhaseEndTotalTime;
            WeatherEventScheduler.Advance(planet, previousBoundary, boundary, dayLength, 86420, config);
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.RainEnding));

            float rainDuration = planet.WeatherPhaseEndTotalTime - rainStart;
            Assert.That(rainDuration, Is.InRange(dayLength * 0.5f, dayLength));
        }

        [Test]
        [Category("Environment.Weather")]
        public void DefaultRainPolicyIsFivePercentAndHalfToFullDay()
        {
            RainEventScheduleConfig config = new RainEventScheduleConfig();

            Assert.That(config.DailyRainChance, Is.EqualTo(0.05f));
            Assert.That(config.MinRainDurationDays, Is.EqualTo(0.5f));
            Assert.That(config.MaxRainDurationDays, Is.EqualTo(1f));
        }

        [Test]
        [Category("Environment.Weather")]
        public void PreviousWeatherScheduleMigratesToNextDailyBoundary()
        {
            PlanetData planet = new PlanetData
            {
                WeatherDataVersion = 1,
                WeatherPhase = WeatherPhase.Clear,
                NextWeatherEventTotalTime = 650f,
                WeatherRandomCursor = 7
            };

            WeatherEventScheduler.InitializeIfNeeded(
                planet,
                640f,
                100f,
                12345,
                new RainEventScheduleConfig());

            Assert.That(planet.WeatherDataVersion, Is.EqualTo(WeatherEventScheduler.CurrentDataVersion));
            Assert.That(planet.WeatherPhase, Is.EqualTo(WeatherPhase.Clear));
            Assert.That(planet.NextWeatherEventTotalTime, Is.EqualTo(700f));
            Assert.That(planet.WeatherRandomCursor, Is.EqualTo(7));
        }

        private static RainEventScheduleConfig CreateFastWeatherConfig()
        {
            return new RainEventScheduleConfig
            {
                DailyRainChance = 1f,
                MinRainDurationDays = 0.4f,
                MaxRainDurationDays = 0.4f,
                ForecastDuration = 1f,
                RainStartingDuration = 1f,
                HeavyRainDuration = 1f,
                RainEndingDuration = 1f,
                RecoveryDuration = 1f
            };
        }
    }
}
