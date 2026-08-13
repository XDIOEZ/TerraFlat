using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEditor;

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

        [Test]
        [Category("Dimension.Smoke")]
        [Category("Smoke")]
        public void DimensionProgressUsesExplicitPresentationContextAndThemes()
        {
            WorldEntryProgressInfo legacy = new WorldEntryProgressInfo(
                "title", "status", 0.5f, WorldEntryProgressState.Running);
            Assert.That(legacy.PresentationMode, Is.EqualTo(WorldEntryPresentationMode.Standard));
            Assert.That(legacy.TargetId, Is.Empty);

            WorldEntryProgressInfo dimension = new WorldEntryProgressInfo(
                "title", "status", 0.5f, WorldEntryProgressState.Running,
                WorldEntryPresentationMode.Dimension, WorldAddress.CaveDimensionId);
            Assert.That(dimension.PresentationMode, Is.EqualTo(WorldEntryPresentationMode.Dimension));
            Assert.That(dimension.TargetId, Is.EqualTo(WorldAddress.CaveDimensionId));

            DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(
                "Assets/Resources/Config/DimensionCatalog_Default.asset");
            Assert.That(catalog, Is.Not.Null);
            DimensionDefinition surface = catalog.Find(WorldAddress.SurfaceDimensionId);
            DimensionDefinition cave = catalog.Find(WorldAddress.CaveDimensionId);
            Assert.That(surface.LoadingTheme.BackgroundTexture, Is.Not.Null);
            Assert.That(surface.LoadingTheme.Icon, Is.Not.Null);
            Assert.That(cave.LoadingTheme.BackgroundTexture, Is.Not.Null);
            Assert.That(cave.LoadingTheme.Icon, Is.Not.Null);
            Assert.That(surface.LoadingTheme.AccentColor, Is.Not.EqualTo(cave.LoadingTheme.AccentColor));

            DimensionLoadingTheme invalid = new DimensionLoadingTheme
            {
                BackgroundColor = Color.clear,
                AccentColor = Color.clear
            };
            DimensionLoadingTheme fallback = invalid.ResolveOrNeutral();
            Assert.That(fallback, Is.Not.SameAs(invalid));
            Assert.That(fallback.BackgroundColor.a, Is.GreaterThan(0f));
            Assert.That(fallback.AccentColor.a, Is.GreaterThan(0f));
        }
    }
}
