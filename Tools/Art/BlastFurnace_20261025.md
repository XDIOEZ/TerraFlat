# 高炉重绘（2026-10-25）

使用内置 `image_gen`，按 `.agents/skills/flatworld-art/SKILL.md` 当前规则绘制。

- 资产：`Assets/6_Art/Generated/Building/BlastFurnace/BlastFurnace.png`。
- 直接使用生成的 1254×1254 RGBA 原图，保留原始 Alpha；未缩图、限色或重新编码。
- Point、无 Mipmap、无压缩，最大纹理尺寸 2048；1254 PPU、中心 Pivot，继续使用原来 1×1 的 Sprite 坐标范围。
- 建筑本体、召唤器/背包图标、源 Prefab 共用此图。空炉状态不烘入火焰，继续使用原有燃烧表现。
- 原图与 PPU 保持不变；建筑 `Render` 等比放大至 2 倍、向上偏移 0.5 单位，源 Prefab 根缩放归一为 1，与运行时定义及放置预览一致。背包/召唤器图标维持原尺寸。
- 按 Alpha≥128 的实际主体边界（x=277～977、y=28～1229）换算，可见底座宽约 1.118 单位、炉身高约 1.917 单位，底部位于放置格内 y≈-0.462。实体碰撞明确为 1×1；受击范围上移并扩大到 1.125×2。现有火焰锚点通过 `source.TransformPoint` 随主图同步，不另写缩放补偿。
- 当前 Unity 处于 Play Mode，未执行重新进世界或游戏内验收；游戏效果由用户验收。
- 落地高炉由数据机器行网格绘制；`ChunkTilemapRenderer.Mechanical` 同步读取主体缩放，`MechanicalShadowRegistry` 按同一矩阵计算接触阴影，火焰继续跟随已提交主体部件。仅修改源 Prefab 或预览定义不足以改变实际落地大小。

## 最终提示词

```text
Use case: stylized-concept.
Asset type: replacement transparent world-building sprite and inventory icon for the blast furnace (高炉) in FlatWorld, a 2D top-down survival sandbox.
Reference images: Image 1 (Building/Tileset.png) establishes the existing slight overhead/front view and compact furnace identity; its tiny grey open-top furnace is around x80-96,y80-96. Do not copy the sheet or other objects. Image 2 (Shovel_Iron_StyleSample.png) establishes ONLY the large hard-edged pixel clusters, muted realistic materials and restrained shading, never the shovel shape.
Primary request: redraw ONE self-contained early iron-working blast furnace as low-saturation, semi-realistic RETRO PIXEL ART. An upright, sturdy, slightly tapered cylindrical shaft of large grey-taupe refractory masonry blocks, a thick clearly open elliptical top charging mouth with a dark interior, two broad worn dark wrought-iron reinforcing hoops, a wider rounded hearth at the bottom and one large dark arched furnace opening facing the viewer. The masonry is functional, simple and believable; a few large uneven brick courses, no busy microtexture. The furnace should read as a compact tall shaft furnace, not a household fireplace or a pot.
Style: deliberately LOW logical resolution, roughly 48-56 chunky pixels across the entire subject, with visibly large squared stair-step silhouette edges and coherent hard pixel clusters. Preserve this coarse appearance in a large exported image. Broad flat value clusters, only a few restrained steps of light/shadow per material, understated highlights. Slight top-down view consistent with a top-down RPG, almost front-facing and naturally proportioned. Light from upper left. Muted warm stone and charcoal blue-grey iron, soot-dark openings.
Composition: square image; ONE centered furnace occupies about 78 percent of canvas width and 86 percent height, complete silhouette with modest transparent margins. Stable base centered horizontally. Keep the bottom arched opening center around 77 percent down the canvas so an independent runtime flame can appear there. Exterior contour remains substantial and clear at small UI size.
State: cold/unlit; black soot-dark empty hearth and mouth, no flames, embers, smoke or glow. Only the closed masonry/metal body and its working furnace openings; NO external pipework, bellows, flanges, shaft connectors, wires, gauges, decorative studs, props, ore piles, ground, platform, pedestal or cast shadow.
Background: true transparent alpha around the isolated silhouette; opaque hard pixel shapes within it. NO white/checkerboard/background illustration. No blur, no antialiasing, no gradients, no fine engraving, no noise/dithering, no smooth 3D render, no thick pure-black cartoon outline. No text, labels, interface, watermark or alternate versions.
Output: retain the full generated high-resolution square PNG and alpha, preferably around 1024 square; do not compress or reduce to a tiny source image.
```
