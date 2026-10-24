# 建筑随区块卸载回归

`dotnet run --project Tools/Tests/BuildingStreaming/BuildingStreaming.Tests.csproj`

直接编译 `ChunkMgr.RuntimeBuildings.cs` 与 `SaveDataMgr.RuntimeBuildingStreaming.cs`。覆盖重复登记、世界/区块隔离、迁移、注销和对象池复用；卸载捕获耐久/库存/模块状态、冷快照在自动保存时保留、身份去重和移交后不复活、反复重入、捕获失败保留现场、飞行所有权及非权威端拒绝。

Unity 对象、Item 工厂、序列化和存档容器用测试边界代替，不能代替真实 GM 造船、落地、跨区块移动、机器网络、渲染或联机验收。Lua `building/chunk_residency` 另采样正式归属索引；用户处于 PlayMode 时不停止游戏运行该测试。
