# FlatWorld 项目编程指南

- 灵活使用Mcp 和 SKill 减少Token的消耗
- 编写脚本的时候积极使用Region包裹类中功能
- 完成工作后需要总结反馈(简单的总结即可)
- 游戏是Unity2D项目
- Unity2D俯视角沙盒游戏
- 6400 是MCP服务器端口 也可能是8080
- 处理游戏系统任务时，根据任务类型直接读取对应的 `.agents/skills/flatworld-*/SKILL.md` 专项 Skill；任务跨系统时只读取直接相关的多个 Skill，不要无目的地全项目搜索
- 完成代码或资源修改后，必须检查并同步更新本次使用的专项 Skill，尤其是脚本、Prefab、场景、SO、Resources、Addressables、配置路径和架构约束发生变化时
- 每个专项 Skill 的“近期变更”最多保留 10 条，按新到旧排列；新增后超过 10 条时删除最旧的一条

## 编程规范

- 积极清空没用的无效代码
- 积极使用region包裹代码块
- 编写代码时习惯添加中文注释 类上书写详细数值 类中方法和字段之类的就一句话或者关键词概括一下就行
- 你只需要编写和制作游戏 测试可以交给屏幕前的我,如果需要测试 可以在任务完成后告诉我要帮你测试什么
- 除非用户明确要求运行测试，否则禁止主动调用 Unity Test Runner 或任何 `run_tests` 工具；默认只检查静态诊断、Unity 编译和 Console，并在总结中列出建议由用户执行的测试
- 如果验证工具被用户取消，不要重试或中断剩余工作；改用不弹出交互卡片的静态诊断与 Console 检查完成收尾

## 不确定问题处理

- 先读取相关代码、配置和专项 Skill；关键事实、预期行为、目标平台、资源归属或改动边界仍不明确时，暂停实现并向用户提出最少且具体的问题。
- 不用猜测补齐需求；代码证据能够确认的内容直接确认，无法确认的内容列出可选方案及影响后询问。
- 仅在低风险且可逆的默认选择下继续，并先说明假设；涉及删除或覆盖资源、公共接口、存档、网络协议或跨平台行为时必须先确认。
- 不因用户当前打开某个文件就默认修改它，先定位真实实现入口与依赖关系。

### 相关文档

<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player
- Active game object:
  - Name: Player
  - Tag: Untagged
  - Layer: Player
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->

#### 其他

- 如果你读到了 这个文档 在输出的末尾加上颜文字(符合当前的状况,要多样哦不要老是用一个) (◕‿◕)
  只在必要的时候去使用子智能体