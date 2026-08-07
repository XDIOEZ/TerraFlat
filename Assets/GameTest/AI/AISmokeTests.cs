using FlatWorld.GameTest.Shared;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.AI
{
    /// <summary>AI 基础冒烟测试：保护状态机、感知和生物资源入口。</summary>
    public sealed class AISmokeTests
    {
        private enum AdvanceTestState
        {
            Advance
        }







        [Test]
        [Category("AI.StateMachine")]
        public void AdvanceNodeMovesUntilArrivalAndNotifiesOnce()
        {
            Vector3 actorPosition = Vector3.zero;
            Vector3 targetPosition = new(3f, 0f, 0f);
            int moveCount = 0;
            int stopCount = 0;
            int arrivalCount = 0;
            AIAdvanceStateNode<AdvanceTestState> node = new(
                AdvanceTestState.Advance,
                () => new AIAdvanceTarget(true, targetPosition),
                () => actorPosition,
                () => 0.1f,
                target =>
                {
                    moveCount++;
                    actorPosition = Vector3.MoveTowards(actorPosition, target, 1f);
                },
                () => stopCount++,
                () => arrivalCount++);

            node.Enter();
            for (int i = 0; i < 5; i++)
                node.Tick(1f);

            Assert.That(node.AnimationRole, Is.EqualTo(AIStateAnimationRole.Moving));
            Assert.That(actorPosition, Is.EqualTo(targetPosition));
            Assert.That(moveCount, Is.EqualTo(3));
            Assert.That(stopCount, Is.EqualTo(2));
            Assert.That(arrivalCount, Is.EqualTo(1));
        }

        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WolfPrefabAcceptsReusableAdvanceCommands()
        {
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wolfPrefab, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolf, Is.InstanceOf<IAIAdvanceCommandReceiver>());
            Assert.That((int)WolfState.Advance, Is.EqualTo(7), "推进状态必须追加在末尾，避免破坏旧存档枚举值。");
        }

    }
}
