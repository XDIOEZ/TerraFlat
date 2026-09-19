using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 无 Prefab 依赖的对话事实扩展注册表；每次调度器启动创建独立观察者，
    /// 便于本体与 MOD 接入有状态事实而不把玩法订阅堆入通用调度器。
    /// </summary>
    public static class CharacterSpeechContributorRegistry
    {
        #region 注册与实例化

        private static readonly SortedDictionary<string, Func<ICharacterSpeechContextContributor>> factories = new();

        /// <summary>按稳定标识注册或替换工厂，不共享角色间的观察状态。</summary>
        public static void Register(string id, Func<ICharacterSpeechContextContributor> factory)
        {
            if (string.IsNullOrWhiteSpace(id) || factory == null)
                throw new ArgumentException("对话事实扩展必须具有标识与工厂。");
            factories[id] = factory;
        }

        /// <summary>MOD 卸载时撤销注册；活动角色可调用 RebuildExtensions 重建。</summary>
        public static void Unregister(string id) => factories.Remove(id);

        /// <summary>为当前角色创建所有注册扩展。</summary>
        public static void AppendTo(List<ICharacterSpeechContextContributor> contributors)
        {
            foreach (Func<ICharacterSpeechContextContributor> factory in factories.Values)
                contributors.Add(factory());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => factories.Clear();

        #endregion
    }
}
