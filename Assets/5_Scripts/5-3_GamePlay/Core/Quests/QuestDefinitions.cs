using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 任务系统的纯数据定义；配置只描述条件、阶段、目标与奖励，具体行为由可注册处理器解释。
    /// definitionVersion 用于配置升级，现阶段任务为一次性任务，进度按玩家独立保存。
    /// </summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestDefinition
    {
        public string Id;
        public int DefinitionVersion = 1;
        public string TitleKey;
        public string Title;
        public string DescriptionKey;
        public string Description;
        /// <summary>仅供开发者工具显式开启；稳定化流程永远不会自动接取。</summary>
        public bool DebugOnly;
        /// <summary>隐藏内部任务，不改变任务自身的运行与查询。</summary>
        public bool HiddenFromJournal;
        /// <summary>允许信号提前记录后续阶段的事件型目标。</summary>
        public bool TrackFutureStageSignals;
        public string AcceptMode = QuestModes.Manual;
        public string TurnInMode = QuestModes.Manual;
        public List<QuestConditionDefinition> Conditions = new();
        public List<QuestStageDefinition> Stages = new();
        public List<QuestRewardDefinition> Rewards = new();

        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public string SourceModId;
        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public string SourceFile;
        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public int SourceIndex;
    }

    /// <summary>任务阶段；completionMode 支持 all 或 any。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestStageDefinition
    {
        public string Id;
        public string CompletionMode = QuestCompletionModes.All;
        public List<QuestObjectiveDefinition> Objectives = new();
    }

    /// <summary>任务目标；parameters 由对应类型的目标处理器验证和读取。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestObjectiveDefinition
    {
        public string Id;
        public string Type;
        public string LabelKey;
        public string Label;
        public float Required = 1f;
        [MemoryPack.MemoryPackAllowSerialize]
        public JObject Parameters = new();
    }

    /// <summary>任务接取条件；parameters 由对应类型的条件处理器验证和读取。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestConditionDefinition
    {
        public string Type;
        [MemoryPack.MemoryPackAllowSerialize]
        public JObject Parameters = new();
    }

    /// <summary>任务奖励；所有处理器先生成计划，再由任务运行时一次性提交。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestRewardDefinition
    {
        public string Id;
        public string Type;
        [MemoryPack.MemoryPackAllowSerialize]
        public JObject Parameters = new();
    }

    /// <summary>内建任务清单。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestManifestDto
    {
        public int SchemaVersion = QuestCatalog.SupportedSchemaVersion;
        public List<QuestPackageDto> Packages = new();
    }

    /// <summary>内建任务分包入口。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestPackageDto
    {
        public string Id;
        public string Path;
        public bool Enabled = true;
    }

    /// <summary>任务分包内容；MOD 定义文件也复用 Quests 数组的数据结构。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class QuestCatalogDto
    {
        public int SchemaVersion = QuestCatalog.SupportedSchemaVersion;
        public List<QuestDefinition> Quests = new();
    }

    /// <summary>已接取任务的持久化状态。</summary>
    public enum QuestStatus
    {
        Active,
        ReadyToClaim,
        Completed
    }

    /// <summary>任务状态快照；UI、日志和测试只读取快照，不直接修改存档记录。</summary>
    public sealed class QuestSnapshot
    {
        public string QuestId { get; set; }
        public string Title { get; set; }
        public QuestStatus Status { get; set; }
        public string CurrentStageId { get; set; }
        public IReadOnlyDictionary<string, float> ObjectiveProgress { get; set; }
    }

    /// <summary>任务配置的稳定字符串常量。</summary>
    public static class QuestModes
    {
        public const string Manual = "manual";
        public const string Auto = "auto";
    }

    /// <summary>阶段完成方式。</summary>
    public static class QuestCompletionModes
    {
        public const string All = "all";
        public const string Any = "any";
    }
}
