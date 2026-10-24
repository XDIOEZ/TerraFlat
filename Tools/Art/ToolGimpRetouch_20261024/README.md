# 工具 GIMP 像素整理源稿

日期：2026-10-24。35 款沿原图用 GIMP MCP 的 `1. Pixel` 铅笔整理，用户认可的铁斧、铜镐、青铜铲精确复用。各 `ID.xcf` 保留 Original 和 Retouch 图层；`plan-*.json` 记录每个明确坐标的原色、新色与原因，`RetouchSummary.json` 汇总当前 PNG 的实际统计。

- 木柄整理为连贯三档明暗，绑带保留两三条清楚色带，金属使用连贯短刃光；石、骨等保留自己的大切面。没有生成新图、模糊、全图限色或重采样。
- 24 款仍为 32×32。斧/镐的 11 款用 GIMP 原生裁切仅去多余透明边，保留 1 像素；PPU 不变，归一化 Pivot 按计划重基，所有可见像素世界位置保持。原 32 画布坐标和裁切 rect 都有记录。
- `.py` 文件须在 GIMP MCP 的 PyGObject 上下文执行，不能用普通 Python 代替绘画。重放需对应 beforeSha256 的原始 PNG；现用成品不可再次裁切。原图可从对应 Git 历史和 XCF 原图层核对。
- XCF 和计划放在 Tools 下，避免 Unity 将源稿误作为运行时贴图导入；正式 PNG 位于 Assets/6_Art/Generated/Tools/PrimitiveSet_20261023/。

本轮未控制 PlayMode、强制 Import/Refresh 或进行游戏验收；游戏内观感由用户验收。
