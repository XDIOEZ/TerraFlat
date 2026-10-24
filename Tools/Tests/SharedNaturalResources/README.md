# 大矿山外部规则回归

在仓库根目录执行 `python Tools/Tests/SharedNaturalResources/verify.py`。

工具不控制 Unity，不刷新资源、不暂停或退出 PlayMode，所有编译和运行结果位于忽略的 `Temp/SharedNaturalResourcesValidation/`。先构建正式 GamePlay 程序集，再在测试副本中禁用四个 Unity 原生 ProfilerMarker 初始化，以便独立 CLR 执行纯生成规则；不修改正式生成算法。Unity 播放时生成的 csproj 可能滞后，临时 MSBuild targets 按现有 asmdef 补齐 GamePlay、UI、WorldModel 源文件。

`--skip-build` 只跳过完整 GamePlay 构建，仍重新编译用于规则测试的 WorldModel 副本。

覆盖确定性候选、加载顺序、周期世界接缝、四区块交界、冷区块地形校验、内部生态过滤、引用宿主交接、取消和配置契约。真实物理、武器命中、ECS 掉落及存档读写需使用 Unity 的 Ecology/Combat 现场回归另行验证。

当前内容：`Items/shells/giant_mines.json` 为11种现有小矿山各提供巨型版，沿用对应矿物贴图、开采特性和产物。普通矿物使用 1402 PPU，巨型保留独立 14.02 倍视觉缩放；实际阻挡宽13、高5/3、底边-2.5，对应底部26格占地，受击框仍为13.8×9.6，生命统一20万。石矿保留原巨型最终掉落表，其余继承对应小矿山表。生成净空仍为13×5，64格候选网格总概率0.13，在矿物规则之间分配；不是每格13%。

2026-10-24 已静态更新上述内容断言；用户正在 PlayMode 且自行验收，本轮未运行本工具。
