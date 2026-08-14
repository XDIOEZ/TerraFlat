---
name: flatworld-guide
description: "Use when: 定位或修改 FlatWorld 的新手引导、教程资格、阶段推导、里程碑存档、玩法进度监听或引导对话 Facts。关键词：NewPlayerGuideController、NewPlayerGuideProgressStore、NewPlayerGuideStage、flatworld.tutorial。"
---

# FlatWorld 新手引导

## 入口

- 控制器：`Assets/5_Scripts/5-3_GamePlay/Presentation/Guide/NewPlayerGuideController.cs`
- 阶段与 ID：同目录 `NewPlayerGuideStage.cs`
- 进度存档：同目录 `NewPlayerGuideProgressStore.cs`
- 引导台词：`Assets/Resources/Dialogue/Soliloquy/guide_survival.json`

## 主链

`本地新角色资格 → 订阅 GameplayProgressEvents → 写入里程碑 → 推导当前阶段 → 提供 Tutorial Facts → 自言自语系统显示`

## 边界

- 只处理本地玩家；旧存档默认不自动开启，引导资格一旦建立必须持久化。
- 里程碑只在玩法事务真正成功后记录，不从按钮点击或预览状态推断成功。
- 进度只写入 `flatworld.tutorial` 命名空间；保留其他 `ItemSpecialData` 数据。
- 控制器只贡献 Facts 和进度，不直接创建引导 UI 或硬编码显示文本。
- 修改制作、建筑、背包或台词联动时，同时使用对应专项 Skill。

## 验证

- 默认检查静态诊断、Unity 编译和 Console。
- 仅用户明确要求时运行 `Guide.Smoke`；测试位于 `Assets/GameTest/Guide/`。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
