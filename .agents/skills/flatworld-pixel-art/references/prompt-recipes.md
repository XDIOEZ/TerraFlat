# FlatWorld 运行时像素美术提示词模板

先读取 `style-guide.md` 并选择最接近目标类别的参考图。以下模板只提供结构，不得无故增加角色、道具或剧情。

## 通用角色或 NPC

```text
Use case: stylized-concept
Asset type: game-ready FlatWorld 2D top-down pixel-art character, <single pose / sprite sheet>, using the runtime canvas and logical pixel density required by the existing character consumer or closest same-category project reference
Input images: Image 1 is the closest existing project character and is the primary reference for world scale, proportions, pixel clusters, outline weight, palette structure, and animation layout; Image 2 is the approved merchant style anchor and may supplement character design language. Use references only for style and scale; create an original subject.
Primary request: create one <角色身份> for FlatWorld.
Subject: <少量可辨识服装与 1-3 个身份配件>
Style/medium: authentic FlatWorld pixel art; oversized head; compact body; dark charcoal outline matching the closest project character's visual weight at the same world scale; limited muted palette; hard square pixel clusters; one shadow and one highlight per major material. Higher logical pixel density is allowed only if the same visual style is preserved.
Composition/framing: exactly one full-body sprite, <朝向>, neutral pose, complete feet and silhouette, generous padding in the high-resolution source; final runtime subject must fit the target consumer canvas, align bottom-center, and preserve the same world-scale proportions and pixel-cluster language as the closest existing project character.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background.
Constraints: no gradients; no anti-aliasing; no cast shadow; no floor; no text; no watermark; no extra characters; do not use #ff00ff in the subject.
Avoid: realistic anatomy; detailed fingers; thin lines; smooth vector edges; tiny decorations that disappear at target size; high-resolution UI portrait treatment.
```

## 动物或怪物

```text
Use case: stylized-concept
Asset type: FlatWorld 2D top-down pixel-art creature sprite
Input images: Image 1 is the closest project animal or monster reference and is the primary authority for species silhouette, world scale, viewpoint, pixel clusters, outline weight, palette structure, and material rendering; Image 2 is the merchant anchor and may only supplement broad FlatWorld color-clustering language when it does not conflict with creature references.
Primary request: create one original <动物/怪物> readable at the target runtime size, using the closest existing project creature as the primary style, world-scale, and pixel-density reference.
Style/medium: compact FlatWorld pixel art; strong species silhouette; limited muted palette; dark outline matching the closest project creature's visual weight at the same world scale; hard color clusters; no anti-aliasing or gradient. Higher logical pixel density is allowed without changing the established creature style.
Composition/framing: exactly one complete creature, <朝向/动作>, no cropped ears, tail, legs, or wings; final runtime subject fits the target canvas and aligns bottom-center. Higher logical pixel density is allowed, but silhouette, outline weight, palette structure, and material rendering must still match the closest existing project creature.
Scene/backdrop: perfectly flat solid chroma-key background.
Constraints: preserve simplified animal anatomy; no clothing unless requested; no floor, shadow, text, watermark, or extra creature.
```

## 物品、工具、树木、植被、建筑或世界道具

```text
Use case: stylized-concept
Asset type: FlatWorld pixel-art <inventory icon / tool / tree / vegetation / building / world prop>
Input images: Image 1 is the closest same-category project asset and is the primary authority for viewpoint, world scale, silhouette, pixel-cluster size, outline treatment, palette structure, and material rendering; additional same-category project assets may be used to confirm the shared style. Do not use the merchant anchor to override item, tool, tree, vegetation, or building style.
Primary request: create one <物品名称>.
Style/medium: FlatWorld pixel art matching the closest same-category runtime asset; strong compact silhouette; hard grouped pixels; limited material color groups; hard edges; limited palette; subtle top-left highlight. Logical pixel density may be higher than older assets, but the result must not become smoother, more realistic, more detailed, or visually denser than the established style language.
Composition/framing: one centered object, complete silhouette, readable at the target runtime size and world scale. Do not force a 16x16 canvas unless the actual consumer requires it; choose canvas size and PPU so the object remains proportionally consistent with existing same-category assets.
Scene/backdrop: perfectly flat chroma-key background.
Constraints: no card background; no frame; no label; no shadow unless same-category references contain one; no watermark; no extra objects. For trees, vegetation, large props, and buildings, preserve the existing project's leaf/shape clustering, trunk or structural massing, outline weight, light direction, saturation, and top-down viewpoint even when using a higher pixel density.
```

## 保持身份的变体或动画

```text
Use case: identity-preserve
Input images: Image 1 is the approved runtime character anchor; Image 2 is the project animation-layout reference.
Primary request: create <新朝向/动作/装备变体> for the same character.
Constraints: preserve head shape, face, proportions, outfit construction, accessory placement, palette, outline thickness, light direction, feet baseline, and identity; change only <明确变化>; no redesign; no extra elements; no text; no watermark.
```

## 迭代原则

- 一次只修正一个可观察问题，例如“让眼睛在目标运行时尺寸下保留”或“让描边在与现有素材相同世界尺度下保持一致的视觉粗细”。
- 每轮重复身份、同类项目参考、调色板、比例、背景、目标画布、世界尺度和对齐约束，不因进入迭代而省略硬规则；其中画风一致性优先于机械复刻某个固定像素尺寸。
- ImageGen 结果只有在本地缩小、量化、清边并通过静态和人工像素检查后，才称为运行时 Sprite。
