# MOD与Lua扩展

快速入口：`rules.lua`；操作：`modding/patch、package_path、candidate_patch`。

已编写的规则覆盖：ModRuntimeManager.ApplyPatchOperation 的前置条件、替换、删除和并集合并；ModPathUtility 的包内路径边界；正式 ResourceReloadContext 和补丁组合的候选事务，后续补丁失败时不发布已经完成的部分内容。

关键流程待补：依赖加载排序、内容事务失败回滚、托管 DLL 与 Lua 生命周期、候选热更新发布/撤销、MOD 集合联机一致性。路径测试不创建或扫描实际安装包。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
