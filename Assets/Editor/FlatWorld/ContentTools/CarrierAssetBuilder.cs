using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>显式生成载具模块及无物理外壳；不自动启动迁移，父任务在编译后主动执行菜单。</summary>
public static class CarrierAssetBuilder
{
    #region 显式构建
    public const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/Module_Carrier.prefab";
    public const string ShellPath = "Assets/2_Prefabs/Gameplay/Items/Common/CarrierBodyShell.prefab";
    public const string SpritePath = "Assets/6_Art/Generated/Vehicles/Boat/Boat_Carrier.png";

    [MenuItem("FlatWorld/其他工具/载具/构建船模块与外壳")]
    public static void BuildBoatAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式显式构建载具资源。");
        if (AddressableAssetSettingsDefaultObject.Settings == null)
            throw new InvalidOperationException("缺少 Addressables Settings。");
        if (AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath) == null)
            throw new InvalidOperationException("简易木筏 Sprite 不存在或不是单图 Sprite。");
        GameObject module = new("Module_Carrier");
        try
        {
            Mod_Carrier carrier = module.AddComponent<Mod_Carrier>();
            carrier.Data.ID = Mod_Carrier.ModuleId;
            carrier.Data.Name = "carrier";
            carrier.Data.WriteData(new CarrierSaveState());
            PrefabUtility.SaveAsPrefabAsset(module, ModulePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(module); }

        GameObject shell = PrefabUtility.LoadPrefabContents("Assets/2_Prefabs/Gameplay/Items/Common/BuildingBodyShell.prefab");
        try
        {
            shell.name = "CarrierBodyShell";
            foreach (ItemPhysicsTemplate2D child in shell.GetComponentsInChildren<ItemPhysicsTemplate2D>(true))
                if (child.gameObject != shell)
                    UnityEngine.Object.DestroyImmediate(child);

            ItemPhysicsTemplate2D physics = shell.GetComponent<ItemPhysicsTemplate2D>();
            if (physics == null) physics = shell.AddComponent<ItemPhysicsTemplate2D>();
            physics.owningModule = null;
            physics.colliderType = nameof(BoxCollider2D);
            physics.colliderEnabled = false;
            physics.isTrigger = false;
            physics.offset = Vector2.zero;
            physics.size = new Vector2(1.5f, 1f);
            physics.hasBody = true;
            physics.bodyType = RigidbodyType2D.Kinematic;
            physics.bodySimulated = true;
            physics.bodyUseAutoMass = false;
            physics.bodyMass = 60f;
            physics.bodyLinearDrag = 0f;
            physics.bodyAngularDrag = 0.05f;
            physics.bodyGravityScale = 0f;
            physics.bodyInterpolation = RigidbodyInterpolation2D.None;
            physics.bodyCollisionDetection = CollisionDetectionMode2D.Continuous;
            physics.bodyConstraints = RigidbodyConstraints2D.FreezeRotation;
            Item item = shell.GetComponent<Item>();
            item.itemData.IDName = "CarrierBodyShell";
            SpriteRenderer renderer = shell.GetComponentInChildren<SpriteRenderer>(true);
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            PrefabUtility.SaveAsPrefabAsset(shell, ShellPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(shell); }
        Register(ModulePath, "Module_Carrier", "Prefab");
        Register(ShellPath, "CarrierBodyShell", "Prefab");
        Register(SpritePath, SpritePath, "ItemSprite");
        AssetDatabase.SaveAssets();
        VerifyBoatAssets();
    }

    /// <summary>只验证当前资源，不刷新、不进入 Play、不生成缺失资源。</summary>
    [MenuItem("FlatWorld/其他工具/载具/静态验证船资源")]
    public static void VerifyBoatAssets()
    {
        GameObject module = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath);
        GameObject shell = AssetDatabase.LoadAssetAtPath<GameObject>(ShellPath);
        if (module == null || module.GetComponent<Mod_Carrier>()?.CanonicalModuleId != Mod_Carrier.ModuleId)
            throw new InvalidOperationException("缺少正确的 Module_Carrier Prefab。");
        ItemPhysicsTemplate2D physics = shell != null ? shell.GetComponent<ItemPhysicsTemplate2D>() : null;
        if (shell == null || shell.GetComponent<Item>() == null || physics == null ||
            physics.colliderType != nameof(BoxCollider2D) || physics.colliderEnabled ||
            !physics.hasBody || !physics.bodySimulated || physics.bodyType != RigidbodyType2D.Kinematic ||
            shell.GetComponent<Collider2D>() != null || shell.GetComponent<Rigidbody2D>() != null)
            throw new InvalidOperationException("载具外壳必须声明禁用的根碰撞框与运动学刚体，实际组件由世界物理服务领取。");
        VerifyEntry(ModulePath, "Module_Carrier");
        VerifyEntry(ShellPath, "CarrierBodyShell");
        Debug.Log("载具静态资源检查通过；尚未验证运行时乘坐、输入或存档。");
    }

    private static void Register(string path, string address, string label)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(settings);
    }

    private static void VerifyEntry(string path, string address)
    {
        var entry = AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
        if (entry == null || entry.address != address || !entry.labels.Contains("Prefab"))
            throw new InvalidOperationException("载具 Prefab 地址或标签不匹配：" + path);
    }
    #endregion
}
