#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>主角使用已加载的正式调度器和屏幕气泡，截图后按用例租约清理自有发言。</summary>
    public static class GameplayTestNarrativeDialogueRuntimeProbe
    {
        #region 真实操作与现场租约
        private const string OwnText = "Lua现场：任务进度和事件通知已送达。";
        private static SpeechLease current;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register()
        {
            GameplayTestRuntimeProbe.RegisterAsync("dialogue", "bubble_priority", Capture);
            GameplayTestRuntimeProbe.Register("dialogue", "hide_bubble", Hide);
        }

        private sealed class SpeechLease : IDisposable
        {
            public readonly ScreenSpaceSpeechBubblePresenter Presenter;
            public readonly Player Player;
            private bool disposed;
            public SpeechLease(Player player, ScreenSpaceSpeechBubblePresenter presenter)
            { Player = player; Presenter = presenter; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (Player != null && Presenter != null && ReadBubble(Player)?.GetComponentInChildren<TMP_Text>(true)?.text == OwnText)
                    Presenter.HideImmediate();
                if (ReferenceEquals(current, this)) current = null;
            }
        }
        #endregion

        #region 实际气泡优先级与非交互性
        private static Transform ReadBubble(Player player)
        {
            UIManager manager = UIManager.ExistingInstance;
            Transform root = manager?.GetPanelRootFor(player.transform);
            return root == null ? null : root.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(value => value.name == RuntimeUIPrefabKeys.CharacterSpeechBubble && value.gameObject.activeInHierarchy);
        }

        private static IEnumerator Capture(Player player, JObject args, Action<JObject> completed)
        {
            var controller = player.GetComponentInChildren<CharacterSoliloquyController>(true);
            var presenter = player.GetComponentInChildren<ScreenSpaceSpeechBubblePresenter>(true);
            if (controller == null || presenter == null || !controller.isActiveAndEnabled || !presenter.isActiveAndEnabled)
                throw new InvalidOperationException("正式主角对话控制器或气泡显示器没有启动。");
            double deadline = Time.realtimeSinceStartupAsDouble + 12d;
            while (presenter.IsVisible)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("主角仍有其他来源的发言，拒绝覆盖非自有气泡。");
                yield return null;
            }
            current = new SpeechLease(player, presenter);
            GameplayTestRuntimeProbe.RegisterCleanup(current);
            int requests = 0, shown = 0;
            Action<CharacterSpeechRequest> onRequest = value => { if (value.Topic == "lua-narrative") requests++; };
            Action<CharacterSpeechRequest> onShown = value => { if (value.Topic == "lua-narrative") shown++; };
            controller.SpeechRequested += onRequest; controller.SpeechShown += onShown;
            var result = new JObject();
            try
            {
                result["accepted"] = controller.Say(OwnText, CharacterSpeechPriority.Emergency, 15f, "lua-narrative");
                result["visibleAfterFirst"] = presenter.IsVisible;
                result["lowerAccepted"] = controller.Say("低优先级不应覆盖当前提示。", CharacterSpeechPriority.Ambient, 15f, "lua-narrative");
                result["priorityAfterLower"] = (int)presenter.VisiblePriority;
                result["blankAccepted"] = controller.Say("   ", CharacterSpeechPriority.Emergency, 15f, "lua-narrative");
                result["equalAccepted"] = controller.Say(OwnText, CharacterSpeechPriority.Emergency, 15f, "lua-narrative");
                Transform bubble = ReadBubble(player);
                TMP_Text text = bubble?.GetComponentInChildren<TMP_Text>(true);
                CanvasGroup group = bubble?.GetComponent<CanvasGroup>();
                result["actualText"] = text?.text;
                result["viewActive"] = bubble != null && bubble.gameObject.activeInHierarchy;
                result["labelActive"] = text != null && text.isActiveAndEnabled;
                result["alpha"] = group?.alpha ?? -1f;
                result["interactable"] = group != null && group.interactable;
                result["blocksRaycasts"] = group == null || group.blocksRaycasts;
                result["requestNotifications"] = requests; result["shownNotifications"] = shown;
                result["visible"] = presenter.IsVisible;
                completed(result);
            }
            finally
            {
                controller.SpeechRequested -= onRequest; controller.SpeechShown -= onShown;
            }
        }

        private static JObject Hide(Player player, JObject args)
        {
            SpeechLease lease = current ?? throw new InvalidOperationException("没有本 Lua 持有的自有发言。");
            if (lease.Player != player) throw new InvalidOperationException("气泡租约属于另一位角色。");
            bool before = lease.Presenter.IsVisible;
            lease.Dispose();
            return new JObject { ["visibleBefore"] = before, ["visibleAfter"] = lease.Presenter.IsVisible };
        }
        #endregion
    }
}
#endif
