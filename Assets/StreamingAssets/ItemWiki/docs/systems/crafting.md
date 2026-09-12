# 制作与加工系统

## 系统定位

负责手工制作、工作台、熔炼/热加工、石臼等基于配方的材料匹配、扣料、产出和加工动作。

## 当前机制

- Recipe JSON 是正式配方唯一真源，旧 Recipe/CookRecipe ScriptableObject 仅保留兼容用途。
- 普通制作通过 `CraftingService` 统一进入。
- `CraftingRecipeMatcher` 负责匹配，`CraftingTransaction` 负责扣料与产出原子提交。
- 普通合成是**无序材料集合**，同一种材料可以集中或分散在多个输入槽；额外无关材料不能屏蔽可制作候选。
- 同一份输入允许匹配多个候选，`CraftingStationController` 保存候选选择、进度和输出预览。
- 多产物必须全部能放下才提交；失败时不扣部分材料、不生成部分产物。
- `amount=0` 的输入参与配方签名，但不被消耗，可用于工具类需求。

## 工作站

- 手工台：`StationId=handcraft`，当前 4 输入 / 2 输出。
- 世界工作台：`StationId=workbench`，当前 5 输入 / 2 输出。
- 未指定 `requiredStation` 的普通配方可被任意普通制作入口使用。
- 热加工继续允许位置/网格语义，不与普通无序合成混用。

## 数据来源

- Manifest：`Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json`
- 分包：`Assets/StreamingAssets/GameConfig/Recipes/`
- 实现：`Assets/5_Scripts/5-3_GamePlay/Items/Crafting/`

## 原子事务原则

- 先预检全部输入与输出空间，再统一提交。
- Exact/Tag 混合需求必须做全局分配，不能逐条贪心扣料。
- 配方动作只能在库存事务成功后运行。
- 事务异常恢复库存快照，玩法进度信号只在最终成功后发布。

## 修改时联动

- Item ID/产物：Item 系统。
- 容量和库存：背包系统。
- 熔炉/工作台实体：建筑系统。
- UI 输入输出槽：UI 系统。

## 对应 Skill

`.agents/skills/flatworld-inventory-crafting/SKILL.md`
