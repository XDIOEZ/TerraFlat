---
name: flatworld-localization
description: "FlatWorld 多语言服务、文本绑定、内容/UI 字符串表、翻译源与语言设置。"
---

# FlatWorld 本地化

## 入口

- `Assets/5_Scripts/5-7_Localization/`：`FlatWorldLocalizationService.cs`、`LocalizedTextBinder.cs`。
- 同步工具：`Assets/Editor/FlatWorld/ProjectTools/Localization/FlatWorldLocalizationSetup.cs`。
- UI 自动绑定：`Assets/5_Scripts/5-5_UI/Core/BasePanel.cs`、`Common/Presentation/FlatWorldUIAutoLocalizer.cs`。
- 翻译源：`Assets/Localization/ItemNames.en.json`、`Assets/Resources/Localization/ItemTagCatalog.json`；补充路径见 `references/file-map.md`。

## 数据与绑定

- Unity Localization 1.4.5；Locale 为 `zh-CN/en`，默认中文，选择存于 `FlatWorld.Localization.Locale`。内容表 `FlatWorld`，正式 UI 表 `FlatWorldUI`，台词表 `FlatWorldDialogue`。
- 业务 ID 与显示文本分离：物品 `id/ItemData.IDName` 稳定；定义 `gameName` 提供默认中文，英文源按 ID 对应。名称表由正式 Manifest 的继承解析结果和英文源生成，同步前校验中英文与键冲突；子物品独立定义 `gameName/labelKey/descriptionKey`。
- 界面名称来自 `RuntimeItemDefinition.DisplayName`，绑定使用 `LabelKey/SourceDisplayName`；跨语言搜索通过 `GetInLocale` 读取正式表，英文 JSON 仅为编辑器源。Tag 保存稳定 ASCII ID，`ItemTagLocalizationCatalog` 提供所有语言别名及 `tag.<TagId>.name`；食性 ID 为 `Carnivore/Herbivore/Omnivore`。
- 静态 Prefab 文本由 Setup 扫描绑定；自动绑定保留业务代码覆盖后的动态文本，动态文案使用 `GetUiText/GetUiFormat` 并随语言事件刷新，UI key 来自 `GetUiTextKey(sourceText)`。物品显式绑定优先于静态扫描；克隆本地化控件会继承旧 key，业务文案和 Binder 的 `Configure` 保持对应。
- 带动态业务名称的 TMP 文本可用 `LocalizedTextBinder.ConfigureRuntime(Func<string>)` 在语言切换时重算；新增运行时 UI 原文需同时更新 `EnglishUiOverrides` 和正式双语 `FlatWorldUI` String Table，不能只写代码或编辑器译文源。
- 英文 UI 翻译先匹配完整文本覆盖，再做关键词回退；中文为 fallback。玩家自由输入与开发日志独立于正式表，节点名保持绑定身份。
- 语言选项由服务的 `SettingsProvider` 以 `ISettingsDropdown` 提供，Locale 代码为稳定 `SettingOption.Id`；语言资源由 Locale、String Table、同步工具和 Addressables 共同组成。
- 菜单 `FlatWorld/Localization/Sync Item Names` 只同步物品名；`Setup Default Tables` 同步全部内容与 UI。
