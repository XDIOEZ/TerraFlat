using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.PlayerInteraction
{
    public sealed class MoverRunInputTests
    {
        private GameObject owner;
        private Mover mover;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("MoverRunInputTests");
            mover = owner.AddComponent<Mover>();
            mover.Data.Speed = new GameValue_float(5f);
            mover.Data.runSpeedRate = 2f;
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
    }
}
