---
name: flatworld-cloud-build
description: "使用 GitHub Actions 为 FlatWorld 云端打包 Windows 或 Android，等待构建、下载产物或诊断失败日志。关键词：云端打包、GitHub Actions、APK、Android、Windows 构建。"
---

# FlatWorld 云端打包

## 固定配置

- 仓库：`XDIOEZ/TerraFlat`，分支：`master`。
- 工作流：[unity-build.yml](../../../.github/workflows/unity-build.yml)，输入名：`target_platform`。
- 安卓、APK 对应 `Android`；Windows、PC 对应 `StandaloneWindows64`。
- 产物名分别为 `FlatWorld-Android`、`FlatWorld-StandaloneWindows64`，只保留 3 天。
- 工作流已配置 Unity Secrets；禁止读取、输出或回显 Secret 内容。

## 主链

1. 用户明确说出平台时，视为授权启动该平台一次构建；未说明平台时只问 Android 还是 Windows。
2. 先用 `gh auth status` 确认登录，再启动一次：

```powershell
gh workflow run unity-build.yml --repo XDIOEZ/TerraFlat --ref master -f target_platform=Android
```

3. 立即把运行链接发给用户；若命令未返回链接，通过下列命令取得最新手动运行：

```powershell
gh run list --repo XDIOEZ/TerraFlat --workflow unity-build.yml --branch master --event workflow_dispatch --limit 1 --json databaseId,status,conclusion,url,createdAt
```

4. 每 45～60 秒用 `gh run view <run-id> --repo XDIOEZ/TerraFlat --json status,conclusion,url,jobs` 查看状态并简短更新，直到结束。
5. 成功后查询产物名称、大小和到期时间：

```powershell
gh api repos/XDIOEZ/TerraFlat/actions/runs/<run-id>/artifacts --jq '.artifacts[] | {name,size_in_bytes,expired,expires_at}'
```

6. 用户要求下载到本机时，下载到忽略目录并返回可跳转文件链接：

```powershell
gh run download <run-id> --repo XDIOEZ/TerraFlat --name FlatWorld-Android --dir .codex-tmp/cloud-build-<run-id>
```

Android 安装文件通常是 `Android/Android.apk`；Windows 必须保留 `.exe`、`*_Data` 和同目录文件，不能只单独取出 `.exe`。

## 失败与边界

- 失败时使用 `gh run view <run-id> --repo XDIOEZ/TerraFlat --log-failed`，只汇总首个真实根因。
- 一次打包请求只启动一次工作流；不自动重跑，不擅自修改代码、Secrets、工作流、分支或提交。
- 若用户进一步授权修复，最小修改后重新构建；不要把失败隐藏成成功，也不要泄露许可证信息。

## 完成反馈

- 成功：给出运行链接、产物名、大小、到期时间和简短安装方法。
- 失败：给出失败阶段、根因、影响和下一步所需授权。
