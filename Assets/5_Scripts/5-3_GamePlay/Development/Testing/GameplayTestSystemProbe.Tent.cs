using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 睡眠遮罩与交互完成规则

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterTentRules()
    {
        Register("player", "interaction_completion", PlayerInteractionCompletion);
#if UNITY_EDITOR
        Register("ui_settings", "tent_veil", TentVeilConfiguration);
#endif
    }

    private sealed class CompletedInteractionProbe : IInteractable
    {
        public int CancelCount;
        public void OnInteractStart(Item playerItem) { }
        public void OnInteractCancel(Item playerItem) => CancelCount++;
    }

    private static JObject PlayerInteractionCompletion(JObject args)
    {
        // 独立发送器只调用正式完成/取消接口，不绑定玩家或输入。
        var root = new GameObject("[LuaInteractionCompletion]");
        root.SetActive(false);
        try
        {
            var sender = root.AddComponent<Mod_InteractSender>();
            var completed = new CompletedInteractionProbe();
            var other = new CompletedInteractionProbe();
            sender.currentReceiver = completed;
            sender.receiversInRange.Add(completed);
            sender.receiversInRange.Add(other);
            bool wrongTargetAccepted = sender.CompleteInteraction(other);
            bool originalPreserved = sender.currentReceiver == completed;
            bool accepted = sender.CompleteInteraction(completed);
            bool currentCleared = sender.currentReceiver == null;
            bool onlyCompletedRemoved = !sender.receiversInRange.Contains(completed) &&
                sender.receiversInRange.Contains(other);
            bool repeatedAccepted = sender.CompleteInteraction(completed);
            sender.CancelCurrentInteraction();
            int completedCancelCount = completed.CancelCount;
            sender.currentReceiver = other;
            bool staleAccepted = sender.CompleteInteraction(completed);
            bool nextPreserved = sender.currentReceiver == other;
            sender.CancelCurrentInteraction();
            return new JObject
            {
                ["wrongTargetAccepted"] = wrongTargetAccepted, ["originalPreserved"] = originalPreserved,
                ["accepted"] = accepted, ["currentCleared"] = currentCleared,
                ["onlyCompletedRemoved"] = onlyCompletedRemoved, ["repeatedAccepted"] = repeatedAccepted,
                ["completedCancelCount"] = completedCancelCount, ["staleAccepted"] = staleAccepted,
                ["nextPreserved"] = nextPreserved, ["nextCancelCount"] = other.CancelCount
            };
        }
        finally { Object.DestroyImmediate(root); }
    }

#if UNITY_EDITOR
    private static JObject TentVeilConfiguration(JObject args)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Sleep.prefab");
        Image veil = prefab.transform.Find("Image").GetComponent<Image>();
        Color color = veil.color;
        return new JObject
        {
            ["red"] = color.r, ["green"] = color.g, ["blue"] = color.b, ["alpha"] = color.a,
            ["plainImage"] = veil.sprite == null && veil.type == Image.Type.Simple,
            ["fullScreen"] = veil.GetComponent<FullScreenRectController>() != null,
            ["panelAlpha"] = prefab.GetComponent<CanvasGroup>().alpha,
            ["animationCount"] = prefab.GetComponents<BaseUIAnimation>().Length
        };
    }
#endif

    #endregion
}
