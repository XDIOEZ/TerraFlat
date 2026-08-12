---
name: flatworld-localization
description: "Use when: 定位或修改 FlatWorld 的多语言系统、Unity Localization、Locale 切换、String Table、UI 文本、物品名称/说明、翻译资源或语言持久化。关键词：FlatWorldLocalizationService、FlatWorldLocalizationSetup、LocalizedTextBinder、FlatWorldUIAutoLocalizer、TrySetLocale、GetUiText、GetItemLabelKey。"
---

# FlatWorld 本地化

## 入口

- 服务：`Assets/5_Scripts/5-7_Localization/FlatWorldLocalizationService.cs`
- 绑定：同目录 `LocalizedTextBinder.cs`
- 编辑器同步：`Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs`
- UI 自动绑定：`Assets/5_Scripts/5-5_UI/{BasePanel,FlatWorldUIAutoLocalizer}.cs`
- 详细文件导航：`references/file-map.md`（只在路径不明确时读）

## 固定契约

- 使用 Unity Localization 1.4.5；Locale 为 `zh-CN`/`en`，默认中文，选择存于 `FlatWorld.Localization.Locale`。
- `FlatWorld` 表放 Item/Quest 等内容；`FlatWorldUI` 放正式 UI；角色台词预留独立 `FlatWorldDialogue`。
- Key 必须稳定：Item 使用显式 label/description key；UI 使用 `GetUiTextKey(sourceText)`；不可用英文译文或显示名作业务 ID。
- 静态 Prefab 文本由 Setup 扫描并自动绑定；动态文本用 `GetUiText/GetUiFormat`，在语言事件后刷新，模板同时登记英文覆盖。
- 保留中文 fallback；玩家自由输入与开发调试输出通常不进正式表。
- 新增语言需同时配置 Locale、String Table、选择 UI、同步工具和 Addressables。

## 工作流

1. 判断文本属于内容、UI、对话还是调试；选目标表与稳定 key。
2. 修改 JSON/Prefab/动态赋值点和英文覆盖。
3. 执行 Unity 菜单 `FlatWorld/Localization/Setup Default Tables`，核对中英文、占位符与 Addressables。
4. UI 文字联动 `flatworld-ui`；Item/Quest/Dialogue 只加载命中的领域 Skill。
5. 默认只做静态诊断、编译、Console 与人工切换语言，不主动运行 Test Runner。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
