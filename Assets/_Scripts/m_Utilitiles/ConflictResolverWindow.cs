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

    [MenuItem("Tools/��ͻ������� Final��")]
    public static void ShowWindow()
    {
        GetWindow<ConflictResolverFinal>("��ͻ������� Final��");
    }

    private void OnGUI()
    {
        GUILayout.Label("��ק�г�ͻ�� C# �ļ�������", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "��ק�ļ���������", EditorStyles.helpBox);

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
            GUILayout.Label("��ѡ���ļ���", EditorStyles.boldLabel);

            foreach (var file in selectedFiles)
            {
                GUILayout.Label(file.name);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("�����һ���ļ�����"))
            {
                LoadFile(selectedFiles[0]);
            }

            if (GUILayout.Button("�������������ļ���Ĭ�ϱ����Ϸ���"))
            {
                BatchProcessAllFiles(true);
            }


            if (GUILayout.Button("�������������ļ���Ĭ�ϱ����·���"))
            {
                BatchProcessAllFiles(false);
            }
        }
        if (conflictBlocks != null && conflictBlocks.Count > 0)
        {
            GUILayout.Space(20);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label($"��ͻ {currentConflictIndex + 1}/{conflictBlocks.Count}", EditorStyles.boldLabel);

            var block = conflictBlocks[currentConflictIndex];

            GUILayout.Label("ѡ�������֣�", EditorStyles.label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(block.chosen == "upper", "�����Ϸ� (Updated)", "Button"))
            {
                block.chosen = "upper";
            }
            if (GUILayout.Toggle(block.chosen == "lower", "�����·� (Stashed)", "Button"))
            {
                block.chosen = "lower";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("�Ϸ����ݣ�", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(string.Join("\n", block.upperPart), GUILayout.Height(100));

            GUILayout.Label("�·����ݣ�", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(string.Join("\n", block.lowerPart), GUILayout.Height(100));

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("��һ����ͻ") && currentConflictIndex > 0)
            {
                currentConflictIndex--;
            }
            if (GUILayout.Button("��һ����ͻ") && currentConflictIndex < conflictBlocks.Count - 1)
            {
                currentConflictIndex++;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            if (GUILayout.Button("Ӧ�õ�ǰ�ļ����޸Ĳ�����"))
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
            EditorUtility.DisplayDialog("��ʾ", $"�ļ� {asset.name} û�м�⵽��ͻ��ǣ�", "�õ�");
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
                // �����ڳ�ͻ����ʱ����ѡ�е�����
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
                // �ڳ�ͻ���ڲ�ʲô��������������
            }
            else
            {
                output.Add(line);
            }
        }

        File.WriteAllText(filePath, string.Join("\n", output));
        AssetDatabase.Refresh();
        Debug.Log($"��ͻ������: {filePath}");
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
        Debug.Log("������ͻ������ɣ�");
    }
}
# endif
