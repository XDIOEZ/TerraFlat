using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 新世界隐藏内容规则采样

    private static JObject ServiceNewGameDeferredContent(JObject args)
    {
        GameObject root = new GameObject("[LuaNewGameContent]", typeof(RectTransform), typeof(CanvasGroup));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        GameObject worldTemplate = new GameObject("WorldContent", typeof(RectTransform));
        GameObject difficultyTemplate = new GameObject("DifficultyContent", typeof(RectTransform));
        worldTemplate.SetActive(false);
        difficultyTemplate.SetActive(false);
        worldTemplate.hideFlags = difficultyTemplate.hideFlags = HideFlags.HideAndDontSave;
        int originalOrder = BasePanel.CurrentOrder;
        try
        {
            BasePanel panel = root.AddComponent<BasePanel>();
            NewGameDeferredContent content = root.AddComponent<NewGameDeferredContent>();
            Transform worldRoot = new GameObject("WorldPage", typeof(RectTransform)).transform;
            Transform difficultyRoot = new GameObject("DifficultyPage", typeof(RectTransform)).transform;
            worldRoot.SetParent(root.transform, false);
            difficultyRoot.SetParent(root.transform, false);
            GameObject worldButton = new GameObject("WorldAction", typeof(RectTransform), typeof(Button));
            worldButton.transform.SetParent(worldTemplate.transform, false);
            GameObject difficultyButton = new GameObject("DifficultyAction", typeof(RectTransform), typeof(Button));
            difficultyButton.transform.SetParent(difficultyTemplate.transform, false);
            content.Configure(worldTemplate, worldRoot, difficultyTemplate, difficultyRoot);
            panel.InitClosed();
            var result = new JObject
            {
                ["initialWorldChildren"] = worldRoot.childCount,
                ["initialDifficultyChildren"] = difficultyRoot.childCount,
                ["initialHasWorld"] = content.HasWorldContent,
                ["initialHasDifficulty"] = content.HasDifficultyContent
            };
            result["firstWorldCreated"] = content.EnsureWorldContent(panel);
            Transform firstWorld = worldRoot.GetChild(0);
            Button boundWorldButton = panel.GetButton("WorldAction");
            boundWorldButton.interactable = false;
            result["difficultyStillUncreated"] = !content.HasDifficultyContent && difficultyRoot.childCount == 0;
            result["repeatWorldCreated"] = content.EnsureWorldContent(panel);
            result["worldInstancePreserved"] = firstWorld == worldRoot.GetChild(0);
            result["worldStatePreserved"] = !boundWorldButton.interactable;
            result["firstDifficultyCreated"] = content.EnsureDifficultyContent(panel);
            result["repeatDifficultyCreated"] = content.EnsureDifficultyContent(panel);
            result["worldChildren"] = worldRoot.childCount;
            result["difficultyChildren"] = difficultyRoot.childCount;
            result["cachedWorldButtonPreserved"] = panel.GetButton("WorldAction") == boundWorldButton;
            result["difficultyButtonRegistered"] = panel.GetButton("DifficultyAction") != null;
            result["instancesOwnedByPanel"] = firstWorld.IsChildOf(root.transform) &&
                difficultyRoot.GetChild(0).IsChildOf(root.transform);
            UnityEngine.Object.DestroyImmediate(root);
            result["childrenDestroyedWithPanel"] = firstWorld == null;
            return result;
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(worldTemplate);
            UnityEngine.Object.DestroyImmediate(difficultyTemplate);
            BasePanel.CurrentOrder = originalOrder;
        }
    }

    #endregion
}
