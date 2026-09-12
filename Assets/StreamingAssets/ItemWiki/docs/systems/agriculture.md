# 农业系统

## 系统定位

负责耕地、播种、作物成长、水肥、气候限制、作物阶段表现、收获以及野生作物和玩家种植作物的生命周期。

## 当前机制

- 播种由 `Mod_Plantable` + `IPlantableCrop` + `PlantingSummoner` 统一处理。
- 耕地状态来自 `FarmlandSystem` 与 `ChunkTerrainData`，锄地进度属于地格而不是锄头实例。
- 玩家种植作物由 `ChunkAgricultureRenderer` 管理并写入 `ChunkSaveRecord.AgricultureCells`，不作为临时自然掉落物保存。
- 普通农作物采用 `CropShell + Mod_Crop + Mod_CropYield + Mod_CropVisual`。
- `Mod_Crop` 保存权威成长状态；产物通过独立 `ICropHarvestAction` 模块扩展。
- 生长图可以使用 `seedling / growing / mature` 三阶段视觉，但中间表现阶段不增加额外持久化状态。

## 一次性作物与持续采果

- 萝卜、水稻等一次性作物成熟后执行产出动作并结束植株生命周期。
- BerryCrop 属于持续采果：单次交互只取 1 份果实库存，植株保留；生产模块周期补果。
- 药草、狗尾草等小型一次性野生作物不能直接继承 Berry 的持续采果语义。

## 野生作物

- 可被武器清除的自然小型作物统一继承 `WildCrop_Base`。
- 通用基类负责自然成熟初态、受击能力和受击碰撞体；死亡掉落仍由具体 Item 的 LootTable 定义。
- 玩家种植作物和自然生态补位是两套来源，玩家种植不会参加野生生态自动补位。

## 环境规则

- 水肥变化必须 Commit 回权威地块数据。
- 植物环境通过 `IPlantEnvironmentCondition` 与气候时间线结算。
- 天气、水肥与 CropGrowthMultiplier 各自只应用一次，避免重复倍率。

## 修改时联动

- 地块和生态：地图系统。
- 季节/温度：环境系统。
- Item/种子/产物：Item 系统。
- 区块持久化：存档系统。

## 对应 Skill

`.agents/skills/flatworld-inventory-crafting/SKILL.md`
