using FlatWorld.Gameplay.Progress;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 监听本地玩家真实熔炼与制作成功事件，把首块粗铁和首件金属工具写入可存档进度，
    /// 并向自言自语上下文贡献稳定 Facts。组件不生成文案，也不处理显示冷却；这些规则继续由
    /// Resources/Dialogue/Soliloquy 下的 JSON 统一控制，避免玩法状态与表现层文字耦合。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MetallurgySpeechFactContributor : MonoBehaviour, ICharacterSpeechContextContributor,
        ICharacterSpeechStateChangeSource
    {
        #region 状态

        private Item actor;
        private ItemData loadedPlayerData;
        private PlayerMetallurgyProgressStore progress;
        private CharacterSoliloquyController observingController;

        public int ContextOrder => 220;

        public void BeginObserving(CharacterSoliloquyController controller) => observingController = controller;
        public void EndObserving() => observingController = null;

        #endregion

        #region 生命周期

        private void Awake()
        {
            ResolveActor();
        }

        private void OnEnable()
        {
            ResolveActor();
            if (actor != null)
                ActorProfileRegistry.GetBinding(actor).Changed += HandleProfileContextChanged;

            GameplayProgressEvents.SmeltSucceeded += HandleSmeltSucceeded;
            GameplayProgressEvents.CraftSucceeded += HandleCraftSucceeded;
            RefreshStore();
        }

        private void OnDisable()
        {
            EndObserving();
            if (actor != null)
                ActorProfileRegistry.GetBinding(actor).Changed -= HandleProfileContextChanged;

            GameplayProgressEvents.SmeltSucceeded -= HandleSmeltSucceeded;
            GameplayProgressEvents.CraftSucceeded -= HandleCraftSucceeded;
        }

        #endregion

        #region Fact 贡献

        public void Contribute(CharacterSpeechContext context)
        {
            RefreshStore();
            context.SetFact(
                CharacterSpeechFacts.MetallurgyFirstRawIronSmelted,
                (progress?.FirstRawIronSmelted == true).ToString());
            context.SetFact(
                CharacterSpeechFacts.MetallurgyFirstMetalToolCrafted,
                (progress?.FirstMetalToolCrafted == true).ToString());
        }

        #endregion

        #region 玩法事件

        private void HandleSmeltSucceeded(Item eventActor, string outputItemId)
        {
            if (CanHandle(eventActor) && progress.RecordSmeltedOutput(outputItemId))
                observingController?.NotifySpeechStateChanged();
        }

        private void HandleCraftSucceeded(Item eventActor, string outputItemId)
        {
            if (CanHandle(eventActor) && progress.RecordCraftedOutput(outputItemId))
                observingController?.NotifySpeechStateChanged();
        }

        #endregion

        #region 玩家上下文

        private void HandleProfileContextChanged()
        {
            loadedPlayerData = null;
            progress = null;
            RefreshStore();
        }

        private void ResolveActor()
        {
            actor ??= GetComponentInParent<Item>();
        }

        private void RefreshStore()
        {
            ResolveActor();
            ItemData playerData = actor != null && ActorProfileRegistry.IsLocallyControlled(actor) ? actor.itemData : null;
            if (ReferenceEquals(playerData, loadedPlayerData))
                return;

            loadedPlayerData = playerData;
            progress = playerData != null
                ? new PlayerMetallurgyProgressStore(playerData)
                : null;
        }

        private bool CanHandle(Item eventActor)
        {
            RefreshStore();
            return progress != null && ReferenceEquals(actor, eventActor);
        }

        #endregion
    }
}
