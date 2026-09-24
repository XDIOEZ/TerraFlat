# 陶罐 UI 剖面资源

- 用途：水容器界面的半截面罐体；水量和水质由独立运行时 Graphic 绘制。
- 身份参考：`../ClayJar/ClayJar_Icon.png`；像素风参考：`../StoneMortar/StoneMortar_Bowl.png`。
- 概念稿：由 imagegen 根据上述素材生成，正面陶土半剖面、厚壁、空腹、无水。
- 概念稿包含灰色棋盘背景；正式素材由 `ClayJarUIArtBuilder` 机械去背景、最近邻缩小和固定 16 色量化，不使用概念稿直接渲染。
- 正式罐体：128×128 RGBA，硬透明、Point、无 Mipmap、无压缩；内腔轮廓单独保存为同尺寸白色透明精灵。
- 重建入口：FlatWorld/UI/Rebuild Water Vessel UI。预览入口：FlatWorld/UI/Preview Water Vessel UI。
- 水层按 LiquidDefinition.VisualState 匹配 Prefab 中的视觉配置；份数 / 当前容器容量决定水面高度。dirty 为浑浊颗粒，drinkable 为蓝色反光，sea 为蓝绿水体和浅色浮沫，filled 为目录默认表现状态。
- 新增液体表现状态时同步配置正式面板 Styles；无需更改液体存档数据。
- 剖面罐口仅保留后侧弧形沿与两侧切边，前侧横沿已移除，使颈部与腹部内腔连续；概念源图与正式精灵同步保存。
- 编辑模式：内置 imagegen 局部编辑；提示词要点：只移除前侧罐沿，以相邻深棕内壁衔接，保留后沿、两侧厚壁、底部、像素风与原构图。仅将罐口区域合回原图，正式精灵继续采用原有 128×128 画布和调色板。
