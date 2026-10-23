# 花朵重绘

采用已认可低像素植物风格，保留原花朵的三朵橙黄花、分枝茎与绿叶身份。内置 `image_gen`、透明背景，原生 PNG 与 Alpha 直接保存。

- 文件：`Flower_Mature.png`，1269×1240。
- 导入 PPU=2480，按原 32×32 / 64 PPU 的 0.5 单位画布高度折算；底中 Pivot=(0.5,0)、Point、无 Mipmap、无压缩、无自动物理轮廓。
- 生成源：`C:\Users\CatStudio\.codex\generated_images\01a16839-f982-7652-9f1b-d767112709d1\exec-4f925a5c-6562-4b07-a6d3-707bfc7ca650.png`。
- 图片用作整株花朵源图，本轮不改游戏定义或引用。

## 最终提示词

```text
Use case: style-transfer. Image 1 is an IDENTITY REFERENCE for the game's existing small wildflower cluster, not a pixel-density reference. Image 2 is the USER-APPROVED PIXEL STYLE REFERENCE only: use its large visible square pixels and subdued foliage shading, do not draw a bush. Redraw one compact wildflower plant cluster: three small ochre-orange blooms with pale cream-yellow centers on slender branching green stems and a few broad olive basal leaves. Preserve naturally small flower heads, irregular bloom heights, and a readable low plant silhouette, about 18–24 logical pixels wide and 24–30 tall in the same coarse style as the approved crop set. FlatWorld art direction: low-saturation semi-realistic retro pixel sprites with VERY OBVIOUS large chunky square pixels, natural plant proportions, restrained 2–3 value shading, about 8–12 main matte colors, upper-left light. Match the coarse pixel density and olive/sage greens of the user-approved sample, not the fine pixels of any old identity reference. Express leaves using large solid stepped clusters, no fine veins, tiny speckles, antialiasing, smooth gradients, photorealistic texture or glossy 3D. Actual transparent alpha for all empty space, no baked checkerboard, ground, pot, shadow, writing, labels or decorative frame. Center one complete plant with a grounded bottom-center baseline, transparent padding, no surrounding foliage or scenery. The entire image is one sprite, no alternate variants.
```
