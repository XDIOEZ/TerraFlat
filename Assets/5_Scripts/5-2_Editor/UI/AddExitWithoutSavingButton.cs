#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>一次性同步设置 Prefab 的“不保存直接退出”按钮。</summary>
internal static class AddExitWithoutSavingButton
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab";
    private const string SessionPagePath = "Scroll View/Viewport/Content/设置分页_会话";
    private const string SourceButtonName = "保存并退出游戏按钮";
    private const string TargetButtonName = "不保存直接退出";

    [MenuItem("FlatWorld/UI/Sync Exit Without Saving Button")]
    private static void Sync()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform sessionPage = root.transform.Find(SessionPagePath);
            if (sessionPage == null)
                throw new MissingReferenceException($"缺少设置会话分页：{SessionPagePath}");

            Transform target = sessionPage.Find(TargetButtonName);
            if (target == null)
            {
                Transform source = sessionPage.Find(SourceButtonName);
                if (source == null)
                    throw new MissingReferenceException($"缺少按钮模板：{SourceButtonName}");

                target = Object.Instantiate(source.gameObject, sessionPage).transform;
                target.name = TargetButtonName;
                target.SetAsLastSibling();
            }

            TMP_Text label = target.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                throw new MissingComponentException($"{TargetButtonName} 缺少 TMP 文本");

            label.text = TargetButtonName;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>编译完成后排队同步，并监听从运行模式返回编辑模式。</summary>
    [InitializeOnLoadMethod]
    private static void QueueSyncAfterCompilation()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall -= Sync;
        EditorApplication.delayCall += Sync;
    }

    /// <summary>若编译发生在 Play Mode，返回编辑模式后补做 Prefab 同步。</summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.delayCall -= Sync;
        EditorApplication.delayCall += Sync;
    }
}
#endif
