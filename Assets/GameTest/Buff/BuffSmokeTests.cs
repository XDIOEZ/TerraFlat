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
        }



    }
}
