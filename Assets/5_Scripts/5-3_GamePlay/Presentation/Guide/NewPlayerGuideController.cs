using System;
using System.Collections.Generic;
using FlatWorld.Dialogue;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using UnityEngine;

namespace FlatWorld.Guide
{
    /// <summary>
    /// 新角色生存引导：3 木棍、3 原木、1 树叶，依次完成钻木器、火种和火堆闭环。
    /// 本组件只贡献教程 Facts 并消费玩法成功事件，所有文字和显示时机由既有自言自语系统负责。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NewPlayerGuideController : MonoBehaviour, ICharacterSpeechContextContributor
    {
        #region 状态

        private Player actor;
        private Data_Player loadedPlayerData;
        private string loadedProfileId;
        private QuestManager questManager;
        private PlayerQuestRuntime questRuntime;
        private readonly HashSet<string> pendingMilestones = new(StringComparer.Ordinal);
        private bool isActiveForPlayer;
        private const string TrueFact = "True";
        private const string FalseFact = "False";

        public int ContextOrder => 200;
        public bool IsTutorialActive => isActiveForPlayer;
        public bool IsTutorialCompleted => isActiveForPlayer && CurrentStage == NewPlayerGuideStage.Completed;
        public NewPlayerGuideStage CurrentStage
        {
            get
            {
                if (questRuntime == null || !questRuntime.IsEnabled ||
                    !questRuntime.TryGetQuestStageState(NewPlayerGuideIds.QuestId, out QuestStatus status, out string stageId))
                    return NewPlayerGuideStage.OpenInventory;
                if (status == QuestStatus.Completed || status == QuestStatus.ReadyToClaim)
                    return NewPlayerGuideStage.Completed;
                return Enum.TryParse(stageId, out NewPlayerGuideStage stage)
                    ? stage
                    : NewPlayerGuideStage.OpenInventory;
            }
        }

        #endregion

        #region 生命周期

        private void Awake()
        {
            actor = GetComponentInParent<Player>();
        }

        private void OnEnable()
        {
            ResolveActor();
            if (actor != null)
                actor.ProfileContextChanged += HandleProfileContextChanged;

            questManager = QuestManager.Instance;
            if (questManager != null)
            {
                questManager.RuntimeReady += HandleQuestRuntimeReady;
                questManager.RuntimeRemoving += HandleQuestRuntimeRemoving;
            }

            GameplayProgressEvents.InventoryOpened += HandleInventoryOpened;
            GameplayProgressEvents.PickupSucceeded += HandlePickupSucceeded;
            GameplayProgressEvents.CraftSucceeded += HandleCraftSucceeded;
            GameplayProgressEvents.BuildingPlaced += HandleBuildingPlaced;
            GameplayProgressEvents.FireSeedCreated += HandleFireSeedCreated;
            GameplayProgressEvents.FurnaceIgnited += HandleFurnaceIgnited;
            RefreshEligibility();
        }

        private void Start()
        {
            RefreshEligibility();
        }

        private void OnDisable()
        {
            if (actor != null)
                actor.ProfileContextChanged -= HandleProfileContextChanged;

            if (questManager != null)
            {
                questManager.RuntimeReady -= HandleQuestRuntimeReady;
                questManager.RuntimeRemoving -= HandleQuestRuntimeRemoving;
            }
            questManager = null;
            questRuntime = null;

            GameplayProgressEvents.InventoryOpened -= HandleInventoryOpened;
            GameplayProgressEvents.PickupSucceeded -= HandlePickupSucceeded;
            GameplayProgressEvents.CraftSucceeded -= HandleCraftSucceeded;
            GameplayProgressEvents.BuildingPlaced -= HandleBuildingPlaced;
            GameplayProgressEvents.FireSeedCreated -= HandleFireSeedCreated;
            GameplayProgressEvents.FurnaceIgnited -= HandleFurnaceIgnited;
        }

        #endregion

        #region Fact 贡献

        public void Contribute(CharacterSpeechContext context)
        {
            // 资格变化由 ProfileContextChanged 与真实玩法事件刷新；语音轮询只读取缓存，禁止反复解析 ItemSpecialData JSON。
            NewPlayerGuideStage stage = CurrentStage;
            context.SetFact(
                CharacterSpeechFacts.TutorialEnabled,
                isActiveForPlayer ? TrueFact : FalseFact);
            context.SetFact(CharacterSpeechFacts.TutorialStage, GetStageFact(stage));
            context.SetFact(
                CharacterSpeechFacts.TutorialCompleted,
                stage == NewPlayerGuideStage.Completed ? TrueFact : FalseFact);
        }

        private static string GetStageFact(NewPlayerGuideStage stage)
        {
            return stage switch
            {
                NewPlayerGuideStage.OpenInventory => nameof(NewPlayerGuideStage.OpenInventory),
                NewPlayerGuideStage.GatherSurvivalMaterials => nameof(NewPlayerGuideStage.GatherSurvivalMaterials),
                NewPlayerGuideStage.CraftSparkMaker => nameof(NewPlayerGuideStage.CraftSparkMaker),
                NewPlayerGuideStage.PlaceSparkMaker => nameof(NewPlayerGuideStage.PlaceSparkMaker),
                NewPlayerGuideStage.CreateFireSeed => nameof(NewPlayerGuideStage.CreateFireSeed),
                NewPlayerGuideStage.CraftBonfire => nameof(NewPlayerGuideStage.CraftBonfire),
                NewPlayerGuideStage.PlaceBonfire => nameof(NewPlayerGuideStage.PlaceBonfire),
                NewPlayerGuideStage.IgniteBonfire => nameof(NewPlayerGuideStage.IgniteBonfire),
                NewPlayerGuideStage.Completed => nameof(NewPlayerGuideStage.Completed),
                _ => nameof(NewPlayerGuideStage.OpenInventory)
            };
        }

        #endregion

        #region 玩法事件

        private void HandleInventoryOpened(Player eventActor)
        {
            if (!CanHandle(eventActor))
                return;

            MarkMilestone(NewPlayerGuideIds.InventoryOpened);
            RefreshMaterialMilestone();
        }

        private void HandlePickupSucceeded(Player eventActor, string itemId)
        {
            if (!CanHandle(eventActor))
                return;

            if (IsSurvivalResource(itemId))
                RefreshMaterialMilestone();
        }

        private void HandleCraftSucceeded(Player eventActor, string outputItemId)
        {
            if (!CanHandle(eventActor))
                return;

            if (string.Equals(outputItemId, NewPlayerGuideIds.SparkMakerSummoner, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.SparkMakerCrafted);
            else if (string.Equals(outputItemId, NewPlayerGuideIds.BonfireSummoner, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.BonfireCrafted);
        }

        private void HandleBuildingPlaced(Player eventActor, string buildingId)
        {
            if (!CanHandle(eventActor))
                return;

            if (string.Equals(buildingId, NewPlayerGuideIds.SparkMaker, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.SparkMakerPlaced);
            else if (string.Equals(buildingId, NewPlayerGuideIds.Bonfire, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.BonfirePlaced);
        }

        private void HandleFireSeedCreated(Player eventActor, string itemId)
        {
            if (CanHandle(eventActor) &&
                string.Equals(itemId, NewPlayerGuideIds.FireSeed, StringComparison.Ordinal))
            {
                MarkMilestone(NewPlayerGuideIds.FireSeedCreated);
            }
        }

        private void HandleFurnaceIgnited(Player eventActor, string furnaceId)
        {
            if (CanHandle(eventActor) &&
                string.Equals(furnaceId, NewPlayerGuideIds.Bonfire, StringComparison.Ordinal))
            {
                MarkMilestone(NewPlayerGuideIds.BonfireIgnited);
            }
        }

        #endregion

        #region 资格与进度

        private void HandleProfileContextChanged()
        {
            RefreshEligibility();
        }

        private void HandleQuestRuntimeReady(Player player, PlayerQuestRuntime runtime)
        {
            if (ReferenceEquals(actor, player))
            {
                RefreshEligibility();
                RefreshMaterialMilestone();
            }
        }

        private void HandleQuestRuntimeRemoving(Player player)
        {
            if (ReferenceEquals(actor, player))
                questRuntime = null;
        }

        private void ResolveActor()
        {
            actor ??= GetComponentInParent<Player>();
        }

        private void RefreshEligibility()
        {
            ResolveActor();
            if (actor == null || !actor.IsLocalProfile || actor.Data == null)
            {
                isActiveForPlayer = false;
                questRuntime = null;
                loadedPlayerData = null;
                loadedProfileId = null;
                pendingMilestones.Clear();
                return;
            }

            bool profileChanged = !ReferenceEquals(loadedPlayerData, actor.Data) ||
                                  !string.Equals(loadedProfileId, actor.ProfileId, StringComparison.Ordinal);
            if (profileChanged || !isActiveForPlayer)
            {
                pendingMilestones.Clear();
                questRuntime = null;
                isActiveForPlayer = actor.IsNewProfile || NewPlayerGuideProgressStore.HasEligibility(actor.Data);
                if (!isActiveForPlayer)
                {
                    loadedPlayerData = null;
                    loadedProfileId = null;
                    return;
                }

                loadedPlayerData = actor.Data;
                loadedProfileId = actor.ProfileId;
                if (actor.IsNewProfile)
                    NewPlayerGuideProgressStore.EstablishEligibility(actor.Data);
                BindQuestRuntimeWhenReady();
                RefreshMaterialMilestone();
                return;
            }

            BindQuestRuntimeWhenReady();
        }

        // 只绑定管理器已有的运行时，等待期间的里程碑仅暂存于当前角色。
        private void BindQuestRuntimeWhenReady()
        {
            if (questRuntime != null && questRuntime.IsEnabled &&
                ReferenceEquals(questRuntime.PlayerData, loadedPlayerData))
                return;
            questRuntime = null;
            if (questManager == null || !questManager.TryGetRuntime(actor, out PlayerQuestRuntime runtime) ||
                !runtime.IsEnabled || !ReferenceEquals(runtime.PlayerData, loadedPlayerData))
                return;

            if (!runtime.TryGetQuestStageState(NewPlayerGuideIds.QuestId, out _, out _) &&
                !runtime.AcceptQuest(NewPlayerGuideIds.QuestId, out string error))
            {
                Debug.LogError($"[Guide] 无法启动教程任务：{error}");
                return;
            }

            questRuntime = runtime;
            foreach (string milestone in pendingMilestones)
                PublishMilestoneToQuestRuntime(milestone);
            pendingMilestones.Clear();
        }

        private bool CanHandle(Player eventActor)
        {
            RefreshEligibility();
            return isActiveForPlayer && ReferenceEquals(actor, eventActor);
        }

        private void MarkMilestone(string milestoneId)
        {
            if (questRuntime?.IsCompleted(NewPlayerGuideIds.QuestId) == true)
                return;
            if (questRuntime == null)
                pendingMilestones.Add(milestoneId);
            else
                PublishMilestoneToQuestRuntime(milestoneId);
        }

        private void PublishMilestoneToQuestRuntime(string milestoneId)
        {
            questRuntime.HandleSignal(new GameplayProgressSignal(
                actor, NewPlayerGuideIds.MilestoneSignal, milestoneId, 1f));
        }

        #endregion

        #region 资源统计

        private void RefreshMaterialMilestone()
        {
            if (!isActiveForPlayer || actor?.itemMods == null ||
                questRuntime?.IsCompleted(NewPlayerGuideIds.QuestId) == true)
                return;

            float stickAmount = QuestInventoryAccess.CountItem(actor, NewPlayerGuideIds.StickWood, StringComparison.Ordinal);
            float logAmount = QuestInventoryAccess.CountItem(actor, NewPlayerGuideIds.Log, StringComparison.Ordinal);
            float leafAmount = QuestInventoryAccess.CountItem(actor, NewPlayerGuideIds.Leaf, StringComparison.Ordinal);
            if (stickAmount >= NewPlayerGuideIds.RequiredStickAmount &&
                logAmount >= NewPlayerGuideIds.RequiredLogAmount &&
                leafAmount >= NewPlayerGuideIds.RequiredLeafAmount)
            {
                MarkMilestone(NewPlayerGuideIds.SurvivalMaterialsGathered);
            }
        }

        private static bool IsSurvivalResource(string itemId)
        {
            return string.Equals(itemId, NewPlayerGuideIds.StickWood, StringComparison.Ordinal) ||
                   string.Equals(itemId, NewPlayerGuideIds.Log, StringComparison.Ordinal) ||
                   string.Equals(itemId, NewPlayerGuideIds.Leaf, StringComparison.Ordinal);
        }

        #endregion
    }
}
