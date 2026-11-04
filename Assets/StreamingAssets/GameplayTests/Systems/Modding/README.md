# MOD与Lua扩展

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`modding/patch、package_path、candidate_patch`。

已编写的规则覆盖：ModRuntimeManager.ApplyPatchOperation 的前置条件、替换、删除和并集合并；ModPathUtility 的包内路径边界；正式 ResourceReloadContext 和补丁组合的候选事务，后续补丁失败时不发布已经完成的部分内容。

`managed_runtime` 经正式加载接口取原始 Harmony DLL，检查程序集复用和无编译期引用，并在自有 NoInlining 方法上验证 17→23→17 的真实补丁与按所有者清理；IL2CPP 明确跳过。尚待补充：依赖加载排序、内容事务失败回滚、管理器自动 Lua 生命周期分派、候选热更新发布/撤销、MOD 集合联机一致性。共享现场只直接驱动正式 Lua 运行时，不扫描实际安装包。

后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享物品链路

入口：[物品、MOD与网络副本测试集](../ItemRuntime/gameplay-suite.json)，Lua业务断言在 [共享脚本](../ItemRuntime/gameplay.lua)。三个系统按同一实际实例链路组合，目录表映射同一案例，不重复执行。

已编写：同一隔离世界真实物品由正式 ModLuaRuntime 直接接收 Item 并修改 itemData 耐久；覆盖主Lua加载/恢复/更新/保存与物品Lua Load/Update/Unload 状态传递。测试包仅写本轮 Library 隔离目录并清理。

快速规则 `lua_direct_cs` 使用正式运行时验证 CS 游戏类型访问、包内 require 缓存/路径拒绝、两个 LuaEnv 的隔离、真实玩家入世事件订阅与 RegisterCleanup 退订。Android IL2CPP 真机和跨端联机不由编辑态规则代替。

覆盖边界：直接驱动正式 Lua 生命周期接口，不扫描用户安装包，不覆盖管理器自动分派、依赖排序、托管DLL、内容发布及热更新。
