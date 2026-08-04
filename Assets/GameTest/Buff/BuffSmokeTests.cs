using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.Buff
{
    /// <summary>Buff 基础冒烟测试：保护核心入口、内置目录与效果处理器绑定。</summary>
    public sealed class BuffSmokeTests
    {
        [Test]
        [Category("Buff.Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Buff/BuffManager.cs",
                "BuffManager");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Buff/BuffDefinition.cs",
                "BuffDefinition");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Buff/BuffInstance.cs",
                "BuffInstance");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Buff/BuffEffectDispatcher.cs",
                "BuffEffectDispatcher");
            GameTestAssertions.AssertAssetExists(
                "Assets/5_Scripts/5-3_GamePlay/Config/StreamingAssetsTextLoader.cs");
            GameTestAssertions.AssertAssetExists(
                "Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/StreamingAssets/GameConfig/Buffs/environment.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/StreamingAssets/GameConfig/Buffs/combat.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/StreamingAssets/GameConfig/Buffs/survival.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/StreamingAssets/GameConfig/Buffs/movement.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/2_Prefabs/Module/Manager/Module_BuffManager.prefab");
        }

        [Test]
        [Category("Buff.Smoke")]
        public void BuiltInCatalogHasUniqueIdsAndCachedHandlers()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            Assert.That(definitions, Is.Not.Empty, "内置 Buff 目录不能为空。");
            Assert.That(definitions, Has.Count.EqualTo(12), "拆分后必须保留全部内置 Buff。");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BuffDefinition definition in definitions)
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(ids.Add(definition.Id), Is.True, $"存在重复 Buff ID：{definition.Id}");

                foreach (BuffEffectDefinition effect in definition.Effects)
                {
                    Assert.That(
                        effect.IsHandlerCached,
                        Is.True,
                        $"Buff {definition.Id} 的效果未缓存处理器：{effect.TypeId}");
                }
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "补水", "潮湿", "出血", "光耀", "饥饿1.6", "饥饿2.0",
                    "禁锢(99)", "流血", "跑步", "生命恢复(1)", "失血", "水体减速"
                },
                ids,
                "Buff 分包迁移不得改变存档使用的稳定 ID。");
        }

        [UnityTest]
        [Category("Buff.Smoke")]
        public IEnumerator BuiltInCatalogLoadsThroughCrossPlatformCoroutine()
        {
            List<BuffDefinition> definitions = null;
            Exception loadError = null;

            yield return BuffCatalogLoader.LoadBuiltInDefinitionsAsync(
                result => definitions = result,
                exception => loadError = exception);

            Assert.That(loadError, Is.Null);
            Assert.That(definitions, Has.Count.EqualTo(12));
        }

        [Test]
        [Category("Buff.Smoke")]
        public void ManifestRejectsDuplicatePackagesAndTraversalPaths()
        {
            var duplicateManifest = new BuffManifestDto
            {
                Packages = new List<BuffPackageDto>
                {
                    new() { Id = "one", Path = "combat.json" },
                    new() { Id = "ONE", Path = "survival.json" }
                }
            };
            Assert.Throws<System.IO.InvalidDataException>(
                () => BuffCatalogLoader.ValidateManifest(duplicateManifest));

            var traversalManifest = new BuffManifestDto
            {
                Packages = new List<BuffPackageDto>
                {
                    new() { Id = "outside", Path = "../outside.json" }
                }
            };
            Assert.Throws<System.IO.InvalidDataException>(
                () => BuffCatalogLoader.ValidateManifest(traversalManifest));
        }

        [Test]
        [Category("Buff.Smoke")]
        public void PackagedStreamingAssetsUseWebRequestPath()
        {
            Assert.That(
                StreamingAssetsTextLoader.RequiresWebRequest(
                    "jar:file:///data/app/com.flatworld/base.apk!/assets/GameConfig/Buffs/buff-manifest.json"),
                Is.True);
        }
    }
}
