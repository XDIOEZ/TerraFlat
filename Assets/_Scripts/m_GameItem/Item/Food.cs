#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CodeLineCounter : EditorWindow
{
    private DefaultAsset targetFolder; // ��ק���ļ���
    private int totalLineCount = 0; // �������������У�
    private int totalValidLineCount = 0; // ����Ч��������
    private int fileCount = 0;
    private int maxLineCount = 0;
    private int minLineCount = int.MaxValue;
    private float averageLineCount = 0f;
    private float medianLineCount = 0f;
    private string maxLineFileName = "";
    private string minLineFileName = "";
    private float averageTotalLineCountPerScript = 0f; // ƽ��ÿ���ű������������������к�ע���У�
    private System.Collections.Generic.List<string> scriptsBelowCustomLimit = new System.Collections.Generic.List<string>(); // �洢С���Զ��������Ľű��ļ���
    private float lineLimit = 10f; // �û��Զ����С���������ޣ�Ĭ��Ϊ10��
    private float fontSize = 12f; // �û��Զ���������С

    [MenuItem("Tools/ͳ����Ч��������")]
    public static void OpenWindow()
    {
        GetWindow<CodeLineCounter>("��Ч��������ͳ����");
    }

    private void OnGUI()
    {
        GUILayout.Label("ͳ���ض��ļ�������Ч��������", EditorStyles.boldLabel);

        GUILayout.Space(10);

        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Ŀ���ļ���", targetFolder, typeof(DefaultAsset), false);

        GUILayout.Space(10);

        // ������������Ч������������
        GUILayout.Label($"����С�ڴ������Ľű���ʾ��{lineLimit} ��");
        lineLimit = EditorGUILayout.Slider(lineLimit, 1f, 50f); // ���û�������ΧΪ1��50��

        GUILayout.Space(10);

        // ��������������ı��������С
        GUILayout.Label($"��������ı��������С��{fontSize:F0}");
        fontSize = EditorGUILayout.Slider(fontSize, 10f, 30f); // ���������С�Ļ�������ΧΪ10��30

        GUILayout.Space(10);

        if (GUILayout.Button("��ʼͳ��"))
        {
            if (targetFolder == null)
            {
                EditorUtility.DisplayDialog("����", "����ѡ��һ��Ŀ���ļ��У�", "ȷ��");
                return;
            }

            string path = AssetDatabase.GetAssetPath(targetFolder);

            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("����", $"ѡ���·����Ч: {path}", "ȷ��");
                return;
            }

            CountLinesInFolder(path);
        }

        GUILayout.Space(20);

        GUILayout.Label($"���ļ�����{fileCount}");
        GUILayout.Label($"�ܴ���������{totalLineCount}���������к�ע���У�");
        GUILayout.Label($"����Ч����������{totalValidLineCount}");
        if (fileCount > 0)
        {
            GUILayout.Label($"�����Ч����������{maxLineCount} ��{maxLineFileName}��");
            GUILayout.Label($"��С��Ч����������{minLineCount} ��{minLineFileName}��");
            GUILayout.Label($"��Ч����������λ����{medianLineCount}");
            GUILayout.Label($"ƽ����Ч����������{averageLineCount:F2}");
            GUILayout.Label($"ƽ��ÿ���ű����ܴ���������{averageTotalLineCountPerScript:F2}");
        }

        GUILayout.Space(10);

        // �����İ�ť���������С���Զ��������Ľű�
        if (GUILayout.Button("�������С���Զ��������Ľű�"))
        {
            OutputScriptsBelowCustomLimit();
        }

        // ��ʾ���з��������Ľű�
        if (scriptsBelowCustomLimit.Count > 0)
        {
            GUILayout.Label($"���½ű�����Ч��������С�� {lineLimit} �У�", EditorStyles.boldLabel);
            GUIStyle textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(fontSize) // ���������С
            };
            foreach (var script in scriptsBelowCustomLimit)
            {
                GUILayout.Label(script, textStyle);
            }
        }
        else
        {
            GUILayout.Label("û�нű�����Ч��������С�����õ�������", EditorStyles.boldLabel);
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
        scriptsBelowCustomLimit.Clear(); // ����ϴ�ͳ�ƵĽ��

        string[] files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
        var validLineCounts = new System.Collections.Generic.List<int>();
        var totalLineCounts = new System.Collections.Generic.List<int>(); // ���ڼ���ÿ���ű����ܴ�������

        foreach (var file in files)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int validLineCount = CountValidLines(lines);
                int totalFileLineCount = lines.Length; // �����������������к�ע���У�
                totalLineCount += totalFileLineCount; // �ӵ��ܴ�������
                totalValidLineCount += validLineCount; // ����Ч��������
                fileCount++;

                // ���������Ч����
                if (validLineCount > maxLineCount)
                {
                    maxLineCount = validLineCount;
                    maxLineFileName = Path.GetFileName(file);
                }

                // ������С��Ч����
                if (validLineCount < minLineCount)
                {
                    minLineCount = validLineCount;
                    minLineFileName = Path.GetFileName(file);
                }

                validLineCounts.Add(validLineCount); // �ռ���Ч����
                totalLineCounts.Add(totalFileLineCount); // �ռ�������
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("����", $"��ȡ�ļ�ʧ��: {file}\n������Ϣ: {ex.Message}", "ȷ��");
            }
        }

        // ����ƽ����������λ��
        if (fileCount > 0)
        {
            averageLineCount = (float)totalValidLineCount / fileCount;
            validLineCounts.Sort();

            // ������λ��
            if (validLineCounts.Count % 2 == 0)
            {
                medianLineCount = (validLineCounts[validLineCounts.Count / 2 - 1] + validLineCounts[validLineCounts.Count / 2]) / 2f;
            }
            else
            {
                medianLineCount = validLineCounts[validLineCounts.Count / 2];
            }

            // ����ÿ���ű���ƽ����Ч��������
            averageTotalLineCountPerScript = (float)totalLineCount / fileCount;
        }

        EditorUtility.DisplayDialog("ͳ�����", $"ͳ�ƽ������ɣ���ͳ���� {fileCount} ���ű�", "ȷ��");
    }

    // ͳ����Ч�����������ų����к�ע���У�
    private int CountValidLines(string[] lines)
    {
        int validLineCount = 0;
        bool isInMultilineComment = false;

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();

            // ������ע�Ϳ�Ŀ�ʼ�ͽ���
            if (isInMultilineComment)
            {
                if (trimmedLine.Contains("*/"))
                {
                    isInMultilineComment = false;
                }
                continue; // ������������
            }

            // ����Ƕ���ע�Ϳ�ʼ����ǽ������ע�Ϳ�
            if (trimmedLine.Contains("/*"))
            {
                isInMultilineComment = true;
                continue;
            }

            // �������л���ע��
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
            {
                continue;
            }

            // �������ע���л��߿��У�����Ч����+1
            validLineCount++;
        }

        return validLineCount;
    }

    // ���������Ч��������С���Զ��������Ľű�
    private void OutputScriptsBelowCustomLimit()
    {
        if (fileCount <= 0)
        {
            EditorUtility.DisplayDialog("��ʾ", "û���ҵ��κνű���", "ȷ��");
            return;
        }

        scriptsBelowCustomLimit.Clear(); // ��վ�����

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
                EditorUtility.DisplayDialog("����", $"��ȡ�ļ�ʧ��: {file}\n������Ϣ: {ex.Message}", "ȷ��");
            }
        }

        // �ڴ�������ʾ���з��������Ľű�
        Repaint();
    }
}
#endif
