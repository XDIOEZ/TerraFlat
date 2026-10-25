# 工业V1现场案例

- fluid_inventory / fluid_thermodynamics：保留共用库存事务与物性基础。
- fluid_state_save：独立罐保存、倒序恢复、拆回保留全部本地内容。
- fluid_topology：只连接无库存、气液分离、阀门、方向、指定气体过滤。
- fluid_transport：混合气比例排放、0停用、速率保存。
- fluid_tanks：自然20%上限、三槽实际铁罐按顺序装填。
- fluid_pumps_compressors：文件ID暂保留；内容已改为普通气泵和真实地表液泵，无压缩机用例。
- fluid_reactions：实际氢氧与地面水引擎；无电解/化学过滤用例。
- fluid_environment：共用地表事务、液体出口及满格保源。

退役的压差、组主分摊、爆炸、压缩功和相变工业断言已删除，以对应简单规则替换。Lua持有断言，C#采样调用生产接口。未覆盖项与本轮执行边界见上层README；本轮因用户PlayMode未执行这些新现场案例。
