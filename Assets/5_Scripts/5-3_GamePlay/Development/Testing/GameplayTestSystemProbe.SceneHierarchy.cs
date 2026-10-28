using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 场景分类层级规则采样
    internal static JObject CaptureRuntimeSceneHierarchy(Player player, ChunkView foot)
    {
        RuntimeSceneObjectHierarchy playerRoot = player.GetComponentInParent<RuntimeSceneObjectHierarchy>();
        RuntimeSceneObjectHierarchy chunkRoot = foot != null ? foot.GetComponentInParent<RuntimeSceneObjectHierarchy>() : null;
        return new JObject
        {
            ["playerCategory"] = player.transform.parent?.name,
            ["chunkCategory"] = foot != null ? foot.transform.parent?.parent?.name : null,
            ["playerSceneCorrect"] = playerRoot != null && playerRoot.gameObject.scene == player.gameObject.scene,
            ["chunkSceneCorrect"] = chunkRoot != null && chunkRoot.gameObject.scene == foot.gameObject.scene,
            ["sameSceneSharedRoot"] = foot != null && (foot.gameObject.scene != player.gameObject.scene || playerRoot == chunkRoot)
        };
    }

    private static JObject ServiceSceneObjectHierarchy(JObject args)
    {
#if UNITY_EDITOR
        Scene previous = SceneManager.GetActiveScene();
        Scene first = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
        Scene second = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
        try
        {
            Transform items = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first, RuntimeSceneObjectCategory.Items);
            Transform chunks = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first, RuntimeSceneObjectCategory.Chunks);
            Transform otherItems = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(second, RuntimeSceneObjectCategory.Items);
            var sample = new JObject
            {
                ["available"] = true,
                ["reused"] = items == RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first, RuntimeSceneObjectCategory.Items),
                ["sharedRoot"] = items.parent == chunks.parent && items != chunks,
                ["sceneIsolation"] = items.root != otherItems.root && items.gameObject.scene == first && otherItems.gameObject.scene == second,
                ["identityTransform"] = items.localToWorldMatrix == Matrix4x4.identity && otherItems.localToWorldMatrix == Matrix4x4.identity
            };

            var item = new GameObject("HierarchyItem");
            SceneManager.MoveGameObjectToScene(item, first);
            item.transform.SetPositionAndRotation(new Vector3(-13, 7, 2), Quaternion.Euler(0, 0, 31));
            item.transform.localScale = new Vector3(2, 3, 1);
            Matrix4x4 pose = item.transform.localToWorldMatrix;
            ItemWorldPlacement.AttachToSceneItemCategory(null, item);
            sample["itemGrouped"] = item.transform.parent == items;
            sample["posePreserved"] = item.transform.localToWorldMatrix == pose && item.scene == first;

            var explicitOwner = new GameObject("ExplicitChunkOwner");
            explicitOwner.transform.SetParent(chunks, false);
            ItemWorldPlacement.Attach(null, item, item.transform.position, explicitOwner);
            sample["explicitOwnerPreserved"] = item.transform.parent == explicitOwner.transform;

            item.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(item, second);
            ItemWorldPlacement.AttachToSceneItemCategory(null, item);
            sample["migrationGrouped"] = item.transform.parent == otherItems && item.scene == second;
            sample["migrationPosePreserved"] = item.transform.localToWorldMatrix == pose;

            var staging = new GameObject("InactivePreparation");
            staging.SetActive(false);
            staging.transform.SetParent(RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first,
                RuntimeSceneObjectCategory.Preparation), false);
            sample["inactivePreserved"] = !staging.activeSelf && !staging.activeInHierarchy;

            Object.DestroyImmediate(items.gameObject);
            Transform replacement = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first, RuntimeSceneObjectCategory.Items);
            sample["categoryRecreated"] = replacement != null && replacement.parent == chunks.parent;
            Object.DestroyImmediate(chunks.root.gameObject);
            Transform recreated = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(first, RuntimeSceneObjectCategory.Items);
            sample["rootRecreated"] = recreated != null && recreated.gameObject.scene == first;
            sample["otherSceneSurvives"] = otherItems != null && item != null && item.transform.parent == otherItems;
            sample["oneSceneRoot"] = first.GetRootGameObjects().Length == 1 && second.GetRootGameObjects().Length == 1;
            return sample;
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(first, true);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(second, true);
        }
#else
        return new JObject { ["available"] = false };
#endif
    }
    #endregion
}
