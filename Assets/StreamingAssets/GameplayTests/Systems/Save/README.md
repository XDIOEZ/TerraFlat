# 实例与世界存档

快速入口：`rules.lua`；操作：`save/snapshot_memory、special_namespace、space_item_chain`。真实入口：`gameplay.lua` / `save/isolated_roundtrip`。

已编写的规则覆盖：正式 ItemInstanceSnapshot 的内存二进制往返、持久身份、当前定义耐久与模块状态恢复；特殊状态命名空间隔离；JSON/Base64/MemoryPack/实例克隆的嵌套库存链、Vector3字段转换、严格版本和时钟边界。真实场景使用正式 SaveToDisk 首次写入、原子替换、备份和临时文件清理，并读取自己的文件恢复库存。

关键流程待补：并发自动保存与退出保存；磁盘故障注入；完整退出再进入世界后的机器和掉落物复活。禁止读取或改真实玩家档案。

写盘门禁要求当前正式存档目录严格位于 `Library/FlatWorldGameplayTests/Saves/<本轮ID>`，拒绝目录联接。保存源为当前档案的冷克隆，测试文件保留在本轮目录的唯一 `Roundtrips` 子目录。读取使用停用独立 SaveDataMgr，调用正式核心解析，校验 MOD 元数据但不发布全局 MOD 状态；Lua 同时检查活跃存档、角色、管理器引用及必要标量没有被修改。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
