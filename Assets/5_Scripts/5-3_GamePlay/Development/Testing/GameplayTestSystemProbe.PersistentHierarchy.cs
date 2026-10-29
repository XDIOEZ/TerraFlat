using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class GameplayTestSystemProbe
{
    #region 常驻分类的编辑态规则采样
    private static JObject ServicePersistentObjectHierarchy(JObject args)
    {
#if UNITY_EDITOR
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        Scene other = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("PersistentHierarchyRuleFixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            var hierarchy = root.AddComponent<PersistentRuntimeObjectHierarchy>();
            var owner = new GameObject("OwnedService");
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.transform.SetPositionAndRotation(new Vector3(13, -7, 2), Quaternion.Euler(0, 0, 31));
            owner.transform.localScale = new Vector3(2, 3, 1);
            Matrix4x4 pose = owner.transform.localToWorldMatrix;
            var child = new GameObject("OwnedChild");
            child.transform.SetParent(owner.transform, false);
            bool grouped = hierarchy.TryGroupOwnedRoot(owner, PersistentRuntimeObjectCategory.World);
            Transform group = owner.transform.parent;
            int groups = root.transform.childCount;
            bool repeated = hierarchy.TryGroupOwnedRoot(owner, PersistentRuntimeObjectCategory.World);
            var sample = new JObject
            {
                ["available"] = true,
                ["grouped"] = grouped && group.name == "World" && group.parent == root.transform,
                ["idempotent"] = repeated && owner.transform.parent == group && root.transform.childCount == groups,
                ["posePreserved"] = owner.transform.localToWorldMatrix == pose,
                ["unitCategory"] = group.localToWorldMatrix == Matrix4x4.identity,
                ["ownerTreePreserved"] = child.transform.parent == owner.transform &&
                    PersistentRuntimeObjectHierarchy.GetBusinessOwnerRoot(child) == owner,
                ["childCategoryResolved"] = hierarchy.TryGetObjectCategory(child, out var childCategory) &&
                    childCategory == PersistentRuntimeObjectCategory.World,
                ["childCannotBeStolen"] = !hierarchy.TryGroupOwnedRoot(child, PersistentRuntimeObjectCategory.Debug) &&
                    child.transform.parent == owner.transform,
                ["structuralNodesRejected"] = !hierarchy.TryGroupOwnedRoot(root, PersistentRuntimeObjectCategory.Core) &&
                    !hierarchy.TryGroupOwnedRoot(group.gameObject, PersistentRuntimeObjectCategory.Core)
            };
            owner.SetActive(false);
            hierarchy.TryGroupOwnedRoot(owner, PersistentRuntimeObjectCategory.Resources);
            sample["inactivePreserved"] = !owner.activeSelf && !owner.activeInHierarchy;
            sample["recategorizedWithoutCloning"] = owner.transform.parent.name == "Resources" &&
                owner.transform.localToWorldMatrix == pose && child.transform.parent == owner.transform;

            var foreign = new GameObject("OtherSceneOwner");
            SceneManager.MoveGameObjectToScene(foreign, other);
            sample["foreignSceneRejected"] = !hierarchy.TryGroupOwnedRoot(foreign, PersistentRuntimeObjectCategory.Core) &&
                foreign.scene == other && foreign.transform.parent == null;
            bool invalidRejected = false;
            int beforeInvalid = root.transform.childCount;
            try { hierarchy.TryGroupOwnedRoot(owner, (PersistentRuntimeObjectCategory)999); }
            catch (ArgumentOutOfRangeException) { invalidRejected = true; }
            sample["invalidCategoryRejected"] = invalidRejected && root.transform.childCount == beforeInvalid;
            sample["editModeCannotPromoteScene"] = !Application.isPlaying &&
                !PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(foreign, PersistentRuntimeObjectCategory.Core) &&
                foreign.scene == other;

            Object.DestroyImmediate(group.gameObject);
            Transform replacement = hierarchy.GetOrCreateCategoryRoot(PersistentRuntimeObjectCategory.World);
            sample["destroyedCategoryRecreated"] = replacement != null && replacement != group &&
                replacement.parent == root.transform;
            Object.DestroyImmediate(owner);
            sample["ownerReleaseDoesNotDestroyHierarchy"] = root != null && replacement != null && child == null;
            Object.DestroyImmediate(root);
            sample["otherSceneUnaffected"] = foreign != null && foreign.scene == other;
            return sample;
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(other);
        }
#else
        return new JObject { ["available"] = false };
#endif
    }
    #endregion
}
