#if UNITY_EDITOR
using NUnit.Framework.Internal;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TESTEditor))]
public class TESTEditor : Editor
{
    public override void OnInspectorGUI()
    {
        Rect rect = GUILayoutUtility.GetRect(1, 20);
        GUI.Label(rect, "TEST (自定义组件)");

        Rect buttonRect = new Rect(rect.xMax - 100, rect.y, 90, 18);
        if (GUI.Button(buttonRect, "清理内存"))
        {
            System.GC.Collect();
            Debug.Log("已手动清理内存");
        }

        GUILayout.Space(5);
        DrawDefaultInspector();
    }
}
#endif
