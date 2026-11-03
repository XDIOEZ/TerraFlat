# 一次性贴图优化器回归

在项目根目录执行 `python Tools/Tests/PixelArtTextures/verify.py`，需要 Pillow、已连接的空闲 Unity 编辑器和最新脚本已编译。PlayMode、编译或导入期间拒绝开始，不切换游戏状态。

使用一次性生成的测试 PNG 走正式扫描、压缩、Unity 重新导入及选中恢复流程。覆盖只读预览、精确最近邻/Alpha、GUID 与 Sprite ID/世界尺寸、PPU、Assets 外逐字节备份、重复执行、过期计划、恢复冲突、损坏备份和恢复原图与 meta。非整比例横图与竖图按长边同步 PPU，短边最多半个目标像素误差、归一化锚点不变；细节图小档不通过时从原图尝试高一档，已优化工作图不得重复降采样；构建链不自动压缩。

测试只恢复自己的测试资源，保留原 Project 选择，结束清理测试资源与其专属备份，不操作正式素材。结果在 `Library/FlatWorldTextureTask/regression-report.json`。
