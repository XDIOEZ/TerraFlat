using UnityEngine;

/// <summary>新世界的隐藏页面只在用户首次访问时创建，实例随所属面板一起释放。</summary>
public sealed class NewGameDeferredContent : MonoBehaviour
{
    #region 页面资源与实例

    [SerializeField] private GameObject worldContentPrefab;
    [SerializeField] private Transform worldContentRoot;
    [SerializeField] private GameObject difficultyContentPrefab;
    [SerializeField] private Transform difficultyContentRoot;
    private GameObject worldContent;
    private GameObject difficultyContent;

    public bool HasWorldContent => worldContent != null;
    public bool HasDifficultyContent => difficultyContent != null;

    public void Configure(GameObject worldPrefab, Transform worldRoot,
        GameObject difficultyPrefab, Transform difficultyRoot)
    {
        worldContentPrefab = worldPrefab;
        worldContentRoot = worldRoot;
        difficultyContentPrefab = difficultyPrefab;
        difficultyContentRoot = difficultyRoot;
    }

    #endregion

    #region 按需创建

    public bool EnsureWorldContent(BasePanel panel)
    {
        return EnsureContent(panel, worldContentPrefab, worldContentRoot, ref worldContent);
    }

    public bool EnsureDifficultyContent(BasePanel panel)
    {
        return EnsureContent(panel, difficultyContentPrefab, difficultyContentRoot, ref difficultyContent);
    }

    private static bool EnsureContent(BasePanel panel, GameObject prefab, Transform root, ref GameObject instance)
    {
        if (instance != null)
            return false;
        if (panel == null || prefab == null || root == null)
        {
            Debug.LogError("[NewGameUI] 按需页面缺少正式资源或所属容器。");
            return false;
        }

        // 容器仍关闭时完成创建与控件注册，切页后再由原面板接管显示和输入。
        instance = Instantiate(prefab, root, false);
        panel.RefreshUIComponents();
        return true;
    }

    #endregion
}
