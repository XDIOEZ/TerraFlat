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

    /// <summary>验证水体动作定义为每个角色创建独立实例，并正确处理长按、补水与脏水感染。</summary>
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

        private sealed class EnvironmentDrinkingFixture : IDisposable
        {
            private readonly GameObject itemObject;
            private readonly GameRes gameRes;
            private readonly BuffDefinition previousInfection;
            private readonly bool hadPreviousInfection;

            public BuffManager BuffManager { get; }
            public Mod_Food Food { get; }
            public EnvironmentInteractionRunner Runner { get; }
            public DrinkWaterActionInstance ActiveDrink =>
                Runner.ActiveAction as DrinkWaterActionInstance;

            public EnvironmentDrinkingFixture()
            {
                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null);
                BuffDefinition infection = BuffCatalogLoader.LoadBuiltInDefinitions()
                    .Single(definition => definition.Id == InfectionBuffIds.Infection);
                hadPreviousInfection = gameRes.BuffDefinitions.TryGetValue(
                    infection.Id, out previousInfection);
                gameRes.BuffDefinitions[infection.Id] = infection;

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

            public void Provide(WaterEnvironmentKind kind) =>
                Runner.SetAvailableActions(
                    new DrinkWaterActionDefinition(kind, 1f, 1f, 125f, 0.2f));

            public void Dispose()
            {
                if (itemObject != null)
                    UnityEngine.Object.DestroyImmediate(itemObject);
                if (hadPreviousInfection)
                    gameRes.BuffDefinitions[InfectionBuffIds.Infection] = previousInfection;
                else
                    gameRes.BuffDefinitions.Remove(InfectionBuffIds.Infection);
            }

            private static Ex_ModData_MemoryPackable CreateModuleData(string id, string name) =>
                new() { ID = id, Name = name };
        }
    }
}
