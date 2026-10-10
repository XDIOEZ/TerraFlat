# 固定结构模板

`fixed-structures.json` 是固定结构的唯一配置入口，当前 GM 生成器消费 `kind: "ship"`。增加模板时新增 `structures` 项，不修改目录解析器。

- `version` 固定为 1；结构 `id` 和成员 `memberId` 必须唯一。
- `width`、`height` 为格数，范围 1～32；`clearance` 为外围避让格数，飞船至少避让一格，`searchRadius` 为查找位置的半径，最大 64。
- `items` 中的 `width`、`height` 表示重复铺设同一物品的矩形，默认 1。坐标从左下角开始，必须完全落在结构范围内。
- 单格成员保留 `memberId`；矩形成员展开为 `memberId:x:y`。多层部件可以位于同格，实际占用冲突由生成器按照正式物品定义检查。
- `quarterTurns` 是逆时针 90 度的次数，范围 0～3，默认 0。
- `batteryChargeRatio` 为电池预充比例，范围 0～1。
- 气体使用 `pressureKPa`、`temperatureCelsius` 与稳定流体 ID 到摩尔比例的 `components`；所有比例必须为正，合计 1。
- `liquids` 的值为升数；`inventories[].name` 必须与设备正式库存名称一致，`items[].amount` 为物品数量。
- `cabinGas` 是首次生成舱室的气体。补充设备资源不应重新填舱，破舱修复仍走正式供气规则。

默认模板 `gm:small-test-ship` 使用真实现有部件：完整地板、密闭舱、门、双侧对接口、六台发动机、控制台、导航、重力、供气、培育仓、电池和钢罐。铜线与光纤分别承担供电和导航信息连接。

资源首次生成时预填；补充按钮按同一模板资源规则恢复设备资源，库存按剩余容量补充，保留其他物品。物品、流体身份和设备容量在实际生成前检查，未知字段或无效目录会拒绝加载，不使用猜测或替代物品。
