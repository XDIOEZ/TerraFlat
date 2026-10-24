# 入海口流场回归

运行 `python Tools/Tests/EstuaryCurrent/verify.py`，在 `Temp/EstuaryCurrentValidation` 编译正式 WorldModel 源码，不刷新或暂停运行中的 Unity。

覆盖扇形边界、两侧发散、全反向洋流转向、无风、格子量化后的物品路径、海水范围、岛屿遮断、相邻区块、世界环绕、重叠来源确定性和取消。

环境 Lua 的 `estuary_current` 另经 `ChunkMgr.TryResolveTerrainWaterCurrent` 验证正式入口、海水身份/水量、平台、湖泊、干格与公共角色速度。独立 CLR 测试不代表 Unity PlayMode、真实物品碰撞、联机或视觉验收。
