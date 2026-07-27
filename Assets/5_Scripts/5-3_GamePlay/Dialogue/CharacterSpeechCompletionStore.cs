using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 使用玩家现有 ItemSpecialData 保存一次性台词完成标记，避免改变 MemoryPack 字段布局。
    /// </summary>
    internal sealed class CharacterSpeechCompletionStore
    {
        private const string DialogueNamespace = "flatworld.dialogue";
        private const string CompletedProperty = "completed";
        private const string LegacyProperty = "flatworld.legacyItemSpecialData";

        private readonly Component owner;
        private readonly HashSet<string> completed = new(StringComparer.Ordinal);
        private Data_Player loadedPlayerData;

        public CharacterSpeechCompletionStore(Component owner)
        {
            this.owner = owner;
        }

        #region 查询与写入

        public bool IsCompleted(string completionFlag)
        {
            if (string.IsNullOrWhiteSpace(completionFlag))
                return false;

            EnsureLoaded();
            return completed.Contains(completionFlag);
        }

        public void MarkCompleted(string completionFlag)
        {
            if (string.IsNullOrWhiteSpace(completionFlag))
                return;

            EnsureLoaded();
            if (loadedPlayerData == null || !completed.Add(completionFlag))
                return;

            JObject root = ReadRoot(loadedPlayerData.ItemSpecialData, preserveLegacy: true);
            JObject dialogue = root[DialogueNamespace] as JObject ?? new JObject();
            root[DialogueNamespace] = dialogue;

            List<string> orderedFlags = new(completed);
            orderedFlags.Sort(StringComparer.Ordinal);
            dialogue[CompletedProperty] = JArray.FromObject(orderedFlags);
            loadedPlayerData.ItemSpecialData = root.ToString(Formatting.None);
        }

        #endregion

        #region 数据解析

        private void EnsureLoaded()
        {
            Data_Player playerData = ResolvePlayerData();
            if (ReferenceEquals(playerData, loadedPlayerData))
                return;

            loadedPlayerData = playerData;
            completed.Clear();
            if (loadedPlayerData == null)
                return;

            JObject root = ReadRoot(loadedPlayerData.ItemSpecialData, preserveLegacy: false);
            if (root[DialogueNamespace] is not JObject dialogue ||
                dialogue[CompletedProperty] is not JArray completedArray)
            {
                return;
            }

            for (int i = 0; i < completedArray.Count; i++)
            {
                string completionFlag = completedArray[i]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(completionFlag))
                    completed.Add(completionFlag);
            }
        }

        private Data_Player ResolvePlayerData()
        {
            Item actorItem = owner != null ? owner.GetComponentInParent<Item>() : null;
            return actorItem?.itemData as Data_Player;
        }

        private static JObject ReadRoot(string rawData, bool preserveLegacy)
        {
            if (string.IsNullOrWhiteSpace(rawData))
                return new JObject();

            try
            {
                JToken token = JToken.Parse(rawData);
                if (token is JObject root)
                    return root;
            }
            catch (JsonException)
            {
                // 旧数据可能不是 JSON；写入时原样保存在独立字段中。
            }

            JObject fallback = new();
            if (preserveLegacy)
                fallback[LegacyProperty] = rawData;
            return fallback;
        }

        #endregion
    }
}
