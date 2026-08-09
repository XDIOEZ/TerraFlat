using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.Buff
{
    /// <summary>Buff 基础冒烟测试：保护核心入口、内置目录与效果处理器绑定。</summary>
    public sealed class BuffSmokeTests
    {



        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void BurningBuffUsesTimedTrueDamageDefinition()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            BuffDefinition burning = definitions.Find(
                definition => definition.Id == BurningBuffIds.Burning);

            Assert.That(burning, Is.Not.Null, "本体目录必须注册燃烧 Buff。");
            Assert.That(burning.DurationSeconds, Is.EqualTo(5f));
            Assert.That(burning.TickIntervalSeconds, Is.EqualTo(1f));
            Assert.That(burning.StackMode, Is.EqualTo(BuffStackMode.RefreshDuration));
            Assert.That(burning.TickEffects.Count, Is.EqualTo(1));

            BuffEffectDefinition tickEffect = burning.TickEffects[0];
            Assert.That(tickEffect.TypeId, Is.EqualTo(BuffEffectTypeIds.TrueDamage));
            Assert.That(tickEffect.Value, Is.EqualTo(1f));
            Assert.That(tickEffect.IsHandlerCached, Is.True);

            AssertBurningVisualConfiguration("Assets/2_Prefabs/Module/Module_Animator.prefab");
            AssertBurningVisualConfiguration("Assets/2_Prefabs/Module/Animator/Module_Animator_AI.prefab");
        }

        /// <summary>玩家和 AI 共享的动画模块都必须持有完整燃烧序列，避免 Buff 生效但角色无提示。</summary>
        private static void AssertBurningVisualConfiguration(string prefabPath)
        {
            GameObject animatorModule = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(animatorModule, Is.Not.Null, $"找不到动画模块 Prefab：{prefabPath}");

            ActorStatusVisualEffectController visualController =
                animatorModule.GetComponent<ActorStatusVisualEffectController>();
            Assert.That(visualController, Is.Not.Null, $"{prefabPath} 必须装配角色状态视觉控制器。");
            Assert.That(
                visualController.IsStatusVisualConfigured(BurningBuffIds.Burning),
                Is.True,
                $"{prefabPath} 必须配置燃烧状态表现。");
            Assert.That(
                visualController.GetStatusVisualFrameCount(BurningBuffIds.Burning),
                Is.EqualTo(8),
                $"{prefabPath} 的燃烧状态表现必须完整引用八帧火焰图。");
        }


    }
}
