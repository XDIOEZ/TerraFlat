---
name: flatworld-art-generation
description: "为 FlatWorld 生成、重绘或评审项目美术素材，并统一角色、NPC、动物、怪物、物品、建筑小道具、世界图标和 Sprite/Sprite Sheet 的像素画风、比例、轮廓、配色、透明背景及 Unity 导入规范。Use when: 用户要求生成游戏美术、像素角色、商人或其他 NPC、动物、怪物、物品图标、建筑素材、风格变体、透明 PNG，或要求新素材参考 FlatWorld 现有画风。关键词：美术生成、像素画、Pixel Art、ImageGen、Sprite、Sprite Sheet、角色立绘、游戏素材、画风统一。"
---

# FlatWorld 美术生成

> 最后核对：2026-08-11。

## 必读资源

- 每次生成或评审素材前，完整读取 [references/style-guide.md](references/style-guide.md)。
- 需要编写 ImageGen 提示词或制作变体时，再读取 [references/prompt-recipes.md](references/prompt-recipes.md)。
- 角色类素材必须将 [assets/merchant-style-anchor.png](assets/merchant-style-anchor.png) 作为主要画风参考，将 [assets/merchant-game-sprite-anchor.png](assets/merchant-game-sprite-anchor.png) 作为最终像素密度参考。
- 生成或编辑位图时同时使用系统 `imagegen` Skill，并遵守其透明背景、输出路径和结果检查规则。

## 核心工作流

1. 明确素材类别、用途、朝向、动画需求和逻辑像素尺寸。标准小型运行时 Sprite 默认使用固定 `16x16` 画布；用户没有指定且不影响方向时，按风格指南的默认值继续，不因次要细节停工。
2. 只检查与目标同类的项目原图。角色优先参考商人锚点与玩家；动物参考牛、狐狸等动物原图；物品或建筑必须另找同类素材，禁止把人物比例硬套到所有类别。
3. 先生成高清设计源图：单主体、完整轮廓、充足留白、无文字和水印。透明素材默认采用内置 ImageGen 的纯色色键流程，不得擅自切换到需要 API Key 的透明背景模型。
4. 将高清源图转换为游戏 Sprite：
   - 使用色键移除工具得到透明 PNG。
   - 先紧裁主体用于缩放；缩小时优先用 `BOX` 保留眼睛和配件，用 `NEAREST` 保留已经对齐的像素块。
   - 标准小型 Sprite 将缩放后的主体放入固定 `16x16` 透明画布，底部居中对齐；主体边界可以小于画布，但最终不得再次紧裁画布。
   - 无抖动量化到通常 `12-20` 个可见颜色，复杂主体最多 `24` 色。
   - 小型不透明 Sprite 将 Alpha 硬化为 `0/255`，清理孤立像素、色键残边和半透明晕边。
   - 在 1 倍和至少 8 倍最近邻预览下检查眼睛、外轮廓、脚底、手持物与识别性。
5. 保存两个层级的结果：
   - `<Name>_Concept_HighRes.png`：透明高清设计源图，仅用于继续设计或生成动画参考。
   - `<Name>_<State>_<Direction>.png`：固定 `16x16` 画布、低分辨率、可进入运行时的标准小型 Sprite，例如 `Merchant_Idle_Front.png`。大型素材按消费方规格例外处理。
   - 默认目录为 `Assets/6_Art/Generated/<Name>/`；不得覆盖已有素材，除非用户明确要求替换。
6. 配置 Unity 导入：运行时 Sprite 使用 `Sprite (2D and UI)`、Point 过滤、关闭 Mipmap、关闭纹理压缩并启用 Alpha Transparency。小型角色默认 `PPU 16`、底部中心 Pivot；图标使用中心 Pivot。Sprite Sheet 根据消费方 Animator 的既有网格设置，禁止凭空假定切片尺寸。
7. 运行静态检查：

   ```powershell
   python .agents/skills/flatworld-art-generation/scripts/validate_pixel_asset.py <sprite.png> --require-alpha --require-hard-alpha --require-transparent-corners --max-visible-colors 24 --exact-size 16x16
   ```

8. 最终反馈分别列出画布尺寸、可见主体边界、颜色数、导入设置、最终提示词和生成模式；说明该素材是单帧、动画表还是仅为设计源图，并给出必要的 Unity 人工观感检查。

## 强制画风约束

- 游戏 Sprite 必须是清晰的低分辨率像素画；禁止平滑矢量边、抗锯齿、渐变、照片纹理和高频噪点。
- 标准小型角色、动物、怪物与物品 Sprite 使用固定 `16x16` 画布；可见主体在画布内对齐，透明留白属于布局契约，不得因“紧裁”被删除。
- 在逻辑分辨率下使用约 1 像素深色轮廓；轮廓应连接主体，不得形成粗黑贴纸边。
- 使用大头、短身体、紧凑轮廓和有限明暗层级；配件首先服务于剪影识别，不追求微小装饰数量。
- 光照默认来自左上方；每种主要材质通常只使用中间色、阴影色和一个高光色。
- 角色面部在小尺寸下使用 1 像素点眼；鼻子和嘴巴仅在仍然清晰且不破坏简洁性时出现。
- 不把投影、地面、光晕或交互 UI 烘焙进普通角色/物品 Sprite，除非同类项目素材明确包含这些内容。
- 禁止文字、Logo、水印、额外角色、无关道具和无法在最终尺寸辨认的装饰。
- 生成动画表时，所有帧必须保持角色身份、比例、轮廓厚度、光源、调色板、基线和 Pivot 一致；未验证网格与帧一致性前不得称为“可直接使用的完整动画”。

## Unity 与项目边界

- 仅生成美术时，不主动创建 Prefab、Animator、SO 或玩法代码；用户要求接入游戏时，再使用对应 FlatWorld 系统 Skill。
- Unity 会话可用时，通过 Unity MCP 刷新并检查 Console；会话不可用时，检查 PNG、`.meta` 和文件路径并明确说明未完成编辑器内验证。
- 不手工复制其他资源的 GUID。只有 Unity 已生成目标 `.meta` 时才修改其导入字段；否则等待 Unity 导入或使用 Unity MCP 配置。
- 高清设计源图默认不进入 Addressables、不挂到 Prefab，也不作为运行时纹理。

## 维护本 Skill

- 商人锚点、生成目录、角色逻辑尺寸、Unity 导入规范或主要项目参考图发生变化时，同步更新本 Skill 与相关 reference。
- “近期变更”最多保留 10 条，按新到旧排列；超过 10 条时删除最旧记录。

## 近期变更

- 2026-08-11：根据用户反馈将标准小型运行时 Sprite 从紧裁尺寸改为固定 `16x16` 画布；商人主体重新缩放并底部居中，锚点、提示词与校验命令同步更新。
- 2026-08-11：创建项目级美术生成规范；初版使用 `14x18` 紧裁商人 Sprite（现已由上条调整为固定 `16x16` 画布），并加入像素素材静态校验脚本。
