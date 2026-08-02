using System.Collections.Generic;
using FlatWorld.Gameplay.Events;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.GameEvents
{
    public sealed class GameEventSystemTests
    {
        [Test]
        [Category("GameEvents.Config")]
        public void ModularJsonCatalogLoadsWithoutScriptableObjects()
        {
            GameEventConfigLoadResult result = GameEventConfigLoader.LoadSources(
                new[]
                {
                    new GameEventConfigSource("test.json", CreateValidCatalog("test_event"))
                },
                logIssues: false);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Definitions.Count, Is.EqualTo(1));
            Assert.That(result.Definitions[0].Id, Is.EqualTo("test_event"));
            Assert.That(result.Definitions[0].Trigger.Type, Is.EqualTo("day.schedule"));
            Assert.That(result.Definitions[0].Actions[0].Type, Is.EqualTo("signal.emit"));
        }

        [Test]
        [Category("GameEvents.Config")]
        public void BrokenCatalogIsIsolatedFromValidCatalog()
        {
            GameEventConfigLoadResult result = GameEventConfigLoader.LoadSources(
                new[]
                {
                    new GameEventConfigSource("broken.json", "{ invalid"),
                    new GameEventConfigSource("valid.json", CreateValidCatalog("still_valid"))
                },
                logIssues: false);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Definitions.Count, Is.EqualTo(1));
            Assert.That(result.Definitions[0].Id, Is.EqualTo("still_valid"));
        }

        [Test]
        [Category("GameEvents.Schedule")]
        public void DayScheduleCrossesEachConfiguredOccurrenceExactlyOnce()
        {
            GameEventDefinition definition = GameEventConfigLoader.LoadSources(
                new[] { new GameEventConfigSource("schedule.json", CreateValidCatalog("schedule")) },
                logIssues: false).Definitions[0];
            DayScheduleGameEventTrigger trigger = new();
            GameEventProgressSaveData progress = new();
            List<GameEventOccurrence> occurrences = new();

            trigger.CollectOccurrences(
                new GameEventTriggerContext("world", "world", 0f, 700f, 100f, 123),
                definition,
                definition.Trigger.Parameters,
                progress,
                occurrences);

            Assert.That(occurrences.Count, Is.EqualTo(2));
            Assert.That(occurrences[0].DayNumber, Is.EqualTo(3));
            Assert.That(occurrences[0].TriggeredTotalTime, Is.EqualTo(210f));
            Assert.That(occurrences[1].DayNumber, Is.EqualTo(6));
            Assert.That(occurrences[1].TriggeredTotalTime, Is.EqualTo(510f));

            occurrences.Clear();
            trigger.CollectOccurrences(
                new GameEventTriggerContext("world", "world", 0f, 700f, 100f, 123),
                definition,
                definition.Trigger.Parameters,
                progress,
                occurrences);
            Assert.That(occurrences, Is.Empty);
        }

        [Test]
        [Category("GameEvents.Trigger")]
        public void GroundMeatMustRemainForFullConfiguredDuration()
        {
            GameObject managerObject = new("GroundMeatTrigger_ItemMgr");
            GameObject meatObject = new("GroundMeatTrigger_Meat");
            try
            {
                ItemMgr itemManager = managerObject.AddComponent<ItemMgr>();
                GameItem meat = meatObject.AddComponent<GameItem>();
                meat.Data = new Data_GeneralItem
                {
                    IDName = "Meat",
                    Guid = 731,
                    inHand = false,
                    Stack = new ItemStack
                    {
                        Amount = 1f,
                        CanBePickedUp = true
                    }
                };
                meat.transform.position = new Vector3(8f, 3f, 0f);
                itemManager.RuntimeItemsGroup["Meat"] = new List<Item> { meat };

                GroundItemDwellGameEventTrigger trigger = new();
                GameEventDefinition definition = new()
                {
                    Id = "ground_meat_test",
                    CooldownDays = 3f
                };
                JObject parameters = JObject.FromObject(new
                {
                    itemId = "Meat",
                    dwellGameSeconds = 120f,
                    requirePickupable = true
                });
                GameEventProgressSaveData progress = new();
                List<GameEventOccurrence> occurrences = new();

                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 0f, 1f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 1f, 120.9f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                Assert.That(occurrences, Is.Empty);

                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 120.9f, 121f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                Assert.That(occurrences.Count, Is.EqualTo(1));

                JObject payload = JObject.Parse(occurrences[0].PayloadJson);
                Assert.That(payload.Value<int>("targetItemGuid"), Is.EqualTo(731));
                Assert.That(payload["targetPosition"]?.Value<float>("x"), Is.EqualTo(8f));

                meat.Data.inHand = true;
                occurrences.Clear();
                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 121f, 122f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                Assert.That(occurrences, Is.Empty);
                Assert.That(
                    JObject.Parse(progress.TriggerRuntimeDataJson).Value<bool>("HasCandidate"),
                    Is.False);

                meat.Data.inHand = false;
                progress.TriggerCount = 1;
                progress.LastTriggeredTotalTime = 121f;
                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 122f, 123f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 123f, 243f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                Assert.That(occurrences, Is.Empty, "肉已驻留两分钟，但三日冷却内不得触发。");

                trigger.CollectOccurrences(
                    new GameEventTriggerContext("world", "world", 243f, 4441f, 1440f, 1),
                    definition,
                    parameters,
                    progress,
                    occurrences);
                Assert.That(occurrences.Count, Is.EqualTo(1), "三日冷却结束后应允许触发。");
            }
            finally
            {
                Object.DestroyImmediate(meatObject);
                Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        [Category("GameEvents.Config")]
        public void ShippedEventCatalogsAndBuiltInExtensionsAreValid()
        {
            GameEventExtensionRegistry.EnsureBuiltInsRegistered();
            GameEventConfigLoadResult result = GameEventConfigLoader.LoadFromResources(logIssues: false);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Definitions.Count, Is.EqualTo(4));
            Assert.That(GameEventExtensionRegistry.TryGetTrigger("day.schedule", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetTrigger("world.item.dwell", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetCondition("dimension.is", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetAction("creature.waves", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetAction("creature.advance", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetAction("weather.override", out _), Is.True);
            Assert.That(GameEventExtensionRegistry.TryGetAction("signal.emit", out _), Is.True);

            GameEventDefinition wolfLure = result.Definitions.Find(
                definition => definition.Id == "raw_meat_wolf_lure");
            Assert.That(wolfLure, Is.Not.Null);
            Assert.That(wolfLure.CooldownDays, Is.EqualTo(3f));
            Assert.That(wolfLure.Trigger.Type, Is.EqualTo("world.item.dwell"));
            Assert.That(wolfLure.Trigger.Parameters.Value<string>("itemId"), Is.EqualTo("Meat"));
            Assert.That(wolfLure.Trigger.Parameters.Value<float>("dwellGameSeconds"), Is.EqualTo(120f));
            Assert.That(wolfLure.Actions[0].Type, Is.EqualTo("creature.advance"));
            Assert.That(wolfLure.Actions[0].Parameters.Value<int>("count"), Is.EqualTo(7));
        }

        [Test]
        [Category("GameEvents.Save")]
        public void NewSaveDataStartsWithIndependentEventCollections()
        {
            GameSaveData first = new();
            GameSaveData second = new();

            first.GameEventData.EventProgress["test"] = new GameEventProgressSaveData();

            Assert.That(first.GameEventData.EventProgress.Count, Is.EqualTo(1));
            Assert.That(second.GameEventData.EventProgress, Is.Empty);
            Assert.That(first.GameEventData.ActiveEvents, Is.Not.Null);
        }

        private static string CreateValidCatalog(string eventId)
        {
            return @"{
                       ""schemaVersion"": 1,
                       ""events"": [
                         {
                           ""id"": """ + eventId + @""",
                           ""displayName"": ""Test"",
                           ""enabled"": true,
                           ""durationDays"": 0,
                           ""trigger"": {
                             ""type"": ""day.schedule"",
                             ""parameters"": {
                               ""minimumDay"": 3,
                               ""repeatEveryDays"": 3,
                               ""timeOfDay"": 10,
                               ""chance"": 1
                             }
                           },
                           ""conditions"": [],
                           ""actions"": [
                             {
                               ""id"": ""emit"",
                               ""type"": ""signal.emit"",
                               ""parameters"": {
                                 ""signal"": ""test.signal"",
                                 ""payload"": {}
                               }
                             }
                           ]
                         }
                       ]
                     }";
        }
    }
}
