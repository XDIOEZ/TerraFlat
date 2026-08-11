using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 玩家任务进度在 ItemSpecialData 中的独立命名空间存储；不修改 MemoryPack 主模型布局。
    /// 未知任务和未知 JSON 字段会被保留，便于暂时移除 MOD 后重新启用以及未来向前兼容。
    /// </summary>
    public static class QuestProgressStore
    {
        #region 常量

        public const string NamespaceKey = "flatworld.quests";
        public const int CurrentVersion = 1;

        #endregion

        #region 读写

        public static QuestProgressSaveDocument Load(Data_Player playerData)
        {
            if (playerData == null)
                throw new ArgumentNullException(nameof(playerData));

            JObject namespaceData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
            QuestProgressSaveDocument document = namespaceData.HasValues
                ? namespaceData.ToObject<QuestProgressSaveDocument>()
                : new QuestProgressSaveDocument();
            document ??= new QuestProgressSaveDocument();

            if (document.Version <= 0)
                document.Version = CurrentVersion;
            if (document.Version > CurrentVersion)
            {
                throw new InvalidDataException(
                    $"任务进度版本 {document.Version} 高于当前支持版本 {CurrentVersion}，已停止写入以保护存档");
            }

            document.Quests = document.Quests != null
                ? new Dictionary<string, QuestProgressSaveRecord>(document.Quests, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, QuestProgressSaveRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestProgressSaveRecord record in document.Quests.Values)
            {
                if (record == null)
                    continue;
                record.ObjectiveProgress = record.ObjectiveProgress != null
                    ? new Dictionary<string, float>(record.ObjectiveProgress, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            return document;
        }

        public static void Save(Data_Player playerData, QuestProgressSaveDocument document)
        {
            if (playerData == null)
                throw new ArgumentNullException(nameof(playerData));
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            document.Version = CurrentVersion;
            ItemSpecialDataJsonStore.WriteNamespace(
                playerData,
                NamespaceKey,
                JObject.FromObject(document));
        }

        #endregion
    }

    /// <summary>任务进度命名空间根对象。</summary>
    [Serializable]
    public sealed class QuestProgressSaveDocument
    {
        [JsonProperty("version")]
        public int Version = QuestProgressStore.CurrentVersion;
        [JsonProperty("quests")]
        public Dictionary<string, QuestProgressSaveRecord> Quests =
            new(StringComparer.OrdinalIgnoreCase);

        [JsonExtensionData] public IDictionary<string, JToken> ExtensionData;
    }

    /// <summary>单个任务的玩家进度记录。</summary>
    [Serializable]
    public sealed class QuestProgressSaveRecord
    {
        [JsonProperty("definitionVersion")]
        public int DefinitionVersion = 1;
        [JsonProperty("status")]
        [JsonConverter(typeof(StringEnumConverter))]
        public QuestStatus Status = QuestStatus.Active;
        [JsonProperty("currentStageId")]
        public string CurrentStageId;
        [JsonProperty("objectiveProgress")]
        public Dictionary<string, float> ObjectiveProgress =
            new(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("completionCount")]
        public int CompletionCount;
        [JsonProperty("rewardsClaimed")]
        public bool RewardsClaimed;

        [JsonExtensionData] public IDictionary<string, JToken> ExtensionData;
    }
}
