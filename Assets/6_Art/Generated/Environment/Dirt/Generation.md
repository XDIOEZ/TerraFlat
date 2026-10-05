# 泥土地面贴图

- 消费：`Tile_Dirt` 地块定义 → `Tile_Dirt.asset` → `Dirt_Ground.png`。单帧、全幅不透明、中心 Pivot，世界尺寸 1×1 格。
- 原泥土与泥土墙共用 `TileBase_Dirt` 和 jawbreaker 图集的 8×8 切片；新地面 Tile 放入已有 TileBase Addressables 文件夹，仅 terrain JSON 改用新外观。泥土墙继续使用旧 Tile，原图集不修改。
- 参考：实际查看 jawbreaker_tiles.png、Clay_Ground.png 和 Peat_Ground.png；选择中棕色普通土壤，以少量土团和颗粒区分黏土与泥炭。
- 模式：内置 ImageGen 新生成，再以内置 ImageGen 简化纹理；最终源 PNG 原样保存，未做本地像素编辑。
- 源图：1254×1254，RGB，1694 个可见颜色，全幅 Alpha=255，主体边界覆盖整个画布。生成器未严格输出五色或原生 32×32 文件，不把提示词要求当作实际统计。
- 导入：Single Sprite、Full Rect、中心 Pivot、Point、无 Mipmap、无压缩；各平台 maxTextureSize=32、源 PPU=1254，按源尺寸换算为一格。运行时导入为 32×32，源图颜色数不等于导入颜色数。
- Sprite 由 Tile 直接依赖加载；新 Tile 使用 `Assets/7_Tiles/Base` 已有 TileBase 文件夹标签，无需额外的 ItemSprite 地址。
- 静态资源接入；游戏内观感与铺接效果由用户验收。

## 生成提示词

Use case: stylized-concept. Asset type: one production pixel-art DIRT GROUND tile for FlatWorld, a top-down 2D sandbox game. Primary request: redraw natural ordinary dirt soil, distinctly medium warm brown, lighter than dark peat and darker than ochre clay. Reference style: existing project clay and peat tiles shown in this conversation use restrained earthy flat colors and small hard pixel clusters. Composition: one square seamless repeating ground TEXTURE covering the entire image, no surrounding scene, no margins. EXACTLY a 32-column x 32-row logical pixel grid, each logical pixel enlarged as a solid hard square across the output; like a native 32x32 tile zoomed with nearest neighbor. Material: mostly calm flat packed loose soil with about 10 irregular small low-contrast dirt clods of 2 to 4 logical pixels each, some sparse paired dark grain marks and very few tan grain highlights; scattered organically without rows or central focal object. Use only 5 earthy flat brown shades near #805936 #906540 #9e744b #735032 #ae8457, dominant #906540. Contrast gentle, avoid black outlines. Seamless opposite edges in both axes, no border or grid lines. Entire canvas fully opaque right to all four edges. No grass, plants, roots, large rocks, bricks, paving, holes, trenches, deep cracks, mound silhouette, perspective, cube, shadows, text, icons, watermarks, gradients, anti-aliasing, dithering or fine noisy photographic texture. The result must read as plain earth ground at 32x32, not stone or farmland.

## 最终简化提示词

Simplify this dirt ground texture for a native 32x32 pixel tile. Preserve medium warm brown soil identity. Change ONLY pixel density and color complexity. EXACTLY 32 columns and 32 rows of large solid flat square logical pixels across the entire image, no finer subpixels. NO gradients inside any logical pixel or anywhere across the canvas. Limit to FIVE SOLID COLORS #805936 #906540 #9e744b #735032 #ae8457. The base is completely uniform #906540 covering 70 percent of the tile. Scatter only TEN small irregular clumps of soil with 2-4 logical pixels each, plus only TWELVE paired dark grain marks, and FIVE single tan highlight pixels TOTAL. Very calm, sparse texture, no detailed noise. 32x32 pixel artwork enlarged sharply to fill a square canvas edge to edge. Opposite edges seamlessly repeat; no border. Entire image opaque. No rocks, plants, grass, bricks, cracks, cube, cast shadows, gradients, anti-aliasing, smoothing, dithering, text or watermark.
