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
Composition/framing: exactly one complete creature using one canonical lateral direction only — default to <向左> so the head and body face screen-left — with <动作>; no cropped ears, tail, legs, or wings; final runtime subject fits the target canvas and aligns bottom-center. Put locomotion and requested special-action frames into one equal-size Sprite Sheet when possible; derive the right-facing version by Unity horizontal mirroring instead of generating duplicate right-facing frames. Never include front-facing, back-facing, upward, downward, or mixed four-direction frames. Higher logical pixel density is allowed, but silhouette, outline weight, palette structure, and material rendering must still match the closest existing project creature.
Scene/backdrop: perfectly flat solid chroma-key background.
Constraints: preserve simplified animal anatomy; no clothing unless requested; no floor, shadow, text, watermark, or extra creature; keep every source frame in the same canonical lateral direction and let Unity's `flipX`/equivalent mirror produce the opposite side.
```

## 物品、工具、树木、植被、建筑或世界道具

```text
Use case: stylized-concept
Asset type: FlatWorld pixel-art <inventory icon / tool / tree / vegetation / building / world prop>
Input images: Image 1 is the closest same-category project asset and is the primary authority for viewpoint, world scale, silhouette, pixel-cluster size, outline treatment, palette structure, and material rendering; additional same-category project assets may be used to confirm the shared style. Do not use the merchant anchor to override item, tool, tree, vegetation, or building style.
Primary request: create one <物品名称>.
Style/medium: FlatWorld pixel art matching the closest same-category runtime asset; strong compact silhouette; hard grouped pixels; limited material color groups; hard edges; limited palette; subtle top-left highlight. Logical pixel density may be higher than older assets, but the result must not become smoother, more realistic, more detailed, or visually denser than the established style language.
Composition/framing: one centered object, complete silhouette, readable at the target runtime size and world scale. Choose canvas size and PPU from the consumer requirements so the object remains proportionally consistent with existing same-category assets.
Scene/backdrop: perfectly flat chroma-key background.
Constraints: no card background; no frame; no label; no shadow unless same-category references contain one; no watermark; no extra objects. For trees, vegetation, large props, and buildings, preserve the existing project's leaf/shape clustering, trunk or structural massing, outline weight, light direction, saturation, and top-down viewpoint even when using a higher pixel density.
```

## 带传动接口的机械设备

先从实际相邻轴、电线和同类机身换算目标可见大小、轴心、铁环内缘位置及机身 PPU，再填入模板。连接件使用现有标准 Sprite，生成器只负责机身；参考图里的接口用于定位，不要求生成器重新画一套接口。

```text
Asset type: FlatWorld pixel-art machine housing, prepared for separate standard mechanical/electrical connector layers.
Input images: Image 1 is the approved housing design; Image 2 is the existing in-game shaft and iron collar; Image 3 is the existing cable when needed. Preserve the housing design and use the connectors as scale and alignment references.
Primary request: create one <设备名称> housing with a visible size of <目标世界宽高> on a <画布尺寸> canvas, intended for <机身 PPU> PPU.
Composition/alignment: preserve the approved housing's aspect ratio; place the connection axis at <画布中的轴心位置>; make the <连接侧> housing edge meet the standard collar's inner edge at <换算后的像素位置>. A marked size box indicates overall size only, not a new aspect ratio.
Layers: housing only; do not bake in the external shaft, iron collar, cable, or cable junction. These will be composed from the original project sprites at their unchanged world size. Leave enough opaque housing around the entry to cover the internal connector; when a flush collar connection is requested, show no wooden stub between the housing and collar.
Style/medium: match the same-category FlatWorld asset's viewpoint, pixel clusters, outline, palette, and lighting; complete silhouette; transparent background; no ground, cast shadow, text, watermark, or extra objects.
```

生成后先按真实 PPU、Pivot、偏移和图层顺序拼出“电线—机身—传动轴”或相应机械组合，查看大小、铁环贴合及 90 度旋转。仅大小不合适时，调整机身 PPU 或等比例缩放；接口保持标准尺寸，并按同一组坐标重合成静态图标，不为尺寸问题重新生成整套图片。

## 保持身份的变体或动画

```text
Use case: identity-preserve
Input images: Image 1 is the approved runtime character anchor; Image 2 is the project animation-layout reference.
Primary request: create <新动作/装备变体> for the same character while keeping the approved canonical lateral direction.
Constraints: preserve head shape, face, proportions, outfit construction, accessory placement, palette, outline thickness, light direction, feet baseline, identity, and the selected canonical lateral direction; change only <明确变化>; do not generate a duplicate opposite-facing frame because Unity mirrors it at runtime; no redesign; no front/back/up/down direction; no extra elements; no text; no watermark.
```

## 迭代原则

- 一次只修正一个可观察问题，例如“让眼睛在目标运行时尺寸下保留”或“让描边在与现有素材相同世界尺度下保持一致的视觉粗细”。
- 每轮重复身份、同类项目参考、调色板、比例、背景、目标画布、世界尺度和对齐约束，不因进入迭代而省略硬规则；其中画风一致性优先于机械复刻某个固定像素尺寸。
- ImageGen 结果需检查消费方尺寸、画风、透明度和导入设置；仅在实际需要时缩放、量化或清边，用户指定原图直用时保留文件内容。
