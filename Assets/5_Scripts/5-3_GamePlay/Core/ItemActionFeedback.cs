using System;

/// <summary>玩法操作反馈契约；玩法只发布文本，由表现层选择气泡等展示方式。</summary>
public static class ItemActionFeedback
{
    public static event Action<Item, string> Requested; // 当前操作的执行者和提示。

    /// <summary>发布一条非阻断操作反馈。</summary>
    public static void Show(Item actor, string message)
    {
        if (actor != null && !string.IsNullOrWhiteSpace(message))
            Requested?.Invoke(actor, message);
    }
}
