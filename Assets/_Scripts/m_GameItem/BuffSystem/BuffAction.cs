using UnityEngine;
using UnityEditor.ProjectWindowCallback;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;

public static class CreateBuffActionScript
{
    [MenuItem("Assets/Create/Buff/创建 Buff Action 脚本", false, 80)]
    public static void CreateBuffActionScriptAsset()
    {
        string path = GetSelectedPathOrFallback();
        string fileName = "NewBuffAction.cs";

        string fullPath = Path.Combine(path, fileName);
        fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

        // 创建一个空文件用于重命名
        var icon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
            0,
            ScriptableObject.CreateInstance<BuffActionScriptAssetCreator>(),
            fullPath,
            icon,
            null
        );
    }

    private static string GetSelectedPathOrFallback()
    {
        string path = "Assets";

        if (Selection.activeObject != null)
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (File.Exists(selectedPath))
                    path = Path.GetDirectoryName(selectedPath);
                else
                    path = selectedPath;
            }
        }

        return path.Replace("\\", "/");
    }
}

public class BuffActionScriptAssetCreator : EndNameEditAction
{
    public override void Action(int instanceId, string pathName, string resourceFile)
    {
        string className = Path.GetFileNameWithoutExtension(pathName);

        string scriptContent =
$@"using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = ""New {className}"", menuName = ""Buff/{className}"")]
public class {className} : BuffAction
{{
    public override void Apply(BuffRunTime data)
    {{
        // TODO: Implement buff logic
        throw new System.NotImplementedException();
    }}
}}";

        File.WriteAllText(pathName, scriptContent);
        AssetDatabase.Refresh();

        var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(pathName);
        ProjectWindowUtil.ShowCreatedAsset(asset);
    }
}
#endif


public abstract class BuffAction : ScriptableObject
{
    public abstract void Apply(BuffRunTime data);
}