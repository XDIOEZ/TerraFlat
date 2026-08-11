# FlatWorld 美术生成提示词模板

先读取 `style-guide.md` 并选择最接近目标类别的参考图。以下模板只提供结构，不得无故增加角色、道具或剧情。

## 通用角色/NPC

```text
Use case: stylized-concept
Asset type: game-ready FlatWorld 2D top-down pixel-art character on a fixed 16x16 logical-pixel canvas, <single pose / sprite sheet>
Input images: Image 1 is the approved merchant style anchor and defines proportions, palette relationships, pixel clusters, and outline treatment; Image 2 is the closest project character reference and defines scale or animation layout. Use references only for style and scale; create an original subject.
Primary request: create one <角色身份> for FlatWorld.
Subject: <少量可辨识服装与 1-3 个身份配件>
Style/medium: authentic low-resolution pixel art; oversized head; compact body; one-pixel dark charcoal outline; limited muted palette; hard square pixel clusters; one shadow and one highlight per major material.
Composition/framing: exactly one full-body sprite, <朝向>, neutral pose, complete feet and silhouette, generous padding in the high-resolution source; final runtime subject must fit inside a fixed 16x16 canvas and align bottom-center.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background.
Constraints: no gradients; no anti-aliasing; no cast shadow; no floor; no text; no watermark; no extra characters; do not use #ff00ff in the subject.
Avoid: realistic anatomy; detailed fingers; thin lines; smooth vector edges; tiny decorations that disappear at target size.
```

## 动物或怪物

```text
Use case: stylized-concept
Asset type: FlatWorld 2D top-down pixel-art creature sprite
Input images: Image 1 is the closest project animal/monster reference and defines species silhouette, scale, and viewpoint; Image 2 is the merchant anchor and only defines shared color clustering and dark-outline language.
Primary request: create one original <动物/怪物> readable on a fixed 16x16 logical-pixel canvas.
Style/medium: compact low-resolution pixel art; strong species silhouette; limited muted palette; one-pixel dark outline; hard color clusters; no anti-aliasing or gradient.
Composition/framing: exactly one complete creature, <朝向/动作>, no cropped ears, tail, legs, or wings; final runtime subject fits inside 16x16 and aligns bottom-center.
Scene/backdrop: perfectly flat solid chroma-key background.
Constraints: preserve animal anatomy in simplified form; no clothing unless requested; no floor, shadow, text, watermark, or extra creature.
```

## 物品、工具或世界小道具

```text
Use case: stylized-concept
Asset type: FlatWorld pixel-art <inventory icon / world prop>
Input images: Image 1 is the closest same-category project asset and defines viewpoint, scale, and material rendering; the merchant anchor is palette language only when useful.
Primary request: create one <物品名称>.
Style/medium: low-resolution pixel art; strong compact silhouette; 2-3 material color groups; hard edges; limited palette; subtle top-left highlight.
Composition/framing: one centered object, complete silhouette, readable on a fixed 16x16 logical-pixel canvas.
Scene/backdrop: perfectly flat chroma-key background.
Constraints: no card background; no frame; no label; no shadow unless same-category references contain one; no watermark; no extra objects.
```

## 保持角色一致的变体或动画

```text
Use case: identity-preserve
Input images: Image 1 is the approved character anchor; Image 2 is the project animation-layout reference.
Primary request: create <新朝向/动作/装备变体> for the same character.
Constraints: preserve head shape, face, proportions, outfit construction, accessory placement, palette, outline thickness, light direction, feet baseline, and identity; change only <明确变化>; no redesign; no extra elements; no text; no watermark.
```

## 迭代原则

- 一次只修正一个问题，例如“让眼睛在 16x16 画布下保留”或“把轮廓减为 1 像素”。
- 每轮重复不可变约束，不要在迭代时省略身份、调色板、比例和背景规则。
- ImageGen 结果只有在本地缩小、量化、清边并通过静态检查后，才称为游戏 Sprite。
