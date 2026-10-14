# 工业气液纯规则回归

- 只创建独立 `FluidCatalog`、`FluidInventory`，检查预留与部分转移、物质量守恒、配方原子扣量、液体容量、热交换、质量版本和同腔相变内能。
- 不修改 `FluidCatalog.Default`。自有物性定义作为明确领域输入，算法来自生产 `FluidInventory`/`FluidThermodynamics`。
- 未覆盖真实管道有向供给、共享吞吐、组合罐拆分、测压器、电解/压缩/发动机设备、超压爆炸和场景操作；这些仍保留在现有 `fluid-suite.json` 集成专项。
- 本快速组没有截图，不能把通过写成全部工业链路通过。
