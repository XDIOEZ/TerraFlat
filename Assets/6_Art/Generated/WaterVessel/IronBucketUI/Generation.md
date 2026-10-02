# 铁桶容器剖面

- 用途：`UI_WaterVessel` 按 `IronBucket` 匹配的剖面和液体内腔遮罩；图中不烘焙液体。
- 参考：`../IronBucket/IronBucket_Icon.png`、`../ClayJarUI/ClayJar_Cutaway.png`。保留铁桶提手、桶沿和金属加固环，沿用现有容器 UI 的正面剖切视角。
- `IronBucket_Cutaway.png`：128×128，16 个可见颜色，Alpha 0/255；主体边界 x=19..108、y=8..119。
- `IronBucket_Interior.png`：同画布白色硬透明遮罩；逐行取内腔边界并填充，避免生成源的透明孔洞影响液体裁剪。
- 导入：Sprite Single、中心 Pivot、PPU 128、Point、无 Mipmap、无压缩。
- 水位区间：21/128..77/128；左出口 (35/128,76.5/128)，右出口 (93/128,76.5/128)；`MouthWidth=58/128`。坐标从画布左下角计。
- 正式 Prefab 与 `RuntimeUIPrefabBuilder.WaterVessel.cs` 共用上述坐标；原有椰子壳、木桶外观在重建器中一并保留。
- 生成：内置 ImageGen；Unity Texture2D 最近邻采样为 128×128，映射到 16 色钢铁调色板；内腔色键提取为遮罩，剖面内腔替换为暗灰底色。
- 本次只完成美术和静态资源接入，游戏内装液、摇晃、倾倒由用户验收。

## 绘图提示词

Use case: stylized-concept. Create one production FlatWorld game UI EMPTY IRON BUCKET VERTICAL CUTAWAY sprite. Reference 1 existing iron bucket icon defines identity: cool steel gray tapered pail, arched carrying handle, simple rim, metal side attachment studs, two reinforcing bands. Reference 2 clay jar UI cutaway defines function and chunky pixel style. Remove the front half wall and front rim completely so viewer sees an unobstructed deep cavity from OPEN TOP to the INSIDE BOTTOM. Show thin steel cut edges on both sloped sides, thick flat rounded base, rear rim only at top, arched handle outside and behind bucket. Symmetric straight-on front cutaway, slight view of rear inside wall, no perspective skew. IMPORTANT cavity interior is one UNIFORM OPAQUE FLAT MAGENTA #ff00ff color, a functional chroma-key for extracting the liquid mask, extending continuously to the open mouth without any front metal band crossing it. Use magenta ONLY in cavity, never on metal. Outer background genuinely transparent. Hard 128x128 logical pixel grid with 1-2 pixel charcoal outline, limited 12-16 steel gray/cool silver colors and broad flat grouped pixel shading, no gradients, no antialiasing, no fine noise. Complete single silhouette centered in square canvas: handle arch fits above bucket; bucket rim around y=40/128, inside bottom y=112/128; side mouth boundaries about x=22/128 and106/128; no ground, water, liquid, text, arrows, labels, watermark, extra object. Preserve existing bucket identity and same visual density as 128px clay jar. Output transparent PNG.
