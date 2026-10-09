# 化工物品正式美术与接入记录

本目录提供 26 款静态单帧透明 PNG：17 类设备（每类供建筑本体与召唤器共用）、2 种材料和 7 件装备，共替换 43 个物品定义。设备只绘制机身；外部流体接管、传动轴接口与电线连接由程序化表现层负责。阀轮、表盘、滤布和喷口格栅是主体功能结构，保留在图中。

制作模式：内置 `image_gen`，逐款独立生成或编辑，`transparent_background=true`。最终 PNG 保留生成尺寸与 RGBA；未通过本地程序裁剪、缩放、量化或硬化 Alpha。图像包含半透明边缘，不能当作硬 Alpha 的固定色数像素图。原始生成源保留在 Codex 的 generated_images 目录。

实际查看的画风参考：

- `Assets/6_Art/Generated/Electrical/Generator_Basic/Generator_Basic_Body.png`
- `Assets/6_Art/Generated/Electrical/Battery_Basic/Battery_Basic_Idle.png`
- `Assets/6_Art/Generated/Mechanical/World/Transmission/World_Gearbox_Wood.png`
- `Assets/6_Art/Generated/WaterVessel/IronBucket/IronBucket_Icon.png`
- `Assets/6_Art/Items/Equipment/Iron Equipment/Iron Chestplate.png`
- `Assets/6_Art/Generated/InventoryCrafting/Powders/OrePowder_Stone.png`

设备采用蓝灰铁、暖铜色加固件与深炭轮廓；宇航服采用米白面料、黑色密封接缝和暗青面板。每款物品保留独立可辨识轮廓。实际世界观感由用户在游戏中验收。

提示词记录：

- 本文：通用管道、阀门、出气口、气体筛选管道最终机身编辑，以及杂质、碱液生成。
- [machine-prompts.md](machine-prompts.md)：九款机电设备原始设计与最终无接管编辑；历史管口方向要求已失效。
- [vessel-edits.md](vessel-edits.md)：铁/钢方块气罐、储液罐与过滤器最终无接管编辑。
- [equipment-prompts.md](equipment-prompts.md)：两件便携气罐与五件宇航服。
- [art-preview.html](art-preview.html)：26 款素材总览，可搜索和放大查看。

导入按项目已有 Sprite 模板配置：单张 Sprite、中心 Pivot、Full Rect、Point、无 Mipmap、无纹理压缩、Alpha Transparency，保留建筑需要的自动物理轮廓。PPU 按源图像素尺寸与消费方世界尺寸换算，参考 [Unity TextureImporter](https://docs.unity3d.com/cn/current/ScriptReference/TextureImporter.html) 与 [spritePixelsPerUnit](https://docs.unity3d.com/ja/current/ScriptReference/TextureImporter-spritePixelsPerUnit.html)。流体节点按宽高分别 Fit，因此设备使用正方形画布；管路节点 0.55 单位，探针 0.32 单位，其余流体设备 0.85 单位，普通电热器 1 单位。材料/装备最长边 1 单位。每款 PNG 注册同址 `ItemSprite` Addressables 条目；装备原乘色设为白色。

按用户要求，未运行 Unity 导入验收、Play Mode、编译或游戏测试。Unity 导入后资源加载、实际大小、程序化接口遮挡和穿戴表现由用户验收。

## FluidPipe

最终机身编辑提示词：

```text
Use case: precise-object-edit. Edit the supplied transparent FlatWorld game sprite. The game's external fluid piping, shafts, axle collars and electrical wires are drawn procedurally at runtime. The delivered PNG must contain ONLY the complete device housing, with no connecting pipe, pipe mouth, flange, shaft, collar or wire on any side. Preserve the observed blue-gray iron, warm copper accents, dark charcoal stepped outline, coarse square pixel clusters, simple top-left highlights and authentic chunky pixel-art style. Use a SQUARE transparent canvas of the same original dimensions, centered composition, clean transparent padding. No antialiasing, gradients, shadow, floor, glow, text or new objects. Remove all FOUR long pipe arms and their copper end flanges from the attached cross pipe. Retain and enlarge the original central CLOSED square blue-gray manifold block with its circular sealed front inspection plate and four copper bolt heads, until that central housing fills about 75 percent of the square canvas in both dimensions. Keep it recognizably a compact junction/manifold housing, with closed straight sides. NO protruding tubes or connectors on north/east/south/west, NO openings, NO pipe stubs. The four procedural connections will attach to the block separately at runtime.
```

## FluidValve

最终机身编辑提示词：

```text
Use case: precise-object-edit. Edit the supplied transparent FlatWorld game sprite. The game's external fluid piping, shafts, axle collars and electrical wires are drawn procedurally at runtime. The delivered PNG must contain ONLY the complete device housing, with no connecting pipe, pipe mouth, flange, shaft, collar or wire on any side. Preserve the observed blue-gray iron, warm copper accents, dark charcoal stepped outline, coarse square pixel clusters, simple top-left highlights and authentic chunky pixel-art style. Use a SQUARE transparent canvas of the same original dimensions, centered composition, clean transparent padding. No antialiasing, gradients, shadow, floor, glow, text or new objects. Remove all FOUR protruding pipe arms and their connector flanges from the attached valve. Preserve the central valve body and large red four-spoke handwheel with brass hub EXACTLY as the device identity. Center that complete closed compact valve housing and enlarge it uniformly so the wheel/body fills about 78 percent of the square canvas. Rebuild closed body edges after deleting tubes. The handwheel is a real operating part and MUST remain; no external pipe, collar, flange, mouth or stub anywhere.
```

## FluidOutlet

最终机身编辑提示词：

```text
Use case: precise-object-edit. Edit the supplied transparent FlatWorld game sprite. The game's external fluid piping, shafts, axle collars and electrical wires are drawn procedurally at runtime. The delivered PNG must contain ONLY the complete device housing, with no connecting pipe, pipe mouth, flange, shaft, collar or wire on any side. Preserve the observed blue-gray iron, warm copper accents, dark charcoal stepped outline, coarse square pixel clusters, simple top-left highlights and authentic chunky pixel-art style. Use a SQUARE transparent canvas of the same original dimensions, centered composition, clean transparent padding. No antialiasing, gradients, shadow, floor, glow, text or new objects. Replace the attached outlet's long left connection tube and long right trumpet tube with a COMPACT closed exhaust-device housing. Preserve the blue-gray central valve housing and the small copper handwheel on top. The front face of the housing has a DARK rectangular recessed ventilation grille with three broad chunky slats, so it remains readable as an outlet device without a protruding pipe or horn. Keep warm copper reinforcement accents around the main closed body. Center the whole compact housing on the same square canvas, about 70 percent width and height. No external tube, trumpet, hose, flange, connector collar, mouth or input/output pipe on any side.
```

## GasSelector

最终机身编辑提示词：

```text
Use case: precise-object-edit. Edit the supplied transparent FlatWorld game sprite. The game's external fluid piping, shafts, axle collars and electrical wires are drawn procedurally at runtime. The delivered PNG must contain ONLY the complete device housing, with no connecting pipe, pipe mouth, flange, shaft, collar or wire on any side. Preserve the observed blue-gray iron, warm copper accents, dark charcoal stepped outline, coarse square pixel clusters, simple top-left highlights and authentic chunky pixel-art style. Use a SQUARE transparent canvas of the same original dimensions, centered composition, clean transparent padding. No antialiasing, gradients, shadow, floor, glow, text or new objects. Remove the three external pipes and copper connector flanges from the TOP, LEFT and BOTTOM of the attached gas selector. Keep the central blue-gray rectangular selector/filter chamber and its two distinctive small front windows, top teal and bottom ochre, with corner bolts. Restore closed flat top/bottom/side housing surfaces after removing all tubes. Center the complete chamber and enlarge it uniformly until its longest dimension is about 74 percent of the same square canvas. No external tube, mouth, flange, shaft, collar or connector on any side; no hole or replacement stub.
```

## FilterResidue

最终生成提示词：

```text
Use case: stylized-concept. Asset type: FlatWorld pixel-art inventory MATERIAL icon for impurities separated by a water filter. Create exactly ONE small compact irregular pile of muddy gray-brown grit: a coherent lumpy mound made of 4-6 dark earth/charcoal chunks, tiny muted beige gravel embedded, two simple angular larger stone fragments. Distinct from a clean pale stone powder pile; subdued olive brown and charcoal, a few warm beige highlights. Match the viewed FlatWorld OrePowder_Stone palette clustering and strong small readable material silhouette. HARD square pixel clusters at approximately 48x48 logical pixels, only 3-4 tones per material, no high-frequency grain or realistic texture. Centered complete isolated pile occupies about 75% width, 50% height on a SQUARE 1:1 1280x1280 transparent PNG with generous transparent padding all sides. No bag, bowl, machine, floor, shadow, text, frame, logo, watermarks, sparkles, gradients or antialiasing. TRUE transparent alpha background.
```

## AlkaliSolution

最终生成提示词：

```text
Use case: stylized-concept. Asset type: FlatWorld pixel-art inventory LIQUID REAGENT icon. Create exactly ONE compact clear pale blue-green liquid sample represented by a simple squat round laboratory bottle with short narrow neck, muted tan cork, dark blue-gray rim and base, flat turquoise liquid level inside about half high. Body is a single clear bulb shape, glass rendered with opaque hard color clusters and only three tones, two simple cream reflection blocks. Rustic copper/iron era sandbox appearance, not futuristic. One object only, strongly readable distinct reagent silhouette, no label or warning icon. Same chunky FlatWorld pixel art language as the viewed iron bucket and item materials: charcoal outline about 2 logical pixels, limited muted palette, top-left lighting, about 64x64 logical pixel detail. Centered complete object occupies about 64% width and 82% height on SQUARE 1:1 1280x1280 PNG with true alpha transparent background. Preserve margins all 4 sides. No ground, cast shadow, glow, smooth gradients, antialiasing, text, numbers, logos, watermarks or other objects.
```


## 最终资源参数

以下数值读取自接入的实际 PNG 和 Meta，不代表游戏运行验收。主体边界以 Alpha >= 128 统计，左上原点，右/下坐标不包含；可见 RGB 是高分辨率像素值数，不是设计调色板大小。全部为静态单帧、RGBA、中心 Pivot。

| ID | 画布 px | 主体边界 | 可见 RGB 数 | Alpha 值数 | PPU |
| --- | --- | --- | ---: | ---: | ---: |
| AlkaliSolution | 1254 x 1254 | 290, 179, 964, 1084 | 20803 | 256 | 1254 |
| ElectricHeater | 1254 x 1254 | 160, 151, 1095, 1117 | 37894 | 256 | 1254 |
| FilterResidue | 1254 x 1254 | 173, 413, 1081, 891 | 9212 | 256 | 1254 |
| FluidFilter | 1254 x 1254 | 141, 183, 1113, 1062 | 42169 | 256 | 1475.294118 |
| FluidOutlet | 1254 x 1254 | 236, 288, 1019, 972 | 45294 | 256 | 2280 |
| FluidPipe | 1254 x 1254 | 155, 185, 1099, 1087 | 24938 | 256 | 2280 |
| FluidValve | 1254 x 1254 | 151, 100, 1104, 1148 | 45451 | 256 | 2280 |
| GasCompressor_Electric | 1254 x 1254 | 120, 179, 1135, 1076 | 39639 | 256 | 1475.294118 |
| GasCompressor_Mechanical | 1254 x 1254 | 85, 276, 1159, 981 | 39445 | 256 | 1475.294118 |
| GasEngine | 1254 x 1254 | 266, 212, 1014, 1107 | 46112 | 256 | 1475.294118 |
| GasPump_Mechanical | 1254 x 1254 | 110, 94, 1145, 1107 | 71373 | 256 | 1475.294118 |
| GasSelector | 1254 x 1254 | 250, 240, 1005, 1014 | 15439 | 254 | 2280 |
| GasTankBlock_Iron | 1254 x 1254 | 169, 185, 1085, 1064 | 48911 | 256 | 1475.294118 |
| GasTankBlock_Steel | 1254 x 1254 | 158, 157, 1096, 1097 | 31195 | 256 | 1475.294118 |
| LiquidPump_Electric | 1254 x 1254 | 207, 248, 1090, 1012 | 24785 | 256 | 1475.294118 |
| LiquidStorageTank | 1254 x 1254 | 177, 152, 1077, 1156 | 36250 | 256 | 1475.294118 |
| PortableGasTank_Iron | 1254 x 1254 | 406, 109, 847, 1154 | 13574 | 256 | 1254 |
| PortableGasTank_Steel | 1254 x 1254 | 354, 148, 901, 1135 | 22309 | 256 | 1254 |
| PressureProbe_Electronic | 1254 x 1254 | 114, 162, 1140, 1141 | 24128 | 256 | 3918.75 |
| PressureProbe_Mechanical | 1254 x 1254 | 292, 131, 960, 1114 | 34417 | 256 | 3918.75 |
| Spacesuit_Feet | 1390 x 1132 | 193, 190, 1197, 959 | 11849 | 256 | 1390 |
| Spacesuit_Hands | 1265 x 1244 | 98, 219, 1167, 1015 | 17015 | 256 | 1265 |
| Spacesuit_Head | 1244 x 1264 | 110, 99, 1137, 1184 | 25133 | 256 | 1264 |
| Spacesuit_Legs | 1117 x 1408 | 232, 155, 885, 1231 | 14950 | 256 | 1408 |
| Spacesuit_Torso | 1312 x 1199 | 56, 159, 1257, 1028 | 23445 | 256 | 1312 |
| WaterElectrolyzer | 1254 x 1254 | 181, 221, 1073, 1047 | 53206 | 256 | 1475.294118 |
