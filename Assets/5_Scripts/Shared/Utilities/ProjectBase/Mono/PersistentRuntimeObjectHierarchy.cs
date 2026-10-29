using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#region 常驻对象分类声明
public enum PersistentRuntimeObjectCategory
{
    Core,
    World,
    Networking,
    Players,
    UI,
    Audio,
    Rendering,
    Resources,
    Debug,
    Infrastructure,
    Testing
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class PersistentRuntimeObjectCategoryAttribute : Attribute
{
    public PersistentRuntimeObjectCategory Category { get; }
    public PersistentRuntimeObjectCategoryAttribute(PersistentRuntimeObjectCategory category) => Category = category;
}
#endregion

/// <summary>常驻对象统一按职责收纳，分类节点不接管业务对象的释放责任。</summary>
[DisallowMultipleComponent]
public sealed class PersistentRuntimeObjectHierarchy : MonoBehaviour
{
    #region 会话入口与退出保护
    public const string RootName = "Persistent Runtime";
    private static PersistentRuntimeObjectHierarchy instance;
    private static bool isShuttingDown;
    public static PersistentRuntimeObjectHierarchy ExistingInstance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Application.quitting -= HandleApplicationQuitting;
        instance = null;
        isShuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSessionEvents()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Application.quitting += HandleApplicationQuitting;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => OrganizeUnregisteredPersistentRoots();
    private static void HandleApplicationQuitting()
    {
        isShuttingDown = true;
        SingletonAutoMonoLifecycle.MarkShuttingDown();
    }

    public static bool MoveToPersistentCategory(GameObject ownerRoot, PersistentRuntimeObjectCategory category)
    {
        if (ownerRoot == null || !Application.isPlaying || isShuttingDown || SingletonAutoMonoLifecycle.IsShuttingDown)
            return false;
        ValidateCategory(category);
        if (ownerRoot.GetComponent<PersistentRuntimeObjectHierarchy>() != null || IsManagedCategoryRoot(ownerRoot.transform))
            return false;

        // 已有业务父对象时不拆散预制体，也不把普通场景的父对象意外提升为常驻对象。
        if (ownerRoot.transform.parent != null && !IsManagedCategoryRoot(ownerRoot.transform.parent))
            return instance != null && instance.TryGetObjectCategory(ownerRoot, out _);

        if (instance == null)
        {
            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(root);
            instance = root.AddComponent<PersistentRuntimeObjectHierarchy>();
        }
        if (ownerRoot.scene != instance.gameObject.scene)
            DontDestroyOnLoad(ownerRoot);
        return instance.TryGroupOwnedRoot(ownerRoot, category);
    }

    public static GameObject GetBusinessOwnerRoot(GameObject value)
    {
        if (value == null) return null;
        Transform owner = value.transform;
        while (owner.parent != null && !IsManagedCategoryRoot(owner.parent)) owner = owner.parent;
        return owner.gameObject;
    }

    private void OnApplicationQuit()
    {
        if (ReferenceEquals(instance, this)) HandleApplicationQuitting();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(instance, this)) instance = null;
    }
    #endregion

    #region 分类节点与业务所有权
    private readonly Dictionary<PersistentRuntimeObjectCategory, Transform> categories = new();
    private readonly List<GameObject> ungroupedRoots = new(32);

    public bool TryGroupOwnedRoot(GameObject ownerRoot, PersistentRuntimeObjectCategory category)
    {
        ValidateCategory(category);
        if (ownerRoot == null || ownerRoot.scene != gameObject.scene || ownerRoot == gameObject ||
            ownerRoot.GetComponent<PersistentRuntimeObjectHierarchy>() != null || IsManagedCategoryRoot(ownerRoot.transform))
            return false;
        Transform parent = ownerRoot.transform.parent;
        if (parent != null && !OwnsCategoryRoot(parent)) return false;
        Transform categoryRoot = GetOrCreateCategoryRoot(category);
        if (parent != categoryRoot) ownerRoot.transform.SetParent(categoryRoot, true);
        return true;
    }

    public bool TryGetObjectCategory(GameObject value, out PersistentRuntimeObjectCategory category)
    {
        category = default;
        if (value == null) return false;
        for (Transform current = value.transform; current != null && current != transform; current = current.parent)
            if (current.parent == transform)
                foreach (var entry in categories)
                    if (entry.Value == current) { category = entry.Key; return true; }
        return false;
    }

    public Transform GetOrCreateCategoryRoot(PersistentRuntimeObjectCategory category)
    {
        ValidateCategory(category);
        if (categories.TryGetValue(category, out Transform root) && root != null) return root;
        root = new GameObject(category.ToString()).transform;
        root.SetParent(transform, false);
        categories[category] = root;
        int siblingIndex = 0;
        foreach (var entry in categories)
            if (entry.Key < category && entry.Value != null) siblingIndex++;
        root.SetSiblingIndex(siblingIndex);
        return root;
    }

    private bool OwnsCategoryRoot(Transform candidate)
    {
        if (candidate == null || candidate.parent != transform) return false;
        foreach (Transform root in categories.Values)
            if (root == candidate) return true;
        return false;
    }

    private static bool IsManagedCategoryRoot(Transform candidate)
    {
        if (candidate == null || candidate.parent == null) return false;
        var hierarchy = candidate.parent.GetComponent<PersistentRuntimeObjectHierarchy>();
        return hierarchy != null && hierarchy.OwnsCategoryRoot(candidate);
    }

    private static void ValidateCategory(PersistentRuntimeObjectCategory category)
    {
        if (category < PersistentRuntimeObjectCategory.Core || category > PersistentRuntimeObjectCategory.Testing)
            throw new ArgumentOutOfRangeException(nameof(category));
    }
    #endregion

    #region 第三方常驻根补充收纳
    public static void OrganizeUnregisteredPersistentRoots()
    {
        // 只在场景加载及资源就绪时收纳第三方已常驻的根，不逐帧扫描或提升场景对象。
        if (instance == null || !Application.isPlaying || isShuttingDown || SingletonAutoMonoLifecycle.IsShuttingDown)
            return;
        Scene scene = instance.gameObject.scene;
        if (!scene.IsValid() || scene.name != "DontDestroyOnLoad") return;
        scene.GetRootGameObjects(instance.ungroupedRoots);
        foreach (GameObject root in instance.ungroupedRoots)
        {
            if (root == null || root == instance.gameObject || (root.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
            instance.TryGroupOwnedRoot(root, PersistentRuntimeObjectCategory.Infrastructure);
        }
        instance.ungroupedRoots.Clear();
    }
    #endregion
}
