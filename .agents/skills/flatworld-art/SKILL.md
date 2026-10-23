---
name: flatworld-art
description: "FlatWorld 游戏素材、人物立绘与机制图示的统一绘图约定。"
---

# FlatWorld 绘图

**主体画风：低饱和、偏写实的复古像素风，像素数量要稍低。**

- 自然比例、可信结构、硬边大像素簇、少量色阶；低像素指较低逻辑分辨率，画布与 PPU 按实际消费方。
- 先看同类参考，保持身份、尺度、配色、光源及动画基线一致。旧参考不覆盖主体画风，用户当次明确要求优先。
- 独立轮廓透明、满格 Tile 不透明；原图直用时保留画布与 Alpha。仅绘图不顺带改玩法或配置。
- 游戏图放 `Assets/6_Art/Generated/<System>/<Name>/`，立绘归 `Actors/<Name>/`；机制图按下方参考保存。

绘图规则只维护在本 Skill，分类细节放 `references/`，不另建绘图 Skill。

## 按需资料

只打开本次需要的参考，不批量读取。

- [锚点与材质](references/art-direction.md)
- [Sprite、动画、换装与导入](references/runtime-sprites.md)
- [人物立绘](references/portraits.md)
- [提示词简表](references/prompts.md)：需要模板时读取。
- [机制 SVG](references/mechanics-svg.md)：说明图以清晰为先，不强制像素化。
