using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Guide
{
    /// <summary>
    /// 将新手引导幂等里程碑保存到 flatworld.tutorial 命名空间，并归一化推导当前阶段。
    /// </summary>
    public sealed class NewPlayerGuideProgressStore
    {
        #region 常量与状态

        public const string TutorialNamespace = "flatworld.tutorial";
        public const int CurrentVersion = 1;

        private const string VersionProperty = "version";
        private const string EligibleProperty = "eligible";
        private const string MilestonesProperty = "milestones";
        private const string StageProperty = "stage";
        private const string CompletedProperty = "completed";

        private readonly Data_Player playerData;
        private readonly HashSet<string> milestones = new(StringComparer.Ordinal);
        private JObject namespaceData;
        private bool isEligible;

        public bool IsEligible => isEligible;
        public NewPlayerGuideStage CurrentStage => DeriveStage(milestones);
        public bool IsCompleted => CurrentStage == NewPlayerGuideStage.Completed;
        public IReadOnlyCollection<string> Milestones => milestones;

        #endregion

        public NewPlayerGuideProgressStore(Data_Player playerData, bool establishEligibility = false)
        {
            this.playerData = playerData ?? throw new ArgumentNullException(nameof(playerData));
            Load();
            if (establishEligibility && !isEligible)
            {
                isEligible = true;
                Save();
            }
        }

        #region 查询与推进

        public bool HasMilestone(string milestoneId)
        {
            return !string.IsNullOrWhiteSpace(milestoneId) && milestones.Contains(milestoneId);
        }

        public bool MarkMilestone(string milestoneId)
        {
            if (!IsKnownMilestone(milestoneId) || !milestones.Add(milestoneId))
                return false;

            Save();
            return true;
        }

        public static bool HasEligibility(Data_Player playerData)
        {
            return playerData != null &&
                   ItemSpecialDataJsonStore
                       .ReadNamespace(playerData, TutorialNamespace)
                       .Value<bool?>(EligibleProperty) == true;
        }

        public static NewPlayerGuideStage DeriveStage(ISet<string> completedMilestones)
        {
            if (!Has(completedMilestones, NewPlayerGuideIds.InventoryOpened))
                return NewPlayerGuideStage.OpenInventory;
            if (!Has(completedMilestones, NewPlayerGuideIds.SurvivalMaterialsGathered))
                return NewPlayerGuideStage.GatherSurvivalMaterials;
            if (!Has(completedMilestones, NewPlayerGuideIds.SparkMakerCrafted))
                return NewPlayerGuideStage.CraftSparkMaker;
            if (!Has(completedMilestones, NewPlayerGuideIds.SparkMakerPlaced))
                return NewPlayerGuideStage.PlaceSparkMaker;
            if (!Has(completedMilestones, NewPlayerGuideIds.FireSeedCreated))
                return NewPlayerGuideStage.CreateFireSeed;
            if (!Has(completedMilestones, NewPlayerGuideIds.BonfireCrafted))
                return NewPlayerGuideStage.CraftBonfire;
            if (!Has(completedMilestones, NewPlayerGuideIds.BonfirePlaced))
                return NewPlayerGuideStage.PlaceBonfire;
            if (!Has(completedMilestones, NewPlayerGuideIds.BonfireIgnited))
                return NewPlayerGuideStage.IgniteBonfire;
            return NewPlayerGuideStage.Completed;
        }

        #endregion

        #region 持久化

        private void Load()
        {
            namespaceData = ItemSpecialDataJsonStore.ReadNamespace(playerData, TutorialNamespace);
            milestones.Clear();
            isEligible = namespaceData.Value<bool?>(EligibleProperty) == true;

            if (namespaceData[MilestonesProperty] is JArray milestoneArray)
            {
                for (int i = 0; i < milestoneArray.Count; i++)
                {
                    string milestoneId = milestoneArray[i]?.Value<string>();
                    if (IsKnownMilestone(milestoneId))
                        milestones.Add(milestoneId);
                }
            }

            if (namespaceData.Value<bool?>(CompletedProperty) == true)
            {
                for (int i = 0; i < NewPlayerGuideIds.OrderedMilestones.Length; i++)
                    milestones.Add(NewPlayerGuideIds.OrderedMilestones[i]);
            }
        }

        private void Save()
        {
            List<string> orderedMilestones = new();
            for (int i = 0; i < NewPlayerGuideIds.OrderedMilestones.Length; i++)
            {
                string milestoneId = NewPlayerGuideIds.OrderedMilestones[i];
                if (milestones.Contains(milestoneId))
                    orderedMilestones.Add(milestoneId);
            }

            namespaceData ??= new JObject();
            namespaceData[VersionProperty] = CurrentVersion;
            namespaceData[EligibleProperty] = isEligible;
            namespaceData[MilestonesProperty] = JArray.FromObject(orderedMilestones);
            namespaceData[StageProperty] = CurrentStage.ToString();
            namespaceData[CompletedProperty] = IsCompleted;
            ItemSpecialDataJsonStore.WriteNamespace(playerData, TutorialNamespace, namespaceData);
        }

        #endregion

        #region 校验

        private static bool Has(ISet<string> completedMilestones, string milestoneId)
        {
            return completedMilestones != null && completedMilestones.Contains(milestoneId);
        }

        private static bool IsKnownMilestone(string milestoneId)
        {
            if (string.IsNullOrWhiteSpace(milestoneId))
                return false;

            for (int i = 0; i < NewPlayerGuideIds.OrderedMilestones.Length; i++)
            {
                if (string.Equals(
                        NewPlayerGuideIds.OrderedMilestones[i],
                        milestoneId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
