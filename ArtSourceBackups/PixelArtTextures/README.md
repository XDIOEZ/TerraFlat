# 高清贴图原图备份

此目录在 `Assets` 外，不是 Unity 资源，不参与 Player、Resources 或 Addressables 打包；请和项目一起保留，不要移入 `Assets`、`Resources` 或 `StreamingAssets`。

工具入口：**FlatWorld → 其他工具 → 美术 → 一次性贴图优化器**。

先「只读扫描」，再「备份并压缩一次」。工作 PNG 在原路径替换，GUID / Sprite ID 保留，PPU 等比例调整，编辑器与构建共用低分辨率工作图。仅处理实际用途目录引用的 Generated 单图 PNG，图标上限 256、其他上限 512；保留 Point、Alpha 和颜色，不增加双线性插值。严格等比例缩小及颜色/轮廓检查未通过的图保留；切片、可读图、自定义轮廓/骨骼/辅助贴图和保护标签不处理。

每次处理在 `<资源 GUID>/<时间_唯一编号>/` 保存 `original.png`、`original.png.meta` 和 `record.json`。写入和校验备份成功后才替换工作图，旧快照不覆盖；单张失败回滚，取消保留已完成部分。`record.json` 记录输入、输出 SHA-256 和事务状态；原图不是 Library 缓存，清理 Library 不会删除它。

恢复时可选中 Project 中的图片后使用「恢复 Project 选中图片」，或「恢复所有最近备份」。恢复图片和导入设置，备份仍保留。压缩后又修改过的图片或 meta 会跳过，避免覆盖后续工作；需要人工处理时先备份当前图，再将 `original.png` 和 `original.png.meta` 配对还原到原路径。不要只恢复 PNG 而保留缩图后的 PPU。

工具不自动启动、不参与打包回调，不在 PlayMode、编译或打包时修改资源。结果报告：`Library/FlatWorldSpriteAtlasPlan/texture-optimization.json`；PNG 文件减少量不等于最终包体减少量，最终大小以实际构建为准。显卡编码沿用原平台设置，不保证不同平台的有损编码结果逐像素一致。
