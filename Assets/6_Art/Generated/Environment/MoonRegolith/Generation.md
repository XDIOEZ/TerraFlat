# 月壤地面贴图

- 消费链：`GameConfig/Tiles/space_surfaces.json` 的 `Tile_MoonRegolith` → `Assets/7_Tiles/Base/Tile_MoonRegolith.asset` → `MoonRegolith_Ground.png`。
- 使用内置 ImageGen 生成，原样复制 PNG，没有本地改色或重绘；灰白低对比粉尘，不画独立石块或环形山，环形山由正式地形生成器负责。
- 源图 1254×1254，全幅不透明；Unity 按 64×64 上限导入、源 PPU=1254，保持一格尺寸。Single Sprite、Full Rect、中心 Pivot、Point、无 Mipmap、无压缩、不生成地表物理轮廓。
- Tile 位于已有 Addressables `TileBase` 文件夹 `Assets/7_Tiles/Base`，无需另加标签或修改沙地资源。
- 颜色及环形山层次参考：[NASA 月球资料](https://science.nasa.gov/moon/facts/)、[NASA 环形山资料](https://science.nasa.gov/moon/lunar-craters/)。尺寸按游戏比例安排，不对应真实公里数。

## 生成提示词

Create a production-ready seamless tileable lunar regolith ground texture for a top-down 2D pixel-art sandbox game. One square full-bleed tile, opaque from edge to edge, orthographic directly overhead. Use ONLY neutral silver gray / pale ash gray / white-gray tones, absolutely NO yellow, tan, beige, brown, blue or purple. Average base tone around RGB 176,178,180. Subtle small dust grain pixel clusters and a few very faint powdery mottled patches, deliberately low contrast (roughly RGB 160..194), flat diffuse lighting. Designed to repeat across thousands of floor cells: all four edges must match seamlessly, no borders, no grid, no large identifiable repeating objects, no stones, pebbles, boulders, footprints, craters, circles, cracks, cast shadows, 3D perspective, text or labels. Craters will be made by game terrain geometry separately. Clean restrained pixel art with fine hard-edged grains; the texture should read well when reduced to 64x64 pixels. Deliver only the seamless square texture filling the entire image.
