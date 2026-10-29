using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#region 场景运行时对象分类
public enum RuntimeSceneObjectCategory
{
    Chunks,
    Items,
    Entities,
    Players,
    DroppedItems,
    Rendering,
    Preparation,
    Physics
}

/// <summary>每个场景按需建立公共分类节点，只整理层级，不接管对象生命周期。</summary>
[DisallowMultipleComponent]
public sealed class RuntimeSceneObjectHierarchy : MonoBehaviour
{
    private static readonly Dictionary<int, RuntimeSceneObjectHierarchy> scenes = new();
    private readonly Dictionary<RuntimeSceneObjectCategory, Transform> categories = new();
    private int sceneHandle;

    public static Transform GetOrCreateCategoryRoot(Scene scene, RuntimeSceneObjectCategory category)
    {
        if (!scenes.TryGetValue(scene.handle, out RuntimeSceneObjectHierarchy hierarchy) || hierarchy == null)
        {
            var root = new GameObject("Runtime Objects") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            hierarchy = root.AddComponent<RuntimeSceneObjectHierarchy>();
            hierarchy.sceneHandle = scene.handle;
            scenes[scene.handle] = hierarchy;
        }

        if (!hierarchy.categories.TryGetValue(category, out Transform categoryRoot) || categoryRoot == null)
        {
            string categoryName = category.ToString();
            categoryRoot = hierarchy.transform.Find(categoryName);
            if (categoryRoot == null)
            {
                categoryRoot = new GameObject(categoryName).transform;
                categoryRoot.SetParent(hierarchy.transform, false);
            }
            hierarchy.categories[category] = categoryRoot;
        }
        return categoryRoot;
    }

    private void OnEnable()
    {
        sceneHandle = gameObject.scene.handle;
        scenes[sceneHandle] = this;
    }

    private void OnDestroy()
    {
        if (scenes.TryGetValue(sceneHandle, out RuntimeSceneObjectHierarchy hierarchy) &&
            ReferenceEquals(hierarchy, this))
            scenes.Remove(sceneHandle);
    }
}
#endregion
