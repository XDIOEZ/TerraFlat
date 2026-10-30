# 世界生成 v1 样本

`python Tools/Tests/WorldGenerationV1/verify.py` 使用 Unity 同版 Roslyn 编译纯世界模型，在隔离进程对地表、洞穴及无限/环绕拓扑各生成三个固定种子区块，比较地形、环境层、高度和生态放置的 SHA-256。测试还检查派生 Profile 保留版本、未知版本被拒绝。

`expected.json` 是 v1 已发布输出基线。任何输出变化都要保留 v1 的生成实现并建立新版本，不能为了让测试通过直接改基线。样本不能证明所有坐标、MOD 和平台完全一致；真实 Unity、Burst、Android 与跨平台确定性仍需单独验证。
