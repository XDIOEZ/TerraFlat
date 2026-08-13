using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.PlayerInteraction
{
    public sealed class FreshWaterDrinkingTestItem : Item
    {
        private Data_GeneralItem data = new() { IDName = "FreshWaterDrinkingTestPlayer" };
        public override ItemData itemData => data;
        protected override void SetItemData(ItemData value) => data = RequireData<Data_GeneralItem>(value);
    }

    /// <summary>验证水体 Buff 对长按饮水、补水和脏水感染的授权边界。</summary>
    public sealed class FreshWaterDrinkingTests
    {
        [Test]
        [Category("PlayerInteraction.Input")]
        public void FreshWaterBuffGatesHoldDrinkingAndDirtyWaterInfection()
        {
            using var fixture = new FreshWaterDrinkingFixture();

            Assert.That(fixture.Sender.BeginFreshWaterDrinkHold(), Is.False,
                "没有淡水环境 Buff 时不能开始饮水。");
            Assert.That(fixture.Sender.ProcessFreshWaterDrinkPulse(0f, false), Is.False);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(100f));

            fixture.BuffManager.AddBuff(FreshWaterBuffIds.Clean);
            Assert.That(fixture.Sender.BeginFreshWaterDrinkHold(), Is.True);
            fixture.Sender.TickFreshWaterDrinking(0.99f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(100f),
                "长按未满1秒前不能补水。");
            fixture.Sender.TickFreshWaterDrinking(0.02f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(125f));
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.False,
                "干净淡水不能触发感染。");

            fixture.Sender.EndFreshWaterDrinkHold();
            fixture.BuffManager.RemoveBuff(FreshWaterBuffIds.Clean);
            fixture.BuffManager.AddBuff(FreshWaterBuffIds.Dirty);
            Assert.That(fixture.Sender.ProcessFreshWaterDrinkPulse(0.2f, false), Is.True);
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.False,
                "20%边界值不应落入感染区间。");
            Assert.That(fixture.Sender.ProcessFreshWaterDrinkPulse(0.1999f, false), Is.True);
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.True,
                "脏水判定值低于20%时必须获得感染。");

            fixture.Sender.EndFreshWaterDrinkHold();
            fixture.BuffManager.RemoveBuff(FreshWaterBuffIds.Dirty);
            fixture.BuffManager.RemoveBuff(InfectionBuffIds.Infection);
            fixture.BuffManager.AddBuff(SaltWaterBuffIds.InSaltWater);
            fixture.Food.Data.nutrition.Water = 100f;
            Assert.That(fixture.Sender.BeginFreshWaterDrinkHold(), Is.True,
                "位于盐水中时必须允许长按 E 键开始饮水。");
            fixture.Sender.TickFreshWaterDrinking(1.01f);
            Assert.That(fixture.Food.Data.nutrition.Water, Is.EqualTo(125f),
                "盐水应复用现有饮水 Tick 的补水逻辑。");
            Assert.That(fixture.BuffManager.HasBuff(InfectionBuffIds.Infection), Is.False,
                "盐水饮水不应误用脏淡水的感染判定。");
        }

        private sealed class FreshWaterDrinkingFixture : IDisposable
        {
            private readonly GameObject itemObject;
            private readonly GameRes gameRes;
            private readonly Dictionary<string, BuffDefinition> previousDefinitions = new();
            private readonly HashSet<string> existingDefinitionIds = new();

            public BuffManager BuffManager { get; }
            public Mod_Food Food { get; }
            public Mod_InteractSender Sender { get; }

            public FreshWaterDrinkingFixture()
            {
                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null);

                List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
                Register(definitions.Single(definition => definition.Id == FreshWaterBuffIds.Clean));
                Register(definitions.Single(definition => definition.Id == FreshWaterBuffIds.Dirty));
                Register(definitions.Single(definition => definition.Id == SaltWaterBuffIds.InSaltWater));
                Register(definitions.Single(definition => definition.Id == InfectionBuffIds.Infection));

                itemObject = new GameObject("FreshWaterDrinkingTestPlayer");
                itemObject.SetActive(false);
                var item = itemObject.AddComponent<FreshWaterDrinkingTestItem>();
                item.itemMods = new ItemMods(item);

                BuffManager = itemObject.AddComponent<BuffManager>();
                BuffManager.ModData = CreateModuleData(ModText.BuffManager, "TestBuffManager");
                Food = itemObject.AddComponent<Mod_Food>();
                Food.FoodModData = new ModData_FoodData
                {
                    ID = ModText.Food,
                    Name = "TestFood"
                };
                Sender = itemObject.AddComponent<Mod_InteractSender>();
                Sender.ModSaveData = CreateModuleData(ModText.Interact, "TestInteractSender");

                item.itemMods.AddMod(BuffManager);
                item.itemMods.AddMod(Food);
                item.itemMods.AddMod(Sender);
                BuffManager.ModuleInit(item, BuffManager.ModData);
                Food.ModuleInit(item, Food.FoodModData);
                Sender.ModuleInit(item, Sender.ModSaveData);
                Food.Data.nutrition.Water = 100f;
                Food.Data.nutrition.Max_Water = 500f;
                itemObject.SetActive(true);
            }

            public void Dispose()
            {
                if (itemObject != null)
                    UnityEngine.Object.DestroyImmediate(itemObject);

                foreach (string id in new[]
                         {
                             FreshWaterBuffIds.Clean,
                             FreshWaterBuffIds.Dirty,
                             SaltWaterBuffIds.InSaltWater,
                             InfectionBuffIds.Infection
                         })
                {
                    if (existingDefinitionIds.Contains(id))
                        gameRes.BuffDefinitions[id] = previousDefinitions[id];
                    else
                        gameRes.BuffDefinitions.Remove(id);
                }
            }

            private void Register(BuffDefinition definition)
            {
                if (gameRes.BuffDefinitions.TryGetValue(definition.Id, out BuffDefinition previous))
                {
                    existingDefinitionIds.Add(definition.Id);
                    previousDefinitions[definition.Id] = previous;
                }

                gameRes.BuffDefinitions[definition.Id] = definition;
            }

            private static Ex_ModData_MemoryPackable CreateModuleData(string id, string name)
            {
                return new Ex_ModData_MemoryPackable { ID = id, Name = name };
            }
        }

    }
}
