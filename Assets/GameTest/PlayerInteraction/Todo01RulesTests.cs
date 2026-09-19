using NUnit.Framework;
using MemoryPack;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>鸟类耐力与旧体温存档字段布局回归；不需要进入真实世界。</summary>
    public sealed class Todo01RulesTests
    {
        #region 放置范围
        [TestCase(3.5f, 0f, true)]
        [TestCase(3.6f, 0f, false)]
        [TestCase(3f, 3f, false)]
        public void PlacementReachUsesCellEdgeAndCircularRadius(float x, float y, bool expected)
        {
            Assert.That(Mod_Building.IsWithinPlacementDistance(UnityEngine.Vector3.zero,
                new UnityEngine.Vector3(x, y, 0f), 3f), Is.EqualTo(expected));
        }
        #endregion

        #region 飞行耐力
        [Test]
        public void FullFlightConsumesExactlyOneHundredSeconds()
        {
            var state = new AI_Bird.FlightState { Phase = BirdFlightPhase.Flying };
            Assert.That(AI_Bird.AdvanceFlightStamina(state, 99f, 100f, 1f, 10f), Is.False);
            Assert.That(state.Stamina, Is.EqualTo(1f));
            Assert.That(AI_Bird.AdvanceFlightStamina(state, 1f, 100f, 1f, 10f), Is.True);
            Assert.That(state.Stamina, Is.Zero);
            Assert.That(state.MustRecoverStamina, Is.True);
        }

        [Test]
        public void ExhaustedBirdCannotImmediatelyRestartWithOneStaminaPoint()
        {
            var state = new AI_Bird.FlightState { Phase = BirdFlightPhase.Ground, Stamina = 0f, MustRecoverStamina = true };
            AI_Bird.AdvanceFlightStamina(state, 0.1f, 100f, 1f, 10f);
            Assert.That(state.Stamina, Is.EqualTo(1f));
            Assert.That(state.MustRecoverStamina, Is.True);
            AI_Bird.AdvanceFlightStamina(state, 9.9f, 100f, 1f, 10f);
            Assert.That(state.Stamina, Is.EqualTo(100f));
            Assert.That(state.MustRecoverStamina, Is.False);
        }

        [Test]
        public void LandingDoesNotRegenerateBeforeTouchingGround()
        {
            var state = new AI_Bird.FlightState { Phase = BirdFlightPhase.Landing, Stamina = 0f, MustRecoverStamina = true };
            AI_Bird.AdvanceFlightStamina(state, 2f, 100f, 1f, 10f);
            Assert.That(state.Stamina, Is.Zero);
        }

        [Test]
        public void FlightStaminaSurvivesModuleSnapshot()
        {
            var state = new AI_Bird.FlightState { Phase = BirdFlightPhase.Ground, Stamina = 17f, MustRecoverStamina = true };
            var data = new Ex_ModData();
            data.WriteData(state);
            var restored = data.GetData<AI_Bird.FlightState>();
            Assert.That(restored.Stamina, Is.EqualTo(17f));
            Assert.That(restored.MustRecoverStamina, Is.True);
        }
        #endregion

        #region 温度存档与固定伤害
        [Test]
        public void TemperatureKeepsEightFloatMemoryPackSlots()
        {
            var value = new Mod_Temperature.TemperatureData { ColdDamagePerTick = 2f, HotDamagePerSecond = 7f };
            byte[] bytes = MemoryPackSerializer.Serialize(value);
            Assert.That(bytes[0], Is.EqualTo(8), "旧存档包含八个标量字段，不能移除中间冷伤槽位。");
            var restored = MemoryPackSerializer.Deserialize<Mod_Temperature.TemperatureData>(bytes);
            Assert.That(restored.ColdDamagePerTick, Is.EqualTo(2f));
            Assert.That(restored.HotDamagePerSecond, Is.EqualTo(7f));
            Assert.That(TemperatureMgr.ColdDamageTickIntervalSeconds, Is.EqualTo(5f));
            Assert.That(TemperatureMgr.DamageTickIntervalSeconds, Is.EqualTo(20f));
        }
        #endregion
    }
}
