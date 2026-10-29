# 植物低像素样例修订

用户反馈上一版像素密度偏高，本轮进一步减少叶脉、碎叶和稻粒的内部细节，以更大的像素色块概括结构，保留植株身份、自然轮廓、透明背景和低饱和复古像素风。

## 图片

- [萝卜](../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/RadishCrop_Mature_LowPixelSample.png)
- [水稻](../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png)
- [浆果丛](../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png)

旧版 `*_Mature_Sample.png` 保留作对比。浆果丛本体继续不烘入果实，游戏内素材引用仍指向原正式资源。这三张仅供画风评审，整套阶段图另行统一真实像素网格、画布、基线与运行时尺寸。

## 生成方式与提示词

使用内置 `image_gen`，逐张以对应旧样例作为编辑目标，`transparent_background=true`。保留生成原图与 Alpha，没有使用 API/CLI 回退或二次缩放。提示词中的约 24～28 逻辑像素为简化目标，不代表文件物理尺寸。

### 萝卜

编辑目标：`RadishCrop_Mature_Sample.png`

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET: the previously generated FlatWorld plant sample.
Primary request: the user says this sample has too many pixels. REDRAW THE SAME PLANT WITH MUCH FEWER, MUCH LARGER PIXELS. Reduce logical pixel density by roughly one half to one third along both axes. The finished ENTIRE plant should look hand-drawn on an approximately 24x28 or 28x24 pixel sprite grid, enlarged by nearest-neighbor for preview. Every logical pixel is a large uniform solid-colored square. The coarser square steps and simpler shading must be visibly obvious compared to the reference.
Keep the plant identity, overall silhouette, natural proportions, muted olive/sage palette, upper-left lighting and transparent background. Keep only 8–12 main matte colors, with just two values per major material. Preserve hard readable pixel contours. Remove tiny veins, speckled highlights, small individual leaves, internal texture noise and narrow intermediate-color strips. Simplify the drawing itself to fewer large color clusters.
Output one isolated plant, transparent alpha, same general framing, full plant visible, no cast shadow, no soil, no label, no text, no background, no baked checkerboard, no smoothing or antialiasing, no glossy shading.
Plant-specific constraints: Keep the complete upright red radish, pale root tip, and five broad green leaves. Reduce each major leaf to roughly 3–5 pixels wide and 7–10 pixels long; reduce the whole root to about 6 pixels wide. Keep a compact natural tapered root rather than a round icon.
```

### 水稻

编辑目标：`RiceCrop_Mature_Sample.png`

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET: the previously generated FlatWorld plant sample.
Primary request: the user says this sample has too many pixels. REDRAW THE SAME PLANT WITH MUCH FEWER, MUCH LARGER PIXELS. Reduce logical pixel density by roughly one half to one third along both axes. The finished ENTIRE plant should look hand-drawn on an approximately 24x28 or 28x24 pixel sprite grid, enlarged by nearest-neighbor for preview. Every logical pixel is a large uniform solid-colored square. The coarser square steps and simpler shading must be visibly obvious compared to the reference.
Keep the plant identity, overall silhouette, natural proportions, muted olive/sage palette, upper-left lighting and transparent background. Keep only 8–12 main matte colors, with just two values per major material. Preserve hard readable pixel contours. Remove tiny veins, speckled highlights, small individual leaves, internal texture noise and narrow intermediate-color strips. Simplify the drawing itself to fewer large color clusters.
Output one isolated plant, transparent alpha, same general framing, full plant visible, no cast shadow, no soil, no label, no text, no background, no baked checkerboard, no smoothing or antialiasing, no glossy shading.
Plant-specific constraints: Keep the upright mature rice clump, green arching blades, four gently drooping straw-gold panicles, and compact common stem base. Group grains into a few solid stepped gold clusters with one shadow shade; remove individual grain highlights and fine branch texture. Leaves should be one or two logical pixels thick.
```

### 浆果丛

编辑目标：`BerryCrop_Mature_Sample.png`

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET: the previously generated FlatWorld plant sample.
Primary request: the user says this sample has too many pixels. REDRAW THE SAME PLANT WITH MUCH FEWER, MUCH LARGER PIXELS. Reduce logical pixel density by roughly one half to one third along both axes. The finished ENTIRE plant should look hand-drawn on an approximately 24x28 or 28x24 pixel sprite grid, enlarged by nearest-neighbor for preview. Every logical pixel is a large uniform solid-colored square. The coarser square steps and simpler shading must be visibly obvious compared to the reference.
Keep the plant identity, overall silhouette, natural proportions, muted olive/sage palette, upper-left lighting and transparent background. Keep only 8–12 main matte colors, with just two values per major material. Preserve hard readable pixel contours. Remove tiny veins, speckled highlights, small individual leaves, internal texture noise and narrow intermediate-color strips. Simplify the drawing itself to fewer large color clusters.
Output one isolated plant, transparent alpha, same general framing, full plant visible, no cast shadow, no soil, no label, no text, no background, no baked checkerboard, no smoothing or antialiasing, no glossy shading.
Plant-specific constraints: Keep the low spreading olive-green berry shrub, broad asymmetric crown and visible brown branches near the base. Simplify the many tiny leaves into about 8–10 broad foliage masses. Each large leaf group should be a solid cluster of only a few pixels, not a mosaic of many tiny leaves. Keep the shrub fruitless because berries are a separate game layer.
```
