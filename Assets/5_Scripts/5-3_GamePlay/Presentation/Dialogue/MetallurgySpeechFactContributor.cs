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
    public sealed class MetallurgySpeechFactContributor : MonoBehaviour, ICharacterSpeechContextContributor
    {
        #region 状态

        private Player actor;
        private Data_Player loadedPlayerData;
        private PlayerMetallurgyProgressStore progress;

        public int ContextOrder => 220;

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
                actor.ProfileContextChanged += HandleProfileContextChanged;

            GameplayProgressEvents.SmeltSucceeded += HandleSmeltSucceeded;
            GameplayProgressEvents.CraftSucceeded += HandleCraftSucceeded;
            RefreshStore();
        }

        private void OnDisable()
        {
            if (actor != null)
                actor.ProfileContextChanged -= HandleProfileContextChanged;

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

        private void HandleSmeltSucceeded(Player eventActor, string outputItemId)
        {
            if (CanHandle(eventActor))
                progress.RecordSmeltedOutput(outputItemId);
        }

        private void HandleCraftSucceeded(Player eventActor, string outputItemId)
        {
            if (CanHandle(eventActor))
                progress.RecordCraftedOutput(outputItemId);
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
            actor ??= GetComponentInParent<Player>();
        }

        private void RefreshStore()
        {
            ResolveActor();
            Data_Player playerData = actor != null && actor.IsLocalProfile ? actor.Data : null;
            if (ReferenceEquals(playerData, loadedPlayerData))
                return;

            loadedPlayerData = playerData;
            progress = playerData != null
                ? new PlayerMetallurgyProgressStore(playerData)
                : null;
        }

        private bool CanHandle(Player eventActor)
        {
            RefreshStore();
            return progress != null && ReferenceEquals(actor, eventActor);
        }

        #endregion
    }
}
