# 人物立绘

- `Portrait` 用于对话/角色 UI，`Concept` 用于探索设计，`Sprite` 用于运行时动画；共享身份，分别按用途构图，不互相缩放替代。
- 身份参考来自 `Assets/6_Art/Generated/Actors/` 或同角色 Sprite，锁定年龄感、体型、发型、面部、服装、主色与配件。风格、姿势参考不覆盖身份；不继承运行时俯视构图。
- 默认单人、平视、竖版全身、自然站姿，头脚与配件完整，约 8%–12% 留白。职业与生活习惯决定细节，避免模板脸、配件堆叠和随机纹理。
- 输出透明 PNG，不带场景、底座、投影、文字或边框。文件为 `Actors/<Name>/<Name>_Portrait_FullBody_<Expression>.png`。
- 默认遵循主体像素画风；明确要求非像素平涂时，用大色块、一档硬阴影与哑光材质。
- 要求接入 UI 时才配置 Unity：像素立绘用 Point、Alpha、无 Mipmap；明确的非像素插画才用平滑过滤/Alpha。锚点、图集和 Addressables 服从 UI，不套世界 Sprite 的 PPU/Pivot。
