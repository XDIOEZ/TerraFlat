---
name: flatworld-guide
description: "FlatWorld 本地新手引导资格、玩法里程碑、教程阶段、存档与对话 Facts。"
---

# FlatWorld 新手引导

## 入口与数据流

`Assets/5_Scripts/5-3_GamePlay/Presentation/Guide/`：`NewPlayerGuideController.cs` 控制，`NewPlayerGuideStage.cs` 阶段/ID，`NewPlayerGuideProgressStore.cs` 保存资格。任务定义在 `GameConfig/Quests/starter.json`，台词在 `Assets/Resources/Dialogue/Soliloquy/guide_survival.json`。

`本地角色资格 → GameplayProgressEvents → PlayerQuestRuntime 目标/阶段 → Tutorial Facts → 自言自语系统`

## 职责

- 引导仅属于本地玩家，资格建立后持久化；里程碑来自成功玩法事务，与按钮点击/预览状态分离。
- `flatworld.tutorial` 只保存资格，里程碑、阶段和完成状态由唯一玩家任务运行时写入 `flatworld.quests`；运行时未就绪时只在当前角色内存暂存里程碑。
- 教程任务允许提前记录后续阶段且不进入任务列表；材料须在当前库存同时达到门槛，达标后永久记录。
- 控制器提供 Facts 和进度；对话系统持有文本/显示，扩展教程阅读入口约定为书籍。
