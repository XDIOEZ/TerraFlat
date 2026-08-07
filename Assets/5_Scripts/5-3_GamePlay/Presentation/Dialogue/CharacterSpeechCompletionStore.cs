using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
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

            JObject dialogue = ItemSpecialDataJsonStore.ReadNamespace(loadedPlayerData, DialogueNamespace);

            List<string> orderedFlags = new(completed);
            orderedFlags.Sort(StringComparer.Ordinal);
            dialogue[CompletedProperty] = JArray.FromObject(orderedFlags);
            ItemSpecialDataJsonStore.WriteNamespace(loadedPlayerData, DialogueNamespace, dialogue);
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

            JObject dialogue = ItemSpecialDataJsonStore.ReadNamespace(loadedPlayerData, DialogueNamespace);
            if (dialogue[CompletedProperty] is not JArray completedArray)
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

        #endregion
    }
}
