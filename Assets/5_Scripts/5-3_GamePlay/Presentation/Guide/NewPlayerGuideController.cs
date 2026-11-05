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
    public sealed class NewPlayerGuideController : MonoBehaviour, ICharacterSpeechContextContributor,
        ICharacterSpeechStateChangeSource
    {
        #region 状态

        private Item actor;
        private ItemData loadedPlayerData;
        private string loadedProfileId;
        private QuestManager questManager;
        private PlayerQuestRuntime questRuntime;
        private readonly HashSet<string> pendingMilestones = new(StringComparer.Ordinal);
        private bool isActiveForPlayer;
        private CharacterSoliloquyController observingController;
        private bool observedEligibility;
        private NewPlayerGuideStage observedStage;
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

        public void BeginObserving(CharacterSoliloquyController controller)
        {
            EndObserving();
            observingController = controller;
            observedEligibility = isActiveForPlayer;
            observedStage = CurrentStage;
            if (questRuntime != null)
                questRuntime.QuestChanged += HandleQuestChanged;
        }

        public void EndObserving()
        {
            if (questRuntime != null)
                questRuntime.QuestChanged -= HandleQuestChanged;
            observingController = null;
        }

        private void HandleQuestChanged(QuestSnapshot snapshot)
        {
            if (snapshot != null && string.Equals(snapshot.QuestId, NewPlayerGuideIds.QuestId, StringComparison.Ordinal))
                NotifyIfGuideFactsChanged();
        }

        private void NotifyIfGuideFactsChanged()
        {
            if (observingController == null)
                return;
            NewPlayerGuideStage stage = CurrentStage;
            if (observedEligibility == isActiveForPlayer && observedStage == stage)
                return;
            observedEligibility = isActiveForPlayer;
            observedStage = stage;
            observingController.NotifySpeechStateChanged();
        }

        #endregion

        #region 生命周期

        private void Awake()
        {
            actor = GetComponentInParent<Item>();
        }

        private void OnEnable()
        {
            ResolveActor();
            if (actor != null)
                ActorProfileRegistry.GetBinding(actor).Changed += HandleProfileContextChanged;

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
            EndObserving();
            if (actor != null)
                ActorProfileRegistry.GetBinding(actor).Changed -= HandleProfileContextChanged;

            if (questManager != null)
            {
                questManager.RuntimeReady -= HandleQuestRuntimeReady;
                questManager.RuntimeRemoving -= HandleQuestRuntimeRemoving;
            }
            questManager = null;
            UnbindQuestRuntime();

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

        private void HandleInventoryOpened(Item eventActor)
        {
            if (!CanHandle(eventActor))
                return;

            MarkMilestone(NewPlayerGuideIds.InventoryOpened);
            RefreshMaterialMilestone();
        }

        private void HandlePickupSucceeded(Item eventActor, string itemId)
        {
            if (!CanHandle(eventActor))
                return;

            if (IsSurvivalResource(itemId))
                RefreshMaterialMilestone();
        }

        private void HandleCraftSucceeded(Item eventActor, string outputItemId)
        {
            if (!CanHandle(eventActor))
                return;

            if (string.Equals(outputItemId, NewPlayerGuideIds.SparkMakerSummoner, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.SparkMakerCrafted);
            else if (string.Equals(outputItemId, NewPlayerGuideIds.BonfireSummoner, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.BonfireCrafted);
        }

        private void HandleBuildingPlaced(Item eventActor, string buildingId)
        {
            if (!CanHandle(eventActor))
                return;

            if (string.Equals(buildingId, NewPlayerGuideIds.SparkMaker, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.SparkMakerPlaced);
            else if (string.Equals(buildingId, NewPlayerGuideIds.Bonfire, StringComparison.Ordinal))
                MarkMilestone(NewPlayerGuideIds.BonfirePlaced);
        }

        private void HandleFireSeedCreated(Item eventActor, string itemId)
        {
            if (CanHandle(eventActor) &&
                string.Equals(itemId, NewPlayerGuideIds.FireSeed, StringComparison.Ordinal))
            {
                MarkMilestone(NewPlayerGuideIds.FireSeedCreated);
            }
        }

        private void HandleFurnaceIgnited(Item eventActor, string furnaceId)
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
            NotifyIfGuideFactsChanged();
        }

        private void HandleQuestRuntimeReady(Item player, PlayerQuestRuntime runtime)
        {
            if (ReferenceEquals(actor, player))
            {
                RefreshEligibility();
                RefreshMaterialMilestone();
                NotifyIfGuideFactsChanged();
            }
        }

        private void HandleQuestRuntimeRemoving(Item player)
        {
            if (ReferenceEquals(actor, player))
            {
                UnbindQuestRuntime();
                NotifyIfGuideFactsChanged();
            }
        }

        private void ResolveActor()
        {
            actor ??= GetComponentInParent<Item>();
        }

        private void RefreshEligibility()
        {
            ResolveActor();
            if (actor == null || !ActorProfileRegistry.IsLocallyControlled(actor) || actor.itemData == null)
            {
                isActiveForPlayer = false;
                UnbindQuestRuntime();
                loadedPlayerData = null;
                loadedProfileId = null;
                pendingMilestones.Clear();
                NotifyIfGuideFactsChanged();
                return;
            }

            bool profileChanged = !ReferenceEquals(loadedPlayerData, actor.itemData) ||
                                  !string.Equals(loadedProfileId, ActorProfileRegistry.GetProfileId(actor), StringComparison.Ordinal);
            if (profileChanged || !isActiveForPlayer)
            {
                pendingMilestones.Clear();
                UnbindQuestRuntime();
                isActiveForPlayer = ActorProfileRegistry.IsNewLocalProfile(actor) || NewPlayerGuideProgressStore.HasEligibility(actor.itemData);
                if (!isActiveForPlayer)
                {
                    loadedPlayerData = null;
                    loadedProfileId = null;
                    NotifyIfGuideFactsChanged();
                    return;
                }

                loadedPlayerData = actor.itemData;
                loadedProfileId = ActorProfileRegistry.GetProfileId(actor);
                if (ActorProfileRegistry.IsNewLocalProfile(actor))
                    NewPlayerGuideProgressStore.EstablishEligibility(actor.itemData);
                BindQuestRuntimeWhenReady();
                RefreshMaterialMilestone();
                NotifyIfGuideFactsChanged();
                return;
            }

            BindQuestRuntimeWhenReady();
            NotifyIfGuideFactsChanged();
        }

        private void UnbindQuestRuntime()
        {
            if (questRuntime != null)
                questRuntime.QuestChanged -= HandleQuestChanged;
            questRuntime = null;
        }

        // 只绑定管理器已有的运行时，等待期间的里程碑仅暂存于当前角色。
        private void BindQuestRuntimeWhenReady()
        {
            if (questRuntime != null && questRuntime.IsEnabled &&
                ReferenceEquals(questRuntime.PlayerData, loadedPlayerData))
                return;
            UnbindQuestRuntime();
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
            if (observingController != null)
                questRuntime.QuestChanged += HandleQuestChanged;
            foreach (string milestone in pendingMilestones)
                PublishMilestoneToQuestRuntime(milestone);
            pendingMilestones.Clear();
        }

        private bool CanHandle(Item eventActor)
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
