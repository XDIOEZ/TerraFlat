using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>推动的速度语义与无 Collider 连续接触回归，纯几何测试不改真实世界。</summary>
    public sealed class WorldMotionTests
    {
        #region 来源速度
        [Test]
        public void LandPushUsesOneFifthOfSourceSpeed()
        {
            Assert.That(WorldMotionSystem.CalculatePushVelocity(new Vector2(3f, 4f), false, 0.2f),
                Is.EqualTo(new Vector2(0.6f, 0.8f)).Using(Vector2Comparer.Instance));
        }

        [Test]
        public void WaterPushPreservesCurrentWaterSpeed()
        {
            Vector2 waterSpeed = new(0.6f, 0.8f);
            Assert.That(WorldMotionSystem.CalculatePushVelocity(waterSpeed, true, 0.2f), Is.EqualTo(waterSpeed));
        }
        #endregion

        #region 无碰撞扫掠
        [Test]
        public void FastMovementCannotSkipBoatEdge()
        {
            Assert.That(WorldMotionSystem.TrySweepBox(new Vector2(-2f, 0f), new Vector2(4f, 0f),
                Vector2.one, out Vector2 normal, out float fraction), Is.True);
            Assert.That(normal, Is.EqualTo(Vector2.left));
            Assert.That(fraction, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void TangentialMovementOutsideHullDoesNotPush()
        {
            Assert.That(WorldMotionSystem.TrySweepBox(new Vector2(-2f, 1.1f), new Vector2(4f, 0f),
                Vector2.one, out _, out _), Is.False);
        }

        [Test]
        public void LeavingContactDoesNotPullBoat()
        {
            Assert.That(WorldMotionSystem.TrySweepBox(new Vector2(-0.99f, 0f), Vector2.left,
                Vector2.one, out _, out _), Is.False);
        }

        [Test]
        public void ExistingContactUsesInwardNormal()
        {
            Assert.That(WorldMotionSystem.TrySweepBox(new Vector2(0f, 0.99f), Vector2.down,
                Vector2.one, out Vector2 normal, out float fraction), Is.True);
            Assert.That(normal, Is.EqualTo(Vector2.up));
            Assert.That(fraction, Is.Zero);
        }
        #endregion

        private sealed class Vector2Comparer : System.Collections.Generic.IEqualityComparer<Vector2>
        {
            public static readonly Vector2Comparer Instance = new();
            public bool Equals(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.000001f;
            public int GetHashCode(Vector2 value) => 0;
        }
    }
}
