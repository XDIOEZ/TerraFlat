# if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class ConflictResolverFinal : EditorWindow
{
    private List<TextAsset> selectedFiles = new List<TextAsset>();
    private Vector2 scrollPos;
    private string fileContent = "";
    private string filePath = "";
    private List<ConflictBlock> conflictBlocks = new List<ConflictBlock>();
    private int currentConflictIndex = 0;

    private class ConflictBlock
    {
        public List<string> upperPart = new List<string>();
        public List<string> lowerPart = new List<string>();
        public string chosen = "upper"; // "upper" or "lower"
    }

    [MenuItem("Tools/冲突解决工具 Final版")]
    public static void ShowWindow()
    {
        GetWindow<ConflictResolverFinal>("冲突解决工具 Final版");
    }

    private void OnGUI()
    {
        GUILayout.Label("拖拽有冲突的 C# 文件到这里", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "拖拽文件到此区域", EditorStyles.helpBox);

        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is TextAsset asset)
                        {
                            selectedFiles.Add(asset);
                        }
                    }
                }
            }
        }

        if (selectedFiles.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("已选择文件：", EditorStyles.boldLabel);

            foreach (var file in selectedFiles)
            {
                GUILayout.Label(file.name);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("载入第一个文件处理"))
            {
                LoadFile(selectedFiles[0]);
            }

            if (GUILayout.Button("批量处理所有文件（默认保留上方）"))
            {
                BatchProcessAllFiles(true);
            }


            if (GUILayout.Button("批量处理所有文件（默认保留下方）"))
            {
                BatchProcessAllFiles(false);
            }
        }
        if (conflictBlocks != null && conflictBlocks.Count > 0)
        {
            GUILayout.Space(20);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label($"冲突 {currentConflictIndex + 1}/{conflictBlocks.Count}", EditorStyles.boldLabel);

            var block = conflictBlocks[currentConflictIndex];

            GUILayout.Label("选择保留部分：", EditorStyles.label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(block.chosen == "upper", "保留上方 (Updated)", "Button"))
            {
                block.chosen = "upper";
            }
            if (GUILayout.Toggle(block.chosen == "lower", "保留下方 (Stashed)", "Button"))
            {
                block.chosen = "lower";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("上方内容：", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(string.Join("\n", block.upperPart), GUILayout.Height(100));

            GUILayout.Label("下方内容：", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(string.Join("\n", block.lowerPart), GUILayout.Height(100));

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一个冲突") && currentConflictIndex > 0)
            {
                currentConflictIndex--;
            }
            if (GUILayout.Button("下一个冲突") && currentConflictIndex < conflictBlocks.Count - 1)
            {
                currentConflictIndex++;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            if (GUILayout.Button("应用当前文件的修改并保存"))
            {
                ApplyChanges();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void LoadFile(TextAsset asset)
    {
        filePath = AssetDatabase.GetAssetPath(asset);
        fileContent = File.ReadAllText(filePath);

        conflictBlocks.Clear();
        currentConflictIndex = 0;

        var lines = new List<string>(fileContent.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None));
        bool insideConflict = false;
        bool upperPart = true;
        ConflictBlock currentBlock = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("<<<<<<<"))
            {
                insideConflict = true;
                upperPart = true;
                currentBlock = new ConflictBlock();
            }
            else if (line.StartsWith("=======") && insideConflict)
            {
                upperPart = false;
            }
            else if (line.StartsWith(">>>>>>>") && insideConflict)
            {
                insideConflict = false;
                conflictBlocks.Add(currentBlock);
                currentBlock = null;
            }
            else if (insideConflict && currentBlock != null)
            {
                if (upperPart)
                    currentBlock.upperPart.Add(line);
                else
                    currentBlock.lowerPart.Add(line);
            }
        }

        if (conflictBlocks.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", $"文件 {asset.name} 没有检测到冲突标记！", "好的");
        }
    }

    private void ApplyChanges()
    {
        var lines = new List<string>(fileContent.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None));
        List<string> output = new List<string>();

        int blockIndex = 0;
        bool insideConflict = false;
        bool upperPart = true;

        foreach (var line in lines)
        {
            if (line.StartsWith("<<<<<<<"))
            {
                insideConflict = true;
                upperPart = true;
            }
            else if (line.StartsWith("=======") && insideConflict)
            {
                upperPart = false;
            }
            else if (line.StartsWith(">>>>>>>") && insideConflict)
            {
                // 这里在冲突结束时插入选中的内容
                insideConflict = false;
                if (blockIndex < conflictBlocks.Count)
                {
                    var chosen = conflictBlocks[blockIndex].chosen;
                    var chosenPart = chosen == "upper" ? conflictBlocks[blockIndex].upperPart : conflictBlocks[blockIndex].lowerPart;
                    output.AddRange(chosenPart);
                }
                blockIndex++;
            }
            else if (insideConflict)
            {
                // 在冲突块内部什么都不做（跳过）
            }
            else
            {
                output.Add(line);
            }
        }

        File.WriteAllText(filePath, string.Join("\n", output));
        AssetDatabase.Refresh();
        Debug.Log($"冲突解决完成: {filePath}");
    }


    private void BatchProcessAllFiles(bool keepUpper)
    {
        foreach (var asset in selectedFiles)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string content = File.ReadAllText(path);

            var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None));
            List<string> output = new List<string>();

            bool insideConflict = false;
            bool upperPart = true;
            List<string> tempUpper = new List<string>();
            List<string> tempLower = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("<<<<<<<"))
                {
                    insideConflict = true;
                    upperPart = true;
                    tempUpper.Clear();
                    tempLower.Clear();
                }
                else if (line.StartsWith("=======") && insideConflict)
                {
                    upperPart = false;
                }
                else if (line.StartsWith(">>>>>>>") && insideConflict)
                {
                    insideConflict = false;
                    output.AddRange(keepUpper ? tempUpper : tempLower);
                }
                else if (insideConflict)
                {
                    if (upperPart)
                        tempUpper.Add(line);
                    else
                        tempLower.Add(line);
                }
                else
                {
                    output.Add(line);
                }
            }

            File.WriteAllText(path, string.Join("\n", output));
        }

        AssetDatabase.Refresh();
        Debug.Log("批量冲突处理完成！");
    }
}
# endif
