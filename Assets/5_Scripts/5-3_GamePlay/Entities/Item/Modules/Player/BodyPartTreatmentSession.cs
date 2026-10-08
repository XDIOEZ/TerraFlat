using System;
using UnityEngine;

/// <summary>独立推进治疗等待与提交，界面、NPC 或其它入口复用同一会话规则。</summary>
public sealed class BodyPartTreatmentSession
{
    #region 会话契约与进度

    public delegate bool TreatmentValidator(Item consumer, BodyPartType part, out string reason);
    public delegate bool TreatmentCommitter(Item consumer, BodyPartType part);

    public enum SessionState { Ready, Running, Completing, Completed, Cancelled, Failed }

    public SessionState State { get; private set; }
    public BodyPartType TargetPart { get; private set; }
    public float DurationSeconds { get; }
    public float ElapsedSeconds { get; private set; }
    public float Progress => DurationSeconds > 0f ? Mathf.Clamp01(ElapsedSeconds / DurationSeconds)
        : State == SessionState.Ready ? 0f : 1f;
    public bool IsRunning => State == SessionState.Running;
    public string Reason { get; private set; }

    private TreatmentValidator validate;
    private TreatmentCommitter commit;
    private Item consumer;
    private uint consumerGeneration;

    public BodyPartTreatmentSession(float durationSeconds, TreatmentValidator validator, TreatmentCommitter committer)
    {
        if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        DurationSeconds = durationSeconds;
        validate = validator ?? throw new ArgumentNullException(nameof(validator));
        commit = committer ?? throw new ArgumentNullException(nameof(committer));
    }

    #endregion

    #region 开始、取消与推进

    public bool TryBegin(Item target, BodyPartType part, out string reason)
    {
        reason = "治疗会话已开始";
        if (State != SessionState.Ready) return false;
        reason = "治疗对象已失效";
        if (target == null || target.DestructionHandled) return false;
        if (!validate(target, part, out reason)) return false;
        consumer = target;
        consumerGeneration = target.RuntimeGeneration;
        TargetPart = part;
        Reason = null;
        State = SessionState.Running;
        return true;
    }

    public bool Cancel(string reason = null)
    {
        // 提交已经进入库存事务后不再接受取消，最后一份用品卸载也不能覆盖提交结果。
        if (State != SessionState.Ready && State != SessionState.Running) return false;
        State = SessionState.Cancelled;
        Reason = reason;
        ReleaseOperation();
        return true;
    }

    public void Advance(float deltaTime)
    {
        if (!IsRunning) return;
        if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!CanContinue(out string reason)) { Cancel(reason); return; }
        ElapsedSeconds = Mathf.Min(DurationSeconds, ElapsedSeconds + deltaTime);
        if (ElapsedSeconds >= DurationSeconds) TryComplete();
    }

    public bool TryComplete()
    {
        if (!IsRunning || ElapsedSeconds < DurationSeconds) return false;
        if (!CanContinue(out string reason)) { Cancel(reason); return false; }
        State = SessionState.Completing;
        bool completed = false;
        try
        {
            completed = commit(consumer, TargetPart);
            return completed;
        }
        finally
        {
            State = completed ? SessionState.Completed : SessionState.Failed;
            Reason = completed ? null : "治疗未完成，请重新选择部位";
            ReleaseOperation();
        }
    }

    private bool CanContinue(out string reason)
    {
        reason = "治疗对象已失效";
        return consumer != null && !consumer.DestructionHandled &&
            consumer.RuntimeGeneration == consumerGeneration && validate(consumer, TargetPart, out reason);
    }

    private void ReleaseOperation()
    {
        consumer = null;
        validate = null;
        commit = null;
    }

    #endregion
}
