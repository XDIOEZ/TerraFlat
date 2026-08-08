using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Building
{
    /// <summary>建筑基础冒烟测试：保护放置、占地和建筑资源入口。</summary>
    public sealed class BuildingSmokeTests
    {
        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs", "Mod_Building");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs", "BuildingOccupancyRegistry");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/BuildingShadow.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/MineEntrance.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");

            GameObject shadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/BuildingShadow.prefab");
            BuildingShadow shadow = shadowPrefab.GetComponentInChildren<BuildingShadow>(true);
            Assert.That(shadow, Is.Not.Null, "建筑虚影预制体缺少 BuildingShadow");
            Assert.That(shadow.ShadowRenderer, Is.Not.Null, "建筑虚影缺少 SpriteRenderer 引用");
            Assert.That(shadow.ShadowRenderer.sharedMaterial, Is.Not.Null, "建筑虚影材质引用已丢失");
            Assert.That(SortingLayer.NameToID("Shadow"), Is.Not.Zero, "项目缺少建筑虚影排序层 Shadow");
        }

        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void ShadowKeepsFallbackMaterialWhenLegacyBuildingMaterialIsMissing()
        {
            GameObject shadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/BuildingShadow.prefab");
            GameObject buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Building/Wall_Wood.prefab");
            GameObject shadowObject = Object.Instantiate(shadowPrefab);
            GameObject buildingObject = Object.Instantiate(buildingPrefab);
            try
            {
                BuildingShadow shadow = shadowObject.GetComponentInChildren<BuildingShadow>(true);
                SpriteRenderer source = buildingObject.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(shadow, Is.Not.Null);
                Assert.That(source, Is.Not.Null);

                shadow.InitShadow(source, buildingObject.transform,
                    new Bounds(Vector3.zero, Vector3.one));

                Assert.That(shadow.ShadowRenderer.enabled, Is.True);
                Assert.That(shadow.ShadowRenderer.sprite, Is.EqualTo(source.sprite));
                Assert.That(shadow.ShadowRenderer.sharedMaterial, Is.Not.Null,
                    "建筑材质引用丢失时，虚影必须保留 Prefab 默认 Sprite 材质。");
            }
            finally
            {
                Object.DestroyImmediate(shadowObject);
                Object.DestroyImmediate(buildingObject);
            }
        }
    }
}
