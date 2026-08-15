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
        public void HoldRunRunsOnlyUntilRelease()
        {
            mover.HandleHoldRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleHoldRunInputReleased();

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void ToggleRunChangesStateOnEveryPress()
        {
            mover.HandleToggleRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleToggleRunInputPressed();

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void HoldAndToggleRunShareOneStableRunState()
        {
            mover.HandleToggleRunInputPressed();
            mover.HandleHoldRunInputPressed();
            mover.HandleHoldRunInputReleased();
            Assert.That(mover.IsRunning, Is.False);

            mover.HandleHoldRunInputPressed();
            Assert.That(mover.IsRunning, Is.True);
            mover.HandleHoldRunInputReleased();

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        /// <summary>验证只开启奔跑模式但没有移动时仍保持奔跑逻辑状态，实际移动由输入决定。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void StationaryRunKeepsRunModeWithoutMovement()
        {
            mover.HandleToggleRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.MoveByInput(Vector2.zero, 0.1f);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(mover.IsRunning, Is.True);
        }

        /// <summary>验证奔跑停止移动后仍保留奔跑模式，并交给 Animator 切换待机而非冻结跑步帧。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void RunningStopKeepsRunModeForIdleTransition()
        {
            mover.HandleToggleRunInputPressed();
            AdvanceInputMovement(Vector2.right, 0.02f, 24);

            AdvanceInputMovement(Vector2.zero, 0.02f, 5);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(mover.IsRunning, Is.True);

            mover.HandleToggleRunInputPressed();
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

            mover.HandleHoldRunInputPressed();
            mover.Move(target, simulationStep);
            Assert.That(body.velocity.x, Is.GreaterThan(5f));
            Assert.That(body.velocity.x, Is.LessThan(mover.Speed.Value));

            AdvanceMovement(target, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(10f).Within(0.01f));

            mover.HandleHoldRunInputReleased();
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

        /// <summary>验证手机虚拟摇杆与手柄左摇杆共用的幅度会线性控制目标速度。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void AnalogMoveInputScalesSpeedAndCapsAtMaximum()
        {
            const float simulationStep = 0.02f;

            AdvanceInputMovement(Vector2.right * 0.4f, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(2f).Within(0.01f));

            AdvanceInputMovement(Vector2.right, simulationStep, 16);
            Assert.That(body.velocity.x, Is.EqualTo(5f).Within(0.01f));

            AdvanceInputMovement(Vector2.one, simulationStep, 16);
            Assert.That(body.velocity.magnitude, Is.EqualTo(5f).Within(0.01f));
        }

        /// <summary>用固定步长推进 Mover，保持测试与真实输入设备无关。</summary>
        private void AdvanceMovement(Vector2 target, float deltaTime, int steps)
        {
            for (int index = 0; index < steps; index++)
                mover.Move(target, deltaTime);
        }

        /// <summary>用固定输入幅度推进玩家移动。</summary>
        private void AdvanceInputMovement(Vector2 input, float deltaTime, int steps)
        {
            for (int index = 0; index < steps; index++)
                mover.MoveByInput(input, deltaTime);
        }
    }
}
