using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Environment
{
    public sealed class WetTemperatureBuffTests
    {
        private GameObject managerObject;
        private TemperatureMgr temperatureManager;

        [SetUp]
        public void SetUp()
        {
            managerObject = new GameObject("WetTemperatureBuffTests");
            temperatureManager = managerObject.AddComponent<TemperatureMgr>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(managerObject);
        }

        [Test]
        [Category("Environment.Temperature")]
        public void WetMultiplierDoublesCoolingWithoutAcceleratingWarming()
        {
            var coolingData = new Mod_Temperature.TemperatureData
            {
                CurrentTemperature = 36f,
                ChangeSpeed = 1f,
                RuntimeCoolingSpeedMultiplier = 2f
            };

            float cooled = temperatureManager.EvaluateNextTemperature(coolingData, 1f);
            Assert.That(cooled, Is.EqualTo(34f).Within(0.001f));

            var warmingData = new Mod_Temperature.TemperatureData
            {
                CurrentTemperature = 10f,
                ChangeSpeed = 1f,
                RuntimeCoolingSpeedMultiplier = 2f
            };

            float warmed = temperatureManager.EvaluateNextTemperature(warmingData, 1f);
            Assert.That(warmed, Is.EqualTo(11f).Within(0.001f));
        }

        [Test]
        [Category("Environment.Temperature")]
        public void BuiltInBuffJsonBuildsWithCachedHandlers()
        {
            var definitions = BuffCatalogLoader.LoadBuiltInDefinitions();

            Assert.That(definitions, Has.Count.EqualTo(12));
            Assert.That(
                definitions.SelectMany(definition => definition.Effects)
                    .All(effect => effect.IsHandlerCached),
                Is.True);
        }

        [Test]
        [Category("Environment.Temperature")]
        public void WetBuffJsonCachesStartAndStopHandlers()
        {
            BuffDefinition wetBuff = BuffCatalogLoader.LoadBuiltInDefinitions()
                .Single(definition => definition.Id == "潮湿");
            Assert.That(wetBuff, Is.Not.Null);
            Assert.That(wetBuff.IsPermanent, Is.True);
            Assert.That(wetBuff.TickIntervalSeconds, Is.Zero);

            Assert.That(wetBuff.StartEffects.Count, Is.EqualTo(1));
            Assert.That(wetBuff.StopEffects.Count, Is.EqualTo(1));
            Assert.That(wetBuff.StartEffects[0].TypeId,
                Is.EqualTo(BuffEffectTypeIds.TemperatureCoolingMultiplier));
            Assert.That(wetBuff.StartEffects[0].Value, Is.EqualTo(2f));
            Assert.That(wetBuff.StartEffects[0].IsHandlerCached, Is.True);
            Assert.That(wetBuff.StopEffects[0].Value, Is.EqualTo(0.5f));
            Assert.That(wetBuff.StopEffects[0].IsHandlerCached, Is.True);
        }

        [Test]
        [Category("Environment.Temperature")]
        public void LegacyBuffJsonFieldsAreRejected()
        {
            const string legacyJson = @"{
                ""schemaVersion"": 1,
                ""buffs"": [
                    {
                        ""id"": ""legacy"",
                        ""duration"": 10,
                        ""loadBehavior"": ""assume_applied"",
                        ""effects"": []
                    }
                ]
            }";

            Assert.Throws<JsonSerializationException>(
                () => BuffDefinitionFactory.Deserialize(legacyJson));
        }
    }
}
