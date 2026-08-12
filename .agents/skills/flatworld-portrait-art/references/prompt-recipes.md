# FlatWorld 高清角色立绘提示词模板

先读取 `portrait-guide.md`。以下模板只提供结构，不得无故增加角色、道具或剧情；每次迭代都要保留身份与参考职责说明。

## 高清全身角色立绘

```text
Use case: stylized-concept
Asset type: FlatWorld full-body character portrait for dialogue and character UI; this is a Portrait asset, not a runtime Sprite and not loose Concept exploration.
Input images: Image 1 is the approved character identity reference and defines the character; Image 2 is an art-style and presentation reference only. Use Image 2's polished anime-inspired game illustration language, refined linework, layered cel shading, selective soft material rendering, eye-level full-body framing, silhouette clarity, detail density, and finish. Do not copy Image 2's character identity, face, hairstyle, costume design, weapons, color scheme, pose-specific props, symbols, or setting.
Primary request: create one polished full-body portrait of <角色名称/身份> for FlatWorld.
Subject: preserve <已确认的发型、面部印象、服装结构、主配色、标志配件>; add only high-resolution construction details that explain the existing design.
Style/medium: follow the art-style reference's high-finish 2D anime-inspired game character illustration language; clean expressive face; refined linework; controlled layered cel shading with selective soft rendering for hair, skin, cloth, and leather; crisp complex clothing and accessory shapes with a readable hierarchy; professional character-display quality; not pixel art, not photorealistic, not 3D.
Material treatment: clean restrained cel shading; fresh matte skin; grouped hair highlights; matte cloth with a few purposeful folds; worn matte leather with sparse edge highlights; tiny controlled metal highlights; balanced color temperature with no heavy amber wash.
Composition/framing: exactly one character, eye-level camera, full body from head to complete feet, natural standing pose, no top-down or low-angle perspective, no strong foreshortening; centered silhouette with 8-12% transparent safety padding; appendages and identity props fully inside canvas.
Scene/backdrop: transparent final background; when using built-in ImageGen, first use a perfectly flat removable chroma-key background with no floor, shadow, gradient, texture, reflection, or lighting variation.
Lighting/mood: readable soft key light from upper-left; mood and expression follow <角色设定/表情> without changing identity.
Constraints: preserve identity, apparent age, body type, outfit construction, palette hierarchy, accessory placement, and recognizable silhouette; no redesign; no extra character; no scenery; no UI frame; no text; no logo; no watermark; no cast or contact shadow.
Avoid: runtime pixel sprite, top-down view, chibi sprite proportions unless the approved design explicitly requires them, cropped head or feet, copied reference character/hairstyle/costume/weapon/color motifs, unexplained new equipment, oily skin, waxy face, plastic hair, wet-looking leather or boots, excessive warm color cast, bloom, excessive micro-folds, fisheye lens, cinematic camera tilt.
```

用户明确要求平涂时，将 `Style/medium` 与 `Material treatment` 替换为：

```text
Style/medium: clean flat-color 2D anime character-sheet illustration; crisp closed dark-brown linework; large uniform local-color fills; broad graphic shapes; professional game Portrait finish; not painterly, not photorealistic, not 3D, not pixel art.
Material treatment: for each major material use primarily one base color plus one hard-edged shadow color, with at most one tiny solid highlight shape where essential; no soft transitions, gradients, airbrush, brush texture, surface grain, bloom, rim light, ambient haze, glossy reflections, or multiple subtle value steps.
```

## 独立游戏作者感 / 去默认生成感

当用户反馈立绘“像 AI”“缺少独立游戏感”时，不把旧图继续当作完整画风参考；先保留必要身份锚点，再用以下约束重新定调：

```text
Art direction: authored small-team indie-game character illustration with one explicit shape language, selective exaggeration, natural asymmetry and visible design economy; recognizable from silhouette and large color blocks rather than rendering polish.
Identity specificity: define at least two causal asymmetric traits and a work/life-shaped posture; use an ordinary specific face rather than a symmetrical idealized model face.
Detail budget: one occupational prop, one narrative repair/wear mark and one tiny material accent only; do not repeat the same occupational message through multiple accessories.
Rendering hierarchy: face silhouette, main garment shape and occupational prop are the only focal information; leave secondary areas broad and quiet. Use varied hand-drawn line weight, a restrained local-color palette and at most one hard-edged shadow per material.
Avoid: generic handsome face, heroic V-taper, symmetrical showroom stance, uniformly distributed detail, random scratches, leather texture noise, micro-folds, accessory stacking, mixed cel-shaded/semi-realistic rendering, cinematic rim light, globally polished mobile-RPG finish.
```

如果旧图的问题正是模板脸、英雄比例、材质噪声或过度润色，应新生成设计稿而不是做 `identity-preserve` 编辑；仅在新方向确认后，才把新版设为身份锚点并做构图或表情的单点迭代。

## 保持身份的表情或姿势变体

```text
Use case: identity-preserve
Input images: Image 1 is the approved Portrait identity anchor; Image 2 is an optional pose or expression reference and controls only <姿势/表情>.
Primary request: create <新表情/新姿势> for the same FlatWorld character.
Constraints: preserve face, apparent age, body type, hairstyle, outfit construction, palette hierarchy, accessory placement, line treatment, material mode, framing contract, and identity; change only <明确变化>; no redesign; no runtime pixel-art treatment; no extra elements; no text; no watermark.
```

## 迭代原则

- 一次只修正一个可观察问题，例如“保留完整双脚”或“移除皮革湿亮反光”。
- 每轮重复身份不变量、参考职责、构图、背景和材质模式，不因进入迭代而省略硬约束。
- 生成结果只有在原尺寸与目标 UI 缩放预览下完成身份、轮廓、遮挡和透明边缘检查后，才称为可交付 Portrait。
