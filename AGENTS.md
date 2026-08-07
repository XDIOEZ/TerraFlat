# FlatWorld 项目编程指南

- 灵活使用 MCP 和 Skill，减少不必要的 Token 消耗。
- 编写脚本时，积极使用 `#region` 按功能组织类内代码。
- 完成工作后提供简短的总结反馈。
- 本项目是 Unity 2D 俯视角沙盒游戏。
- Unity MCP 服务器端口通常为 `6400`，也可能为 `8080`。
- 处理游戏系统任务时，根据任务类型直接读取对应的 `.agents/skills/flatworld-*/SKILL.md` 专项 Skill；任务跨系统时只读取直接相关的多个 Skill，不要无目的地搜索整个项目。
- 完成代码或资源修改后，必须检查并同步更新本次使用的专项 Skill，尤其是在脚本、Prefab、场景、SO、Resources、Addressables、配置路径或架构约束发生变化时。
- 每个专项 Skill 的“近期变更”最多保留 10 条，按新到旧排列；新增后超过 10 条时删除最旧的一条。

## 编程规范

- 积极清理无效、未使用的代码。
- 积极使用 `#region` 包裹相关代码块。
- 编写代码时添加中文注释：类级注释应详细说明用途和关键数值；方法和字段使用一句话或关键词概括即可。
- 主要负责编写和制作游戏；需要人工测试时，在任务完成后明确告诉用户应测试什么。
- 除非用户明确要求运行测试，否则禁止主动调用 Unity Test Runner 或任何 `run_tests` 工具；默认只检查静态诊断、Unity 编译和 Console，并在总结中列出建议由用户执行的测试。
- 如果验证工具被用户取消，不要重试或中断剩余工作；改用不弹出交互卡片的静态诊断与 Console 检查完成收尾。

## 项目信息

<!-- UNITY CODE ASSIST INSTRUCTIONS START -->

- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: GoldenPathWorld_7ffe2d39
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender
- Active game object:
  - Name: Player
  - Tag: Untagged
  - Layer: Default

<!-- UNITY CODE ASSIST INSTRUCTIONS END -->

## 其他

- 如果读取了本文档，在最终回复末尾添加符合当前情境的颜文字，并注意保持多样性。
- 只在确有必要时使用子智能体。
- 一旦已经定位到明确实现位置，不继续进行泛化搜索。
  只有出现依赖不明确时才扩大搜索范围。
- 阶段性压缩上下文,不是只有在满的时候才压缩,检测到编辑系统出现跨度较大的更改的时候压缩上下文
