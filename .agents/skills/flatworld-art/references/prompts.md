# 位图提示词

先按主入口确定画风，再选择同类参考与实际消费规格。模板中的变量由本次目标填写，不为填满模板增添角色、道具、剧情或固定尺寸。

## 共用主体方向

角色、动物、物品、建筑、图标和默认人物立绘共用以下风格段：

```text
Style/medium: low-saturation, semi-realistic retro pixel art with deliberately low logical resolution and relatively few pixels; large readable hard-edged pixel clusters; believable proportions, functional construction and restrained material colors; a limited palette with broad light and shadow groups. Keep detail sparse and purposeful at the intended display size.
Avoid: exaggerated chibi proportions unless required by the approved identity, glossy 3D rendering, photorealistic textures, smooth vector edges, soft painted transitions, anti-aliased or blurry pixel edges, dense single-pixel noise, oversaturated neon colors and excessive tiny accessories.
```

## 单体素材与 Sprite Sheet

```text
Asset type: FlatWorld <runtime Sprite / Sprite Sheet / item / tool / plant / building / UI icon>.
Primary request: create <主体与动作> for <实际用途>.
Input images: Image 1 defines <identity or functional structure>; Image 2 supplies the closest same-category project's viewpoint, world scale and layout. Use only reference traits consistent with the shared art direction.
Composition: <方向、目标世界大小、画布或帧网格、Pivot/基线>; complete readable silhouette, preserving the consumer's required padding. Use the shared low-resolution pixel-art style.
Background: <transparent for standalone silhouettes / fully opaque and cell-filling for connected tiles>.
Constraints: no scenery, floor, cast shadow, text, watermark, card frame or extra subjects. Preserve identity, palette, proportions, light direction and pixel-cluster size across frames.
```

按类别补充实际需要的内容：

- **AI 动物/怪物**：`One canonical lateral direction, default screen-left; equal-size locomotion and requested action frames; Unity mirrors the opposite side. No front, back, up, down or duplicate right-facing groups.` 不把此方向约束套给其他角色。
- **树木/植被**：`Use broad leaf clusters, a readable trunk and restrained shading at the existing world scale; no individually rendered leaf noise.`
- **工业机身**：以铁罐/钢罐为结构与材质参考，补充 `Large muted gray-blue panels, straight reinforcement frames, dark seams and a few square rivets; complete closed housing only. External pipes, connector flanges, axle ports, collars, cables and cable junctions are separate runtime layers. Keep real working parts such as valve wheels, gauges and grilles.` 满格连片块明确正视、方形、全幅不透明、中心连续；独立设备明确透明背景。尺寸和遮挡见 [runtime-sprites.md](runtime-sprites.md)。

## 人物立绘

```text
Asset type: FlatWorld Portrait for <dialogue / character UI>, following the shared low-saturation, semi-realistic retro pixel-art direction with relatively few logical pixels.
Input images: Image 1 defines the character's identity: <年龄感、体型、发型、面部印象、服装、主配色与配件>; Image 2 controls <style or pose> only, without copying another character's design.
Primary request: one <人物/表情> portrait, naturally eye-level, <全身或用户指定构图>, in a natural work/life-shaped stance.
Composition: complete head, feet and identity props when full-body; roughly 8-12% safety padding, <实际 UI 构图与尺寸>, readable large shapes and sparse purposeful details.
Background: transparent; no scene, ground, shadow, halo, frame, text, logo or watermark.
Constraints: preserve approved identity and muted palette hierarchy; do not turn a larger output canvas into finer logical pixels or a polished anime illustration by default.
```

用户明确指定其他画风时替换风格段，保留身份、用途、构图和背景约束。明确要求非像素平涂时使用 `broad flat local colors, one hard-edged shadow per material, matte surfaces, no soft gradients or glossy highlights`。

## 保持身份的变体

```text
Input images: Image 1 is the approved identity anchor; Image 2 controls only <动作/表情/姿势>.
Primary request: change only <本次明确变化> for the same subject.
Constraints: preserve identity, apparent age, body type, silhouette, clothing construction, palette, pixel-cluster size, light direction, material treatment and the asset's framing/layout contract. No redesign or unrelated additions.
```

动物变体另保留标准侧向和基线；人物立绘按 UI 构图。每轮保留相关参考职责与消费规格，不因迭代省略主体画风。
