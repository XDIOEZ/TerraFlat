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
            AssertRadianceLightConfiguration("Assets/2_Prefabs/Module/Module_Animator.prefab");
            AssertRadianceLightConfiguration("Assets/2_Prefabs/Module/Animator/Module_Animator_AI.prefab");
        }

        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void InfectionBuffUsesTimedTrueDamageAndSharedActorTint()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            BuffDefinition infection = definitions.Find(
                definition => definition.Id == InfectionBuffIds.Infection);

            Assert.That(infection, Is.Not.Null, "本体目录必须注册感染 Buff。");
            Assert.That(infection.DurationSeconds, Is.EqualTo(30f));
            Assert.That(infection.TickIntervalSeconds, Is.EqualTo(1f));
            Assert.That(infection.StackMode, Is.EqualTo(BuffStackMode.RefreshDuration));
            Assert.That(infection.TickEffects.Count, Is.EqualTo(1));
            Assert.That(infection.TickEffects[0].TypeId, Is.EqualTo(BuffEffectTypeIds.TrueDamage));
            Assert.That(infection.TickEffects[0].Value, Is.EqualTo(1f));
            Assert.That(infection.TickEffects[0].IsHandlerCached, Is.True);

            AssertInfectionTintConfiguration("Assets/2_Prefabs/Module/Module_Animator.prefab");
            AssertInfectionTintConfiguration("Assets/2_Prefabs/Module/Animator/Module_Animator_AI.prefab");
        }

        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void FreshWaterCapabilityBuffsArePermanentAndEffectFree()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            foreach (string id in new[] { FreshWaterBuffIds.Clean, FreshWaterBuffIds.Dirty })
            {
                BuffDefinition definition = definitions.Find(candidate => candidate.Id == id);
                Assert.That(definition, Is.Not.Null, $"本体目录必须注册淡水能力 Buff：{id}");
                Assert.That(definition.DurationSeconds, Is.Null, $"{id} 必须由地块进入/离开控制生命周期。");
                Assert.That(definition.StartEffects, Is.Empty);
                Assert.That(definition.TickEffects, Is.Empty);
                Assert.That(definition.StopEffects, Is.Empty);
            }
        }

        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void RunningBuffHalvesOnlyItsHydrationDrainImpact()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            BuffDefinition running = definitions.Find(definition => definition.Id == "饥饿2.0");

            Assert.That(running, Is.Not.Null, "本体目录必须注册奔跑消耗 Buff。");
            Assert.That(running.StartEffects.Count, Is.EqualTo(2));
            Assert.That(running.StopEffects.Count, Is.EqualTo(2));
            Assert.That(
                FindEffect(running.StartEffects, BuffEffectTypeIds.FoodConsumeSpeedMultiplier).Value,
                Is.EqualTo(2f),
                "奔跑对其他营养的原有效果必须保持不变。");
            Assert.That(
                FindEffect(running.StartEffects, BuffEffectTypeIds.WaterConsumeSpeedMultiplier).Value,
                Is.EqualTo(0.5f),
                "奔跑时的水分消耗必须在原结果上减半。");
            Assert.That(
                FindEffect(running.StopEffects, BuffEffectTypeIds.WaterConsumeSpeedMultiplier).Value,
                Is.EqualTo(2f),
                "停止奔跑时必须准确还原水分倍率。");
        }

        private static BuffEffectDefinition FindEffect(
            IReadOnlyList<BuffEffectDefinition> effects,
            string typeId)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].TypeId == typeId)
                    return effects[i];
            }

            Assert.Fail($"找不到 Buff 效果：{typeId}");
            return null;
        }

        /// <summary>玩家和 AI 的动画模块都必须装配 Buff 光照观察者。</summary>
        private static void AssertRadianceLightConfiguration(string prefabPath)
        {
            GameObject animatorModule = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(animatorModule, Is.Not.Null, $"找不到动画模块 Prefab：{prefabPath}");
            Assert.That(
                animatorModule.GetComponent<ActorBuffLightController>(),
                Is.Not.Null,
                $"{prefabPath} 必须装配光耀 Buff 的 2D 光照控制单元。");
        }

        /// <summary>玩家和 AI 共用动画模块必须同时具备 Buff 观察者与统一角色染色模块。</summary>
        private static void AssertInfectionTintConfiguration(string prefabPath)
        {
            GameObject animatorModule = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(animatorModule, Is.Not.Null, $"找不到动画模块 Prefab：{prefabPath}");
            Assert.That(
                animatorModule.GetComponent<ActorBuffLightController>(),
                Is.Not.Null,
                $"{prefabPath} 必须装配感染 Buff 观察者。");
            Assert.That(
                animatorModule.GetComponent<ActorRenderColorEffect>(),
                Is.Not.Null,
                $"{prefabPath} 必须装配角色统一染色模块。");
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
