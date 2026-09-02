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

            AssertBurningVisualConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator.prefab");
            AssertBurningVisualConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator_AI.prefab");
            AssertRadianceLightConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator.prefab");
            AssertRadianceLightConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator_AI.prefab");
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

            AssertInfectionTintConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator.prefab");
            AssertInfectionTintConfiguration("Assets/2_Prefabs/Gameplay/Modules/Animation/Module_Animator_AI.prefab");
        }

        /// <summary>脱水 Buff 必须每秒扣除三点水分，并在重复获得时累加十秒时长。</summary>
        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void DehydrationBuffUsesStackingWaterLossDefinition()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            BuffDefinition dehydration = definitions.Find(
                definition => definition.Id == DehydrationBuffIds.Dehydration);

            Assert.That(dehydration, Is.Not.Null, "本体目录必须注册脱水 Buff。");
            Assert.That(dehydration.DurationSeconds, Is.EqualTo(10f));
            Assert.That(dehydration.TickIntervalSeconds, Is.EqualTo(1f));
            Assert.That(dehydration.StackMode, Is.EqualTo(BuffStackMode.ExtendDuration));
            Assert.That(dehydration.TickEffects.Count, Is.EqualTo(1));

            BuffEffectDefinition tickEffect = dehydration.TickEffects[0];
            Assert.That(tickEffect.TypeId, Is.EqualTo(BuffEffectTypeIds.NutritionChange));
            Assert.That(tickEffect.TargetId, Is.EqualTo("water"));
            Assert.That(tickEffect.Value, Is.EqualTo(-3f));
            Assert.That(tickEffect.IsHandlerCached, Is.True);
        }

        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void BuiltInBuffManifestContainsUniqueDefinitions()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.That(definitions, Has.Count.EqualTo(14));
            foreach (BuffDefinition definition in definitions)
                Assert.That(ids.Add(definition.Id), Is.True, $"本体 Buff 清单包含重复 ID：{definition.Id}");
        }

        [Test]
        [Category("Buff.Smoke")]
        [Category("Smoke")]
        public void MovementHungerIsNotRegisteredAsBuff()
        {
            List<BuffDefinition> definitions = BuffCatalogLoader.LoadBuiltInDefinitions();
            Assert.That(
                definitions.Exists(definition => definition.Id == "饥饿1.6"),
                Is.False,
                "移动饥饿必须由 Mover 的独立动作提供，不能重新注册为 Buff。");
            Assert.That(
                definitions.Exists(definition => definition.Id == "饥饿2.0"),
                Is.False,
                "奔跑饥饿必须由 Mover 的独立动作提供，不能重新注册为 Buff。");
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
