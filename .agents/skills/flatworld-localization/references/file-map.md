# FlatWorld 本地化文件地图

## 运行时入口

| 路径 | 权威职责 | 首选搜索词 |
| --- | --- | --- |
| `Assets/5_Scripts/5-7_Localization/FlatWorldLocalizationService.cs` | Locale 初始化、语言切换、PlayerPrefs 持久化、String Table 查询、物品/UI key | `TrySetLocale`, `Get(`, `GetUiText`, `GetUiFormat`, `LanguageChanged` |
| `Assets/5_Scripts/5-7_Localization/LocalizedTextBinder.cs` | TMP 文本绑定、语言事件订阅、fallback 刷新 | `Configure`, `Refresh`, `LanguageChanged` |
| `Assets/5_Scripts/5-7_Localization/FlatWorld.Localization.asmdef` | 运行时本地化程序集；引用 `Unity.Localization` 与 `Unity.TextMeshPro` | `FlatWorld.Localization` |
| `Assets/5_Scripts/5-5_UI/FlatWorldUIAutoLocalizer.cs` | 为面板内仍含中文的 TMP 文本动态补充 UI 表绑定；不改布局 | `BindStaticTexts`, `ContainsChinese` |
| `Assets/5_Scripts/5-5_UI/BasePanel.cs` | 面板初始化/刷新时调用自动本地化器 | `BindStaticTexts`, `CollectUIComponents` |
| `Assets/5_Scripts/5-5_UI/FlatWorldUITheme.cs` | 主题工具生成或更新的标题、eyebrow、动态 TMP 本地化 | `GetUiText`, `LocalizedTextBinder` |

## 编辑器与资源

| 路径 | 权威职责 | 注意事项 |
| --- | --- | --- |
| `Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs` | 菜单 `FlatWorld/Localization/Setup Default Tables`；创建 Locale、同步物品/任务/UI 表 | 修改同步规则后必须重新执行菜单并检查 Console |
| `Assets/5_Scripts/5-2_Editor/Editor.asmdef` | 编辑器程序集引用 `FlatWorld.Localization`、`Unity.Localization.Editor`、`Unity.TextMeshPro` | 编辑器扫描 TMP 时缺少引用会产生编译错误 |
| `Assets/Localization/LocalizationSettings.asset` | Unity Localization Settings | 不要手工删除后只保留代码配置 |
| `Assets/Localization/Locales/zh-CN.asset` | 简体中文 Locale | 默认语言 |
| `Assets/Localization/Locales/en.asset` | 英文 Locale | 当前唯一已翻译的外语 |
| `Assets/Localization/StringTables/FlatWorld_*.asset` | 物品名称/说明等本体文本 | key 通常为 `item.<id>.name/description` |
| `Assets/Localization/StringTables/FlatWorldUI_*.asset` | 正式 UI 静态文本与动态模板 | key 为 `ui.text.<FNV-1a hash>` |
| `Assets/AddressableAssetsData/AssetGroups/Localization-*.asset` | Locale、String Table 和共享表数据的 Addressables 注册 | 表存在但未进入 Addressables 时运行时可能回退 fallback |

## 内容来源

| 路径 | 内容 | 本地化处理 |
| --- | --- | --- |
| `Assets/StreamingAssets/GameConfig/Items/` | 物品 JSON 的稳定 `id`、旧中文 `gameName/description`、可选 `labelKey/descriptionKey` | 由 Setup 菜单同步至 `FlatWorld` 表；业务配置不按语言复制 |
| `Assets/StreamingAssets/GameConfig/Quests/` | 任务 JSON 的稳定 `titleKey/descriptionKey/objective.labelKey` 与中文 fallback；含 `debug-tests.json` 的 `debugOnly` GM 测试任务 | 由 Setup 菜单同步至 `FlatWorld` 表；英语集中维护在 `EnglishQuestOverrides`，调试标记不改变本地化流程 |
| `Assets/2_Prefabs/2-1_UI/` | 正式 UI Prefab 的 TMP 静态文本 | Setup 扫描 CJK 文本；运行时由 `BasePanel` 自动绑定 |
| `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/` | 设置面板、输入绑定、难度、`GameSaveStatusHUD`、Buff HUD、`PlayerQuestTrackerHUD`/`QuestTrackerRowView` 等动态 UI | UI 状态使用 `GetUiText/GetUiFormat`；任务内容按配置 key 查询 `FlatWorld`，新增模板同步到编辑器覆盖字典 |
| `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.UI.cs` | 主菜单、世界加载、难度和语言状态文本 | 不要把 UI 文本常量继续直接赋给 TMP |
| `Assets/Resources/Dialogue/Soliloquy/` | 角色自言自语 JSON | 属于 `flatworld-dialogue`；未来使用独立 `FlatWorldDialogue` 表 |

## 相关程序集与跨系统边界

- `FlatWorld.Localization`：运行时查询和 TMP 绑定；不保存玩法状态。
- `Editor`：表生成、Prefab 扫描、JSON 同步和资源注册；不把编辑器翻译逻辑带入运行时。
- `UI`：正式面板、Prefab、动态控件；布局问题追加 `flatworld-ugui-layout`，业务设置状态追加对应 gameplay Skill。
- `FlatWorld.Dialogue`：角色自言自语调度与气泡；文本本地化时同时加载 `flatworld-dialogue`，但不要新建第二套调度器。
- `StreamingAssets/GameConfig/Items` 与物品业务：key 变化需检查 `flatworld-item-building` / `flatworld-data-save`，避免破坏存档、配方、掉落或 MOD 引用。
- Addressables：本地化资产的运行时可用性取决于 `Assets/AddressableAssetsData/` 中的实际条目，不要只检查磁盘文件。

## 限定搜索配方

```powershell
rg -n "FlatWorldLocalizationService|LocalizedTextBinder|GetUiText|GetUiFormat|TrySetLocale" `
  Assets/5_Scripts/5-7_Localization `
  Assets/5_Scripts/5-5_UI `
  Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.UI.cs `
  Assets/5_Scripts/5-3_GamePlay/Presentation/UI
```

```powershell
rg -n '"(id|labelKey|descriptionKey|gameName|description)"' `
  Assets/StreamingAssets/GameConfig/Items
```

```powershell
rg -n "FlatWorldUI|FlatWorld_.*(en|zh-CN)|Localization-String-Tables" `
  Assets/Localization `
  Assets/AddressableAssetsData/AssetGroups
```

不要一开始扫描整个仓库；只有当调用链越过本地图未记录的程序集或资源注册边界时才扩大搜索，并把新权威入口补回本文件。
