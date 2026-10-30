# 动物行为树离线验证

在项目根目录执行 `python Tools/Tests/AnimalBehaviorTree/verify.py`。脚本编译正式纯树执行器、继承解析、共享子树合并、ECS 参数映射、伤害类型与出血规则，使用项目自带 XLua 执行与编辑器相同的断言；另从生产节点注册源码提取名称，通过正式继承解析和树编译展开全部动物 JSON，并执行正式觅食决策与空中捕食中断清理回归。临时结果写入 `.codex-tmp/animal-behavior-tree-tests`。目录结构检查中的节点工厂、觅食候选、猎物和环境接口是替身，Actor 装配、导航、物理、攻击和动物现场表现仍需 Unity 验证。
