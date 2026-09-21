# FlatWorld 项目编程指南

- 灵活使用Mcp 和 SKill 减少Token的消耗
- 编写脚本的时候积极使用Region包裹类中功能
- 完成工作后需要总结反馈(简单的总结即可)
- 游戏是Unity2D项目
- Unity2D俯视角沙盒游戏
- 处理游戏系统任务时，根据任务类型直接读取对应的 `.agents/skills/flatworld-*/SKILL.md` 专项 Skill；任务跨系统时只读取直接相关的多个 Skill，不要无目的地全项目搜索
- 完成代码或资源修改后，必须检查并同步更新本次使用的专项 Skill，尤其是脚本、Prefab、场景、SO、Resources、Addressables、配置路径和架构约束发生变化时
- 每个专项 Skill 的“近期变更”最多保留 10 条，按新到旧排列；新增后超过 10 条时删除最旧的一条

## 编程规范

- 积极清空没用的无效代码
- 积极使用region包裹代码块
- 编写代码时习惯添加中文注释 类上书写详细数值 类中方法和字段之类的就一句话或者关键词概括一下就行
- 功能完成后直接进入真实 Play Mode / GamePlayMCP 实机流程验证，不把测试转交给用户，也不再使用 Unity Test Runner、`run_tests`、冒烟测试或独立静态验证脚本。
- 编译与 Console 只负责阻止带错误进入实机或帮助定位故障；最终是否通过以真实游戏操作和可观察结果为准。
- 实现更稳，强化工程结构
- 你是专业的Unity开发者 有架构思维 目光长远

## 不确定问题处理

- 先读取相关代码、配置和专项 Skill；关键事实、预期行为、目标平台、资源归属或改动边界仍不明确时，暂停实现并向用户提出最少且具体的问题。
- 不用猜测补齐需求；代码证据能够确认的内容直接确认，无法确认的内容列出可选方案及影响后询问。
- 仅在低风险且可逆的默认选择下继续，并先说明假设；涉及删除或覆盖资源、公共接口、存档、网络协议或跨平台行为时必须先确认。
- 不因用户当前打开某个文件就默认修改它，先定位真实实现入口与依赖关系。

<!-- UNITY CODE ASSIST INSTRUCTIONS END -->

#### 其他

- 如果你读到了 这个文档 在输出的末尾加上颜文字(符合当前的状况,要多样哦不要老是用一个) (◕‿◕)
  只在必要的时候去使用子智能体
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: AIECS 连续绘制批次
  - Tag: Untagged
  - Layer: AIECSRuntime
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: UI_Bag
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: FWUI_Chrome
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: FWUI_Shadow
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: FWUI_Body
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: FWUI_Footer
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: Text (TMP)
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f3c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore, Ghost
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender, Player, AIECSRuntime
- Active game object:
  - Name: 整理
  - Tag: Untagged
  - Layer: UI
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
