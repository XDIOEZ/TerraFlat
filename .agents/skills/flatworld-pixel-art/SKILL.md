---
name: flatworld-pixel-art
description: "为 FlatWorld 生成、重绘、转换或评审运行时像素美术，并处理画风一致性、动画一致性、透明 PNG 校验和 Unity Sprite 导入。Use when: 游戏内小人、玩家或 NPC Sprite、俯视像素角色、动物、怪物、物品、工具、树木、建筑、世界道具、图标、Pixel Art、Sprite、Sprite Sheet 或动画帧。运行时素材不强制固定 16×16，优先匹配项目内同类素材的画风、比例、视角和视觉像素密度。不要用于高清角色立绘、对话人物图或 UI 人物展示；这些任务使用 flatworld-portrait-art。"
---

# FlatWorld 运行时像素美术

## 必读

- 每次完整读取 `references/style-guide.md`；编写 ImageGen 提示词、生成或制作变体时，再完整读取 `references/prompt-recipes.md`。
- 所有类别首先选项目内已经实际使用的同类运行时素材作为主参考，优先统一轮廓、像素簇、描边、配色、明暗、视角和世界尺度。角色类可额外使用 `assets/merchant-style-anchor.png` 理解角色设计语言，并使用 `assets/merchant-game-sprite-anchor.png` 参考小尺寸可读性和对齐，但它们都不得强制其他素材采用 16×16 或相同像素密度。
- 生成或编辑位图时同时使用系统 `imagegen` Skill，并遵守其参考图、透明背景、输出路径与结果检查规则。
- 若目标是高清角色立绘、对话人物图或 UI 人物展示，停止套用本 Skill，改用 `flatworld-portrait-art`。同一任务需要两类资产时分别生成、命名、验收和导入。

## 工作流

1. 明确运行时类别、消费位置、朝向、动作、帧布局、逻辑尺寸与 Pivot。接入现有 Animator、Tile、Prefab 或 UI 图标前先检查消费方契约，不凭空假定切片网格。
2. 只选择同类别项目素材作为比例、视角和画风主参考；角色、动物、物品、树木和建筑不得强行共用比例。运行时素材不再默认固定 `16×16`：画布和逻辑像素密度由消费方契约与最近的同类现有素材共同决定，可以使用更高像素密度。尺寸变高时仍必须保持同类素材的剪影语言、像素簇尺度、描边观感、有限色块、光源方向和材质表达，禁止因为分辨率更高就变成细腻插画、平滑矢量或另一套像素画风。
3. 需要设计探索时可先生成 `<Name>_Concept_HighRes.png` 作为本流程的内部设计源，但它不是 UI Portrait，也不能直接作为运行时纹理。
4. 将源图转换为运行时 Sprite：移除纯色色键、紧裁主体用于缩放，再放回目标画布；根据源图选择 `BOX` 或 `NEAREST` 缩放；颜色数量优先匹配同类参考，不因提高分辨率而机械增加色阶；普通不透明素材将 Alpha 硬化为 `0/255`。
   - 量化前将透明像素的 RGB 清零，避免残留色键占用调色板；若少量高光被中位切分量化合并，可用 `MAXCOVERAGE` 保留明暗端点，并重新检查小尺寸辨识度。
5. 将角色和世界实体底部居中，图标居中；清理色键残边、半透明晕边、孤立像素与透明孔洞。目标画布中的透明留白属于 Pivot、动画基线和世界尺度契约，不得在没有检查消费方的情况下再次紧裁删除。
6. 在 1 倍和至少 8 倍最近邻预览下检查剪影、眼睛、脚底、手持物、身份配件和像素簇。动画帧还必须保持身份、比例、轮廓、调色板、光源、基线与 Pivot 一致。
7. 保存到 `Assets/6_Art/Generated/<Name>/`，运行时单帧默认命名 `<Name>_<State>_<Direction>.png`；除非用户明确要求替换，不覆盖旧素材。
8. 配置 Unity 时使用 `Sprite (2D and UI)`、Point、关闭 Mipmap、关闭纹理压缩并启用 Alpha Transparency。PPU 不再机械固定为 `16`：优先继承同类别现有素材的世界尺度；若新素材使用更高逻辑像素密度，则按比例提高 PPU 或遵循消费方现有设置，确保放进场景后的实际大小与同类素材一致。角色/世界实体通常使用底部中心 Pivot，图标使用中心 Pivot；Sprite Sheet 切片服从现有消费方。
9. 对运行时 Sprite 运行静态校验。默认校验透明度、硬 Alpha、透明角和颜色规模，不强制 `16×16`；只有消费方、Sprite Sheet 网格或替换目标明确要求精确尺寸时才传 `--exact-size`：

```powershell
python .agents/skills/flatworld-pixel-art/scripts/validate_pixel_asset.py <sprite.png> --require-alpha --require-hard-alpha --require-transparent-corners --max-visible-colors 24
# 仅在真实契约要求时追加，例如：--exact-size 16x16
```

## 边界与交付

- 禁止抗锯齿、渐变、照片纹理、高频噪点、无关背景、地面、投影、光晕、文字、Logo、水印或无法在最终尺寸辨认的装饰；半透明特效等明确例外按消费方单独制定规则。
- 仅生成美术时不创建 Prefab、Animator、SO 或玩法代码；需要接入时再读取对应 FlatWorld 领域 Skill，通过 Unity MCP 操作时读取 `unity-mcp-orchestrator`。
- 不复制其他资源的 GUID；仅在目标 `.meta` 已存在时精确修改导入字段。高清设计源默认不进 Addressables，也不挂到 Prefab。
- 替换 JSON 定义物品的贴图时，先核对 `visual.spriteAddress`，它可能覆盖外壳 Prefab 的 Sprite；若旧引用指向共享地形图集，应为道具生成独立 Sprite 并同步注册同址 `ItemSprite` Addressables 条目，禁止直接覆盖共享图集。
- 将继承其他物品贴图的染色占位物替换为独立成图时，同步检查 `visual.color` 与继承的 `rendererLocalScale`；原有乘色会改变新图配色，成图通常显式设为白色，尺寸则结合继承缩放和 PPU 验收。
- 手持工具的 Pivot 必须对齐实际握柄，不能机械套用图标中心；先核对外壳 `Render` 层级和 JSON `visual.rendererLocalPosition`，避免导入 Pivot 与外壳偏移重复补偿。PNG 以左上计像素，Unity Pivot 以左下归一化；像素中心 `(x, y)` 对应 `((x + 0.5) / width, 1 - (y + 0.5) / height)`。
- 铺满整格的地面 Tile 使用中心 Pivot、全幅不透明画布和当前地块 PPU；不要套用物品的透明四角/底部对齐检查，否则拼接时会露出原地形。其 Sprite 图标可复用同一图，运行时地面不能保留图标安全留白。
- 最终报告资产类别、画布尺寸、主体边界、可见颜色数、透明度、动画/单帧状态、Unity 导入设置、使用的提示词/模式和必要人工观感检查；同时说明选用了哪些同类项目素材作为画风/比例参考，并确认新素材在相同世界尺度下没有因更高像素密度而产生明显风格跳变。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
