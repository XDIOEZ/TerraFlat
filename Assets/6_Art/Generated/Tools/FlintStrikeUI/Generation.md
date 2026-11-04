# 燧石取火 UI 部件

- 工具：内置 `image_gen`，透明背景；原图保留于 Codex generated_images。
- 参考：`../PrimitiveSet_20261023/FlintStrike/FlintStrike_Icon.png`。
- 产物：`FlintStrike_Components.png`，1774×887 RGBA；左右半幅分别为燧石片与敲击石，Point、无 mipmap、无压缩，源尺寸保留。
- 消费：`UI_FlintStrike` 的 `FlintStrikeToolGraphic` 使用左右 UV；动画只跟随真实敲击与事务成败。

## 生成提示词

Use case: stylized-concept. Asset: production 2D game UI animated tool component atlas for FlatWorld. Reference image shows the identity: two primitive chipped grey stones, NOT metal striker. Generate one transparent wide canvas, 2 equal-width cells in a single horizontal row. LEFT CELL: one large irregular bluish grey chipped flint blade, its pointed striking edge aimed diagonally down-right. RIGHT CELL: one chunky dark charcoal grey rough striking stone, its striking edge aimed diagonally down-left. Each stone entirely inside its own half, centered with generous transparent margins (15% of cell on all sides); NO overlap and NO other objects. Keep both stones separate, ready for UI animation toward each other. Low saturation coarse blocky pixel art, large hard edged pixel clusters, only 3-5 broad shade regions per stone, simple physical natural shapes, no tiny noisy texture, no smooth gradients, no thin strokes. Match reference identity and primitive technology. No hands, no wood, no platform, no frames, no texts, no arrows, no sparks, no baked glow, no shadow outside silhouette. Real transparent background. Canvas layout strictly 2 columns 1 row, each stone occupies at most 70% of its cell.
