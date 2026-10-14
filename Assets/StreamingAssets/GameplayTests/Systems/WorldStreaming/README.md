# 世界模型与区块流送

快速入口：`rules.lua`；操作：`world_streaming/chunk_leases、generation_lifecycle、commit_snapshot`。真实入口：`gameplay.lua` / `world_streaming/live_window`。

已编写的规则覆盖：独立 WorldRuntime 的模拟、表现、寻路租约，以及重复归还和逐出保护；生成失败消息的请求版本、取消和世界 Epoch 失效保护；真正 Commit 的唯一所有权移交、旧结果拒绝释放、地形修改和逐出后快照保持冻结。真实场景只读正式窗口、脚下区块数据、ChunkView和租约。

关键流程待补：真正并发生成与取消任务的完整释放；玩家移动时队列重排；跨区画面；世界退出取消工作。快照和请求生命周期在独立纯世界执行，真实窗口采样不额外生成地形。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
