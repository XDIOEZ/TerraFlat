# 音频

快速入口：`rules.lua`；操作：`audio/bus_volume、catalog_lifecycle`。

已编写的规则覆盖：AudioUserSettings 五总线与总音量范围；AudioPlayOptions 的全局和空间位置参数；JsonUtility装配独立 AudioCue/AudioCatalog、重复ID校验、目录索引重建与规范查询，随后释放临时资源。未创建全局 AudioService。

关键流程待补：实际 AudioService 的声源池、并发策略、旧句柄不能误停复用声源、空间衰减、淡入淡出、Mixer路由及设备听感。数据测试不播放声音或修改用户音量。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
