---
name: flatworld-localization
description: "Use when: 定位或修改 FlatWorld 的多语言系统、Unity Localization、Locale 切换、String Table、UI 文本、物品名称/说明、翻译资源或语言持久化。关键词：FlatWorldLocalizationService、FlatWorldLocalizationSetup、LocalizedTextBinder、FlatWorldUIAutoLocalizer、TrySetLocale、GetUiText、GetItemLabelKey。"
---

# FlatWorld 本地化系统定位

> 最后核对：2026-08-09。修改本地化表、语言代码、资源路径或程序集边界后必须同步更新本 Skill 与 `references/file-map.md`。

## 修改前先读
1. `Assets/5_Scripts/5-7_Localization/FlatWorldLocalizationService.cs`：语言初始化、Locale 切换、持久化、String Table 查询和稳定 key。
2. `Assets/5_Scripts/5-7_Localization/LocalizedTextBinder.cs`：TMP 文本绑定与 `LanguageChanged` 刷新。
3. `Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs`：Locale、String Table、物品 JSON 和 UI Prefab 的编辑器同步入口。
4. `Assets/5_Scripts/5-5_UI/BasePanel.cs` 与 `Assets/5_Scripts/5-5_UI/FlatWorldUIAutoLocalizer.cs`：正式 UI 的静态文本自动绑定。

## 权威数据链
```text
语言选择 UI
→ FlatWorldLocalizationService.TrySetLocale()
→ LocalizationSettings.SelectedLocale
→ LanguageChanged
→ LocalizedTextBinder / 动态 UI 重新查询
```
```text
StreamingAssets/GameConfig/Items/*.json
→ labelKey / descriptionKey
→ FlatWorld String Table
→ FlatWorldLocalizationService.Get()
```
```text
StreamingAssets/GameConfig/Quests/*.json
→ titleKey / descriptionKey / objective.labelKey
→ FlatWorld String Table
→ 任务追踪 HUD 按稳定 key 查询
```

```text
正式 UI Prefab TMP 文本
→ FlatWorldLocalizationSetup 扫描并生成 key
→ FlatWorldUI String Table
→ BasePanel + FlatWorldUIAutoLocalizer
→ LocalizedTextBinder
```

## 固定契约
- 当前 Unity 包为 `com.unity.localization` 1.4.5；不要绕过 Unity Localization 另建全局语言单例或按语言复制 Prefab。
- 当前 Locale 代码为 `zh-CN` 与 `en`；默认语言为 `zh-CN`，用户选择保存到 `FlatWorld.Localization.Locale`。
- `FlatWorld` 表用于物品名称/说明、任务标题/说明/目标标签等本体内容；`FlatWorldUI` 表用于正式 UI 的标题、状态和模板。未来角色自言自语使用独立的 `FlatWorldDialogue` 表，不要混入 UI 表。
- 物品与 UI 的 key 必须稳定：物品使用 `item.{itemId}.name/description` 或 JSON 明确提供的 `labelKey/descriptionKey`；UI 使用 `GetUiTextKey(sourceText)` 生成的 `ui.text.{hash}`。不要用翻译后的英文作为 key。

## 按任务定位
- **语言切换或语言记忆**：先读 `FlatWorldLocalizationService`、`GameManager.UI.cs` 和主菜单设置 Prefab；检查 Locale Code、PlayerPrefs key、事件解绑和失败回退。
- **Prefab 里大量中文**：先运行 `FlatWorld/Localization/Setup Default Tables` 同步 `FlatWorldUI`，再检查 `BasePanel` 和 `FlatWorldUIAutoLocalizer`；不要手工给每个静态 TMP 节点复制一套语言节点。
- **运行时动态 UI 中文**：在赋值点使用 `GetUiText` 或 `GetUiFormat`；同时把源模板加入 `FlatWorldLocalizationSetup.EnglishUiOverrides`，重新执行同步菜单。动态列表重建时必须使用当前语言，不要只在面板 Awake 时翻译一次。
- **物品名称或说明**：修改 `Assets/StreamingAssets/GameConfig/Items/` 中的稳定 `id`、`labelKey`、`descriptionKey`；再由编辑器同步工具更新 `FlatWorld` 表。不要把 `name_zh/name_en/name_xx` 堆回物品业务配置。
- **任务标题、说明或目标标签**：修改 `Assets/StreamingAssets/GameConfig/Quests/` 中的稳定 `titleKey`、`descriptionKey`、`labelKey` 与中文 fallback；英语进入 `EnglishQuestOverrides`，再由同步工具更新 `FlatWorld` 表，业务 JSON 不按语言复制字段。
- **新增语言**：同时更新 Locale 资产、默认/可用语言配置、语言选择 UI、String Table 列、编辑器同步工具和 Addressables；不要只新增一个代码常量。
- **角色自言自语**：同时读取 `flatworld-dialogue`。保留 JSON 的触发条件、优先级、冷却、一次性完成标记；建议为每条台词增加稳定 key，并在 `FlatWorldDialogue` 表按 key 翻译。不要新建第二个调度器。

## 标准工作流
1. 先判断文本类别：本体物品、正式 UI、角色自言自语、玩家自由输入、开发者调试输出。只把面向玩家且可配置的文本接入本地化。
2. 按 `references/file-map.md` 读取最小入口；跨 UI、物品、对话或存档时只追加实际命中的专项 Skill，避免无目的全仓库搜索。
3. 选择稳定 key 和目标 String Table。不要以中文长句直接作为长期 key，也不要让同一条文本同时分散在多个表。
4. 修改运行时代码时保留 fallback，并让语言事件能够刷新已显示的动态控件；不要在业务层缓存翻译结果作为永久状态。
5. 修改 JSON、Prefab 扫描规则或翻译覆盖字典后，执行 Unity 菜单 `FlatWorld/Localization/Setup Default Tables`，检查 `Assets/Localization/` 和 Addressables 条目。
6. 刷新 Unity 并检查 Console/编译错误。按项目 `AGENTS.md`，除非用户明确要求，不主动运行 Unity Test Runner；需要验证时优先做静态检查、编译和人工语言切换。

## 常见错误
- 只改 `languageCode` 或下拉框，不配置 Locale/String Table/Addressables，导致切换成功但文本仍显示中文。
- 只给 Prefab 增加 `LocalizedTextBinder`，却没有把 key 写入目标表；绑定组件会回退到中文，看起来像“本地化失效”。
- 动态文本使用中文拼接后直接赋给 TMP，绕过 `GetUiText/GetUiFormat`。
- 将物品的显示名当作业务 ID，导致翻译或改名破坏存档、配方、掉落或 MOD 引用。

## 自言自语本地化边界
当前 JSON 结构中 `lines` 是中文字符串数组，`ConfiguredSpeechProvider` 负责随机选择、条件匹配、冷却和一次性完成标记。后续接入时建议：
1. 将每条台词关联稳定 `dialogue.<entryId>.<variantIndex>` key，并保留中文 fallback。
2. 新建 `FlatWorldDialogue` String Table，按 key 存放 `zh-CN/en` 文本。
3. 由 `ConfiguredSpeechProvider` 完成候选选择后再按当前 Locale 解析文本，继续生成同一个 `CharacterSpeechRequest`。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：四个 GM 测试任务的标题、说明和目标标签进入 `FlatWorld` 中英文表；`debugOnly` 只影响接取来源，不改变内容本地化与中文 fallback 契约，GM 开发者操作文字仍不进入正式 UI 表。
- 2026-08-11：任务内容接入 `FlatWorld` 表：同步工具扫描本体 Quest JSON 的标题、说明和目标标签稳定 key；任务追踪 HUD 的标题/状态/空态进入 `FlatWorldUI`，语言切换会事件刷新全部可见任务卡。
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：按键绑定新增“切换奔跑 / Toggle Run”和“长按奔跑 / Hold to Run”，并登记到 `EnglishUiOverrides` 与 `FlatWorldUI` 表。
- 2026-08-11：设置会话分页新增“不保存直接退出 / Exit Without Saving”，中文源文案登记到 `EnglishUiOverrides`，Prefab 静态文本继续由 `FlatWorldUI` 自动扫描绑定。
- 2026-08-09：GMReflectionConsole 属于开发者专用运行时调试 Canvas；本次仅调整 Buff 操作 fallback 文案，保留中文安全回退，未把调试字符串写入正式 `FlatWorldUI` 表。
- 2026-08-09：新增 Buff 状态 HUD 的动态/静态文案（`状态效果 / BUFFS`、`暂无状态`、`永久`、`剩余 {0}s`）；已登记到 `EnglishUiOverrides` 并同步写入 `FlatWorldUI` 中英文 String Table，语言切换由 HUD 事件刷新。
- 2026-08-09：按键绑定 UI 新增“清除”及清除状态文案；中文源模板与英文表达已登记到 `EnglishUiOverrides`，并同步写入 `FlatWorldUI` 的中英文 String Table。

## 修改后维护本 Skill
新增/移动本地化脚本、String Table、Locale、Addressables 组、JSON key、UI 绑定入口或语言切换流程后，必须在同一任务内更新本 Skill 与 `references/file-map.md`；如果命中 UI、物品、对话、存档或联机边界，同时更新对应专项 Skill。
