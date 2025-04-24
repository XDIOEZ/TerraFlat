using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace TheKiwiCoder.EditorTools
{
    public class BTNodeCleaner : EditorWindow
    {

        [MenuItem("TheKiwiCoder/������Ч�ڵ�")]
        public static void CleanBrokenNodes()
        {
            string[] guids = AssetDatabase.FindAssets("t:BehaviourTree");

            int totalFixed = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BehaviourTree tree = AssetDatabase.LoadAssetAtPath<BehaviourTree>(path);

                if (tree == null) continue;

                int before = tree.nodes.Count;
                tree.nodes.RemoveAll(n => n == null);
                int after = tree.nodes.Count;

                if (before != after)
                {
                    EditorUtility.SetDirty(tree);
                    Debug.Log($"[BTNodeCleaner] �����ļ� {path}��ɾ���� {before - after} ����Ч�ڵ����á�");
                    totalFixed += before - after;
                }
            }

            AssetDatabase.SaveAssets();

            if (totalFixed > 0)
            {
                EditorUtility.DisplayDialog("�������", $"������ {totalFixed} ����ʧ���õĽڵ㡣", "�õ�");
            }
            else
            {
                EditorUtility.DisplayDialog("�������Ҫ", "������Ϊ���ڵ��Ϊ��Ч���á�", "����");
            }
        }
    }
}
