using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.Dimension
{
    public sealed class DimensionLifecycleTests
    {
        [UnityTest]
        [Category("Dimension.Smoke")]
        [Category("Smoke")]
        public IEnumerator FastPlayModeCreatesDimensionManager()
        {
            yield return null;

            Assert.That(DimensionManager.Instance, Is.Not.Null,
                "DimensionManager must recover after leaving Play Mode with Domain Reload disabled.");
        }
    }
}
