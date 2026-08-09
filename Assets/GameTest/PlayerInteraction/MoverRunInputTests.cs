using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.PlayerInteraction
{
    public sealed class MoverRunInputTests
    {
        private GameObject owner;
        private Mover mover;
        private Rigidbody2D body;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("MoverRunInputTests");
            body = owner.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            mover = owner.AddComponent<Mover>();
            mover.rb = body;
            mover.Data.Speed = new GameValue_float(5f);
            mover.Data.runSpeedRate = 2f;
            mover.speedTransitionDuration = 0.24f;
            mover.stopTransitionDuration = 0.07f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner);
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void ShortShiftPressDoesNotToggleRunState()
        {
            mover.HandleRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleRunInputReleased(0.1d);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void LongShiftPressRunsOnlyUntilRelease()
        {
            mover.HandleRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleRunInputReleased(1d);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void RepeatedShiftPressesDoNotCreatePersistentRunState()
        {
            mover.HandleRunInputPressed();
            mover.HandleRunInputReleased(1d);
            Assert.That(mover.IsRunning, Is.False);

            mover.HandleRunInputPressed();
            Assert.That(mover.IsRunning, Is.True);
            mover.HandleRunInputReleased(1d);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        /// <summary>验证走路、奔跑与松开方向均不会瞬间切换速度。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void MoveSmoothlyTransitionsBetweenWalkRunAndStop()
        {
            const float simulationStep = 0.02f;
            Vector2 target = body.position + Vector2.right;

            mover.Move(target, simulationStep);
            Assert.That(body.velocity.x, Is.GreaterThan(0f));
            Assert.That(body.velocity.x, Is.LessThan(mover.Speed.Value));

            AdvanceMovement(target, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(5f).Within(0.01f));

            mover.HandleRunInputPressed();
            mover.Move(target, simulationStep);
            Assert.That(body.velocity.x, Is.GreaterThan(5f));
            Assert.That(body.velocity.x, Is.LessThan(mover.Speed.Value));

            AdvanceMovement(target, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(10f).Within(0.01f));

            mover.HandleRunInputReleased(1d);
            mover.Move(target, simulationStep);
            Assert.That(body.velocity.x, Is.GreaterThan(5f));
            Assert.That(body.velocity.x, Is.LessThan(10f));

            AdvanceMovement(target, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(5f).Within(0.01f));

            mover.Move(body.position, simulationStep);
            Assert.That(body.velocity.x, Is.GreaterThan(0f));

            AdvanceMovement(body.position, simulationStep, 3);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
        }

        /// <summary>用固定步长推进 Mover，保持测试与真实输入设备无关。</summary>
        private void AdvanceMovement(Vector2 target, float deltaTime, int steps)
        {
            for (int index = 0; index < steps; index++)
                mover.Move(target, deltaTime);
        }
    }
}
