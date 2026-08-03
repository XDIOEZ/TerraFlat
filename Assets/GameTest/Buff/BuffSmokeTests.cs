using System;
using System.Collections.Generic;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;

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
                "Assets/StreamingAssets/GameConfig/Buffs/buffs.json");
            GameTestAssertions.AssertAssetExists(
                "Assets/2_Prefabs/Module/Manager/Module_BuffManager.prefab");
        }

        [Test]
        [Category("Buff.Smoke")]
        public void BuiltInCatalogHasUniqueIdsAndCachedHandlers()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            Assert.That(definitions, Is.Not.Empty, "内置 Buff 目录不能为空。");

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
        }
    }
}
