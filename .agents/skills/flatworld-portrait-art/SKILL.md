---
name: flatworld-portrait-art
description: "为 FlatWorld 生成、重绘或评审高清角色立绘，并处理角色身份继承、画风参考、平视全身构图、透明 PNG 验收与 Unity UI 导入。Use when: 角色立绘、全身立绘、对话人物图、角色页人物展示、任务人物图、表情立绘、高清 UI 人物图、立绘平涂或赛璐璐风格。不要用于游戏内像素小人、NPC Sprite、动物、怪物、物品、建筑、图标或 Sprite Sheet；这些任务使用 flatworld-pixel-art。"
---

# FlatWorld 高清角色立绘

## 必读

- 每次完整读取 `references/portrait-guide.md`。
- 编写 ImageGen 提示词、生成、重绘或制作变体时，再完整读取 `references/prompt-recipes.md`。
- 生成或编辑位图时同时使用系统 `imagegen` Skill，并遵守其参考图、透明背景、输出路径与结果检查规则。
- 用户要求把立绘接入现有 UI 时，同时读取 `flatworld-ui`；通过 Unity MCP 操作编辑器时再读取 `unity-mcp-orchestrator`。
- 若目标是场景运行时像素素材，停止套用本 Skill，改用 `flatworld-pixel-art`。同一任务同时需要立绘和 Sprite 时可使用两个 Skill，但分别生成、命名、验收和导入。

## 工作流

1. 将交付物明确标记为 `Portrait`，确认消费位置、构图范围、表情、姿势、最终显示尺寸和是否需要接入 Unity；不要把设计探索图或运行时 Sprite 冒充正式立绘。
2. 为每张参考图声明职责：`identity reference` 决定角色是谁，`art-style and presentation reference` 只决定表现语言，`pose reference` 只决定姿势。冲突时身份参考优先。
3. 先列出必须保持的身份不变量：年龄感、体型、头发轮廓、面部印象、服装结构、主配色、标志配件和职业气质。缺失细节只做最少量、可解释的高清化。
4. 默认生成单人、自然平视、竖版全身立绘；头顶、双脚和身份配件完整可见，四周保留约 `8%-12%` 安全留白。用户或 UI 契约另有要求时服从实际消费方。
5. 使用透明背景流程生成无场景、地面、投影、边框、文字、Logo 或水印的 PNG；立绘允许平滑 Alpha，禁止套用像素素材的硬 Alpha 和颜色数限制。
6. 保存到 `Assets/6_Art/Generated/<Name>/`，默认命名为 `<Name>_Portrait_FullBody_<Expression>.png`；除非用户明确要求替换，不覆盖旧素材。
7. 在原尺寸和目标 UI 缩放尺寸下检查面部、完整轮廓、遮挡关系、身份一致性与透明边缘；视觉问题不得仅凭文件尺寸或脚本统计判定通过。
8. 仅在用户要求接入 UI 时配置 Unity：使用适合缩放的平滑过滤、Alpha 和关闭 Mipmap；禁止使用运行时小人的 `PPU 16`、Point 过滤、底部中心世界 Pivot 或 Sprite Sheet 切片。

## 边界与交付

- 运行时像素角色只能作为身份参考，不把俯视角、点眼、大头短身、16×16、硬 Alpha 或 24 色上限继承到立绘。
- 仅生成立绘时不创建 Prefab、Animator、SO、Addressables 条目或玩法代码；接入时先检查实际 UI 消费方。
- 不复制其他资源的 GUID；仅在目标 `.meta` 已存在时精确修改导入字段。
- 最终报告画布尺寸、主体边界、透明度、身份不变量、各参考图职责、使用的提示词/模式、Unity 导入设置和必要人工观感检查。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
