/*#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomPropertyDrawer(typeof(TagDictionary))]
public class TagDictionaryDrawer : PropertyDrawer
{
    private readonly Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
    private string currentPropertyPath;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        currentPropertyPath = property.propertyPath;

        EditorGUI.BeginProperty(position, label, property);
        var originalIndent = EditorGUI.indentLevel;

        // ��ȡ�������ã�����ֵ��飩
        var keysProp = property.FindPropertyRelative("keys");
        var valuesProp = property.FindPropertyRelative("values");

        // ��ȫ��ʼ����ȷ���������ݽṹ��Ч
        if (!InitializeProperties(property, ref keysProp, ref valuesProp))
        {
            EditorGUI.LabelField(position, label, "�޷���ʼ��TagDictionary����");
            EditorGUI.EndProperty();
            return;
        }

        // ȷ��keys��values���鳤��ʼ��һ��
        SyncArraySizes(keysProp, valuesProp);

        // ��������ǩ
        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, label);
        position.y += position.height + 2;

        EditorGUI.indentLevel++;

        // �������м�ֵ��
        for (int i = 0; i < keysProp.arraySize; i++)
        {
            // ˫�ذ�ȫ��飬��ֹ����Խ��
            if (i >= valuesProp.arraySize) break;

            var keyProp = keysProp.GetArrayElementAtIndex(i);
            var valueProp = valuesProp.GetArrayElementAtIndex(i);

            // ���Ƶ�����ֵ��
            position = DrawKeyValuePair(position, keyProp, valueProp, i, keysProp, valuesProp);
        }

        // ��������¼�ֵ�԰�ť
        position = DrawAddButton(position, keysProp, valuesProp);

        // �ָ���������
        EditorGUI.indentLevel = originalIndent;
        EditorGUI.EndProperty();
    }

    /// <summary>
    /// ȷ�����Ժͻ������ݽṹ����ȷ��ʼ��
    /// </summary>
    private bool InitializeProperties(SerializedProperty property, ref SerializedProperty keysProp, ref SerializedProperty valuesProp)
    {
        // ��ȷ��ȡĿ�����ʹ�÷��ͷ�����������ת�����⣩
        TagDictionary targetDict = GetTargetObjectOfProperty(property) as TagDictionary;
        bool isNewInstance = false;

        // ������󲻴��ڣ�������ʵ��
        if (targetDict == null)
        {
            targetDict = new TagDictionary();
            isNewInstance = true;

            // ��ȷ����ֵ����������ת������
            SetTargetObjectOfProperty(property, targetDict);
        }

        // ȷ�������б��ѳ�ʼ��
        if (targetDict.keys == null)
            targetDict.keys = new List<string>();

        if (targetDict.values == null)
            targetDict.values = new List<List<string>>();

        // ȷ����ֵ������ƥ��
        while (targetDict.keys.Count > targetDict.values.Count)
            targetDict.values.Add(new List<string>());

        while (targetDict.values.Count > targetDict.keys.Count)
            targetDict.keys.Add($"AutoKey_{targetDict.keys.Count}");

        // ������´�����ʵ�������һ��Ĭ�ϼ�ֵ��
        if (isNewInstance && targetDict.keys.Count == 0)
        {
            targetDict.keys.Add("DefaultKey");
            targetDict.values.Add(new List<string> { "exampleTag" });
        }

        // ���»�ȡ��������
        keysProp = property.FindPropertyRelative("keys");
        valuesProp = property.FindPropertyRelative("values");

        // ���ռ��
        return keysProp != null && valuesProp != null;
    }

    /// <summary>
    /// ȷ���������鳤��һ��
    /// </summary>
    private void SyncArraySizes(SerializedProperty keys, SerializedProperty values)
    {
        if (keys.arraySize == values.arraySize) return;

        int newSize = Mathf.Max(keys.arraySize, values.arraySize);
        keys.arraySize = newSize;
        values.arraySize = newSize;
    }

    /// <summary>
    /// ���Ƶ�����ֵ��
    /// </summary>
    private Rect DrawKeyValuePair(Rect position, SerializedProperty keyProp, SerializedProperty valueProp, int index,
                                 SerializedProperty keysProp, SerializedProperty valuesProp)
    {
        // ��ȡΨһ��ʶ�����۵�״̬
        string foldoutKey = $"{currentPropertyPath}_{index}";
        if (!foldoutStates.ContainsKey(foldoutKey))
            foldoutStates[foldoutKey] = false;

        // ���Ƽ����۵���ť
        var foldoutRect = new Rect(position.x, position.y, 16, EditorGUIUtility.singleLineHeight);
        var keyRect = new Rect(position.x + 20, position.y, 100, EditorGUIUtility.singleLineHeight);
        var removeRect = new Rect(position.x + position.width - 50, position.y, 50, EditorGUIUtility.singleLineHeight);

        // �����۵���ť
        foldoutStates[foldoutKey] = EditorGUI.Foldout(foldoutRect, foldoutStates[foldoutKey], GUIContent.none);

        // ���Ƽ��ı���
        keyProp.stringValue = EditorGUI.TextField(keyRect, keyProp.stringValue);

        // ����ɾ����ť���޸��������ô���
        if (GUI.Button(removeRect, "ɾ��"))
        {
            // ��ȷ��ɾ�������������������ϵ��ã���������SerializedObject��
            keysProp.DeleteArrayElementAtIndex(index);
            valuesProp.DeleteArrayElementAtIndex(index);
            foldoutStates.Remove(foldoutKey);
            return position; // �˳���ǰ���ƣ�������������
        }

        position.y += EditorGUIUtility.singleLineHeight + 2;

        // ���չ��������ֵ�б�
        if (foldoutStates[foldoutKey])
        {
            EditorGUI.indentLevel++;
            position = DrawStringList(position, valueProp);
            EditorGUI.indentLevel--;
        }

        return position;
    }

    /// <summary>
    /// �����ַ����б�
    /// </summary>
    private Rect DrawStringList(Rect position, SerializedProperty listProp)
    {
        // �����б����
        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, "��ǩ�б�:");
        position.y += position.height + 2;

        // �����б���
        for (int j = 0; j < listProp.arraySize; j++)
        {
            var itemProp = listProp.GetArrayElementAtIndex(j);
            var itemRect = new Rect(position.x, position.y, position.width - 60, EditorGUIUtility.singleLineHeight);
            var removeItemRect = new Rect(position.x + position.width - 50, position.y, 50, EditorGUIUtility.singleLineHeight);

            itemProp.stringValue = EditorGUI.TextField(itemRect, itemProp.stringValue);

            if (GUI.Button(removeItemRect, "�Ƴ�"))
            {
                listProp.DeleteArrayElementAtIndex(j);
                break; // �˳�ѭ����������������
            }

            position.y += EditorGUIUtility.singleLineHeight + 2;
        }

        // ������ӱ�ǩ��ť
        if (GUI.Button(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), "��ӱ�ǩ"))
        {
            listProp.InsertArrayElementAtIndex(listProp.arraySize);
            var newItem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            newItem.stringValue = "NewTag";
        }

        position.y += EditorGUIUtility.singleLineHeight + 4;
        return position;
    }

    /// <summary>
    /// ��������¼�ֵ�԰�ť
    /// </summary>
    private Rect DrawAddButton(Rect position, SerializedProperty keysProp, SerializedProperty valuesProp)
    {
        if (GUI.Button(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), "��Ӽ�ֵ��"))
        {
            // ����¼�
            keysProp.InsertArrayElementAtIndex(keysProp.arraySize);
            var newKey = keysProp.GetArrayElementAtIndex(keysProp.arraySize - 1);
            newKey.stringValue = $"Key_{keysProp.arraySize}";

            // �����ֵ�б�
            valuesProp.InsertArrayElementAtIndex(valuesProp.arraySize);
            var newValue = valuesProp.GetArrayElementAtIndex(valuesProp.arraySize - 1);
            newValue.arraySize = 0;
        }

        position.y += EditorGUIUtility.singleLineHeight + 2;
        return position;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // �����߶ȣ����� + ��Ӱ�ť��
        float height = EditorGUIUtility.singleLineHeight * 2 + 4;

        var keysProp = property.FindPropertyRelative("keys");
        var valuesProp = property.FindPropertyRelative("values");

        if (keysProp == null || valuesProp == null)
            return height;

        // ����ÿ����ֵ�Եĸ߶�
        for (int i = 0; i < keysProp.arraySize; i++)
        {
            // ���и߶�
            height += EditorGUIUtility.singleLineHeight + 2;

            // ���չ��������б����ݸ߶�
            string foldoutKey = $"{property.propertyPath}_{i}";
            if (foldoutStates.TryGetValue(foldoutKey, out bool isExpanded) && isExpanded)
            {
                // �б����߶�
                height += EditorGUIUtility.singleLineHeight + 2;

                // �б���߶�
                if (i < valuesProp.arraySize)
                {
                    var listProp = valuesProp.GetArrayElementAtIndex(i);
                    height += listProp.arraySize * (EditorGUIUtility.singleLineHeight + 2);
                }

                // ��ӱ�ǩ��ť�߶�
                height += EditorGUIUtility.singleLineHeight + 4;
            }
        }

        return height;
    }

    // ������������ȡSerializedProperty��Ŀ�����
    private object GetTargetObjectOfProperty(SerializedProperty prop)
    {
        var path = prop.propertyPath.Replace(".Array.data[", "[");
        object obj = prop.serializedObject.targetObject;
        var elements = path.Split('.');

        foreach (var element in elements)
        {
            if (element.Contains("["))
            {
                var elementName = element.Substring(0, element.IndexOf("["));
                var index = int.Parse(element.Substring(element.IndexOf("[") + 1, element.IndexOf("]") - element.IndexOf("[") - 1));
                obj = GetValue_Imp(obj, elementName, index);
            }
            else
            {
                obj = GetValue_Imp(obj, element);
            }
        }

        return obj;
    }

    // ��������������SerializedProperty��Ŀ�����
    private void SetTargetObjectOfProperty(SerializedProperty prop, object value)
    {
        var path = prop.propertyPath.Replace(".Array.data[", "[");
        object obj = prop.serializedObject.targetObject;
        var elements = path.Split('.');

        for (int i = 0; i < elements.Length - 1; i++)
        {
            var element = elements[i];
            if (element.Contains("["))
            {
                var elementName = element.Substring(0, element.IndexOf("["));
                var index = int.Parse(element.Substring(element.IndexOf("[") + 1, element.IndexOf("]") - element.IndexOf("[") - 1));
                obj = GetValue_Imp(obj, elementName, index);
            }
            else
            {
                obj = GetValue_Imp(obj, element);
            }
        }

        var elementNameLast = elements[elements.Length - 1];
        SetValue_Imp(obj, elementNameLast, value);
    }

    private object GetValue_Imp(object source, string name)
    {
        if (source == null)
            return null;

        var type = source.GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field != null)
            return field.GetValue(source);

        var property = type.GetProperty(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property != null)
            return property.GetValue(source, null);

        return null;
    }

    private object GetValue_Imp(object source, string name, int index)
    {
        var enumerable = GetValue_Imp(source, name) as System.Collections.IEnumerable;
        if (enumerable == null)
            return null;

        var enumerator = enumerable.GetEnumerator();
        for (int i = 0; i <= index; i++)
        {
            if (!enumerator.MoveNext())
                return null;
        }

        return enumerator.Current;
    }

    private void SetValue_Imp(object source, string name, object value)
    {
        if (source == null)
            return;

        var type = source.GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(source, value);
            return;
        }

        var property = type.GetProperty(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property != null)
        {
            property.SetValue(source, value, null);
            return;
        }
    }
}
#endif
*/