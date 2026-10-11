---
name: flatworld-portrait-art
description: "FlatWorld 高清角色立绘、对话人物图及 UI 人物展示；运行时像素 Sprite 使用 flatworld-pixel-art。"
---

# FlatWorld 高清角色立绘

## 资源与规格

- 同角色与画风参考：`Assets/6_Art/Generated/Actors/`。身份参考决定人物，画风参考决定表现，姿势参考决定动作；身份冲突以同角色素材为准。
- 身份包含年龄感、体型、发型、面部、服装、主配色和标志配件。运行时 Sprite 可以提供身份信息，其俯视比例和像素规格不继承到立绘。
- 默认单人、平视、竖版全身，头脚和配件完整，四周约 8%～12% 留白；实际构图服从用户与 UI 消费方。
- 输出为透明 PNG，允许平滑 Alpha，不带场景、地面、投影、文字或水印。目录 `Assets/6_Art/Generated/Actors/<Name>/`，命名 `<Name>_Portrait_FullBody_<Expression>.png`。
- UI 导入使用适合缩放的平滑过滤、Alpha、关闭 Mipmap；显示尺寸由 UI 布局决定。仅制作立绘不连带创建玩法或 Unity 资源配置。

## 按需资料

- [portrait-guide.md](references/portrait-guide.md)：画风、身份与构图细节。
- [prompt-recipes.md](references/prompt-recipes.md)：生成、重绘和表情变体的提示词。
