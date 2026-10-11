---
name: flatworld-cloud-build
description: "通过 GitHub Actions 为 FlatWorld 构建 Android 或 Windows，查看日志和下载产物。"
---

# FlatWorld 云端构建

## 配置

- 仓库 `XDIOEZ/TerraFlat`，构建分支 `master`。
- 工作流：[unity-build.yml](../../../.github/workflows/unity-build.yml)，输入 `target_platform`：`Android` 或 `StandaloneWindows64`。
- 产物 `FlatWorld-Android` / `FlatWorld-StandaloneWindows64`，保留 3 天；许可证由 GitHub Secrets 提供，内容不进入日志或输出。

## 操作

平台明确的打包请求对应一次工作流启动；未指定平台时先确定平台。以下以 Android 为例：

```powershell
gh auth status
gh workflow run unity-build.yml --repo XDIOEZ/TerraFlat --ref master -f target_platform=Android
gh run list --repo XDIOEZ/TerraFlat --workflow unity-build.yml --branch master --event workflow_dispatch --limit 1 --json databaseId,status,conclusion,url,createdAt
gh run view <run-id> --repo XDIOEZ/TerraFlat --json status,conclusion,url,jobs
gh api repos/XDIOEZ/TerraFlat/actions/runs/<run-id>/artifacts
gh run download <run-id> --repo XDIOEZ/TerraFlat --name FlatWorld-Android --dir .codex-tmp/cloud-build-<run-id>
```

失败日志：`gh run view <run-id> --repo XDIOEZ/TerraFlat --log-failed`。一次请求不自动重跑或修改代码、工作流、Secrets、分支；修复和重试按用户授权范围处理。运行 URL、状态及产物名称/大小/到期时间是交付信息。

Android 安装文件通常为 `Android/Android.apk`；Windows 包包含 `.exe`、`*_Data` 和同目录依赖。
