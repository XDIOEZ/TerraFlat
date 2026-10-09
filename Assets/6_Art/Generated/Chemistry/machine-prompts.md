# 化工设备像素美术生成提示词

最终规则：本批 9 款资产只绘制设备机身。所有流体输入/输出、传动轴口、轴环和电线接口由程序化运行表现接入与离开，PNG 不再绘制外露短管、嘴口、法兰、管环、轴头、轴环或电线。原始生成记录里的方向管口要求已取消，只作为历史追踪保留。

模式：built-in image_gen；每款设备独立生成透明 PNG。6 款带管口设备已通过 built-in image_gen 编辑移除全部外露流体接口，删口位置恢复完整闭合机壳；电热器及两个探针原本无外接口，保留原图。所有原始生成源留在 Codex generated_images 目录，项目仅接入最终 PNG。没有使用 Python 修改任何像素。

参考图（已实际打开）：

- `Assets/6_Art/Generated/Electrical/Generator_Basic/Generator_Basic_Body.png`
- `Assets/6_Art/Generated/Electrical/Battery_Basic/Battery_Basic_Idle.png`
- `Assets/6_Art/Generated/Mechanical/World/Transmission/World_Gearbox_Wood.png`

共同画风：正面微俯视、暖铜与黄铜、蓝灰铁、深炭描边、硬方像素簇、有限材质色阶、透明留白，无背景、文字、地面或投影。保留设备内部工作机构、表盘、支架和各款身份；活塞帽、闭合电机壳、压缩腔、内部连杆、主体固定螺栓属于机身结构，不当作外露接口删除。

实际 PNG 参数（仅读取文件，不修改像素）：9 张画布均为 `1254×1254` RGBA；四角 Alpha 均为 0。所有成图包含边缘中间 Alpha 和多种 RGB 值，未声称硬 Alpha 或固定 16 色。实际参考发电机、电池同样是 `1254×1254` 且含中间 Alpha。因极淡边缘像素导致 Alpha>0 的包围盒偏大，下表用 Alpha>=128 表示主体可见边界，坐标从 PNG 左上开始，右、下边界为 exclusive。

| ID | 可见主体边界（Alpha>=128） | 可见 RGB 颜色数（Alpha>0） |
| --- | --- | ---: |
| GasPump_Mechanical | (110,94)-(1145,1107) | 72263 |
| LiquidPump_Electric | (207,248)-(1090,1012) | 25660 |
| PressureProbe_Mechanical | (292,131)-(960,1114) | 35210 |
| PressureProbe_Electronic | (114,162)-(1140,1141) | 24413 |
| GasCompressor_Mechanical | (85,276)-(1159,981) | 40575 |
| GasCompressor_Electric | (120,179)-(1135,1076) | 40254 |
| ElectricHeater | (160,151)-(1095,1117) | 38450 |
| WaterElectrolyzer | (181,221)-(1073,1047) | 53641 |
| GasEngine | (266,212)-(1014,1107) | 47125 |

PNG 的真实导入、Addressables 注册及游戏内显示由接入流程处理；本文件不代表 Unity 或运行验收已执行。

## GasPump_Mechanical

最终路径：`Assets/6_Art/Generated/Chemistry/GasPump_Mechanical/GasPump_Mechanical_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. The only connector to delete is the hollow brass pipe sticking out of the RIGHT side midway down the sprite, including its flange and socket. Close that right casing edge with the matching blue-gray iron face. Keep the twin upright brass pumping cylinders, their top solid piston caps, enclosed central brass crank linkage, large round cream dial, crankcase, bolts, brass retaining strips and iron feet untouched. Solid piston caps are part of the pumping mechanism, not fluid pipe mouths; preserve them.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept.
Asset type: single game-ready FlatWorld pixel-art machine housing sprite, one mechanical gas pump.
Primary request: create the definitive independent sprite GasPump_Mechanical_Idle. Match the observed project generator and battery sprites: warm copper and brass with blue-gray iron ribs, dark charcoal pixel outline, front orthographic view with just a shallow visible top. Authentic chunky square pixel clusters as if made at a 112x112 logical grid and enlarged with nearest-neighbor; three hard shades per material, subtle top-left highlight, no antialiasing, no gradients, no soft illustration.
Subject: compact squat piston air pump. Two short brass vertical pumping cylinders over one blue-gray crankcase, a clearly visible chunky lever/linkage inside the housing between them, sturdy iron feet, one pale round pressure dial with just one dark needle and no markings or numerals. Recognizable pump silhouette, not a generator.
Ports and layers: ONE SHORT GAS OUTLET on the RIGHT edge only, a small brass pipe mouth located at vertical canvas center. Left mechanical drive is a separate runtime layer: the left housing at vertical center must be opaque and flush for that overlay, with NO exterior shaft, collar, iron ring or wooden axle drawn in this image. No left fluid pipe, no top or bottom pipe. No electric cables, no plugs.
Composition: exactly one complete centered machine, symmetric framing in a square canvas, visible object occupies about 80 percent of canvas width and height, transparent empty padding around every side, axis level aligned near image center; a 1x1 game-world device.
Scene/backdrop: genuinely transparent alpha, no background scene or card, no floor or ground, no drop or cast shadow, no glow, no smoke, no fire, no decorative steam. No letters, numbers, writing, watermarks, logos or extra objects.
```

历史定向编辑提示词（管口位置要求已失效）：

```text
Use case: precise-object-edit. Edit the attached mechanical gas pump sprite with ONLY ONE change: move its ONE brass right-side outlet upward so the CENTER of that outlet mouth is EXACTLY halfway down the entire 1280x1280 PNG canvas (y=640 from top). Currently the pipe is on the lower-right crankcase around y=770; erase that old pipe cleanly and restore the blue-gray closed casing edge there. Reinstall that identical short brass pipe on the RIGHT housing edge at y=640; there must be exactly one right fluid outlet, no others. Preserve all twin cylinders, crank linkage, cream pressure dial, brass/copper and blue-gray palette, hard chunky pixel style, shape, silhouette, outline, feet, positions, sizes and original square 1280x1280 transparent canvas. Do NOT add any outside mechanical shaft, iron collar, axle ring, cable or wire. This is a transparent game sprite, no background, ground, shadow, text, logos or new objects.
```

历史第二次管口定位编辑（已失效）：

```text
Use case: precise-object-edit. Only change the right-side pipe height in this attached pump sprite. RAISE the one right brass outlet a further approximately 75 pixels. Its center must coincide with the full-image horizontal midpoint: the horizontal IRON SEAM separating the TWIN PISTONS' LOWER COLLARS from the broad bottom PRESSURE GAUGE CASE. Mount the pipe centered on that casing seam, NOT beside the middle of the round pressure gauge. Remove the current lower outlet so there is only ONE right outlet. Do not change any other pixels or parts; keep the twin pistons, brass crank linkage, circular pressure gauge, housing, feet, copper/blue-gray palette, chunky hard pixel art, exact shape/scale/position, square 1280x1280 PNG and transparent padding. No left/top/bottom outlet, exterior shaft, collar, rings, wires, labels, background, ground or shadows.
```

## LiquidPump_Electric

最终路径：`Assets/6_Art/Generated/Chemistry/LiquidPump_Electric/LiquidPump_Electric_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. Delete BOTH projecting brass fluid pipe nozzles on the LEFT and RIGHT sides, including their connecting flanges and external rings. Restore continuous closed blue-gray pump/motor casing ends there. Preserve the big solid round spiral centrifugal pump case on the left, its front central gold fastening bolt and circular cover seam, the electric motor with rectangular vent slots on the right, central brass strap and two iron feet. The solid circular pump cover is internal machinery, not an external interface, and must remain.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive electric liquid pump idle sprite. A squat round blue-gray centrifugal snail pump casing on the lower left, paired with a compact horizontal iron electric motor housing on the upper right; a wide brass strap wraps the motor, and chunky vent slits distinguish electric drive from piston machinery. Two sturdy small iron feet. Exactly one short brass liquid inlet mouth at the LEFT edge and one short brass outlet mouth at the RIGHT edge, both at canvas center y. NO top or bottom pipes and NO mechanical shaft. Not a compressor tank. All pipe fittings small and secondary to the pump silhouette.
```

## PressureProbe_Mechanical

最终路径：`Assets/6_Art/Generated/Chemistry/PressureProbe_Mechanical/PressureProbe_Mechanical_Idle.png`

最终状态：原图仅含闭合机身及内部机构，没有外露流体、传动或电线接口。

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive mechanical pressure probe idle sprite. This is a SMALL COMPACT sensor, not a large machine. One large circular brass-rimmed cream pressure dial is mounted above a squat blue-gray clutch/valve housing and a chunky brass hinge. Dial has only one black needle pointed up-right and three unlabeled chunky tick marks, no numbers. No exposed rotating rotor. NO fluid pipe ports anywhere: it monitors a pipe in its grid cell and mechanically gates the independent shaft network. Housing flush opaque sides at canvas center to conceal the separate left and right standard shaft overlays. No exterior shaft, axle, connector collars or rings. Rounded top silhouette and stable short iron feet. A readable traditional analog gauge.
```

## PressureProbe_Electronic

最终路径：`Assets/6_Art/Generated/Chemistry/PressureProbe_Electronic/PressureProbe_Electronic_Idle.png`

最终状态：原图仅含闭合机身及内部机构，没有外露流体、传动或电线接口。

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive electronic pressure probe idle sprite. A SMALL rectangular blue-gray control box, copper corner reinforcement, shallow top panel, a single centered dark screen with EXACTLY THREE square segment indicators colored muted green, amber and charcoal; tiny simple side vent slots. An integrated short rectangular sensing prong extends downward from the case center, no pipe mouth. No words, numbers or pictograms. NO FLUID PORTS and NO MECHANICAL AXIS CONNECTION. No gauges or circular dial. Not a battery: use a compact sensor controller with clear small downward probe silhouette. Static inactive device, no emissions or glowing light.
```

历史定向编辑提示词（管口位置要求已失效）：

```text
Use case: precise-object-edit. Edit the attached electronic pressure probe sprite by changing ONLY its framed scale: enlarge the entire original probe uniformly, preserving its exact aspect ratio and appearance, until the probe occupies roughly 80% of the width of this same 1280x1280 square transparent PNG canvas. It currently occupies roughly half the width. Keep the complete case, four brass corners, vents, exact three square indicator segments and integrated downward rectangular sensing prong all visible and centered. Do NOT redesign the probe. Preserve original charcoal pixel outline, blue-gray/copper palette and hard square chunky pixel clusters, no antialiasing or gradient. Leave fully transparent margins on every side. NO pipe mouths, mechanical connectors, shafts, rings, electric wire, text, logo, extra objects, ground or shadows.
```

## GasCompressor_Mechanical

最终路径：`Assets/6_Art/Generated/Chemistry/GasCompressor_Mechanical/GasCompressor_Mechanical_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. Delete BOTH projecting tiny brass tube connectors on the LEFT and RIGHT outer ends, including pipe collars and flanges. Restore closed smooth blue-gray crankcase edge on the left and closed pressure receiver end on the right. Keep the horizontal pressure receiver with its three brass bands, upper-right vertical brass compression cylinder and fins, enclosed left crank linkage, small cream round pressure gauge, bottom iron frame and feet unchanged. Internal mechanical linkage and solid top cylinder cap are device structure, not interfaces; preserve them.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive MECHANICAL gas compressor idle sprite. Distinct low wide machine, NOT the pump design: one horizontal thick blue-gray pressure receiver cylinder with three warm copper bands, a visible short vertical brass reciprocating compressor cylinder rises at upper-right, an attached mechanical crankcase at left shows one small blocky brass linkage inside the housing (do not add any projecting crank or shaft). Small plain cream round pressure dial on front with one needle, no numbers. Sturdy iron rails and short feet. Exactly one short brass inlet pipe at LEFT canvas midline y=50%, and one short brass outlet pipe at RIGHT canvas midline y=50%. Flush opaque housing at LEFT canvas center covers separate mechanical drive overlay. NO external shaft, collar, rings, cables or plug; NO upper/lower fluid pipes. Machine occupies 80% of square canvas width with proportionate low height, centered and whole.
```

历史定向编辑提示词（管口位置要求已失效）：

```text
Use case: precise-object-edit. Edit the attached mechanical gas compressor sprite with ONLY ONE change: relocate both existing tiny left and right brass fluid pipe mouths upwards so BOTH mouth centers sit on the exact full-canvas HORIZONTAL MIDLINE, y=640 from the top of the 1280x1280 PNG. The present two mouths are a little lower around y=720; erase the old mouths and restore opaque machine casing at old sites. Put one identical short brass inlet on the left case edge and one short brass outlet on the right case edge at y=640. These are the only fluid pipes; do not add others. Preserve all original mechanical compressor tank, copper bands, upper right cylinder, left crank housing with enclosed linkage, small cream dial, feet, exact dimensions and pixel positions, brass/blue-gray/charcoal palette and hard square pixel-art style. Preserve square 1280x1280 transparent canvas. Do not draw an external shaft, collars, rings, cables or wires; no background, shadows, writing or extra objects.
```

## GasCompressor_Electric

最终路径：`Assets/6_Art/Generated/Chemistry/GasCompressor_Electric/GasCompressor_Electric_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. Delete BOTH projecting tiny brass fluid tubes on the far LEFT and RIGHT side at motor-height, including their flanges and external collars. Restore continuous closed blue-gray iron casing at both sites. Preserve electric motor case and its rectangular vents, finned compression block on top-right, small cream gauge, horizontal pressure receiver at bottom, copper retaining bands, rail feet, bolts and original overall design untouched. The small solid top bolt/cap is device structure, not a pipe; preserve it. Result is only the closed compressor machine body.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive ELECTRIC gas compressor idle sprite. Distinct stepped silhouette: a broad horizontal blue-gray pressure receiver cylinder on the bottom wrapped by two copper straps, a compact rectangular iron electric motor and square finned compressor block on top. Motor top has broad dark vent slits; small plain cream circular pressure gauge on front with one needle. No mechanical crank or lever. Brass corner rivets and two small stable iron feet. Exactly one short brass inlet pipe at LEFT canvas midline y=50% and one short brass outlet pipe at RIGHT canvas midline y=50%; no top/bottom fluid pipes. No projecting mechanical shaft, ring, wires or plugs. Machine occupies 80% of canvas width and 70% of canvas height, centered and whole.
```

历史定向编辑提示词（管口位置要求已失效）：

```text
Use case: precise-object-edit. Keep the attached electric compressor completely identical EXCEPT MOVE THE TWO FLUID PIPE FITTINGS UP. The full PNG is 1280x1280. Put the CENTER OF BOTH the left and right pipe mouths precisely at Y=640 pixels from the top: exactly halfway down the complete image, not halfway down the bottom tank. Their current centers are near y=820, much too low. At the new height they must connect to the upper tank shoulders / lower part of motor/compressor housing. Erase both old pipes and restore closed blue-gray tank ends at their old height. Keep exactly two short identical brass pipes, one LEFT, one RIGHT; no other ports. Preserve original motor and finned compressor, pressure gauge, bottom pressure receiver, copper bands, iron feet, palette, pixel clusters, lighting, silhouette, scale, positions and square 1280x1280 transparent canvas. No exterior shaft, axle, rings, collars, wires, cables, text, background, ground or shadows.
```

## ElectricHeater

最终路径：`Assets/6_Art/Generated/Chemistry/ElectricHeater/ElectricHeater_Idle.png`

最终状态：原图仅含闭合机身及内部机构，没有外露流体、传动或电线接口。

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Primary request: definitive idle ELECTRIC HEATER sprite. A squat square blue-gray iron heater housing, warm brass rim and small copper corner bolts, open front reveals FIVE thick vertical warm copper radiator fins spaced clearly inside a dark inset recess. The copper fins are solid copper material, NOT glowing red heating elements, NO fire/flame/heat glow. A small simple unlabeled rotary control knob on top-right of the front bezel, two sturdy square iron feet, shallow top face. NO FLUID PIPE mouths at any side, no exhaust chimney, no mechanical shaft, rings, wire, electric cable or plug. Strong compact radiator grille silhouette. Object centered and filling roughly 80% of image width and height.
```

## WaterElectrolyzer

最终路径：`Assets/6_Art/Generated/Chemistry/WaterElectrolyzer/WaterElectrolyzer_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. Delete BOTH entire projecting fluid connector assemblies on the LEFT and RIGHT side, including all exterior brass end flanges, collars and short blue-gray/cyan pipe stems. Restore the vertical blue-gray frame pillars to clean unbroken closed side edges. Preserve the broad iron lid, copper/brass frame, exactly two cyan chamber windows, two straight electrode plates in each window, front small dark control square, lower trim and two feet untouched. No replacement socket or hose. Result is only the rectangular electrolyzer body.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Canvas MUST BE exactly 1:1 SQUARE 1280x1280 PNG, preserve transparent space above/below even for wide objects. Primary request: definitive WATER ELECTROLYZER idle sprite. A squat brass-trimmed rectangular blue-gray iron bench case, two tall cyan-blue inset electrode chamber windows under one broad iron lid, two dark solid straight plate electrodes visible through each window. Cyan surfaces are fully opaque pixel color blocks, not real transparency or glow. One small plain black square control below the chamber windows. Two short iron feet. Exactly ONE short liquid inlet fitting on LEFT edge of the machine at image midpoint y=640 of a 1280x1280 square, and exactly ONE short mixed gas outlet fitting on RIGHT edge at the SAME midpoint y=640. Electrode chambers join inside the one case; DO NOT make two gas outputs or separate collection spouts. No top/bottom pipe, no external shaft/collars/wires, no letters/numbers. Body should fill 80% square canvas while preserving clear transparent padding.
```

## GasEngine

最终路径：`Assets/6_Art/Generated/Chemistry/GasEngine/GasEngine_Idle.png`

最终生效的去接口编辑提示词：

```text
Use case: precise-object-edit. EDIT the attached existing FlatWorld machine pixel sprite. New production requirement: this asset is MACHINE HOUSING ONLY. ALL external fluid inlets/outlets and ALL mechanical/electrical interfaces are rendered procedurally at runtime and MUST NOT be baked into the sprite. Remove every projecting fluid pipe, mouth, hollow nozzle, connection stub, flange and external pipe ring from this machine. Fill their former attachment sites with complete closed iron/brass housing in the exact existing pixel style. Preserve all other original machine body structure, internal working mechanisms, meters, feet, palette, lighting, hard chunky pixel clusters, silhouette identity, proportions, scale and centered location. Preserve the original 1:1 square canvas and transparent margins. No external connector of any kind, no pipe mouth, no shaft, collar, ring, cable or wire. Do not add replacement knobs or sockets at removed port locations. No text, background, ground, shadow or unrelated detail. This engine has THREE fluid connector tubes to DELETE: (1) entire hollow brass pipe on LEFT with its exterior iron flange; (2) entire hollow brass vertical pipe ABOVE the copper combustion cylinder with its external iron collar; and (3) entire hollow brass vertical pipe BELOW the crankcase between the feet with its exterior iron collar. Erase all three completely and close their old sites with matching casing: smooth continuous closed left iron/copper wall, low closed iron cylinder head over the copper cylinder at top, and flat enclosed blue-gray crankcase lower edge between feet. The result must have NO chimney, no protruding bottom nozzle, no hollow pipe mouth anywhere. Preserve main copper cylinder, iron cooling fins, retaining straps, big solid front crank cover with four brass bolts, and both iron feet. Keep all original engine body dimensions/proportions/position, identity and 1:1 square transparent canvas; do not enlarge it after deleting tubes.
```

历史原始生成提示词（其中外露方向管口要求已取消）：

```text
Use case: stylized-concept. Asset type: exactly one single FlatWorld game-ready machine sprite. Match the existing project generator, battery and wooden gearbox images observed earlier, with dark charcoal outlines, cool desaturated blue-gray iron and warm brass/copper trim. 2D front orthographic view with a shallow visible top, NEVER isometric or three-quarter. Authentic chunky hard square pixel blocks as a roughly 112x112 logical pixel drawing enlarged nearest-neighbor, no antialiasing or gradient. Limit each material to three hard shade groups, subtle top-left highlight. One centered complete object on square transparent-alpha canvas, subject about 80% of canvas in the longest dimension, generous fully transparent padding on all four sides. Clear silhouette readable at tiny 1x1 world scale. No ground, exterior shadow, card, background, text, numbers, letters, logo, watermark, loose objects, fire, steam, glow, exterior electric cable, external wooden shaft or standard iron shaft ring. Separate runtime layers handle shaft and electrical connections; draw only housing. Canvas MUST BE exactly 1:1 SQUARE 1280x1280 PNG, preserve transparent space above/below even for wide objects. Primary request: definitive HYDROGEN ENGINE idle sprite, a compact copper and blue-gray iron reciprocating piston engine housing. One central thick warm copper vertical combustion cylinder rising from a broad iron crankcase, chunky iron cooling fins near cylinder head, brass retaining straps, simple dark front crank cover with 4 bolts, no external flywheel or rotating wheel. Unique stepped silhouette, sturdy two short feet. It uses THREE fluid ports only: one short hydrogen gas inlet on LEFT edge at the exact image midline y=640; one short oxygen inlet centered on BOTTOM edge at x=640 extending down between feet; one short reclaimed-water outlet centered on TOP edge at x=640 rising up above cylinder. All three pipes are modest brass mouths with small blue-gray sockets, integrated in housing. RIGHT edge at image midpoint must be flush opaque iron housing for a separate runtime mechanical drive shaft overlay, but DO NOT draw any mechanical shaft, collar, ring, wooden axle or electric wire. No extra ports, smoke, flame, glow, running effects. Complete object centered with transparent margins, occupying 80% of square canvas.
```

历史定向编辑提示词（管口位置要求已失效）：

```text
Use case: precise-object-edit. Keep the attached hydrogen engine completely identical EXCEPT MOVE ITS LEFT HYDROGEN PIPE FITTING UP. The full PNG is 1280x1280. Move the center of the LEFT pipe mouth precisely to Y=640 pixels from the top: exactly halfway down the complete image, at the lower-left corner of the vertical copper cylinder near the joint with the iron crankcase. Its current center is around y=790, too low. Erase that old left pipe and restore closed blue-gray crankcase edge at its old site; reattach the identical short brass left inlet on the housing side at y=640. Keep the existing TOP centered reclaimed-water outlet and BOTTOM centered oxygen inlet unchanged. Exactly three fluid pipe mouths in total: LEFT, TOP, BOTTOM; none on right. The right case edge must remain flush housing because an external runtime shaft is separate, do NOT draw shafts, collars, rings, wooden axle or electric cables. Preserve all original piston engine/copper cylinder/iron fins/crank cover/four brass bolts/two feet, their positions and pixel dimensions, hard pixel clusters, palette, lighting, complete silhouette and square 1280x1280 transparent canvas. No background, floor, shadow, text or extra objects.
```
