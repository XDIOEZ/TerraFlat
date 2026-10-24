# 播种、作物与土壤

- Mod_Plantable→CreateEntityCrop→扣真实种子，失败RollbackEntityCrop；resource作物不临时物化Item。空耕地播种不验水肥；基质范围保留播种意图，已占格/失败不回退食用，离开才由食用接管。
- FarmlandSystem改ChunkTerrainData；锄地未完成仅会话进度，停工10秒恢复，完成才写Tile_Farmland。CommitSoil提交水肥，工具等IWeaponActionAnimation确认真实挥动。
- SoilSamplingBatch仅共享同步寻址/容器，快照借到下次读取、不可留回调/实体；水肥每次读权威层、九格逐份泵水并按原顺序提交，不跨帧缓存。自然实体保留TryRead地块并CommitSampled写回，其他仍普通Commit。
- 自然 ECS 的水肥提交只跳过零量运算；本次结算前后水量/水肥尾进度/肥力完全相同，且 Source/Water/双进度/Fertility 与生成 moisture/fertility 层全部已同步，才略过重复的 CommitSampled 和农业存档写入。无初始化来源、表现层缺失或实际差量仍逐株按原顺序提交，不能因“水量没变化”丢弃不足一份的累计进度。规则 `agriculture/soil_commit_unchanged`。
- 农业差量查询复用执行世界的缓存名称和 `SaveDataMgr.TryGetChunkDelta(planet, Vector2Int)` 有界存档键；只缓存名称，不缓存差量对象，换档后仍查当前字典。`Soil.Commit.{Read,Write,Persist}` 拆开读取、提交与持久化，不能把逐株耗时直接当作重复批写；规则 `agriculture/soil_persistence_keys`。
- waterValue为int 0～10，一份世界水增一级；SurfaceLiquidAdded只通知本格吸水，作物读取仍九格灌溉。速率用级/秒，补/耗水尾进度随农业差量保存、消费源共用地块进度，湿度比例显式转float。
- 播种句柄归ChunkAgricultureRenderer，快照入AgricultureCells；天然资源用生态GUID/差量，解绑非死亡。一次收获先交付全部产物再删源，持续采果只扣库存，植株/产物不同ID。
- 植物存绝对游戏秒，经IWorldTimePlant/PlantClimateTimeline补冻结历史；统一结算气候/水肥/天气/最终成长配置，游标未追上不收获。雨雪由WorldWeatherSurfaceSystem供水，植物不重复加雨。
- LightLayerMgr实时当地光照达到minimumLightIntensity（默认0.2）才成长，ECS/GO共用EntityPlantRules。耗水独立世界秒游标，全天含成熟期按`2 * waterConsumptionMultiplier / DayLength`扣水，肥仅成长耗；allowNaturalSoil读写原地表，不改耕地/栽培身份，当前不交换气体。
- seedling/growing/mature由normalizedGrowth派生、不加存档阶段；BodyClip的BodyMinV/BodyMaxV为本地Y，主体/描边保留DisableBatching。伴生物由HostGuid和INaturalCompanionHostCondition门禁。
- 植株使用 `Generated/Farming/PlantSet_LowPixel_20261023`，独立 Sprite 在 `PlantSprites` Addressables 组登记；Point、FullRect、128 导入上限保留源 PPU。裁底线与地表原点对齐需 PivotY=buriedClip（萝卜0.36、水稻0.08），其余根点按新图可见底边；浆果果实锚点独立于植株缩放。草图集保留原源尺寸，ChunkView按209像素格/418 PPU切32个变体。
- BerryCrop按资源库存/日历生产，DayLength定批次、满库存照常走时钟；稳定GUID固定产量区间，每次采一份、不额外乘掉落倍率。WildCrop_Base给野生成熟初态/受击，死亡表仍由lootTableId定。
- ManualHarvestWork统一GO/原生采集：有效交互先付基础1体力并抖可见节点，默认5次工作结算一次、不乘产量；非法不推进，工具保留伤害/掉落。成熟作物击杀仅复用一次正常收获，通用死亡表对收获产物ID去重；原生ECS仅短期反馈对象动态更新。
- ChunkAgricultureRenderer初绑只收未完锄地/非零伤害格，无覆盖层/渐变停Update，由地形/施工事件唤醒；Terrain.Changed即时更新静态格。
- Mod_CropVisual临时停用还原外壳/解绑，重新启用重捕材质/图片/缩放并显示最新成长；正式卸载不恢复。规则`agriculture/crop_visual_reenable`。
