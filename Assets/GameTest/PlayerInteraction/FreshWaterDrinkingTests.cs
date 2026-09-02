using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.PlayerInteraction
{
    public sealed class EnvironmentDrinkingTestItem : Item
    {
        private Data_GeneralItem data = new() { IDName = "EnvironmentDrinkingTestPlayer" };
        public override ItemData itemData => data;
        protected override void SetItemData(ItemData value) =>
            data = RequireData<Data_GeneralItem>(value);
    }

    /// <summary>验证水体动作定义为每个角色创建独立实例，并正确处理长按、补水与水质后果。</summary>
    public sealed class EnvironmentDrinkingTests
    {
        [Test]
        [Category("PlayerInteraction.Input")]
        public void WaterDefinitionCreatesIndependentDrinkInstanceAndDirtyWaterCanInfect()
        {
            using var fixture = new EnvironmentDrinkingFixture();

            Assert.That(fixture.Runner.BeginPreferredAction(), Is.False,
                "没有环境动作定义时不能开始动作。");

            fixture.Provide(WaterEnvironmentKind.CleanFresh);
            Assert.That(fixture.Runner.BeginPreferredAction(), Is.True);
            DrinkWaterActionInstance clean = fixture.ActiveDrink;
            fixture.Runner.TickActiveAction(0.99f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(100f));
            fixture.Runner.TickActiveAction(0.02f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(225f));
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.False);

            fixture.Runner.CancelActiveAction();
            fixture.Provide(WaterEnvironmentKind.DirtyFresh);
            Assert.That(fixture.Runner.BeginPreferredAction(), Is.True);
            DrinkWaterActionInstance dirty = fixture.ActiveDrink;
            Assert.That(dirty, Is.Not.SameAs(clean),
                "每次开始环境动作都必须创建角色独享实例。");
            Assert.That(dirty.ProcessPulse(0.2f, false), Is.True);
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.False);
            Assert.That(dirty.ProcessPulse(0.1999f, false), Is.True);
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.True);

            fixture.Runner.ClearAvailableActions();
            Assert.That(fixture.Runner.ActiveAction, Is.Null);
            Assert.That(fixture.Runner.AvailableActionCount, Is.Zero);
        }

        /// <summary>饮用海水必须补十点水分、添加脱水，并在重复饮用时只累加持续时间。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void SaltWaterHydratesAndStacksDehydrationDuration()
        {
            using var fixture = new EnvironmentDrinkingFixture();
            fixture.Provide(WaterEnvironmentKind.Salt, 10f);
            Assert.That(fixture.Runner.BeginPreferredAction(), Is.True);

            DrinkWaterActionInstance saltWater = fixture.ActiveDrink;
            Assert.That(saltWater.ProcessPulse(1f, false), Is.True);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(110f));
            Assert.That(fixture.BuffManager.TryGetBuff(
                DehydrationBuffIds.Dehydration, out BuffInstance dehydration), Is.True);
            Assert.That(dehydration.RemainingDuration, Is.EqualTo(10f));

            Assert.That(saltWater.ProcessPulse(1f, false), Is.True);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(120f));
            Assert.That(dehydration.RemainingDuration, Is.EqualTo(20f));

            fixture.BuffManager.Tick(1f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(117f));
            Assert.That(dehydration.RemainingDuration, Is.EqualTo(19f));
        }

        private sealed class EnvironmentDrinkingFixture : IDisposable
        {
            private readonly GameObject itemObject;
            private readonly GameRes gameRes;
            private readonly BuffDefinition previousInfection;
            private readonly bool hadPreviousInfection;
            private readonly BuffDefinition previousDehydration;
            private readonly bool hadPreviousDehydration;

            public BuffManager BuffManager { get; }
            public Mod_Food Food { get; }
            public EnvironmentInteractionRunner Runner { get; }
            public DrinkWaterActionInstance ActiveDrink =>
                Runner.ActiveAction as DrinkWaterActionInstance;

            public EnvironmentDrinkingFixture()
            {
                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null);
                BuffDefinition[] definitions = BuffCatalogLoader.LoadBuiltInDefinitions().ToArray();
                BuffDefinition infection = definitions.Single(
                    definition => definition.Id == InfectionBuffIds.Infection);
                hadPreviousInfection = gameRes.BuffDefinitions.TryGetValue(
                    infection.Id, out previousInfection);
                gameRes.BuffDefinitions[infection.Id] = infection;
                BuffDefinition dehydration = definitions.Single(
                    definition => definition.Id == DehydrationBuffIds.Dehydration);
                hadPreviousDehydration = gameRes.BuffDefinitions.TryGetValue(
                    dehydration.Id, out previousDehydration);
                gameRes.BuffDefinitions[dehydration.Id] = dehydration;

                itemObject = new GameObject("EnvironmentDrinkingTestPlayer");
                itemObject.SetActive(false);
                EnvironmentDrinkingTestItem item = itemObject.AddComponent<EnvironmentDrinkingTestItem>();
                item.itemMods = new ItemMods(item);

                BuffManager = itemObject.AddComponent<BuffManager>();
                BuffManager.ModData = CreateModuleData(ModText.BuffManager, "TestBuffManager");
                Food = itemObject.AddComponent<Mod_Food>();
                Food.FoodModData = new ModData_FoodData { ID = ModText.Food, Name = "TestFood" };
                Runner = itemObject.AddComponent<EnvironmentInteractionRunner>();

                item.itemMods.AddMod(BuffManager);
                item.itemMods.AddMod(Food);
                BuffManager.ModuleInit(item, BuffManager.ModData);
                Food.ModuleInit(item, Food.FoodModData);
                Runner.Bind(item);
                Food.Data.nutrition.Water = 100f;
                Food.Data.nutrition.Max_Water = 500f;
                itemObject.SetActive(true);
            }

            /// <summary>向运行器提供指定水质与单次补水量的饮水动作。</summary>
            public void Provide(WaterEnvironmentKind kind, float waterGain = 125f) =>
                Runner.SetAvailableActions(
                    new DrinkWaterActionDefinition(kind, 1f, 1f, waterGain, 0.2f));

            public void Dispose()
            {
                if (itemObject != null)
                    UnityEngine.Object.DestroyImmediate(itemObject);
                if (hadPreviousInfection)
                    gameRes.BuffDefinitions[InfectionBuffIds.Infection] = previousInfection;
                else
                    gameRes.BuffDefinitions.Remove(InfectionBuffIds.Infection);
                if (hadPreviousDehydration)
                    gameRes.BuffDefinitions[DehydrationBuffIds.Dehydration] = previousDehydration;
                else
                    gameRes.BuffDefinitions.Remove(DehydrationBuffIds.Dehydration);
            }

            private static Ex_ModData_MemoryPackable CreateModuleData(string id, string name) =>
                new() { ID = id, Name = name };
        }
    }
}
