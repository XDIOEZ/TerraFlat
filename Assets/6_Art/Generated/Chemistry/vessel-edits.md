# 储罐与过滤器机身接口移除

制作方式：内置 ImageGen 编辑；每张编辑前均实际查看原图。设备的输入输出接口由程序化图层绘制，下面 4 张机身图已移除外露管道、铜口及法兰。

所有输出均为 1254 × 1254 的 RGBA PNG，保留生成的透明度与原始文件尺寸，没有本地裁剪、缩放、量化或像素修改。原始生成结果保留在 `C:/Users/CatStudio/.codex/generated_images/01a11edb-109d-7002-b530-cb6facb124e3/`。

| 机身 | 保留的识别特征 | Codex 源文件 |
| --- | --- | --- |
| GasTankBlock_Iron | 双竖箍、铜护角、铆钉、正面封闭检修盖 | exec-212cc17e-a162-46bd-a907-28228e0cc26d.png |
| GasTankBlock_Steel | 蓝灰箱体、X 加固条、四角螺栓、闭合加强块 | exec-5015d6f1-d3e7-4fd5-8743-ff384628ee7e.png |
| LiquidStorageTank | 圆柱筒体、铜绑带、观察条、底环和支脚 | exec-097b94b4-d39d-4eb1-ab70-d68b64648454.png |
| FluidFilter | 双滤筒、左侧滤布、铜箍、支架、内部桥接与支脚 | exec-796d9145-a2fd-46d9-9811-5740b7b022c4.png |

成品已覆盖 `Assets/6_Art/Generated/Chemistry/<ID>/<ID>_Idle.png`；本次编辑没有更改其他装备、JSON、导入 Meta 或资源地址。

## 使用的提示词

### GasTankBlock_Iron

```text
Use case: precise-object-edit.
Input image: Image 1 is the existing approved FlatWorld equipment machine-body Sprite and the exact edit target.
Primary request: remove external connection hardware from the machine body because separate programmatic Sprite layers draw every input/output interface.
Global invariants: keep the original object's identity, muted material palette, hard grouped pixel clusters, top-left highlights, silhouette proportions, scale of the main housing and original centered placement. Make only the requested local removals and reconstruct solid closed housing where the connection hardware had been. Preserve the original 1254 by 1254 square transparent canvas, without resizing, changing framing or zooming the main body.
Background: genuine transparent alpha; hard opaque pixel-art body and clean stepped boundary with fully transparent space around it.
Constraints: no exterior pipes, pipe stubs, nozzles, open holes, hose connectors, flanges, copper port rings, cables, sockets or transmission-shaft ports. No new connection hardware anywhere. No ground, cast shadow, grey halo, glow, gradient backdrop, text, label, logo or watermark. Do not redesign the existing body.
Exact edit: REMOVE the entire short vertical copper-and-grey pipe and open circular port on TOP of the square iron vessel. REMOVE the entire downward copper pipe and open port on the BOTTOM. REMOVE the complete protruding copper-ring pipe stub on LEFT and RIGHT. Replace the top and bottom roots with a continuous sealed grey iron shell edge, aligned with the existing vessel body; left and right edges likewise become closed unbroken grey iron casing.
Keep: the square grey-blue iron housing, two vertical reinforcing straps, rivets, copper corner protection plates and the large CLOSED circular inspection hatch in the middle with its four bolt heads. The front inspection hatch is NOT a pipe port: keep it intact, opaque and sealed. Only the four perimeter connection stubs disappear. The four copper corner guards remain.
```

### GasTankBlock_Steel

```text
Use case: precise-object-edit.
Input image: Image 1 is the existing approved FlatWorld equipment machine-body Sprite and the exact edit target.
Primary request: remove external connection hardware from the machine body because separate programmatic Sprite layers draw every input/output interface.
Global invariants: keep the original object's identity, muted material palette, hard grouped pixel clusters, top-left highlights, silhouette proportions, scale of the main housing and original centered placement. Make only the requested local removals and reconstruct solid closed housing where the connection hardware had been. Preserve the original 1254 by 1254 square transparent canvas, without resizing, changing framing or zooming the main body.
Background: genuine transparent alpha; hard opaque pixel-art body and clean stepped boundary with fully transparent space around it.
Constraints: no exterior pipes, pipe stubs, nozzles, open holes, hose connectors, flanges, copper port rings, cables, sockets or transmission-shaft ports. No new connection hardware anywhere. No ground, cast shadow, grey halo, glow, gradient backdrop, text, label, logo or watermark. Do not redesign the existing body.
Exact edit: REMOVE the complete copper open neck, flange and dark ring hole at the TOP of the steel block. REMOVE the complete copper downward port at the BOTTOM. REMOVE the complete protruding copper-ring pipe couplings on LEFT and RIGHT. Fill their roots with continuous closed dark blue-grey steel housing. Top and bottom form a flat sealed casing edge; no raised central socket or recess.
Keep: the square dark blue-grey pressure vessel, thick X-shaped cross braces, four corner brackets and silver bolt heads, central silver fastening bolt, horizontal strengthening bars, existing pixel palette and main-body proportions. Only the four copper perimeter connection stubs disappear.
```

### LiquidStorageTank

```text
Use case: precise-object-edit.
Input image: Image 1 is the existing approved FlatWorld equipment machine-body Sprite and the exact edit target.
Primary request: remove external connection hardware from the machine body because separate programmatic Sprite layers draw every input/output interface.
Global invariants: keep the original object's identity, muted material palette, hard grouped pixel clusters, top-left highlights, silhouette proportions, scale of the main housing and original centered placement. Make only the requested local removals and reconstruct solid closed housing where the connection hardware had been. Preserve the original 1254 by 1254 square transparent canvas, without resizing, changing framing or zooming the main body.
Background: genuine transparent alpha; hard opaque pixel-art body and clean stepped boundary with fully transparent space around it.
Constraints: no exterior pipes, pipe stubs, nozzles, open holes, hose connectors, flanges, copper port rings, cables, sockets or transmission-shaft ports. No new connection hardware anywhere. No ground, cast shadow, grey halo, glow, gradient backdrop, text, label, logo or watermark. Do not redesign the existing body.
Exact edit: REMOVE the entire copper-rimmed top pipe neck and open port from the centre of the TOP dome. Reconstruct a continuous sealed grey iron dome matching its stepped curvature and lighting. REMOVE the entire LEFT and RIGHT protruding copper-ring short pipe assemblies. Fill their roots into the cylinder's closed side wall. REMOVE the complete downward-facing copper pipe elbow and open ring at the BOTTOM FRONT. Restore the continuous sealed dark iron lower wall behind it.
Keep: the cylindrical grey iron vessel, rounded top dome, two copper reinforcing straps with rivets, narrow copper-framed vertical dark glass liquid level observation strip on the front, metal base ring and both supportive feet. Do not remove copper decorative structural straps or the observation-window frame; only remove the four connection pipes.
```

### FluidFilter

```text
Use case: precise-object-edit.
Input image: Image 1 is the existing approved FlatWorld equipment machine-body Sprite and the exact edit target.
Primary request: remove external connection hardware from the machine body because separate programmatic Sprite layers draw every input/output interface.
Global invariants: keep the original object's identity, muted material palette, hard grouped pixel clusters, top-left highlights, silhouette proportions, scale of the main housing and original centered placement. Make only the requested local removals and reconstruct solid closed housing where the connection hardware had been. Preserve the original 1254 by 1254 square transparent canvas, without resizing, changing framing or zooming the main body.
Background: genuine transparent alpha; hard opaque pixel-art body and clean stepped boundary with fully transparent space around it.
Constraints: no exterior pipes, pipe stubs, nozzles, open holes, hose connectors, flanges, copper port rings, cables, sockets or transmission-shaft ports. No new connection hardware anywhere. No ground, cast shadow, grey halo, glow, gradient backdrop, text, label, logo or watermark. Do not redesign the existing body.
Exact edit: REMOVE only the LEFT and RIGHT externally protruding grey pipe stubs and their copper connection rings, including all hardware extending outside the support frame at middle height. Reconstruct the closed dark iron side housing where those external pipe roots existed.
Keep: the pair of tall cylindrical filter cartridges, the clearly visible tan woven filter cloth in the LEFT cartridge, the dark grey sealed RIGHT cartridge, copper clamp bands and bolts at the top and bottom of each cartridge, CLOSED steel service caps on top, rigid dark iron side support frame, both feet and internal bridge structures connecting the two cartridges inside the frame. Do not erase internal structural bridges or draw new external connectors. The service caps remain closed and are not open pipe ports.
```
