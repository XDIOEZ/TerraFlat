#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CodeLineCounter : EditorWindow
{
    private DefaultAsset targetFolder; // 拖拽的文件夹
    private int totalLineCount = 0; // 总行数（所有行）
    private int totalValidLineCount = 0; // 总有效代码行数
    private int fileCount = 0;
    private int maxLineCount = 0;
    private int minLineCount = int.MaxValue;
    private float averageLineCount = 0f;
    private float medianLineCount = 0f;
    private string maxLineFileName = "";
    private string minLineFileName = "";
    private float averageTotalLineCountPerScript = 0f; // 平均每个脚本的总行数（包括空行和注释行）
    private System.Collections.Generic.List<string> scriptsBelowCustomLimit = new System.Collections.Generic.List<string>(); // 存储小于自定义行数的脚本文件名
    private float lineLimit = 10f; // 用户自定义的小于行数下限，默认为10行
    private float fontSize = 12f; // 用户自定义的字体大小

    [MenuItem("Tools/统计有效代码行数")]
    public static void OpenWindow()
    {
        GetWindow<CodeLineCounter>("有效代码行数统计器");
    }

    private void OnGUI()
    {
        GUILayout.Label("统计特定文件夹下有效代码行数", EditorStyles.boldLabel);

        GUILayout.Space(10);

        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("目标文件夹", targetFolder, typeof(DefaultAsset), false);

        GUILayout.Space(10);

        // 滑动条控制有效代码行数下限
        GUILayout.Label($"设置小于此行数的脚本显示：{lineLimit} 行");
        lineLimit = EditorGUILayout.Slider(lineLimit, 1f, 50f); // 设置滑动条范围为1到50行

        GUILayout.Space(10);

        // 滑动条控制输出文本的字体大小
        GUILayout.Label($"设置输出文本的字体大小：{fontSize:F0}");
        fontSize = EditorGUILayout.Slider(fontSize, 10f, 30f); // 设置字体大小的滑动条范围为10到30

        GUILayout.Space(10);

        if (GUILayout.Button("开始统计"))
        {
            if (targetFolder == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择一个目标文件夹！", "确定");
                return;
            }

            string path = AssetDatabase.GetAssetPath(targetFolder);

            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("错误", $"选择的路径无效: {path}", "确定");
                return;
            }

            CountLinesInFolder(path);
        }

        GUILayout.Space(20);

        GUILayout.Label($"总文件数：{fileCount}");
        GUILayout.Label($"总代码行数：{totalLineCount}（包括空行和注释行）");
        GUILayout.Label($"总有效代码行数：{totalValidLineCount}");
        if (fileCount > 0)
        {
            GUILayout.Label($"最大有效代码行数：{maxLineCount} （{maxLineFileName}）");
            GUILayout.Label($"最小有效代码行数：{minLineCount} （{minLineFileName}）");
            GUILayout.Label($"有效代码行数中位数：{medianLineCount}");
            GUILayout.Label($"平均有效代码行数：{averageLineCount:F2}");
            GUILayout.Label($"平均每个脚本的总代码行数：{averageTotalLineCountPerScript:F2}");
        }

        GUILayout.Space(10);

        // 新增的按钮：输出所有小于自定义行数的脚本
        if (GUILayout.Button("输出所有小于自定义行数的脚本"))
        {
            OutputScriptsBelowCustomLimit();
        }

        // 显示所有符合条件的脚本
        if (scriptsBelowCustomLimit.Count > 0)
        {
            GUILayout.Label($"以下脚本的有效代码行数小于 {lineLimit} 行：", EditorStyles.boldLabel);
            GUIStyle textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(fontSize) // 设置字体大小
            };
            foreach (var script in scriptsBelowCustomLimit)
            {
                GUILayout.Label(script, textStyle);
            }
        }
        else
        {
            GUILayout.Label("没有脚本的有效代码行数小于设置的行数。", EditorStyles.boldLabel);
        }
    }

    private void CountLinesInFolder(string path)
    {
        totalLineCount = 0;
        totalValidLineCount = 0;
        fileCount = 0;
        maxLineCount = 0;
        minLineCount = int.MaxValue;
        averageLineCount = 0f;
        medianLineCount = 0f;
        averageTotalLineCountPerScript = 0f;
        maxLineFileName = "";
        minLineFileName = "";
        scriptsBelowCustomLimit.Clear(); // 清空上次统计的结果

        string[] files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
        var validLineCounts = new System.Collections.Generic.List<int>();
        var totalLineCounts = new System.Collections.Generic.List<int>(); // 用于计算每个脚本的总代码行数

        foreach (var file in files)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int validLineCount = CountValidLines(lines);
                int totalFileLineCount = lines.Length; // 所有行数（包括空行和注释行）
                totalLineCount += totalFileLineCount; // 加到总代码行数
                totalValidLineCount += validLineCount; // 总有效代码行数
                fileCount++;

                // 更新最大有效行数
                if (validLineCount > maxLineCount)
                {
                    maxLineCount = validLineCount;
                    maxLineFileName = Path.GetFileName(file);
                }

                // 更新最小有效行数
                if (validLineCount < minLineCount)
                {
                    minLineCount = validLineCount;
                    minLineFileName = Path.GetFileName(file);
                }

                validLineCounts.Add(validLineCount); // 收集有效行数
                totalLineCounts.Add(totalFileLineCount); // 收集总行数
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("错误", $"读取文件失败: {file}\n错误信息: {ex.Message}", "确定");
            }
        }

        // 计算平均行数和中位数
        if (fileCount > 0)
        {
            averageLineCount = (float)totalValidLineCount / fileCount;
            validLineCounts.Sort();

            // 计算中位数
            if (validLineCounts.Count % 2 == 0)
            {
                medianLineCount = (validLineCounts[validLineCounts.Count / 2 - 1] + validLineCounts[validLineCounts.Count / 2]) / 2f;
            }
            else
            {
                medianLineCount = validLineCounts[validLineCounts.Count / 2];
            }

            // 计算每个脚本的平均有效代码行数
            averageTotalLineCountPerScript = (float)totalLineCount / fileCount;
        }

        EditorUtility.DisplayDialog("统计完成", $"统计结果已完成！共统计了 {fileCount} 个脚本", "确定");
    }

    // 统计有效代码行数（排除空行和注释行）
    private int CountValidLines(string[] lines)
    {
        int validLineCount = 0;
        bool isInMultilineComment = false;

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();

            // 检查多行注释块的开始和结束
            if (isInMultilineComment)
            {
                if (trimmedLine.Contains("*/"))
                {
                    isInMultilineComment = false;
                }
                continue; // 继续跳过该行
            }

            // 如果是多行注释开始，标记进入多行注释块
            if (trimmedLine.Contains("/*"))
            {
                isInMultilineComment = true;
                continue;
            }

            // 跳过空行或单行注释
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
            {
                continue;
            }

            // 如果不是注释行或者空行，则有效行数+1
            validLineCount++;
        }

        return validLineCount;
    }

    // 输出所有有效代码行数小于自定义行数的脚本
    private void OutputScriptsBelowCustomLimit()
    {
        if (fileCount <= 0)
        {
            EditorUtility.DisplayDialog("提示", "没有找到任何脚本！", "确定");
            return;
        }

        scriptsBelowCustomLimit.Clear(); // 清空旧数据

        string[] files = Directory.GetFiles(AssetDatabase.GetAssetPath(targetFolder), "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int validLineCount = CountValidLines(lines);
                if (validLineCount < lineLimit)
                {
                    scriptsBelowCustomLimit.Add(Path.GetFileName(file));
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("错误", $"读取文件失败: {file}\n错误信息: {ex.Message}", "确定");
            }
        }

        // 在窗口中显示所有符合条件的脚本
        Repaint();
    }
}
#endif
