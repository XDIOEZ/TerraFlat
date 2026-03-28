using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEditor.Animations;

public static class SyncAllPrefabItemDataEditor
{
    private const string WeaponRootFolder = "Assets/2_Prefabs/Weapon";
    private const string WeaponFolder = "Assets/2_Prefabs/Weapon/Weapon";
    private const string LegacySuffix = "(Legacy)";
    private const string ObsoleteTag = "Obsolete";
    private const string ModernTag = "Modern";
    private const string AnimationTemplatePath = "Assets/2_Prefabs/Weapon/Weapon/ChippedTool.prefab";
    private const string AnimationActionNodeName = "Module_Weapon_AnimationAction";
    private const string ModDamagePrefabPath = "Assets/2_Prefabs/Module/Combat/Mod_Damage.prefab";

    [MenuItem("FlatWorld/同步所有Prefab ItemData（名字+Guid）")]
    public static void SyncAllPrefabItemsInAssets()
    {
        Debug.Log("开始同步所有 Prefab ItemData");

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int total = prefabGuids.Length;

        if (total == 0)
        {
            Debug.LogWarning("未找到任何 Prefab。");
            return;
        }

        int syncedCount = 0;
        int skippedCount = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    skippedCount++;
                    continue;
                }

                EditorUtility.DisplayProgressBar("同步Prefab ItemData", $"同步 {assetPath} ({i + 1}/{total})", (float)i / total);

                try
                {
                    if (!prefab.TryGetComponent<Item>(out Item item))
                    {
                        item = prefab.GetComponentInChildren<Item>(true);
                    }

                    if (item == null || item.itemData == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    item.itemData.IDName = item.gameObject.name;
                    if (string.IsNullOrEmpty(item.itemData.GameName))
                    {
                        item.itemData.GameName = item.gameObject.name;
                    }

                    item.SyncItemData();
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);

                    syncedCount++;
                    if ((i + 1) % 20 == 0)
                    {
                        AssetDatabase.SaveAssets();
                    }
                }
                catch (System.Exception ex)
                {
                    skippedCount++;
                    Debug.LogError($"[SyncAllPrefabItemDataEditor] 处理 Prefab {assetPath} 出错: {ex.Message}\n{ex.StackTrace}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Prefab ItemData 同步完成：成功 {syncedCount}，跳过/失败 {skippedCount}，总数 {total}。");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("FlatWorld/武器/批量拆分旧武器为Legacy+新版")]
    public static void SplitLegacyWeaponsAndCreateModernCopies()
    {
        if (!AssetDatabase.IsValidFolder(WeaponFolder))
        {
            Debug.LogError($"[WeaponSplit] 未找到目录: {WeaponFolder}");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponFolder });
        if (prefabGuids.Length == 0)
        {
            Debug.LogWarning("[WeaponSplit] 目录内没有Prefab。");
            return;
        }

        int scanned = 0;
        int converted = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                scanned++;
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("拆分旧武器", $"处理中 {fileName} ({i + 1}/{prefabGuids.Length})", (float)i / prefabGuids.Length);

                if (fileName.EndsWith(LegacySuffix))
                {
                    skipped++;
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    skipped++;
                    continue;
                }

                if (!IsLegacyWeaponPrefab(prefab))
                {
                    skipped++;
                    continue;
                }

                string legacyName = fileName + LegacySuffix;
                string legacyPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, legacyName + ".prefab").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath) != null)
                {
                    // 已有 Legacy 资产，说明这把武器已经处理过。
                    skipped++;
                    continue;
                }

                string renameError = AssetDatabase.RenameAsset(path, legacyName);
                if (!string.IsNullOrEmpty(renameError))
                {
                    skipped++;
                    Debug.LogError($"[WeaponSplit] 重命名失败: {path} -> {legacyName}, {renameError}");
                    continue;
                }

                AssetDatabase.SaveAssets();

                SetItemMetadataTagAndName(legacyPath, legacyName, ObsoleteTag);

                string newPath = path;
                if (!AssetDatabase.CopyAsset(legacyPath, newPath))
                {
                    skipped++;
                    Debug.LogError($"[WeaponSplit] 复制新版失败: {legacyPath} -> {newPath}");
                    continue;
                }

                SetItemMetadataTagAndName(newPath, fileName, ModernTag);
                converted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponSplit] 完成。扫描={scanned}, 转换={converted}, 跳过={skipped}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("FlatWorld/武器/修复Weapon目录新旧标签")]
    public static void NormalizeWeaponLegacyAndModernTags()
    {
        if (!AssetDatabase.IsValidFolder(WeaponFolder))
        {
            Debug.LogError($"[WeaponTagFix] 未找到目录: {WeaponFolder}");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponFolder });
        int fixedCount = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("修复武器标签", $"处理中 {fileName} ({i + 1}/{prefabGuids.Length})", (float)i / prefabGuids.Length);

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    skipped++;
                    continue;
                }

                if (!prefab.TryGetComponent<Item>(out Item item))
                {
                    item = prefab.GetComponentInChildren<Item>(true);
                }

                if (item == null || item.itemData == null)
                {
                    skipped++;
                    continue;
                }

                bool isLegacy = fileName.EndsWith(LegacySuffix);
                bool changed = false;

                changed |= SetTagPresence(item.itemData.Tags, ObsoleteTag, isLegacy);
                changed |= SetTagPresence(item.itemData.Tags, ModernTag, !isLegacy);

                if (changed)
                {
                    item.SyncItemData();
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                    fixedCount++;
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponTagFix] 完成。修复={fixedCount}, 跳过={skipped}, 总数={prefabGuids.Length}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("FlatWorld/武器/迁移为动画驱动（停用冷兵器模组）")]
    public static void MigrateWeaponsToAnimationDriven()
    {
        if (!AssetDatabase.IsValidFolder(WeaponFolder))
        {
            Debug.LogError($"[WeaponAnimMigrate] 未找到目录: {WeaponFolder}");
            return;
        }

        RuntimeAnimatorController templateController = ResolveTemplateController();
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponFolder });

        int migrated = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("迁移动画驱动", $"处理中 {fileName} ({i + 1}/{prefabGuids.Length})", (float)i / prefabGuids.Length);

                if (fileName.EndsWith(LegacySuffix))
                {
                    skipped++;
                    continue;
                }

                if (TryMigrateSingleWeaponPrefab(path, templateController))
                {
                    migrated++;
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponAnimMigrate] 完成。迁移={migrated}, 跳过={skipped}, 总数={prefabGuids.Length}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("FlatWorld/武器/全量迁移所有武器工具为动画驱动")]
    public static void MigrateAllWeaponAndToolPrefabsToAnimationDriven()
    {
        if (!AssetDatabase.IsValidFolder(WeaponRootFolder))
        {
            Debug.LogError($"[WeaponAnimMigrate-All] 未找到目录: {WeaponRootFolder}");
            return;
        }

        RuntimeAnimatorController templateController = ResolveTemplateController();
        string[] prefabPaths = CollectNonLegacyPrefabPaths(WeaponRootFolder);

        int migrated = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < prefabPaths.Length; i++)
            {
                string path = prefabPaths[i];
                string fileName = Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("全量迁移动画驱动", $"处理中 {fileName} ({i + 1}/{prefabPaths.Length})", (float)i / prefabPaths.Length);

                if (TryMigrateSingleWeaponPrefab(path, templateController))
                {
                    migrated++;
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponAnimMigrate-All] 完成。迁移={migrated}, 跳过={skipped}, 总数={prefabPaths.Length}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("FlatWorld/武器/迁移这四个Axe为动画驱动")]
    public static void MigrateFourAxePrefabsToAnimationDriven()
    {
        string[] targetPaths =
        {
            "Assets/2_Prefabs/Weapon/Axe/Axe_Bronze.prefab",
            "Assets/2_Prefabs/Weapon/Axe/Axe_Copper.prefab",
            "Assets/2_Prefabs/Weapon/Axe/Axe_Iron.prefab",
            "Assets/2_Prefabs/Weapon/Axe/Axe_RawIron.prefab"
        };

        RuntimeAnimatorController templateController = ResolveTemplateController();
        int migrated = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < targetPaths.Length; i++)
            {
                string path = targetPaths[i];
                string fileName = Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("迁移四个Axe", $"处理中 {fileName} ({i + 1}/{targetPaths.Length})", (float)i / targetPaths.Length);

                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    skipped++;
                    continue;
                }

                if (TryMigrateSingleWeaponPrefab(path, templateController))
                {
                    migrated++;
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponAnimMigrate-Axe4] 完成。迁移={migrated}, 跳过={skipped}, 总数={targetPaths.Length}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static bool TryMigrateSingleWeaponPrefab(string path, RuntimeAnimatorController templateController)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Item item = null;
            if (!prefabRoot.TryGetComponent<Item>(out item))
            {
                item = prefabRoot.GetComponentInChildren<Item>(true);
            }
            bool hasItemData = item != null && item.itemData != null;

            bool changed = false;

            // 非武器工具或无结构需求的Item直接跳过，避免误改。
            bool hasDamageSenderChild = false;
            Transform[] allTransforms = prefabRoot.GetComponentsInChildren<Transform>(true);
            foreach (Transform trans in allTransforms)
            {
                if (trans != prefabRoot.transform && trans.name.IndexOf("DamageSender", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasDamageSenderChild = true;
                    break;
                }
            }

            bool hasColdWeapon = prefabRoot.GetComponentInChildren<Mod_ColdWeapon>(true) != null;
            bool hasAnimationAction = prefabRoot.GetComponentInChildren<Mod_Weapon_AnimationAction>(true) != null;
            bool hasDamage = prefabRoot.GetComponentInChildren<Mod_Damage>(true) != null;
            bool hasRender = prefabRoot.transform.Find("Render") != null;
            if (!hasColdWeapon && !hasAnimationAction && !hasDamageSenderChild && !hasDamage && !hasRender)
            {
                return false;
            }

            // 1) 停用旧冷兵器程序控制模块
            Mod_ColdWeapon[] coldWeapons = prefabRoot.GetComponentsInChildren<Mod_ColdWeapon>(true);
            foreach (Mod_ColdWeapon coldWeapon in coldWeapons)
            {
                if (coldWeapon.enabled)
                {
                    coldWeapon.enabled = false;
                    changed = true;
                }
            }

            // 2) 补齐Animator
            Animator animator = prefabRoot.GetComponent<Animator>();
            if (animator == null)
            {
                animator = prefabRoot.AddComponent<Animator>();
                changed = true;
            }

            if (animator.runtimeAnimatorController == null && templateController != null)
            {
                animator.runtimeAnimatorController = templateController;
                changed = true;
            }

            // 3) 强制动画模块使用独立子节点：Root/Module_Weapon_AnimationAction
            Transform actionNodeTransform = prefabRoot.transform.Find(AnimationActionNodeName);
            if (actionNodeTransform == null)
            {
                GameObject actionNode = new GameObject(AnimationActionNodeName);
                actionNode.transform.SetParent(prefabRoot.transform, false);
                actionNodeTransform = actionNode.transform;
                changed = true;
            }

            Mod_Weapon_AnimationAction actionModule = actionNodeTransform.GetComponent<Mod_Weapon_AnimationAction>();
            if (actionModule == null)
            {
                actionModule = actionNodeTransform.gameObject.AddComponent<Mod_Weapon_AnimationAction>();
                changed = true;
            }

            Mod_Weapon_AnimationAction rootActionModule = prefabRoot.GetComponent<Mod_Weapon_AnimationAction>();
            if (rootActionModule != null)
            {
                EditorUtility.CopySerialized(rootActionModule, actionModule);
                Object.DestroyImmediate(rootActionModule, true);
                changed = true;
            }

            if (actionModule.animator != animator)
            {
                actionModule.animator = animator;
                changed = true;
            }

            // 统一使用角色输入，不走本地Input.GetMouseButton
            SerializedObject actionSo = new SerializedObject(actionModule);
            SerializedProperty useLocalInputProperty = actionSo.FindProperty("useLocalInput");
            if (useLocalInputProperty != null && useLocalInputProperty.boolValue)
            {
                useLocalInputProperty.boolValue = false;
                actionSo.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            // 4) 清理旧伤害层级：删除DamageSender，并将Mod_Damage挂到Render下
            changed |= CleanupDamageHierarchy(prefabRoot);

            // 保证非Legacy武器至少带Modern标签
            if (hasItemData)
            {
                changed |= SetTagPresence(item.itemData.Tags, ObsoleteTag, false);
                changed |= SetTagPresence(item.itemData.Tags, ModernTag, true);
            }

            if (!changed)
            {
                return false;
            }

            if (hasItemData)
            {
                item.SyncItemData();
            }
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool IsLegacyWeaponPrefab(GameObject prefab)
    {
        bool hasAnimator = prefab.GetComponent<Animator>() != null;
        bool hasAnimationAction = prefab.GetComponentInChildren<Mod_Weapon_AnimationAction>(true) != null;
        return !hasAnimator || !hasAnimationAction;
    }

    private static RuntimeAnimatorController ResolveTemplateController()
    {
        GameObject templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimationTemplatePath);
        if (templatePrefab == null)
        {
            Debug.LogWarning($"[WeaponAnimMigrate] 模板预制体不存在: {AnimationTemplatePath}");
            return null;
        }

        Animator templateAnimator = templatePrefab.GetComponent<Animator>();
        if (templateAnimator == null)
        {
            Debug.LogWarning($"[WeaponAnimMigrate] 模板预制体没有Animator: {AnimationTemplatePath}");
            return null;
        }

        if (templateAnimator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[WeaponAnimMigrate] 模板Animator未设置Controller: {AnimationTemplatePath}");
            return null;
        }

        return templateAnimator.runtimeAnimatorController;
    }

    private static bool CleanupDamageHierarchy(GameObject prefabRoot)
    {
        bool changed = false;

        Transform render = prefabRoot.transform.Find("Render");
        if (render == null)
        {
            return false;
        }

        Transform modDamage = null;
        Transform modDamageByName = prefabRoot.transform.Find("Mod_Damage");
        if (modDamageByName != null)
        {
            modDamage = modDamageByName;
        }
        else
        {
            Mod_Damage damageComponent = prefabRoot.GetComponentInChildren<Mod_Damage>(true);
            if (damageComponent != null)
            {
                modDamage = damageComponent.transform;
            }
        }

        if (modDamage != null && modDamage.parent != render)
        {
            modDamage.SetParent(render, true);
            changed = true;
        }

        if (modDamage == null)
        {
            GameObject modDamagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModDamagePrefabPath);
            if (modDamagePrefab != null)
            {
                GameObject modDamageInstance = PrefabUtility.InstantiatePrefab(modDamagePrefab, prefabRoot.scene) as GameObject;
                if (modDamageInstance != null)
                {
                    modDamageInstance.transform.SetParent(render, false);
                    modDamageInstance.name = "Mod_Damage";
                    changed = true;
                }
            }
        }
        else if (modDamage.name != "Mod_Damage")
        {
            modDamage.name = "Mod_Damage";
            changed = true;
        }

        var childrenToDelete = new System.Collections.Generic.List<Transform>();
        Transform[] allTransforms = prefabRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allTransforms)
        {
            if (child == prefabRoot.transform)
            {
                continue;
            }

            if (child.name.IndexOf("DamageSender", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                childrenToDelete.Add(child);
            }
        }

        foreach (Transform child in childrenToDelete)
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
            {
                PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Object.DestroyImmediate(child.gameObject, true);
            changed = true;
        }

        return changed;
    }

    private static void SetItemMetadataTagAndName(string prefabPath, string targetName, string tagToAdd)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            throw new System.InvalidOperationException($"[WeaponSplit] 无法加载Prefab: {prefabPath}");
        }

        if (!prefab.TryGetComponent<Item>(out Item item))
        {
            item = prefab.GetComponentInChildren<Item>(true);
        }

        if (item == null || item.itemData == null)
        {
            throw new System.InvalidOperationException($"[WeaponSplit] Prefab缺少Item或itemData: {prefabPath}");
        }

        prefab.name = targetName;
        item.gameObject.name = targetName;
        item.itemData.IDName = targetName;
        item.itemData.GameName = targetName;

        bool isLegacyTarget = tagToAdd == ObsoleteTag;
        SetTagPresence(item.itemData.Tags, ObsoleteTag, isLegacyTarget);
        SetTagPresence(item.itemData.Tags, ModernTag, !isLegacyTarget);

        item.SyncItemData();
        EditorUtility.SetDirty(prefab);
        PrefabUtility.SavePrefabAsset(prefab);
    }

    private static bool SetTagPresence(System.Collections.Generic.List<string> tags, string tag, bool shouldExist)
    {
        int index = tags.IndexOf(tag);
        if (shouldExist)
        {
            if (index >= 0)
            {
                return false;
            }

            tags.Add(tag);
            return true;
        }

        if (index < 0)
        {
            return false;
        }

        tags.RemoveAt(index);
        return true;
    }

    private static string[] CollectNonLegacyPrefabPaths(string folder)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        var paths = new System.Collections.Generic.List<string>(prefabGuids.Length);

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.EndsWith(LegacySuffix))
            {
                continue;
            }

            paths.Add(path);
        }

        return paths.ToArray();
    }
}
