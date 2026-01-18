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

        // 获取属性引用（带空值检查）
        var keysProp = property.FindPropertyRelative("keys");
        var valuesProp = property.FindPropertyRelative("values");

        // 安全初始化：确保基础数据结构有效
        if (!InitializeProperties(property, ref keysProp, ref valuesProp))
        {
            EditorGUI.LabelField(position, label, "无法初始化TagDictionary数据");
            EditorGUI.EndProperty();
            return;
        }

        // 确保keys和values数组长度始终一致
        SyncArraySizes(keysProp, valuesProp);

        // 绘制主标签
        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, label);
        position.y += position.height + 2;

        EditorGUI.indentLevel++;

        // 绘制所有键值对
        for (int i = 0; i < keysProp.arraySize; i++)
        {
            // 双重安全检查，防止数组越界
            if (i >= valuesProp.arraySize) break;

            var keyProp = keysProp.GetArrayElementAtIndex(i);
            var valueProp = valuesProp.GetArrayElementAtIndex(i);

            // 绘制单个键值对
            position = DrawKeyValuePair(position, keyProp, valueProp, i, keysProp, valuesProp);
        }

        // 绘制添加新键值对按钮
        position = DrawAddButton(position, keysProp, valuesProp);

        // 恢复缩进级别
        EditorGUI.indentLevel = originalIndent;
        EditorGUI.EndProperty();
    }

    /// <summary>
    /// 确保属性和基础数据结构被正确初始化
    /// </summary>
    private bool InitializeProperties(SerializedProperty property, ref SerializedProperty keysProp, ref SerializedProperty valuesProp)
    {
        // 正确获取目标对象（使用泛型方法避免类型转换问题）
        TagDictionary targetDict = GetTargetObjectOfProperty(property) as TagDictionary;
        bool isNewInstance = false;

        // 如果对象不存在，创建新实例
        if (targetDict == null)
        {
            targetDict = new TagDictionary();
            isNewInstance = true;

            // 正确设置值，避免类型转换错误
            SetTargetObjectOfProperty(property, targetDict);
        }

        // 确保基础列表已初始化
        if (targetDict.keys == null)
            targetDict.keys = new List<string>();

        if (targetDict.values == null)
            targetDict.values = new List<List<string>>();

        // 确保键值对数量匹配
        while (targetDict.keys.Count > targetDict.values.Count)
            targetDict.values.Add(new List<string>());

        while (targetDict.values.Count > targetDict.keys.Count)
            targetDict.keys.Add($"AutoKey_{targetDict.keys.Count}");

        // 如果是新创建的实例，添加一个默认键值对
        if (isNewInstance && targetDict.keys.Count == 0)
        {
            targetDict.keys.Add("DefaultKey");
            targetDict.values.Add(new List<string> { "exampleTag" });
        }

        // 重新获取属性引用
        keysProp = property.FindPropertyRelative("keys");
        valuesProp = property.FindPropertyRelative("values");

        // 最终检查
        return keysProp != null && valuesProp != null;
    }

    /// <summary>
    /// 确保两个数组长度一致
    /// </summary>
    private void SyncArraySizes(SerializedProperty keys, SerializedProperty values)
    {
        if (keys.arraySize == values.arraySize) return;

        int newSize = Mathf.Max(keys.arraySize, values.arraySize);
        keys.arraySize = newSize;
        values.arraySize = newSize;
    }

    /// <summary>
    /// 绘制单个键值对
    /// </summary>
    private Rect DrawKeyValuePair(Rect position, SerializedProperty keyProp, SerializedProperty valueProp, int index,
                                 SerializedProperty keysProp, SerializedProperty valuesProp)
    {
        // 获取唯一标识用于折叠状态
        string foldoutKey = $"{currentPropertyPath}_{index}";
        if (!foldoutStates.ContainsKey(foldoutKey))
            foldoutStates[foldoutKey] = false;

        // 绘制键和折叠按钮
        var foldoutRect = new Rect(position.x, position.y, 16, EditorGUIUtility.singleLineHeight);
        var keyRect = new Rect(position.x + 20, position.y, 100, EditorGUIUtility.singleLineHeight);
        var removeRect = new Rect(position.x + position.width - 50, position.y, 50, EditorGUIUtility.singleLineHeight);

        // 绘制折叠按钮
        foldoutStates[foldoutKey] = EditorGUI.Foldout(foldoutRect, foldoutStates[foldoutKey], GUIContent.none);

        // 绘制键文本框
        keyProp.stringValue = EditorGUI.TextField(keyRect, keyProp.stringValue);

        // 绘制删除按钮（修复方法调用错误）
        if (GUI.Button(removeRect, "删除"))
        {
            // 正确的删除方法：在数组属性上调用，而不是在SerializedObject上
            keysProp.DeleteArrayElementAtIndex(index);
            valuesProp.DeleteArrayElementAtIndex(index);
            foldoutStates.Remove(foldoutKey);
            return position; // 退出当前绘制，避免索引问题
        }

        position.y += EditorGUIUtility.singleLineHeight + 2;

        // 如果展开，绘制值列表
        if (foldoutStates[foldoutKey])
        {
            EditorGUI.indentLevel++;
            position = DrawStringList(position, valueProp);
            EditorGUI.indentLevel--;
        }

        return position;
    }

    /// <summary>
    /// 绘制字符串列表
    /// </summary>
    private Rect DrawStringList(Rect position, SerializedProperty listProp)
    {
        // 绘制列表标题
        position.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(position, "标签列表:");
        position.y += position.height + 2;

        // 绘制列表项
        for (int j = 0; j < listProp.arraySize; j++)
        {
            var itemProp = listProp.GetArrayElementAtIndex(j);
            var itemRect = new Rect(position.x, position.y, position.width - 60, EditorGUIUtility.singleLineHeight);
            var removeItemRect = new Rect(position.x + position.width - 50, position.y, 50, EditorGUIUtility.singleLineHeight);

            itemProp.stringValue = EditorGUI.TextField(itemRect, itemProp.stringValue);

            if (GUI.Button(removeItemRect, "移除"))
            {
                listProp.DeleteArrayElementAtIndex(j);
                break; // 退出循环，避免索引问题
            }

            position.y += EditorGUIUtility.singleLineHeight + 2;
        }

        // 绘制添加标签按钮
        if (GUI.Button(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), "添加标签"))
        {
            listProp.InsertArrayElementAtIndex(listProp.arraySize);
            var newItem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            newItem.stringValue = "NewTag";
        }

        position.y += EditorGUIUtility.singleLineHeight + 4;
        return position;
    }

    /// <summary>
    /// 绘制添加新键值对按钮
    /// </summary>
    private Rect DrawAddButton(Rect position, SerializedProperty keysProp, SerializedProperty valuesProp)
    {
        if (GUI.Button(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), "添加键值对"))
        {
            // 添加新键
            keysProp.InsertArrayElementAtIndex(keysProp.arraySize);
            var newKey = keysProp.GetArrayElementAtIndex(keysProp.arraySize - 1);
            newKey.stringValue = $"Key_{keysProp.arraySize}";

            // 添加新值列表
            valuesProp.InsertArrayElementAtIndex(valuesProp.arraySize);
            var newValue = valuesProp.GetArrayElementAtIndex(valuesProp.arraySize - 1);
            newValue.arraySize = 0;
        }

        position.y += EditorGUIUtility.singleLineHeight + 2;
        return position;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 基础高度（标题 + 添加按钮）
        float height = EditorGUIUtility.singleLineHeight * 2 + 4;

        var keysProp = property.FindPropertyRelative("keys");
        var valuesProp = property.FindPropertyRelative("values");

        if (keysProp == null || valuesProp == null)
            return height;

        // 计算每个键值对的高度
        for (int i = 0; i < keysProp.arraySize; i++)
        {
            // 键行高度
            height += EditorGUIUtility.singleLineHeight + 2;

            // 如果展开，添加列表内容高度
            string foldoutKey = $"{property.propertyPath}_{i}";
            if (foldoutStates.TryGetValue(foldoutKey, out bool isExpanded) && isExpanded)
            {
                // 列表标题高度
                height += EditorGUIUtility.singleLineHeight + 2;

                // 列表项高度
                if (i < valuesProp.arraySize)
                {
                    var listProp = valuesProp.GetArrayElementAtIndex(i);
                    height += listProp.arraySize * (EditorGUIUtility.singleLineHeight + 2);
                }

                // 添加标签按钮高度
                height += EditorGUIUtility.singleLineHeight + 4;
            }
        }

        return height;
    }

    // 辅助方法：获取SerializedProperty的目标对象
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

    // 辅助方法：设置SerializedProperty的目标对象
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