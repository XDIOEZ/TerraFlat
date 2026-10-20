using System;
using InputSystem;
using Newtonsoft.Json.Linq;
using UnityEngine.InputSystem;

public static partial class GameplayTestSystemProbe
{
    #region 独立按键设置会话采样

    /// <summary>使用独立输入资产和内存存储采样正式会话，不接触当前玩家输入或偏好。</summary>
    private static JObject ServiceInputBindingEditSession(JObject args)
    {
        string mode = Text(args, "mode", "unchanged");
        if (mode != "unchanged" && mode != "modified" && mode != "reverted" && mode != "committed")
            throw new ArgumentException("未知按键设置会话采样模式：" + mode);

        bool existingOverride = Flag(args, "existingOverride", false);
        var inputActions = new PlayerInputActions();
        try
        {
            var store = new InputSettingsMemoryStore();
            using var service = new InputBindingService(inputActions.asset, store);
            InputBindingEntry entry = service.Entries[0];
            if (existingOverride)
                service.ClearBinding(entry);
            store.SaveCount = store.ClearCount = 0;

            int notifications = 0;
            service.BindingsChanged += () => notifications++;
            service.BeginSettingsEditSession();
            string initialJson = inputActions.asset.SaveBindingOverridesAsJson();
            string baselineJson = initialJson;
            if (mode != "unchanged")
            {
                if (existingOverride) service.ResetBindingToDefault(entry);
                else service.ClearBinding(entry);
            }
            if (mode == "reverted")
            {
                if (existingOverride) service.ClearBinding(entry);
                else service.ResetBindingToDefault(entry);
            }
            if (mode == "committed")
            {
                service.CommitSettingsEditSession();
                baselineJson = inputActions.asset.SaveBindingOverridesAsJson();
            }

            string beforeDiscardJson = inputActions.asset.SaveBindingOverridesAsJson();
            bool hasChangesBeforeDiscard = service.HasSettingsEditSessionChanges;
            int editNotifications = notifications;
            int writesBeforeDiscard = store.SaveCount + store.ClearCount;
            service.DiscardSettingsEditSession();
            int discardNotifications = notifications - editNotifications;
            int writesAfterDiscard = store.SaveCount + store.ClearCount;
            int discardStoreCalls = writesAfterDiscard - writesBeforeDiscard;
            bool sessionActiveAfterDiscard = service.IsSettingsEditSessionActive;
            bool hasChangesAfterDiscard = service.HasSettingsEditSessionChanges;
            service.DiscardSettingsEditSession();

            return new JObject
            {
                ["mode"] = mode, ["existingOverride"] = existingOverride,
                ["initialJson"] = initialJson, ["baselineJson"] = baselineJson,
                ["beforeDiscardJson"] = beforeDiscardJson,
                ["finalJson"] = inputActions.asset.SaveBindingOverridesAsJson(),
                ["persistedJson"] = store.Json,
                ["hasChangesBeforeDiscard"] = hasChangesBeforeDiscard,
                ["hasChangesAfterDiscard"] = hasChangesAfterDiscard,
                ["sessionActiveAfterDiscard"] = sessionActiveAfterDiscard,
                ["editNotificationCount"] = editNotifications,
                ["discardNotificationCount"] = discardNotifications,
                ["repeatedDiscardNotificationCount"] = notifications - editNotifications - discardNotifications,
                ["loadCount"] = store.LoadCount, ["saveCount"] = store.SaveCount,
                ["clearCount"] = store.ClearCount, ["discardStoreCallCount"] = discardStoreCalls,
                ["repeatedDiscardStoreCallCount"] = store.SaveCount + store.ClearCount - writesAfterDiscard
            };
        }
        finally
        {
            // 同步规则在编辑态也会执行，独立输入资产必须当场释放。
            UnityEngine.Object.DestroyImmediate(inputActions.asset);
        }
    }

    private sealed class InputSettingsMemoryStore : IInputBindingStore
    {
        public string Json = string.Empty;
        public int LoadCount;
        public int SaveCount;
        public int ClearCount;

        public string Load() { LoadCount++; return Json; }
        public void Save(string json) { SaveCount++; Json = json ?? string.Empty; }
        public void Clear() { ClearCount++; Json = string.Empty; }
    }

    #endregion

    #region 统一返回与 UI 绑定采样

    private static JObject ServiceCancelBindings(JObject args)
    {
        string mode = Text(args, "mode", "default");
        var actions = new PlayerInputActions();
        try
        {
            var escape = actions.Win10.ESC;
            if (mode == "rebound")
                escape.ApplyBindingOverride(0, "<Keyboard>/f10");
            else if (mode == "cleared")
            {
                escape.ApplyBindingOverride(0, string.Empty);
                escape.ApplyBindingOverride(1, string.Empty);
                var submit = actions.Win10.LeftClick;
                for (int i = 0; i < submit.bindings.Count; i++)
                {
                    if (submit.bindings[i].groups == "Gamepad")
                        submit.ApplyBindingOverride(i, string.Empty);
                }
            }
            else if (mode != "default")
                throw new ArgumentException("未知返回绑定采样模式：" + mode);

            // 只同步独立资产，不配置现场 EventSystem，也不写入玩家偏好。
            EventSystemGuard.SynchronizeUIInputBindings(actions.asset);
            var cancel = actions.asset.FindAction("FlatWorldUI/Cancel", true);
            int initialCount = cancel.bindings.Count;
            EventSystemGuard.SynchronizeUIInputBindings(actions.asset);
            var submitPaths = new JArray();
            var uiSubmit = actions.asset.FindAction("FlatWorldUI/Submit", true);
            for (int i = 2; i < uiSubmit.bindings.Count; i++)
                submitPaths.Add(uiSubmit.bindings[i].effectivePath ?? string.Empty);
            return new JObject
            {
                ["keyboard"] = cancel.bindings[0].effectivePath ?? string.Empty,
                ["gamepadBack"] = cancel.bindings[1].effectivePath,
                ["gamepadSettings"] = cancel.bindings[2].effectivePath ?? string.Empty,
                ["mobile"] = cancel.bindings[3].effectivePath,
                ["count"] = cancel.bindings.Count,
                ["initialCount"] = initialCount,
                ["submitGamepadPaths"] = submitPaths
            };
        }
        finally
        {
            actions.Disable();
            UnityEngine.Object.DestroyImmediate(actions.asset);
        }
    }

    private static JObject ServiceCancelDispatch(JObject args)
    {
        int first = 0, consumer = 0, tail = 0;
        Func<InputControl, bool> firstHandler = _ => { first++; return false; };
        Func<InputControl, bool> consumerHandler = _ => { consumer++; return true; };
        Func<InputControl, bool> tailHandler = _ => { tail++; return false; };
        EventSystemGuard.CancelRequested += firstHandler;
        EventSystemGuard.CancelRequested += consumerHandler;
        EventSystemGuard.CancelRequested += tailHandler;
        try
        {
            // 空来源不会触发真实玩家返回，只验证正式分发的消费和注销契约。
            bool consumed = EventSystemGuard.TryHandleCancelRequest(null);
            EventSystemGuard.CancelRequested -= consumerHandler;
            bool afterRemoval = EventSystemGuard.TryHandleCancelRequest(null);
            return new JObject
            {
                ["consumed"] = consumed, ["afterRemoval"] = afterRemoval,
                ["first"] = first, ["consumer"] = consumer, ["tail"] = tail
            };
        }
        finally
        {
            EventSystemGuard.CancelRequested -= firstHandler;
            EventSystemGuard.CancelRequested -= consumerHandler;
            EventSystemGuard.CancelRequested -= tailHandler;
        }
    }

    #endregion
}
