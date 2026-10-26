# 石油化工离线回归

执行 `python -X utf8 Tools/Tests/Petrochemicals/verify.py`。

脚本从生产 `ItemMatterReaction.cs` 原文截取 DTO、编译器与反应匹配器，与完整生产 `CraftingMaterialAllocator.cs`、`CraftingTransaction.cs` 一起编译；同时截取 `MixedLiquidContents` 组分存储/恢复与 `InventoryVesselHeating.ProcessComponents` 原文，编译正式 `ThermalRuntime.cs`。测试输入来自正式石油物品 JSON。替身只提供 Unity 数学、简单物品存储/复制和场景外库存外壳，不重写匹配、扣料、容量分配、液体加工或事务算法。

覆盖蒸馏和裂解温度边界、缺料、整批、多产物满槽原子拒绝、重复提交、催化剂缺失/冷却/拖拽/零堆叠/锁槽、七批聚合后催化剂保留及重复使用、非法零耗料反应；补液体持久化写前抛错、写后抛错、恢复写入再次抛错的双侧回滚、原始异常保留和成功重试。

该入口不连接 Unity，不刷新或中断用户 PlayMode；不覆盖正式物品容量/堆叠实现、坩埚液体加热、实际管路、画面和联机。对应 Lua 的 Processing/rules.lua 与 Mechanical/mechanical_processing.lua 使用正式库存和坩埚入口补充这些边界，本轮因用户 PlayMode 未执行。
