# 第二轮工具样例提示词

方式：内置 `image_gen`，编辑已有样例，所有调用均请求 `transparent_background: true`。

用户反馈：工具需要原始时代手作感；斧、镐像素太密，铁铲的像素粗细合适。像素颗粒统一参考首轮 `Shovel_Iron_StyleSample.png`，保留各工具原有材质身份。

## 燧石斧（Axe_Flint）

输入角色：图 1：C:\Users\CatStudio\Desktop\TerraFlat-master\Assets\6_Art\Generated\Tools\StyleSamples_20261023\Axe_Flint_StyleSample.png；图 2：C:\Users\CatStudio\Desktop\TerraFlat-master\Assets\6_Art\Generated\Tools\StyleSamples_20261023\Shovel_Iron_StyleSample.png。具体用途在提示词中说明。

```text
Use case: style-transfer.
Asset type: a revised FlatWorld primitive handmade tool sprite style sample.
USER CORRECTION IS MANDATORY: these tools must feel made by hand in a primitive survival world. The previous axe and pickaxe contain far too many small pixels. Match the APPROVED SHOVEL reference's BIG CHUNKY PIXEL SIZE, not the old target's fine detail.
Pixel technique: reconstruct the entire tool as if drawn on a very low resolution 48 x 48 logical canvas, enlarged with nearest neighbor. One uniform square pixel grid. Large, clearly visible step edges, broad connected pixel clusters, around 12 muted colors, ONLY 2 or 3 tones per material. At the same displayed scale, individual pixels must be at least as large as those in the approved shovel. A typical shaft is only 3 to 4 logical pixels thick, no internal micro-pixels or tiny grain. Remove at least 75% of small texture and specular detail. Readability comes from simple silhouette and big material planes.
Primitive craft language: hand-shaped uneven branchwood haft, modest natural taper and slight bend, simple cut end, only a few broad fibre or hide bindings where structurally needed, a small practical handmade head. Believable robust construction. Keep each material's identity; do not turn bronze or iron into stone.
Composition: ONE complete isolated tool, comfortably within a square canvas. Diagonal lower-left handle to upper-right head, preserve overall purpose, muted earthy palette and upper-left light. Actual transparent alpha outside the silhouette, no scene, floor or cast shadow. No text, border, label, decorative elements or other tools.
Avoid: regular factory-made collars, threaded screws, ergonomic manufactured grips, perfect symmetry, stamped sheet-metal ribs, polished continuous bright rims, glossy gold, smooth gradients, antialiasing, blur, detailed painting, fine pixel noise.
Input roles: image 1 is the OLD AXE EDIT TARGET, only for identity and composition; image 2 is the APPROVED SHOVEL PIXEL-SIZE AND SIMPLICITY REFERENCE ONLY, not a structural reference. Output ONLY the axe.
Redesign: a crude practical FLINT AXE with a compact irregular dark-grey knapped stone wedge, a plain slightly crooked unpolished wooden haft, and just 2 to 3 broad tan rawhide binding bands securing the head in a split/notched haft. Show only THREE large stone facets, one broad cutting-edge plane. The edge is irregular from hand knapping, not a clean symmetrical polished fan. The shaft has at most three connected brown planes and a single large simple knot, no dense wood grain, no tiny crosshatched lashings. The whole sprite must be visibly much COARSER and simpler than image 1, matching image 2.
```

## 青铜镐（Pickaxe_Bronze）

输入角色：图 1：C:\Users\CatStudio\Desktop\TerraFlat-master\Assets\6_Art\Generated\Tools\StyleSamples_20261023\Pickaxe_Bronze_StyleSample.png；图 2：C:\Users\CatStudio\Desktop\TerraFlat-master\Assets\6_Art\Generated\Tools\StyleSamples_20261023\Shovel_Iron_StyleSample.png。具体用途在提示词中说明。

```text
Use case: style-transfer.
Asset type: a revised FlatWorld primitive handmade tool sprite style sample.
USER CORRECTION IS MANDATORY: these tools must feel made by hand in a primitive survival world. The previous axe and pickaxe contain far too many small pixels. Match the APPROVED SHOVEL reference's BIG CHUNKY PIXEL SIZE, not the old target's fine detail.
Pixel technique: reconstruct the entire tool as if drawn on a very low resolution 48 x 48 logical canvas, enlarged with nearest neighbor. One uniform square pixel grid. Large, clearly visible step edges, broad connected pixel clusters, around 12 muted colors, ONLY 2 or 3 tones per material. At the same displayed scale, individual pixels must be at least as large as those in the approved shovel. A typical shaft is only 3 to 4 logical pixels thick, no internal micro-pixels or tiny grain. Remove at least 75% of small texture and specular detail. Readability comes from simple silhouette and big material planes.
Primitive craft language: hand-shaped uneven branchwood haft, modest natural taper and slight bend, simple cut end, only a few broad fibre or hide bindings where structurally needed, a small practical handmade head. Believable robust construction. Keep each material's identity; do not turn bronze or iron into stone.
Composition: ONE complete isolated tool, comfortably within a square canvas. Diagonal lower-left handle to upper-right head, preserve overall purpose, muted earthy palette and upper-left light. Actual transparent alpha outside the silhouette, no scene, floor or cast shadow. No text, border, label, decorative elements or other tools.
Avoid: regular factory-made collars, threaded screws, ergonomic manufactured grips, perfect symmetry, stamped sheet-metal ribs, polished continuous bright rims, glossy gold, smooth gradients, antialiasing, blur, detailed painting, fine pixel noise.
Input roles: image 1 is the OLD BRONZE PICKAXE EDIT TARGET, only for identity and composition; image 2 is the APPROVED SHOVEL PIXEL-SIZE AND SIMPLICITY REFERENCE ONLY. Output ONLY the mining pickaxe.
Redesign: a simple handmade mining pickaxe with a small sturdy crudely cast BRONZE head on an unpolished slightly irregular branchwood handle. The head has a longer downward-pointing mining pick and a shorter blunt/chisel tail, slightly unequal and hand-finished; preserve an unmistakable pickaxe silhouette. A plain thick central eye and wooden wedge secure the head, optionally one simple broad hide band, no industrial socket or engineered clamp. Bronze is subdued dull earthy ochre-brown, with one broad dark side and just a SHORT, discontinuous muted pale highlight on the working tip, not bright gold and no thin continuous shiny rim. Three big wood color planes, no small grain. The whole sprite must be visibly much COARSER and simpler than image 1, matching image 2.
```

## 铁铲（Shovel_Iron）

输入角色：图 1：C:\Users\CatStudio\Desktop\TerraFlat-master\Assets\6_Art\Generated\Tools\StyleSamples_20261023\Shovel_Iron_StyleSample.png。具体用途在提示词中说明。

```text
Use case: style-transfer.
Asset type: a revised FlatWorld primitive handmade tool sprite style sample.
USER CORRECTION IS MANDATORY: these tools must feel made by hand in a primitive survival world. The previous axe and pickaxe contain far too many small pixels. Match the APPROVED SHOVEL reference's BIG CHUNKY PIXEL SIZE, not the old target's fine detail.
Pixel technique: reconstruct the entire tool as if drawn on a very low resolution 48 x 48 logical canvas, enlarged with nearest neighbor. One uniform square pixel grid. Large, clearly visible step edges, broad connected pixel clusters, around 12 muted colors, ONLY 2 or 3 tones per material. At the same displayed scale, individual pixels must be at least as large as those in the approved shovel. A typical shaft is only 3 to 4 logical pixels thick, no internal micro-pixels or tiny grain. Remove at least 75% of small texture and specular detail. Readability comes from simple silhouette and big material planes.
Primitive craft language: hand-shaped uneven branchwood haft, modest natural taper and slight bend, simple cut end, only a few broad fibre or hide bindings where structurally needed, a small practical handmade head. Believable robust construction. Keep each material's identity; do not turn bronze or iron into stone.
Composition: ONE complete isolated tool, comfortably within a square canvas. Diagonal lower-left handle to upper-right head, preserve overall purpose, muted earthy palette and upper-left light. Actual transparent alpha outside the silhouette, no scene, floor or cast shadow. No text, border, label, decorative elements or other tools.
Avoid: regular factory-made collars, threaded screws, ergonomic manufactured grips, perfect symmetry, stamped sheet-metal ribs, polished continuous bright rims, glossy gold, smooth gradients, antialiasing, blur, detailed painting, fine pixel noise.
Input role: image 1 is BOTH the iron shovel EDIT TARGET and the APPROVED PIXEL-SIZE REFERENCE. Its pixel coarseness is correct. DO NOT add any smaller pixels or increase texture density. Output ONLY the shovel.
Change structure for a primitive hand-forged feel while retaining its existing chunky pixel style: a plain roughly forged broad pointed IRON blade, slightly uneven shoulders and edge, no stamped central stiffening rib, no perfectly rolled factory sleeve. A short simple iron tang is fitted into a split/notched wooden haft and secured with 2 broad rawhide wraps. Keep the blade recognizably IRON: grey-black matte face, one mid-grey face, just a few sparse hard light edge pixels and ONE or TWO large oxidation/hammer-mark clusters. The wooden haft is a slightly crooked, modestly tapered branch with a plain irregular cut butt and only three broad brown shading bands. This must feel hand-made and practical, not a modern manufactured spade. Preserve the low logical resolution, large square clusters and approximate overall size of image 1.
```

## 燧石斧补齐画布留白

第二轮斧头第一次输出的右侧贴边；以下编辑只修复完整轮廓和画布留白，交付采用此结果。

```text
Use case: precise-object-edit. The single input image is the edit target: an already approved-style coarse pixel FLINT AXE. Preserve its primitive handmade construction, color palette, large square pixel size, silhouette, stone facets and rawhide bindings.
Make ONLY ONE CORRECTION: the stone blade at the right edge is clipped by the canvas. Restore the COMPLETE cutting edge and provide generous actual TRANSPARENT PADDING on every side. Fit the WHOLE complete axe inside the center 76% of a square transparent canvas so no part touches any edge. Slightly reduce the size of the entire object and re-center it; keep the diagonal orientation, no cropping.
CRITICAL: keep exactly the same chunky low resolution pixel appearance. Do not introduce more detail, smaller pixels, extra shades, antialiasing, blur or smoother painting. Do not alter the tool into anything else. No text, background, checkerboard, shadow, floor, extra objects, lighting effects. True transparent alpha outside the tool.
```
