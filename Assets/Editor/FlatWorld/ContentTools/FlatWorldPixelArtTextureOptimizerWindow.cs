using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>集中显示一次性贴图缩小的预览、执行结果和高清备份恢复入口。</summary>
public sealed class FlatWorldPixelArtTextureOptimizerWindow : EditorWindow
{
    #region 工具窗口

    private FlatWorldPixelArtTextureOptimizer.Report report;
    private Vector2 scroll;
    private string message;

    [MenuItem("FlatWorld/其他工具/美术/一次性贴图优化器")]
    public static void Open() => GetWindow<FlatWorldPixelArtTextureOptimizerWindow>("一次性贴图优化器");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("工作图缩小，高清原图留存", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("编辑器、游戏和打包共用 Assets 中的低分辨率图。仅手动执行一次，不挂构建回调，不持续运行。\n" +
            "最近邻保留像素颜色；同步 PPU 并保留 GUID、归一化锚点，短边取整误差最多半个目标像素。图标优先 256、其他单图优先 512；画质不通过时尝试较高一档。\n" +
            "原图和原 .meta 保存在项目根目录 ArtSourceBackups（不进入 Unity 资源与包体），不要删除此目录。", MessageType.Info);
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer;
        if (busy) EditorGUILayout.HelpBox("当前正在运行、编译或打包：这里只查看，不中断 Unity。", MessageType.Warning);
        using (new EditorGUI.DisabledScope(busy))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("1. 只读扫描")) Run(() => FlatWorldPixelArtTextureOptimizer.Preview());
                using (new EditorGUI.DisabledScope(report == null || report.operation != "preview" || report.cancelled || report.acceptedCount == 0))
                    if (GUILayout.Button("2. 备份并压缩一次") && EditorUtility.DisplayDialog("生成低分辨率工作图",
                            $"处理通过检查的 {report.acceptedCount} 张贴图。先逐张备份高清原图与导入设置，再替换工作图。\n备份失败不会缩图；保留引用，世界尺寸仅容许像素取整误差。", "开始", "取消"))
                        Run(() => FlatWorldPixelArtTextureOptimizer.Apply(report));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("恢复 Project 选中图片") && ConfirmRestore())
                    Run(() => FlatWorldPixelArtTextureOptimizer.Restore(true));
                if (GUILayout.Button("恢复所有最近备份") && ConfirmRestore())
                    Run(() => FlatWorldPixelArtTextureOptimizer.Restore(false));
            }
        }
        if (GUILayout.Button("打开高清原图备份目录"))
        {
            Directory.CreateDirectory(FlatWorldPixelArtTextureOptimizer.BackupRoot);
            EditorUtility.RevealInFinder(Path.GetFullPath(FlatWorldPixelArtTextureOptimizer.BackupRoot));
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);
        if (report == null) return;
        EditorGUILayout.LabelField($"检查 {report.inspectedCount} 张 ｜ 可压缩 {report.acceptedCount} 张 ｜ 已处理 {report.changedCount} 张 ｜ 错误 {report.errors.Count} 条");
        if (report.cancelled) EditorGUILayout.HelpBox("操作已取消；此前完成的图片和备份保留。", MessageType.Warning);
        long before = report.entries.Where(entry => entry.optimizedBytes > 0).Sum(entry => entry.originalBytes);
        long after = report.entries.Sum(entry => entry.optimizedBytes);
        if (before > 0) EditorGUILayout.LabelField($"本次 PNG 源文件：{before / 1048576d:F2} → {after / 1048576d:F2} MB（不等同最终包体）");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string error in report.errors) EditorGUILayout.HelpBox(error, MessageType.Error);
        foreach (FlatWorldPixelArtTextureOptimizer.Entry entry in report.entries)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位", GUILayout.Width(42)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(entry.path));
                EditorGUILayout.LabelField($"{Path.GetFileName(entry.path)}  {entry.originalWidth}×{entry.originalHeight} → {entry.width}×{entry.height}\n{entry.status}", GUILayout.Height(36));
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static bool ConfirmRestore() => EditorUtility.DisplayDialog("恢复高清工作图",
        "恢复图片和原导入设置，备份继续保留。压缩后又被编辑的图片会跳过，避免覆盖新改动。", "恢复", "取消");

    private void Run(Func<FlatWorldPixelArtTextureOptimizer.Report> operation)
    {
        try { message = null; report = operation(); }
        catch (Exception error) { message = error.Message; Debug.LogException(error); }
        Repaint();
    }

    #endregion
}
