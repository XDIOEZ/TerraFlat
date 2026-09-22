using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通用燃烧组合资源装配器：生成燃烧链所需的独立模块 Prefab 与燃料交互面板。
/// 具体物品只在 JSON 中选择这些组件并填写参数。
/// </summary>
public static class CombustionAssetBuilder
{
    public const string Menu = "FlatWorld/Content/Combustion/Build Runtime Assets";
    private const string CombustionModulePath =
        "Assets/2_Prefabs/Gameplay/Modules/World/Module_Combustion.prefab";
    private const string FuelInteractionModulePath =
        "Assets/2_Prefabs/Gameplay/Modules/World/Module_FuelInteraction.prefab";
    private const string LightSourceModulePath =
        "Assets/2_Prefabs/Gameplay/Modules/World/Module_LightSource.prefab";
    private const string CombustionVisualModulePath =
        "Assets/2_Prefabs/Gameplay/Modules/World/Module_CombustionVisual.prefab";
    private const string LocalTemperatureModulePath =
        "Assets/2_Prefabs/Gameplay/Modules/World/Module_LocalTemperatureSource.prefab";

    [MenuItem(Menu)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在非 Play 模式装配燃烧资源。");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
            throw new InvalidOperationException("Addressables 默认组尚未配置。");

        Directory.CreateDirectory(Path.GetDirectoryName(CombustionModulePath));
        BuildModule<Mod_Combustion>(settings, CombustionModulePath, "Module_Combustion",
            module =>
            {
                module._Data.ID = Mod_Combustion.ModuleId;
                module._Data.Name = "combustion";
                module._Data.isRunning = true;
            });
        BuildModule<Mod_FuelInteraction>(settings, FuelInteractionModulePath, "Module_FuelInteraction",
            module =>
            {
                module._Data.ID = Mod_FuelInteraction.ModuleId;
                module._Data.Name = "fuelInteraction";
                module._Data.isRunning = true;
            });
        BuildLightSourceModule(settings);
        BuildCombustionVisualModule(settings);
        BuildLocalTemperatureModule(settings);

        RuntimeUIPrefabBuilder.RebuildFuelInteractionUI();
        AssetDatabase.SaveAssets();
        Debug.Log("[CombustionAssetBuilder] 已生成独立的燃烧、燃料交互、光源、燃烧视觉、局部温度模块与 UI。");
    }

    private static void BuildLightSourceModule(AddressableAssetSettings settings)
    {
        var root = new GameObject("Module_LightSource");
        try
        {
            Light2D light = root.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.intensity = 1f;
            light.pointLightInnerRadius = 0.1f;
            light.pointLightOuterRadius = 8f;

            Mod_LightSource module = root.AddComponent<Mod_LightSource>();
            module._Data.ID = ModText.LightSource;
            module._Data.Name = "light";
            module._Data.isRunning = true;
            module.TargetLight = light;
            SaveAndRegister(settings, root, LightSourceModulePath, "Module_LightSource");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void BuildCombustionVisualModule(AddressableAssetSettings settings)
    {
        var root = new GameObject("Module_CombustionVisual");
        try
        {
            root.AddComponent<CombustionVisualEffect>();
            Mod_CombustionVisual module = root.AddComponent<Mod_CombustionVisual>();
            module._Data.ID = Mod_CombustionVisual.ModuleId;
            module._Data.Name = "combustionVisual";
            module._Data.isRunning = true;
            SaveAndRegister(settings, root, CombustionVisualModulePath, "Module_CombustionVisual");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void BuildLocalTemperatureModule(AddressableAssetSettings settings)
    {
        var root = new GameObject("Module_LocalTemperatureSource");
        try
        {
            root.AddComponent<LocalTemperatureSource>();
            Mod_LocalTemperatureSource module = root.AddComponent<Mod_LocalTemperatureSource>();
            module._Data.ID = Mod_LocalTemperatureSource.ModuleId;
            module._Data.Name = "localTemperature";
            module._Data.isRunning = true;
            SaveAndRegister(settings, root, LocalTemperatureModulePath, "Module_LocalTemperatureSource");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void BuildModule<T>(
        AddressableAssetSettings settings,
        string path,
        string address,
        Action<T> configure) where T : Module
    {
        var root = new GameObject(address);
        try
        {
            T module = root.AddComponent<T>();
            configure(module);
            SaveAndRegister(settings, root, path, address);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SaveAndRegister(
        AddressableAssetSettings settings,
        GameObject root,
        string path,
        string address)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
                                      settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = address;
        entry.SetLabel("Prefab", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

}
