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
        public void ShortShiftPressTogglesPersistentRunState()
        {
            double shortPressDuration = mover.RunToggleTapThreshold * 0.5d;

            mover.HandleRunInputPressed();
            mover.HandleRunInputReleased(shortPressDuration);

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.IsRunToggleEnabled, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleRunInputPressed();
            mover.HandleRunInputReleased(shortPressDuration);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.IsRunToggleEnabled, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void LongShiftPressRunsOnlyUntilRelease()
        {
            double longPressDuration = mover.RunToggleTapThreshold * 2d;

            mover.HandleRunInputPressed();

            Assert.That(mover.IsRunning, Is.True);
            Assert.That(mover.Speed.Value, Is.EqualTo(10f).Within(0.001f));

            mover.HandleRunInputReleased(longPressDuration);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.IsRunToggleEnabled, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        [Category("PlayerInteraction.Input")]
        public void LongShiftPressCancelsAnExistingRunToggle()
        {
            mover.HandleRunInputPressed();
            mover.HandleRunInputReleased(mover.RunToggleTapThreshold * 0.5d);
            Assert.That(mover.IsRunToggleEnabled, Is.True);

            mover.HandleRunInputPressed();
            mover.HandleRunInputReleased(mover.RunToggleTapThreshold * 2d);

            Assert.That(mover.IsRunning, Is.False);
            Assert.That(mover.IsRunToggleEnabled, Is.False);
            Assert.That(mover.Speed.Value, Is.EqualTo(5f).Within(0.001f));
        }
    }
}
