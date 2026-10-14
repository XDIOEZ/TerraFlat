# 角色对话

快速入口：`rules.lua`；操作：`dialogue/condition、config`。

已编写的规则覆盖：CharacterSpeechConditionEvaluator 数值/文本/缺失Fact条件；CharacterSpeechConfigLoader 的 JSON/schema/Fact 校验与无效条目隔离。

关键流程待补：本地玩家筛选、实际 Provider 冷却和优先级、一次性完成只能在成功显示后写入、屏幕气泡与语言切换。叶子程序集登记入口，GamePlay 不反向引用 Dialogue。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
