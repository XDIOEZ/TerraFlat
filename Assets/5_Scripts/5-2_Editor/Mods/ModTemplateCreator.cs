#if UNITY_EDITOR
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class ModTemplateCreator
{
    #region 菜单

    [MenuItem("FlatWorld/MOD/创建示例 MOD")]
    private static void CreateExampleMod()
    {
        string root = Path.GetFullPath(Path.Combine("Assets", "FlatWorldMods", "example.flatworld.mod"));
        if (Directory.Exists(root) &&
            !EditorUtility.DisplayDialog("覆盖示例 MOD", $"目录已存在：\n{root}\n\n是否覆盖示例文件？", "覆盖", "取消"))
            return;

        Directory.CreateDirectory(Path.Combine(root, "Defs"));
        Directory.CreateDirectory(Path.Combine(root, "Patches"));
        Directory.CreateDirectory(Path.Combine(root, "Localization"));
        Directory.CreateDirectory(Path.Combine(root, "Settings"));
        Directory.CreateDirectory(Path.Combine(root, "Lua"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), CreateManifestJson());
        File.WriteAllText(Path.Combine(root, "Defs", "items.json"), CreateItemDefinitionsJson());
        File.WriteAllText(Path.Combine(root, "Patches", "balance.json"), CreatePatchJson());
        File.WriteAllText(Path.Combine(root, "Localization", "zh-CN.json"), CreateLocalizationJson("zh-CN", "训练短剑", "由完整 MOD Def 管线创建的训练武器。"));
        File.WriteAllText(Path.Combine(root, "Localization", "en.json"), CreateLocalizationJson("en", "Training Shortsword", "A training weapon created by the complete MOD Def pipeline."));
        File.WriteAllText(Path.Combine(root, "Settings", "settings.json"), CreateSettingsJson());
        File.WriteAllText(Path.Combine(root, "Lua", "main.lua"), CreateMainLua());
        File.WriteAllText(Path.Combine(root, "Lua", "actor.lua"), CreateActorLua());
        File.WriteAllText(Path.Combine(root, "README.md"), CreateReadme());

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(root);
        Debug.Log($"[MOD] 示例作者工程已创建：{root}。请使用“创作与打包工具”构建安装。");
    }

    [MenuItem("FlatWorld/MOD/打开 MOD 目录")]
    private static void OpenModsFolder()
    {
        string root = Path.Combine(Application.persistentDataPath, "Mods");
        Directory.CreateDirectory(root);
        EditorUtility.RevealInFinder(root);
    }

    #endregion

    #region 模板内容

    private static string CreateManifestJson()
    {
        ModManifest manifest = new()
        {
            Id = "example.flatworld.mod",
            Name = "FlatWorld 示例 MOD",
            Version = "1.0.0",
            Author = "FlatWorld",
            Description = "演示 Def 继承、Patch、本地化、设置和 Lua 游戏事件。",
            DefinitionFiles = new() { "Defs/items.json" },
            PatchFiles = new() { "Patches/balance.json" },
            LocalizationFiles = new() { "Localization/zh-CN.json", "Localization/en.json" },
            SettingsFile = "Settings/settings.json",
            EntryLua = "Lua/main.lua"
        };
        return JsonConvert.SerializeObject(manifest, Formatting.Indented);
    }

    private static string CreateItemDefinitionsJson()
    {
        ModDefinitionDocument document = new();
        document.Items.Add(new ModItemDefinition
        {
            Id = "example.flatworld.mod:weapon_base",
            Abstract = true,
            BasePrefab = "Sword_Bronze",
            Durability = 25f,
            MaxDurability = 25f,
            Tags = new() { "modded", "weapon", "training" }
        });
        document.Items.Add(new ModItemDefinition
        {
            Id = "example.flatworld.mod:training_sword",
            Parent = "example.flatworld.mod:weapon_base",
            LabelKey = "example.flatworld.mod:training_sword.label",
            DescriptionKey = "example.flatworld.mod:training_sword.description",
            Amount = 1f,
            Volume = 1f,
            CanBePickedUp = true
        });
        JObject root = JObject.FromObject(document);
        root["actors"] = new JArray
        {
            new JObject
            {
                ["id"] = "example.flatworld.mod:forest_wolf",
                ["parent"] = "Wolf",
                ["gameName"] = "Forest Wolf",
                ["description"] = "继承本体 Wolf 逻辑、由 JSON 覆盖参数并附加 Lua 行为。",
                ["tags"] = new JArray("Wolf", "Predator", "example.flatworld.mod:forest"),
                ["modules"] = new JObject
                {
                    ["ai"] = new JObject
                    {
                        ["parameters"] = new JObject
                        {
                            ["alertDetectDistance"] = 24f,
                            ["chaseTriggerDistance"] = 34f
                        }
                    },
                    ["lua"] = new JObject
                    {
                        ["prefab"] = Mod_LuaBehaviour.ModuleId,
                        ["id"] = Mod_LuaBehaviour.ModuleId,
                        ["enabled"] = true,
                        ["parameters"] = new JObject
                        {
                            // modId 由运行时强制写入，作者不能借此读取其他 MOD 的脚本。
                            ["scriptPath"] = "Lua/actor.lua",
                            ["tickMode"] = (int)ModuleTickMode.FixedInterval,
                            ["fixedTickInterval"] = 0.5f
                        }
                    }
                }
            }
        };
        return root.ToString(Formatting.Indented);
    }

    private static string CreatePatchJson()
    {
        ModPatchDocument document = new();
        document.Patches.Add(new ModPatchOperation
        {
            Target = "example.flatworld.mod:training_sword",
            Operation = "add",
            Path = "tags",
            Value = Newtonsoft.Json.Linq.JToken.FromObject("example-patched")
        });
        document.Patches.Add(new ModPatchOperation
        {
            Target = "Sword_Bronze",
            Operation = "set",
            Path = "description",
            Value = Newtonsoft.Json.Linq.JToken.FromObject("该文本由示例 MOD Patch 修改。"),
            Optional = true
        });
        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    private static string CreateLocalizationJson(string language, string label, string description)
    {
        ModLocalizationDocument document = new()
        {
            Language = language,
            Entries = new()
            {
                ["training_sword.label"] = label,
                ["training_sword.description"] = description,
                ["setting.verbose.label"] = language == "zh-CN" ? "详细日志" : "Verbose logging"
            }
        };
        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    private static string CreateSettingsJson()
    {
        ModSettingsDocument document = new();
        document.Settings.Add(new ModSettingDefinition
        {
            Id = "verbose",
            Type = "bool",
            Scope = "client",
            DefaultValue = Newtonsoft.Json.Linq.JToken.FromObject(true),
            LabelKey = "example.flatworld.mod:setting.verbose.label"
        });
        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    private static string CreateMainLua()
    {
        return @"local M = {}

function M.OnLoad(api)
    if api:GetBoolSetting('verbose', true) then
        api:Log('示例 MOD 已加载；新物品 ID：example.flatworld.mod:training_sword')
    end
end

function M.OnEvent(api, eventName, payloadJson)
    if api:GetBoolSetting('verbose', true) and eventName ~= 'item.spawned' then
        api:Log('收到事件：' .. eventName .. ' / ' .. payloadJson)
    end
end

function M.OnSave(api, state)
    return state
end

function M.OnLoadSave(api, state)
    api:Log('已恢复示例 MOD 的全局状态')
end

return M
";
    }

    private static string CreateActorLua()
    {
        return @"local M = {}

function M.OnLoad(actor, state, deltaTime, api)
    api:Log('Actor loaded: ' .. actor.Id)
    return state
end

function M.OnUpdate(actor, state, deltaTime, api)
    -- actor 提供 X/Y、Health/MaxHealth、MoveTo 与 StopMoving。
    -- 保留本体 Wolf 状态机时，该状态机仍可能在后续 Tick 覆盖移动目标。
    return state
end

function M.OnSave(actor, state, deltaTime, api)
    return state
end

return M
";
    }

    private static string CreateReadme()
    {
        return @"# FlatWorld 示例 MOD

## Actor JSON

- `actors` 可用 `parent: Wolf/Chicken/WildBoar/Ghost` 继承本体状态机与全部模块。
- `modules` 只填写差异参数；`Mod_LuaBehaviour` 可增加 OnLoad/OnUpdate/OnAct/OnSave 钩子。
- 自定义外形使用 `visual.spriteBundle + spriteAsset`；动画使用 `animatorControllerBundle + animatorControllerAsset`。
- 单纯换皮不需要制作新 AI Prefab；自定义 Prefab 仍必须只使用游戏提供的组件。

- `Defs/items.json`：物品 Def 和继承。
- `Patches/balance.json`：set/add/remove/replace/merge/test Patch。
- `Localization/`：按语言注册命名空间文本。
- `Settings/settings.json`：client/world/server 设置 schema。
- `Lua/main.lua`：OnLoad、OnUpdate、OnEvent、OnSave、OnLoadSave、OnUnload 生命周期。
- `Lua/actor.lua`：Actor 实例级安全 Lua 扩展。

游戏主菜单按 F10 打开 MOD 管理器。修改启停或顺序后，在未进入世界时重载内容。
";
    }

    #endregion
}
#endif
