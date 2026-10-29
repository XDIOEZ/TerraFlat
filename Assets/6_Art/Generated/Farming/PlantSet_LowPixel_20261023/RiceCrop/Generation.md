# 水稻三阶段重绘

成熟母版采用用户已认可的 `../../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png`；幼苗、生长期以该母版为编辑目标，使用内置 `image_gen`、透明背景独立生成。原生 PNG/Alpha 保留，没有程序缩放或裁切。

三张均 1173×1341，统一 1341 PPU、Pivot=(0.5,0.03)、Point、无 Mipmap、无压缩、无自动物理轮廓。按旧图一世界单位画布高度换算。幼苗保留上方留白，生长期为绿叶与未熟穗，成熟期为金黄下垂穗。原模块 buriedClip=0.08 本轮不改。

## 原始源文件

- 成熟：`../../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png`。
- RiceCrop_Seedling.png：`C:\Users\CatStudio\.codex\generated_images\01a16839-f982-7652-9f1b-d767112709d1\exec-71cf0c97-68b3-4f84-96e1-8daa52160f16.png`。
- RiceCrop_Growing.png：`C:\Users\CatStudio\.codex\generated_images\01a16839-f982-7652-9f1b-d767112709d1\exec-f3a39893-01ad-474d-85bc-b4ecc3deee02.png`。

## 最终提示词

成熟母版完整提示词见 [认可样例记录](../../PlantStyleSamplesV2/LowPixelSamples.md) 的水稻条目。

### RiceCrop_Seedling.png

```text
Use case: precise-object-edit. Image 1 is the EDIT TARGET and approved mature-rice style anchor. Create the SEEDLING phase of exactly this same rice plant. FlatWorld art direction: low-saturation semi-realistic retro pixel sprites with VERY OBVIOUS large chunky square pixels, natural plant proportions, restrained 2–3 value shading, about 8–12 main matte colors, upper-left light. Match the coarse pixel density and olive/sage greens of the user-approved sample, not the fine pixels of any old identity reference. Express leaves using large solid stepped clusters, no fine veins, tiny speckles, antialiasing, smooth gradients, photorealistic texture or glossy 3D. Actual transparent alpha for all empty space, no baked checkerboard, ground, pot, shadow, writing, labels or decorative frame. Keep the full image canvas, transparent margins, pixel block size, lighting and the planted root/base pixel at the SAME bottom-center location as the input. The young plant itself is only about 30% of the mature plant height and 25% of its width: 3–4 fresh green angular blades rising from a compact base, a little taller middle blade and two shorter outward blades. No grain heads, no flowers, no golden stalks. Do not scale up the young plant to fill the canvas; most of the upper canvas stays transparent. One plant, not a stage sheet.
```

### RiceCrop_Growing.png

```text
Use case: precise-object-edit. Image 1 is the EDIT TARGET and approved mature-rice style anchor. Create the GROWING phase of exactly this same rice plant. FlatWorld art direction: low-saturation semi-realistic retro pixel sprites with VERY OBVIOUS large chunky square pixels, natural plant proportions, restrained 2–3 value shading, about 8–12 main matte colors, upper-left light. Match the coarse pixel density and olive/sage greens of the user-approved sample, not the fine pixels of any old identity reference. Express leaves using large solid stepped clusters, no fine veins, tiny speckles, antialiasing, smooth gradients, photorealistic texture or glossy 3D. Actual transparent alpha for all empty space, no baked checkerboard, ground, pot, shadow, writing, labels or decorative frame. Keep the full image canvas, transparent margins, pixel block size, lighting and planted base at the SAME bottom-center position. This green vegetative rice clump is about 70% of mature height and 65% of mature width: several upright green stalks, narrow arching blade leaves, no heavy grain heads, no ripe gold. A couple small immature green developing panicles are acceptable, but the plant reads distinctly younger than the input mature plant. Leave transparent space above the shorter plant. Do not enlarge it to fill the frame. One plant, not a stage sheet.
```
