# 草和花的离线回归

在项目根目录运行 `powershell -File Tools/Tests/GroundCover/verify.ps1`，需要 .NET 8 SDK 和 Unity 已生成的项目引用。

脚本先编译当前生产程序集，再调用真实 `BuildSurfaceCell` 和 `ChunkEcologyGenerator`，读取正式地表 Profile 与花朵 JSON。

- 草：零降水、截图降水、湿润地面逐渐增加密度；固定种子保持相同格子和结果；泥土与泥炭允许草；极冷、极热、石地和水下排除草。
- 花：截图的 14.2℃、0.106 降水、0.657 高度不再被旧硬门槛排除；零降水仍保留稀疏生成；极冷、极热拒绝生成。

草测试注入固定气候到正式单格构建链，花测试注入固定地形到正式生态链；不验证风携水汽算法、游戏画面、原生 BRG/GPU、存档恢复或操作体验。所有构建产物保存在忽略的 `Temp/GroundCoverValidation`，不启动、暂停或退出 Unity。
