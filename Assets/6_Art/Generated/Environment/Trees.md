# 低饱和树木贴图

本轮只接入已认可的橡树、修正后的松树和新增枯树，不替换果树、椰子树或柳树。

## 正式资源

| 实体 | PNG | 根部宽度上限 | 物理碰撞尺寸（缩放前） |
| --- | --- | --- | --- |
| `Tree_Oak` 橡树 | `OakTree/OakTree_Idle.png` | 14 / 24 × 1.2 = 0.70 单位 | 0.60 × 0.25 |
| `Tree_Pine` 松树 | `PineTree/PineTree_Idle.png` | 13 / 24 × 1.2 = 0.65 单位 | 0.55 × 0.25 |
| `Tree_Dead` 枯树 | `DeadTree/DeadTree_Idle.png` | 22 / 24 = 0.917 单位 | 0.92 × 0.333 |

三张图均为 64×96、24 PPU、Point、无 Mipmap、无压缩、硬 Alpha（0/255）；锚点位于各自树根落地区域中心。活树最大生长缩放沿用 1.2，枯树固定 1 倍；树冠可跨格，根部与实体占地保留一格。根宽按图像底部八行的可见像素范围计算，不把树冠宽度当作地面碰撞。

- 橡树和松树沿用原 PNG 地址与资源 GUID，碰撞盒随新根部对齐。
- 松树树冠内的正面树干由针叶遮挡；最低层枝叶下方的树干保留。
- 枯树由 `Items/shells/dead_tree.json` 定义，继承松树的资源实体、生命与斧头受击规则；不生长、不产树脂，砍伐掉落原木与木棍。
- 枯树不使用叶片摇摆材质，已注册 `ItemSprite` Addressables；本轮未加入自然生成规则，可在创造背包搜索“枯树”放置。

## 绘图记录

使用内置 imagegen 生成、修订像素美术；GIMP 仅用于最近邻缩小和无抖动的 24 色/硬透明导出。角色参考仍为现有玩家，树木画风优先遵循用户提供的低饱和森林参考。

通用提示词约束：low-saturation naturalistic retro pixel art; a tiny 64×96 logical pixel grid; coarse square pixels; muted olive / grey-green / moss-green foliage and earth-brown bark; a few readable leaf clusters; upper-left light; compact narrow roots; transparent background; no glow, gradients, high-resolution leaf texture, ground patch or cast shadow. 橡树使用不对称的圆阔树冠，松树使用错落的锥形针叶，枯树使用少量不规则分叉和灰褐树干。

### 松树修订提示词

Use case: precise-object-edit. Input image 1 is the EXACT low-resolution pine sprite to edit. Input image 2 is the user's annotation showing the defect, not a source of background or red paint. Fix ONLY the missing FRONT FOLIAGE along the tree's central upper axis, inside the red circled area shown by input 2. Between the needle bough layers the vertical brown trunk is currently visible as a repeated ladder of brown gaps. Add overlapping front-facing olive/grey-green needle foliage clusters over ALL these central brown gaps, from the crown tip down to the bottom edge of the lowest leafy bough. The trunk must be completely hidden behind foliage THROUGHOUT THE CROWN. Keep the lower bare trunk and tiny compact roots below the lowest bough visible and unchanged. Preserve the tree's external silhouette, irregular tapered branches, position, proportions, coarse square pixel scale, limited muted palette, upper-left light, and transparent background. Use the existing dark forest green, mid olive and pale desaturated olive-green pixel clusters so the filled central foliage joins naturally with the existing side boughs. Do NOT simply repaint wood green with a straight stripe: create natural interlocking needle clumps across the center. This is still a tiny roughly 64x96 logical pixel sprite, no new high-resolution detail, no blur, no gradient, no tiny needle texture. Every visible patch of brown wood above the lowest canopy edge must disappear behind opaque foliage. Retain the compact narrow 1-unit ground-contact base. No red annotations, no checkerboard, no text, no halo, no glow, no background atmosphere or ground. Truly transparent outside solid tree.
