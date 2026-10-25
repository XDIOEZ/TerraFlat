using System;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class DimensionManager
{
    #region 本机迁移全程保护
    private LocalDimensionPresentation localDimensionPresentation;

    // 从请求发出前到加载幕退场，共用同一份输入与运动租约，重试不重复捕获冻结后的状态。
    private sealed class LocalDimensionPresentation : IDisposable
    {
        public readonly Player Actor;
        public readonly WorldAddress Target;
        public readonly GameSaveData Save;
        public readonly GameManager Game;
        public readonly INetworkSession Session;
        public readonly bool RequiresConnection;
        public readonly Mod_GameController Controller;
        public readonly Mod_Mover Mover;
        public readonly Rigidbody2D Body;
        public readonly bool WasSimulated;
        public readonly bool WasRunning;
        public bool Committed;
        private bool disposed;

        public LocalDimensionPresentation(Player actor, WorldAddress target)
        {
            Actor = actor;
            Target = target;
            Save = SaveDataMgr.Instance?.SaveData;
            Game = GameManager.Instance;
            Session = GameNetwork.Session;
            RequiresConnection = GameNetwork.IsOnline;
            Controller = actor.GetComponentInChildren<Mod_GameController>(true);
            Mover = actor.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Body = actor.GetComponent<Rigidbody2D>();
            WasSimulated = Body != null && Body.simulated;
            WasRunning = Mover != null && Mover.RunPreference;
        }

        public void Freeze()
        {
            if (Controller != null) Controller.AcquireGameplayInputLock(this);
            if (Mover != null) Mover.ResetMotion();
            if (Body != null)
            {
                Body.velocity = Vector2.zero;
                Body.angularVelocity = 0f;
                Body.simulated = false;
            }
        }

        public void RestorePhysics()
        {
            if (Mover != null) Mover.ResetMotion();
            if (Body != null)
            {
                Body.velocity = Vector2.zero;
                Body.angularVelocity = 0f;
                Body.simulated = WasSimulated;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { RestorePhysics(); }
            finally
            {
                if (Controller != null) Controller.ReleaseGameplayInputLock(this);
                if (Actor != null) RestorePlayerRunState(Actor, WasRunning);
            }
        }
    }

    private bool TryBeginLocalDimensionPresentation(Player actor, WorldAddress target, DimensionDefinition definition)
    {
        if (localDimensionPresentation != null)
            return localDimensionPresentation.Actor == actor && localDimensionPresentation.Target == target &&
                IsLocalDimensionPresentationValid(localDimensionPresentation) &&
                localDimensionPresentation.Game.BeginNetworkDimensionLoading(definition);
        if (actor == null || actor != ItemMgr.Instance?.User_Player ||
            !GameManager.Instance.BeginNetworkDimensionLoading(definition)) return false;
        try
        {
            localDimensionPresentation = new LocalDimensionPresentation(actor, target);
            localDimensionPresentation.Game.Event_GameWorldExit += OnLocalDimensionWorldExit;
            localDimensionPresentation.Session.StateChanged += OnLocalDimensionSessionChanged;
            localDimensionPresentation.Freeze();
            return true;
        }
        catch
        {
            GameManager.ExistingInstance?.EndNetworkDimensionLoading(false, "维度切换保护未能建立。");
            ReleaseLocalDimensionPresentation(localDimensionPresentation);
            throw;
        }
    }

    private bool IsLocalDimensionPresentationValid(LocalDimensionPresentation presentation) =>
        presentation != null && ReferenceEquals(localDimensionPresentation, presentation) && presentation.Actor != null &&
        presentation.Game != null && presentation.Game.IsInGameWorld &&
        presentation.Actor == ItemMgr.Instance?.User_Player &&
        ReferenceEquals(presentation.Save, SaveDataMgr.Instance?.SaveData) &&
        (!presentation.RequiresConnection || ReferenceEquals(presentation.Session, GameNetwork.Session) && GameNetwork.IsOnline);

    private void LateUpdate()
    {
        // 仅迁移持租约期间检查静态身份引用，覆盖 F5 换主角和直接销毁而没有世界退出事件的情况。
        if (localDimensionPresentation != null && !IsLocalDimensionPresentationValid(localDimensionPresentation))
            CancelLocalDimensionPresentation("维度切换期间本机玩家或世界会话已改变。");
    }

    private void CompleteLocalDimensionRequest(LocalDimensionPresentation presentation, bool accepted, string reason)
    {
        if (presentation == null || !ReferenceEquals(localDimensionPresentation, presentation)) return;
        if (accepted) { presentation.Committed = true; return; }
        // 已提交后的迟到拒绝不能解开尚未完成呈现的目标世界，后续快照仍复用保护租约。
        if (presentation.Committed) return;
        CancelLocalDimensionPresentation(reason);
    }

    private void ReleaseLocalDimensionPresentation(LocalDimensionPresentation presentation)
    {
        if (presentation == null || !ReferenceEquals(localDimensionPresentation, presentation)) return;
        localDimensionPresentation = null;
        networkTransitionPending = false;
        if (presentation.Game != null) presentation.Game.Event_GameWorldExit -= OnLocalDimensionWorldExit;
        presentation.Session.StateChanged -= OnLocalDimensionSessionChanged;
        presentation.Dispose();
    }

    private void CancelLocalDimensionPresentation(string reason)
    {
        LocalDimensionPresentation presentation = localDimensionPresentation;
        if (presentation == null) return;
        if (presentation.Game != null) presentation.Game.EndNetworkDimensionLoading(false, reason);
        ReleaseLocalDimensionPresentation(presentation);
    }

    private void OnLocalDimensionWorldExit() => CancelLocalDimensionPresentation("维度切换期间已退出世界。");

    private void OnLocalDimensionSessionChanged(NetworkSessionState state)
    {
        if (state != NetworkSessionState.Online)
            CancelLocalDimensionPresentation("维度切换期间联机会话已结束。");
    }
    #endregion
}
