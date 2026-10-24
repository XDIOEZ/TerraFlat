# 基类耐久独立测试

运行：

```text
dotnet run --project Tools/Tests/ItemDurability/ItemDurability.Tests.csproj -c Release
```

链接正式 `ItemData`、`ItemDurabilityRules`、`ItemSlot`、`Inventory_Data`、容量策略和耐久事务源码，验证当前件损耗、碎裂/清槽、拆分/整堆移动、合堆限制、多槽接收、整理、容量预检、旧引用和非法消耗拒绝。测试不启动 Unity，也不修改玩家库存。

`Stubs.cs` 只替代 Unity 属性/数学、事件、序列化及质量监听等外壳，克隆仅复制本夹具使用的独立数据，不模拟模块装配。实际 MemoryPack 存档、拾取器、网络和运行时外壳回收由 `GameplayTests/Systems/Inventory/rules.lua` 等 Unity 回归负责，独立测试通过不能替代这些验收。
