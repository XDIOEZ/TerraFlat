using System.Collections;
using System.IO;
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
            Assert.That(cave.UseFixedLighting, Is.True);
            Assert.That(cave.FixedLighting, Is.EqualTo(0.2f).Within(0.0001f));

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

        /// <summary>全局光必须由运行时日月 Prefab 持有，启动场景不能抢占时间系统单例。</summary>
        [Test]
        [Category("Dimension.Smoke")]
        [Category("Smoke")]
        public void RuntimeTimeSystemOwnsGlobalLightWithoutStartupSceneDuplicate()
        {
            GameObject timeSystemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Core/Managers/Time/TimeSystem.prefab");
            Assert.That(timeSystemPrefab, Is.Not.Null);
            Assert.That(timeSystemPrefab.GetComponent<DayTimeSystem>(), Is.Not.Null);
            bool hasGlobalLightComponent = false;
            foreach (Component component in timeSystemPrefab.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == "Light2D")
                {
                    hasGlobalLightComponent = true;
                    break;
                }
            }
            Assert.That(hasGlobalLightComponent, Is.True);

            string dayTimeScriptGuid = AssetDatabase.AssetPathToGUID(
                "Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs");
            string startupSceneSource = File.ReadAllText("Assets/3_Scenes/GameStartScene.unity");
            Assert.That(startupSceneSource, Does.Not.Contain($"guid: {dayTimeScriptGuid}"));
        }
    }
}
