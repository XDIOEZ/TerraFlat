using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>在独立 Prefab 内容中检查玩家模板、模块初始化和依赖，不启动游戏或保存资源。</summary>
public static class PlayerModuleAssemblyDiagnostics
{
    #region 玩家能力契约

    private const string PlayerPrefabPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
    private static readonly Dictionary<Type, string> FixedIdentities = new()
    {
        [typeof(Mod_Temperature)] = ModText.Temperature,
        [typeof(Mod_Stamina)] = ModText.Stamina,
        [typeof(Mod_Cam)] = ModText.Camera,
        [typeof(Mod_AnimatorController)] = ModText.AnimatorReceiver,
        [typeof(Mod_PlayerTraits)] = Mod_PlayerTraits.ModuleId,
        [typeof(Mod_Equipment)] = ModText.Equipment_Module,
        [typeof(Mod_ItemPicker)] = ModText.Picker,
        [typeof(Mod_San)] = Mod_San.ModuleId,
    };

    #endregion

    #region 静态装配入口

    [MenuItem("FlatWorld/诊断/校验玩家模块装配")]
    public static void Validate() => Debug.Log(Run());

    /// <summary>同时覆盖原始模板、空 ID、空白 ID 和错误 ID，避免依赖 Awake 的初始化顺序。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请退出 Play Mode 后手动运行玩家静态装配检查。");

        int moduleCount = ValidateScenario(false, null, out int binderCount);
        ValidateScenario(true, null, out _);
        ValidateScenario(true, "  ", out _);
        ValidateScenario(true, "incorrect-prefab-id", out _);
        return $"[PlayerModuleAssemblyDiagnostics] PASS：4 组模板身份检查，{moduleCount} 个模块初始化，{binderCount} 个依赖绑定器；未启动游戏或保存资源。";
    }

    private static int ValidateScenario(bool corruptId, string invalidId, out int binderCount)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Player player = root.GetComponent<Player>();
            Check(player != null, "Player Prefab 缺少 Player 组件。");
            var modules = new List<Module>();
            foreach (Module module in root.GetComponentsInChildren<Module>(true))
            {
                if (module.GetComponentInParent<Item>(true) != player) continue;
                Check(module._Data != null, $"{module.GetType().Name} 缺少模板数据。");
                if (corruptId && IsFixedIdentity(module)) module._Data.ModuleId = invalidId;
                modules.Add(module);
            }

            Data_Player first = player.Get_NewItemData() as Data_Player;
            Data_Player second = player.Get_NewItemData() as Data_Player;
            Check(first != null && second != null, "玩家模板未生成 Data_Player。");
            Check(first.ModuleDataDic.Count == modules.Count, "模板模块集合与玩家层级不一致。");
            foreach (var pair in first.ModuleDataDic)
                Check(!ReferenceEquals(pair.Value, second.ModuleDataDic[pair.Key]), $"模块 {pair.Key} 在两个玩家之间共享实例数据。");

            player.BindData(first);
            foreach (Module module in modules)
            {
                module._Data = first.ModuleDataDic[module.StableName];
                player.itemMods.AddMod(module);
            }
            foreach (Module module in modules) module.ModuleInit(player, module._Data);

            ValidateRequiredCapabilities(player.itemMods);
            Check(root.GetComponentsInChildren<Mod_PlayerAdminController>(true).Length == 1,
                "玩家管理员模块必须只有一份。");

            binderCount = 0;
            foreach (Module module in modules)
            {
                if (module is not IItemModuleDependencyBinder binder) continue;
                binder.BindModuleDependencies(player.itemMods);
                binderCount++;
            }
            foreach (Module module in modules)
            {
                Check(ReferenceEquals(player.itemMods.GetMod_ByName(module.StableName), module),
                    $"模块 {module.GetType().Name} 初始化后修改了稳定名。");
                IReadOnlyList<Module> group = player.itemMods.GetModList_ByID(module.ResolvedModuleId);
                Check(group != null && Contains(group, module), $"模块 {module.GetType().Name} 初始化后修改了能力 ID。");
            }
            return modules.Count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    #endregion

    #region 依赖与身份检查

    private static bool IsFixedIdentity(Module module)
    {
        foreach (Type type in FixedIdentities.Keys)
            if (type.IsInstanceOfType(module)) return true;
        return false;
    }

    private static void ValidateRequiredCapabilities(ItemMods modules)
    {
        modules.RequireSingleModById<Mod_Temperature>(ModText.Temperature);
        modules.RequireSingleModById<Mod_Stamina>(ModText.Stamina);
        modules.RequireSingleModById<Mod_Cam>(ModText.Camera);
        modules.RequireSingleModById<Mod_AnimatorController>(ModText.AnimatorReceiver);
        modules.RequireSingleModById<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        modules.RequireSingleModById<Mod_Equipment>(ModText.Equipment_Module);
        modules.RequireSingleModById<Mod_ItemPicker>(ModText.Picker);
        modules.RequireSingleModById<Mod_GameController>(ModText.Controller);
        modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        modules.RequireSingleModById<Mod_Food>(ModText.Food);
        modules.RequireSingleModById<Mod_BuffManager>(ModText.Mod_BuffManager);
        modules.RequireSingleModById<Mod_Mover>(ModText.Mod_Mover);
        modules.RequireSingleModById<Mod_ChunkLoader>(ModText.ChunkLoader);
        modules.RequireSingleModById<Mod_FocusPoint>(ModText.FocusPoint);
        modules.RequireSingleModById<Mod_TurnBack>(ModText.TrunBody);
        modules.RequireSingleModById<Mod_HotBar>(ModText.Hotbar);
        modules.RequireSingleModById<Mod_Oxygen>(ModText.Oxygen);
        modules.RequireSingleModById<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
        foreach (Module module in modules.Mods.Values)
        {
            foreach (var pair in FixedIdentities)
            {
                if (pair.Key.IsInstanceOfType(module))
                    Check(module.ResolvedModuleId == pair.Value, $"{module.GetType().Name} 的能力 ID 应为 {pair.Value}。");
            }
        }
    }

    private static bool Contains(IReadOnlyList<Module> modules, Module expected)
    {
        for (int i = 0; i < modules.Count; i++)
            if (ReferenceEquals(modules[i], expected)) return true;
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    #endregion
}
