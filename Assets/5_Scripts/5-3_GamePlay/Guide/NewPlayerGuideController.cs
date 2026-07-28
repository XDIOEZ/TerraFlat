using System;
using System.Collections.Generic;
using FlatWorld.Dialogue;
using FlatWorld.Gameplay.Progress;
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
        private NewPlayerGuideProgressStore progress;
        private bool isActiveForPlayer;

        public int ContextOrder => 200;
        public bool IsTutorialActive => isActiveForPlayer;
        public bool IsTutorialCompleted => progress?.IsCompleted == true;
        public NewPlayerGuideStage CurrentStage => progress?.CurrentStage ?? NewPlayerGuideStage.OpenInventory;

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
            RefreshEligibility();
            context.SetFact(CharacterSpeechFacts.TutorialEnabled, isActiveForPlayer.ToString());
            context.SetFact(CharacterSpeechFacts.TutorialStage, CurrentStage.ToString());
            context.SetFact(CharacterSpeechFacts.TutorialCompleted, IsTutorialCompleted.ToString());
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

        private void ResolveActor()
        {
            actor ??= GetComponentInParent<Player>();
        }

        private void RefreshEligibility()
        {
            ResolveActor();
            bool runtimeNewProfile = actor != null && actor.IsNewProfile;
            bool persistedEligibility = actor?.Data != null &&
                                        NewPlayerGuideProgressStore.HasEligibility(actor.Data);
            bool shouldBeActive = actor != null &&
                                  actor.IsLocalProfile &&
                                  actor.Data != null &&
                                  (runtimeNewProfile || persistedEligibility);
            if (!shouldBeActive)
            {
                isActiveForPlayer = false;
                progress = null;
                loadedPlayerData = null;
                return;
            }

            isActiveForPlayer = true;
            if (!ReferenceEquals(loadedPlayerData, actor.Data))
            {
                loadedPlayerData = actor.Data;
                progress = new NewPlayerGuideProgressStore(
                    actor.Data,
                    establishEligibility: runtimeNewProfile);
                RefreshMaterialMilestone();
            }
        }

        private bool CanHandle(Player eventActor)
        {
            RefreshEligibility();
            return isActiveForPlayer && ReferenceEquals(actor, eventActor);
        }

        private void MarkMilestone(string milestoneId)
        {
            progress?.MarkMilestone(milestoneId);
        }

        #endregion

        #region 资源统计

        private void RefreshMaterialMilestone()
        {
            if (!isActiveForPlayer || progress == null || actor?.itemMods == null)
                return;

            float stickAmount = CountInventoryItem(NewPlayerGuideIds.StickWood);
            float logAmount = CountInventoryItem(NewPlayerGuideIds.Log);
            float leafAmount = CountInventoryItem(NewPlayerGuideIds.Leaf);
            if (stickAmount >= NewPlayerGuideIds.RequiredStickAmount &&
                logAmount >= NewPlayerGuideIds.RequiredLogAmount &&
                leafAmount >= NewPlayerGuideIds.RequiredLeafAmount)
            {
                progress.MarkMilestone(NewPlayerGuideIds.SurvivalMaterialsGathered);
            }
        }

        private float CountInventoryItem(string itemId)
        {
            float total = 0f;
            HashSet<Inventory_Data> visited = new();

            Mod_Inventory bag = actor.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            if (bag?.InventoryInstances != null)
            {
                for (int i = 0; i < bag.InventoryInstances.Count; i++)
                    total += CountInventoryItem(bag.InventoryInstances[i], itemId, visited);
            }

            Inventory_HotBar hotbar = actor.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
            total += CountInventoryItem(hotbar?.RuntimeInventory, itemId, visited);
            return total;
        }

        private static float CountInventoryItem(
            Inventory inventory,
            string itemId,
            HashSet<Inventory_Data> visited)
        {
            if (inventory?.Data?.itemSlots == null || !visited.Add(inventory.Data))
                return 0f;

            float total = 0f;
            for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            {
                ItemData itemData = inventory.Data.itemSlots[i]?.itemData;
                if (itemData?.Stack == null ||
                    !string.Equals(itemData.IDName, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                total += Mathf.Max(0f, itemData.Stack.Amount);
            }

            return total;
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
