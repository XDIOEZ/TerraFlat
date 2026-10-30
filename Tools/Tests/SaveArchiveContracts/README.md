# 存档字段与载荷检查

运行 `python Tools/Tests/SaveArchiveContracts/verify.py`：隔离编译正式 Data 和 WorldModel 程序集及根、世界、地形、机械与生态 DTO，读取 `core-v1.bin`、`world-v1.bin`、`terrain-v1.bin`、`item-v1.bin`、`food-v1.bin`、`tilemap-v1.bin`、`machine-v1.bin`、`machine-payload-v1.bin`、`atmosphere-v1.bin`、`time-gradient-v1.bin` 和 `chunk-v1.bin`。测试覆盖事件/生成/时间、星球/生态/大气、玩家与物质、槽位布局与嵌套库存、食物计时器与未知规则载荷、农田及多层地块、草层和环境、机器 decimal、压力事件及时间渐变，也检查缺字段缺省、未知新增字段跳过、未注册 unmanaged 结构拒绝与显式 codec 的 Schema ID 和版本。样本是兼容基线，不可用新格式覆盖。

根、世界、地形及嵌套存档字段来自生产代码；测试替身只补不参与编码的世界注册、时间运行时和大气服务。工作区其他任务的 FireCells 字段仅从生成的临时测试源码中排除，避免把外部未提交火焰工作作为该测试的依赖。

`python Tools/Tests/SaveArchiveContracts/audit.py` 使用 Unity 现有编译响应文件和 Roslyn 分析 `Ex_ModData_MemoryPackable`、机器存取封装及飞船质量读取的泛型调用，结果位于 `Temp/SaveArchivePayloadAudit/payload-types.json`。其他泛型封装、反射、MOD 或尚未加入编译的源码可能不在结果中；引用类型及其内嵌载荷需要分别审查，不能只根据此表认定所有类型已经具有兼容契约。
