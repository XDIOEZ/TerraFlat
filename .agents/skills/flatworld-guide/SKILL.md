---
name: flatworld-guide
description: "FlatWorld 本地新手引导资格、玩法里程碑、教程阶段、存档与对话 Facts。"
---

# FlatWorld 新手引导

## 入口与数据流

`Assets/5_Scripts/5-3_GamePlay/Presentation/Guide/`：`NewPlayerGuideController.cs` 控制，`NewPlayerGuideStage.cs` 阶段/ID，`NewPlayerGuideProgressStore.cs` 进度。台词：`Assets/Resources/Dialogue/Soliloquy/guide_survival.json`。

`本地角色资格 → GameplayProgressEvents → 里程碑 → 当前阶段 → Tutorial Facts → 自言自语系统`

## 职责

- 引导仅属于本地玩家，资格建立后持久化；里程碑来自成功玩法事务，与按钮点击/预览状态分离。
- 数据仅写 `ItemSpecialData.flatworld.tutorial` 命名空间。
- 控制器提供 Facts 和进度；对话系统持有文本/显示，扩展教程阅读入口约定为书籍。
