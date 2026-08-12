---
name: flatworld-pixel-art
description: "为 FlatWorld 生成、重绘、转换或评审运行时像素美术，并处理动画一致性、透明 PNG 校验和 Unity Sprite 导入。Use when: 游戏内小人、玩家或 NPC Sprite、俯视像素角色、动物、怪物、物品、工具、建筑、世界道具、图标、Pixel Art、Sprite、Sprite Sheet、动画帧或 16×16 素材。不要用于高清角色立绘、对话人物图或 UI 人物展示；这些任务使用 flatworld-portrait-art。"
---

# FlatWorld 运行时像素美术

## 必读

- 每次完整读取 `references/style-guide.md`；编写 ImageGen 提示词、生成或制作变体时，再完整读取 `references/prompt-recipes.md`。
- 角色类素材使用 `assets/merchant-style-anchor.png` 约束设计语言，使用 `assets/merchant-game-sprite-anchor.png` 约束最终 16×16 像素密度和对齐；其他类别优先选项目内同类运行时素材。
- 生成或编辑位图时同时使用系统 `imagegen` Skill，并遵守其参考图、透明背景、输出路径与结果检查规则。
- 若目标是高清角色立绘、对话人物图或 UI 人物展示，停止套用本 Skill，改用 `flatworld-portrait-art`。同一任务需要两类资产时分别生成、命名、验收和导入。

## 工作流

1. 明确运行时类别、消费位置、朝向、动作、帧布局、逻辑尺寸与 Pivot。接入现有 Animator、Tile、Prefab 或 UI 图标前先检查消费方契约，不凭空假定切片网格。
2. 只选择同类别项目素材作为比例与视角参考；角色、动物、物品和建筑不得强行共用比例。标准小型角色、动物、怪物和物品默认固定 `16×16` 画布，消费方明确要求更大尺寸时例外。
3. 需要设计探索时可先生成 `<Name>_Concept_HighRes.png` 作为本流程的内部设计源，但它不是 UI Portrait，也不能直接作为运行时纹理。
4. 将源图转换为运行时 Sprite：移除纯色色键、紧裁主体用于缩放，再放回固定画布；根据源图选择 `BOX` 或 `NEAREST` 缩放；无抖动量化到通常 `12-20` 个可见颜色、复杂主体最多 `24` 色；普通不透明素材将 Alpha 硬化为 `0/255`。
5. 将角色和世界实体底部居中，图标居中；清理色键残边、半透明晕边、孤立像素与透明孔洞。固定画布的透明留白属于 Pivot 和动画基线契约，不得再次紧裁删除。
6. 在 1 倍和至少 8 倍最近邻预览下检查剪影、眼睛、脚底、手持物、身份配件和像素簇。动画帧还必须保持身份、比例、轮廓、调色板、光源、基线与 Pivot 一致。
7. 保存到 `Assets/6_Art/Generated/<Name>/`，运行时单帧默认命名 `<Name>_<State>_<Direction>.png`；除非用户明确要求替换，不覆盖旧素材。
8. 配置 Unity 时使用 `Sprite (2D and UI)`、Point、关闭 Mipmap、关闭纹理压缩并启用 Alpha Transparency。小型角色默认 `PPU 16` 与底部中心 Pivot，图标使用中心 Pivot；Sprite Sheet 切片服从现有消费方。
9. 对标准小型 Sprite 运行静态校验；非 16×16 素材按实际契约调整尺寸参数，不机械套用默认值：

```powershell
python .agents/skills/flatworld-pixel-art/scripts/validate_pixel_asset.py <sprite.png> --require-alpha --require-hard-alpha --require-transparent-corners --max-visible-colors 24 --exact-size 16x16
```

## 边界与交付

- 禁止抗锯齿、渐变、照片纹理、高频噪点、无关背景、地面、投影、光晕、文字、Logo、水印或无法在最终尺寸辨认的装饰；半透明特效等明确例外按消费方单独制定规则。
- 仅生成美术时不创建 Prefab、Animator、SO 或玩法代码；需要接入时再读取对应 FlatWorld 领域 Skill，通过 Unity MCP 操作时读取 `unity-mcp-orchestrator`。
- 不复制其他资源的 GUID；仅在目标 `.meta` 已存在时精确修改导入字段。高清设计源默认不进 Addressables，也不挂到 Prefab。
- 最终报告资产类别、画布尺寸、主体边界、可见颜色数、透明度、动画/单帧状态、Unity 导入设置、使用的提示词/模式和必要人工观感检查。
- 锚点、逻辑尺寸、命名、验证脚本或 Unity 导入契约变化时同步更新本 Skill；“近期变更”最多保留 5 条，按新到旧排列。

## 近期变更

- 2026-08-12：从原综合美术 Skill 独立拆出运行时像素流程，彻底移除高清立绘构图、材质和 UI 导入规则。
- 2026-08-11：标准小型运行时 Sprite 从紧裁尺寸改为固定 `16×16` 画布；商人主体、锚点、提示词与校验命令同步更新。
- 2026-08-11：创建项目级像素美术规范、商人参考锚点与像素素材静态校验脚本。
