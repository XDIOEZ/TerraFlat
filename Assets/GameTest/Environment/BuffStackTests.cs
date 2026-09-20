using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Environment
{
    /// <summary>保持历史三字段布局，用于验证追加层数字段后的读档兼容，不接触真实存档。</summary>
    [MemoryPackable]
    public partial class LegacyThreeFieldBuffFixture
    {
        public string DefinitionId;
        public float RemainingDurationSeconds;
        public float TickElapsedSeconds;
    }

    /// <summary>隔离的 Buff 宿主，不生成世界，不读取或写入玩家存档。</summary>
    public sealed class BuffStackTestItem : Item
    {
        private Data_GeneralItem data = new() { IDName = "BuffStackTestItem" };
        public override ItemData itemData => data;
        protected override void SetItemData(ItemData value) => data = RequireData<Data_GeneralItem>(value);
    }

    /// <summary>仅记录真实伤害入口收到的数值，避免测试引发正式死亡、掉落或身体创伤。</summary>
    public sealed class BuffStackTestDamageReceiver : DamageReceiver
    {
        public float Received;
        public override float ForceHurt(float damage) { Received += damage; return damage; }
    }

    /// <summary>覆盖潮湿/燃烧层数、真实水深上限、生命周期、存档与周期伤害。</summary>
    public sealed class BuffStackTests
    {
        #region 隔离夹具
        private GameObject host;
        private BuffManager manager;
        private BuffStackTestDamageReceiver damage;
        private Mod_Temperature temperature;
        private GameRes resources;
        private readonly Dictionary<string, BuffDefinition> previous = new();

        [SetUp]
        public void SetUp()
        {
            resources = GameRes.Instance;
            foreach (BuffDefinition definition in BuffCatalogLoader.LoadBuiltInDefinitions().Where(
                         value => value.Id == WetBuffIds.Wet || value.Id == BurningBuffIds.Burning))
            {
                resources.BuffDefinitions.TryGetValue(definition.Id, out BuffDefinition old);
                previous[definition.Id] = old;
                resources.BuffDefinitions[definition.Id] = definition;
            }
            host = new GameObject("BuffStackTest");
            host.SetActive(false);
            var item = host.AddComponent<BuffStackTestItem>();
            item.itemMods = new ItemMods(item);
            manager = host.AddComponent<BuffManager>();
            manager.ModData = new Ex_ModData_MemoryPackable { ID = ModText.BuffManager, Name = "BuffStackTest" };
            item.itemMods.AddMod(manager);
            manager.ModuleInit(item, manager.ModData);
            damage = host.AddComponent<BuffStackTestDamageReceiver>();
            damage.modData = new Ex_ModData { ID = ModText.Hp, Name = "BuffDamageTest" };
            item.itemMods.AddMod(damage);
            damage.ModuleInit(item, damage.modData);
            temperature = host.AddComponent<Mod_Temperature>();
            temperature.modData = new Ex_ModData_MemoryPackable { ID = ModText.Temperature, Name = "BuffTemperatureTest" };
            item.itemMods.AddMod(temperature);
            temperature.ModuleInit(item, temperature.modData);
        }

        [TearDown]
        public void TearDown()
        {
            if (manager != null) manager.ClearAllBuffs();
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            foreach (var pair in previous)
                if (pair.Value == null) resources.BuffDefinitions.Remove(pair.Key);
                else resources.BuffDefinitions[pair.Key] = pair.Value;
            previous.Clear();
        }
        #endregion

        #region 叠层与相互制约
        [TestCase(0f, 0)]
        [TestCase(0.1f, 3)]
        [TestCase(0.2f, 6)]
        [TestCase(0.3f, 9)]
        [TestCase(0.4f, 10)]
        [TestCase(1f, 10)]
        public void RealWaterDepthDeterminesLimit(float depth, int cap)
        {
            Assert.That(BuffManager.ResolveWaterStackLimit(depth, resources.GetBuffDefinition(WetBuffIds.Wet)), Is.EqualTo(cap));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void BurningDamageEqualsStacksPerSecond(int stacks)
        {
            manager.AddBuff(BurningBuffIds.Burning, stacks);
            manager.Tick(1f);
            Assert.That(damage.Received, Is.EqualTo(stacks).Within(0.0001f));
        }

        [Test]
        public void WaterBlocksEqualFireAndStrongerFireEvaporatesWater()
        {
            manager.AddBuff(WetBuffIds.Wet, 3);
            Assert.That(manager.AddBuff(BurningBuffIds.Burning, 3), Is.False);
            Assert.That(manager.AddBuff(BurningBuffIds.Burning, 4), Is.True);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.Zero);
            manager.AddBuff(WetBuffIds.Wet, 3);
            Assert.That(manager.GetBuffStacks(BurningBuffIds.Burning), Is.EqualTo(4));
            manager.AddBuff(WetBuffIds.Wet);
            Assert.That(manager.GetBuffStacks(BurningBuffIds.Burning), Is.Zero);
            Assert.That(manager.AddBuff(BurningBuffIds.Burning, 4), Is.False);
        }

        [Test]
        public void RepeatedTorchStacksCapAtFiveAndKeepDamageClock()
        {
            manager.AddBuff(BurningBuffIds.Burning, 2);
            manager.Tick(0.5f);
            manager.AddBuff(BurningBuffIds.Burning, 2);
            manager.Tick(0.5f);
            Assert.That(damage.Received, Is.EqualTo(4f));
            manager.AddBuff(BurningBuffIds.Burning, int.MaxValue);
            Assert.That(manager.GetBuffStacks(BurningBuffIds.Burning), Is.EqualTo(5));
            Assert.That(manager.ActiveBuffs[BurningBuffIds.Burning].VisualScale, Is.EqualTo(1.4f).Within(0.0001f));
        }

        [Test]
        public void WaterAccumulatesAfterOneSecondAndSurvivesTileCrossingAndLeaving()
        {
            manager.SetWaterStackExposure(true);
            manager.AdvanceWaterWetness(0.1f, 0.6f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.Zero);
            manager.SetWaterStackExposure(false);
            manager.SetWaterStackExposure(true);
            manager.AdvanceWaterWetness(0.1f, 0.4f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.EqualTo(1));
            manager.AdvanceWaterWetness(0.1f, 100f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.EqualTo(3));
            manager.AdvanceWaterWetness(0.4f, 7f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.EqualTo(10));
            manager.AdvanceWaterWetness(0.1f, 1f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.EqualTo(10));
            manager.SetWaterStackExposure(false);
            manager.AdvanceWaterWetness(1f, 99f);
            manager.Tick(29f);
            Assert.That(manager.GetBuffStacks(WetBuffIds.Wet), Is.EqualTo(10));
            manager.Tick(1f);
            Assert.That(manager.HasBuff(WetBuffIds.Wet), Is.False);
        }

        [Test]
        public void WetCoolingIsRegisteredOnceAndRemovedOnce()
        {
            manager.AddBuff(WetBuffIds.Wet, 2);
            manager.AddBuff(WetBuffIds.Wet, 2);
            Assert.That(temperature.Data.RuntimeCoolingSpeedMultiplier, Is.EqualTo(2f));
            manager.RemoveBuff(WetBuffIds.Wet);
            Assert.That(temperature.Data.RuntimeCoolingSpeedMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void StackCountPersistsInMemoryPackWithoutReceiverReferences()
        {
            manager.AddBuff(BurningBuffIds.Burning, 4);
            var runtime = manager.ActiveBuffs[BurningBuffIds.Burning];
            var restored = MemoryPackSerializer.Deserialize<BuffInstance>(MemoryPackSerializer.Serialize(runtime));
            Assert.That(restored.StackCount, Is.EqualTo(4));
            Assert.That(restored.DefinitionId, Is.EqualTo(BurningBuffIds.Burning));
            Assert.That(restored.Definition, Is.Null);
            Assert.That(restored.Receiver, Is.Null);
        }

        [Test]
        public void LegacyThreeFieldSaveRestoresAsOneStack()
        {
            var legacy = new LegacyThreeFieldBuffFixture {
                DefinitionId = BurningBuffIds.Burning, RemainingDurationSeconds = 2.5f, TickElapsedSeconds = 0.25f };
            var restored = MemoryPackSerializer.Deserialize<BuffInstance>(MemoryPackSerializer.Serialize(legacy));
            Assert.That(restored.Restore(manager.item), Is.True);
            Assert.That(restored.StackCount, Is.EqualTo(1));
            Assert.That(restored.RemainingDurationSeconds, Is.EqualTo(2.5f));
            Assert.That(restored.TickElapsedSeconds, Is.EqualTo(0.25f));
        }
        #endregion
    }
}
