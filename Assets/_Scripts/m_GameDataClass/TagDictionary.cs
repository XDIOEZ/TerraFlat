using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class TagDictionary
{
    [SerializeField] public List<TagData> keys = new List<TagData> { };

    [System.Serializable]
    [MemoryPackable]
    public partial class TagData
    {
        public string tag;
        public List<string> values;

        public TagData(string tag)
        {
            this.tag = tag;
            this.values = new List<string>();
        }
    }

    [MemoryPackIgnore]
    public TagData TypeTag
    {
        get
        {
            EnsureTagStructure(); // 只确保结构存在，不验证值
            if (keys.Count > 0)
                return keys[0];
            return null;
        }
    }

    [MemoryPackIgnore]
    public TagData MaterialTag
    {
        get
        {
            EnsureTagStructure(); // 只确保结构存在，不验证值
            if (keys.Count > 1)
                return keys[1];
            return null;
        }
    }

    public TagDictionary()
    {
        keys.Add(new TagData("Type"));
        keys.Add(new TagData("Material"));
        keys.Add(new TagData("MakeTag"));
    }

    [Button("检测并修复标签索引与值")]
    public void ValidateAndUpdateIndices()
    {
        // 处理Type标签
        HandleTag("Type", 0);

        // 处理Material标签
        HandleTag("Material", 1);

        // 处理MakeTag标签
        HandleTag("MakeTag", 2);
    }

    /// <summary>
    /// 仅确保标签结构存在，不验证值
    /// </summary>
    public void EnsureTagStructure()
    {
        // 确保列表至少有两个元素
        while (keys.Count < 2)
        {
            keys.Add(new TagData(string.Empty));
        }

        // 确保Type标签存在
        if (keys.FindIndex(data => data.tag == "Type") == -1)
        {
            keys[0] = new TagData("Type");
        }

        // 确保Material标签存在
        if (keys.FindIndex(data => data.tag == "Material") == -1)
        {
            keys[1] = new TagData("Material");
        }
    }

    /// <summary>
    /// 处理指定标签的位置和值验证
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="targetIndex">目标索引位置</param>
    private void HandleTag(string tagName, int targetIndex)
    {
        // 查找现有标签的位置
        int currentIndex = keys.FindIndex(data => data.tag == tagName);

        // 如果标签不存在，创建它
        if (currentIndex == -1)
        {
            // 如果目标位置不是目标标签，替换它
            if (keys[targetIndex].tag != tagName)
            {
                keys[targetIndex] = new TagData(tagName);
            }
            currentIndex = targetIndex;
        }
        // 如果标签存在但不在目标索引，移动它
        else if (currentIndex != targetIndex)
        {
            var tagData = keys[currentIndex];
            keys.RemoveAt(currentIndex);

            // 计算插入位置（考虑移除元素后的索引变化）
            int insertIndex = currentIndex < targetIndex ? targetIndex - 1 : targetIndex;
            if (insertIndex >= keys.Count)
            {
                keys.Add(tagData);
            }
            else
            {
                keys.Insert(insertIndex, tagData);
            }
        }

        // 验证标签值是否有效
        ValidateTagValues(keys[targetIndex]);
    }
    
    /// <summary>
    /// 验证标签值是否存在于验证列表中
    /// </summary>
    /// <param name="tagData">要验证的标签数据</param>
    private void ValidateTagValues(TagData tagData)
    {
        if (tagData == null || string.IsNullOrEmpty(tagData.tag))
            return;

        List<string> validValues = GetValidValuesForTag(tagData.tag);
        if (validValues == null)
            return;

        bool hasInvalidValues = false;

        // 检查无效值
        foreach (string value in tagData.values)
        {
            if (!validValues.Contains(value))
            {
                hasInvalidValues = true;
                Debug.LogWarning($"发现无效的{tagData.tag}值: {value} - 请检查是否拼写错误");
            }
        }

        // 输出绿色的验证通过反馈（使用Unity控制台颜色标记）
        if (!hasInvalidValues)
        {
            Debug.Log("<color=green>所有标签值验证通过</color>");
        }
    }

    /// <summary>
    /// 获取指定标签的有效值列表
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <returns>有效值列表，如无对应列表则返回null</returns>
    private List<string> GetValidValuesForTag(string tagName)
    {
        switch (tagName)
        {
            case "Type":
                return TagVerify.Type;
            case "Material":
                return TagVerify.Material;
            case "Creature":
                return TagVerify.Creature;
            case "MakeTag":
                return TagVerify.MakeTag;
            default:
                Debug.LogWarning($"未找到{tagName}的验证列表");
                return null;
        }
    }

    #region 增删改查功能

    /// <summary>
    /// 添加标签（增）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="index">插入位置，-1表示添加到末尾</param>
    /// <returns>添加成功返回true，否则返回false</returns>
    [Button("添加标签")]
    public bool AddTag(string tagName, int index = -1)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            Debug.LogError("标签名称不能为空");
            return false;
        }

        // 检查标签是否已存在
        if (keys.Any(data => data.tag == tagName))
        {
            Debug.LogWarning($"标签 {tagName} 已存在");
            return false;
        }

        TagData newTag = new TagData(tagName);
        
        if (index == -1 || index >= keys.Count)
        {
            keys.Add(newTag);
        }
        else
        {
            keys.Insert(index, newTag);
        }

        Debug.Log($"成功添加标签 {tagName}");
        return true;
    }

    /// <summary>
    /// 删除标签（删）
    /// </summary>
    /// <param name="tagName">要删除的标签名称</param>
    /// <returns>删除成功返回true，否则返回false</returns>
    [Button("删除标签")]
    public bool RemoveTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            Debug.LogError("标签名称不能为空");
            return false;
        }

        // 不能删除核心标签
        if (tagName == "Type" || tagName == "Material")
        {
            Debug.LogWarning($"不能删除核心标签 {tagName}");
            return false;
        }

        int index = keys.FindIndex(data => data.tag == tagName);
        if (index == -1)
        {
            Debug.LogWarning($"未找到标签 {tagName}");
            return false;
        }

        keys.RemoveAt(index);
        Debug.Log($"成功删除标签 {tagName}");
        return true;
    }

    /// <summary>
    /// 重命名标签（改）
    /// </summary>
    /// <param name="oldTagName">旧标签名称</param>
    /// <param name="newTagName">新标签名称</param>
    /// <returns>重命名成功返回true，否则返回false</returns>
    [Button("重命名标签")]
    public bool RenameTag(string oldTagName, string newTagName)
    {
        if (string.IsNullOrEmpty(oldTagName) || string.IsNullOrEmpty(newTagName))
        {
            Debug.LogError("标签名称不能为空");
            return false;
        }

        if (oldTagName == newTagName)
        {
            Debug.LogWarning("新旧标签名称相同");
            return false;
        }

        // 检查新名称是否已存在
        if (keys.Any(data => data.tag == newTagName))
        {
            Debug.LogWarning($"标签 {newTagName} 已存在");
            return false;
        }

        int index = keys.FindIndex(data => data.tag == oldTagName);
        if (index == -1)
        {
            Debug.LogWarning($"未找到标签 {oldTagName}");
            return false;
        }

        keys[index].tag = newTagName;
        Debug.Log($"成功将标签 {oldTagName} 重命名为 {newTagName}");
        return true;
    }

    /// <summary>
    /// 获取所有标签名称（查）
    /// </summary>
    /// <returns>标签名称列表</returns>
    public List<string> GetAllTagNames()
    {
        return keys.Where(data => !string.IsNullOrEmpty(data.tag)).Select(data => data.tag).ToList();
    }

    /// <summary>
    /// 获取指定标签的所有值（查）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <returns>标签值列表，如果标签不存在则返回null</returns>
    public List<string> GetTagValues(string tagName)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData?.values;
    }

    /// <summary>
    /// 检查标签是否存在（查）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <returns>存在返回true，否则返回false</returns>
    public bool HasTag(string tagName)
    {
        return keys.Any(data => data.tag == tagName);
    }
/// <summary>
/// 检查指定标签类型中是否包含指定的标签名称
/// </summary>
/// <param name="tagType">标签类型（如"Type"、"Material"等）</param>
/// <param name="tagName">标签名称（如"Animal"、"Wood"等）</param>
/// <returns>如果标签类型中包含该标签名称则返回true，否则返回false</returns>
public bool HasTag(string tagType, string tagName)
{
    // 参数验证
    if (string.IsNullOrEmpty(tagType) || string.IsNullOrEmpty(tagName))
        return false;

    // 先查找Tag的类型
    TagData tagData = keys.FirstOrDefault(data => data.tag == tagType);
    
    // 如果找不到对应的标签类型，返回false
    if (tagData == null)
        return false;

    // 从类型中查找对应的Tag名字
    return tagData.values.Contains(tagName);
}

/// <summary>
/// 检查物品是否具有指定的标签类型和标签名称
/// </summary>
/// <param name="tagType">标签类型（如"Type"、"Material"等）</param>
/// <param name="tagName">标签名称（如"Animal"、"Wood"等）</param>
/// <returns>如果物品具有该标签则返回true，否则返回false</returns>
public bool HasTypeTag(string tagType, string tagName)
{
    return HasTag(tagType, tagName);
}

    /// <summary>
    /// 获取标签数据对象（查）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <returns>标签数据对象，如果不存在则返回null</returns>
    public TagData GetTagData(string tagName)
    {
        return keys.FirstOrDefault(data => data.tag == tagName);
    }

    /// <summary>
    /// 安全添加标签值（增）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="value">要添加的值</param>
    /// <returns>添加成功返回true，否则返回false</returns>
    [Button("添加标签值")]
    public bool AddTagValue(string tagName, string value)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(value))
        {
            Debug.LogError("标签名称和值不能为空");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"未找到标签 {tagName}");
            return false;
        }

        if (tagData.values.Contains(value))
        {
            Debug.LogWarning($"标签 {tagName} 中已存在值 {value}");
            return false;
        }

        tagData.values.Add(value);
        Debug.Log($"成功在标签 {tagName} 中添加值 {value}");
        return true;
    }

    /// <summary>
    /// 删除标签值（删）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="value">要删除的值</param>
    /// <returns>删除成功返回true，否则返回false</returns>
    [Button("删除标签值")]
    public bool RemoveTagValue(string tagName, string value)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(value))
        {
            Debug.LogError("标签名称和值不能为空");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"未找到标签 {tagName}");
            return false;
        }

        if (!tagData.values.Remove(value))
        {
            Debug.LogWarning($"标签 {tagName} 中未找到值 {value}");
            return false;
        }

        Debug.Log($"成功从标签 {tagName} 中删除值 {value}");
        return true;
    }

    /// <summary>
    /// 重命名标签值（改）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="oldValue">旧值</param>
    /// <param name="newValue">新值</param>
    /// <returns>重命名成功返回true，否则返回false</returns>
    [Button("重命名标签值")]
    public bool RenameTagValue(string tagName, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(oldValue) || string.IsNullOrEmpty(newValue))
        {
            Debug.LogError("标签名称和值不能为空");
            return false;
        }

        if (oldValue == newValue)
        {
            Debug.LogWarning("新旧值相同");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"未找到标签 {tagName}");
            return false;
        }

        int index = tagData.values.IndexOf(oldValue);
        if (index == -1)
        {
            Debug.LogWarning($"标签 {tagName} 中未找到值 {oldValue}");
            return false;
        }

        // 检查新值是否已存在
        if (tagData.values.Contains(newValue))
        {
            Debug.LogWarning($"标签 {tagName} 中已存在值 {newValue}");
            return false;
        }

        tagData.values[index] = newValue;
        Debug.Log($"成功将标签 {tagName} 中的值 {oldValue} 重命名为 {newValue}");
        return true;
    }

    /// <summary>
    /// 检查标签值是否存在（查）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="value">要检查的值</param>
    /// <returns>存在返回true，否则返回false</returns>
    public bool HasTagValue(string tagName, string value)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData != null && tagData.values.Contains(value);
    }

    /// <summary>
    /// 获取标签值的数量
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <returns>标签值的数量，如果标签不存在则返回-1</returns>
    public int GetTagValueCount(string tagName)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData?.values.Count ?? -1;
    }

    #endregion

    /// <summary>
    /// 安全添加标签值（不触发验证）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="value">要添加的值</param>
    /// <returns>添加成功返回true，否则返回false</returns>
    public bool TryAddValue(string tagName, string value)
    {
        EnsureTagStructure(); // 只确保结构，不验证值

        TagData tagData = null;
        if (tagName == "Type")
            tagData = TypeTag;
        else if (tagName == "Material")
            tagData = MaterialTag;

        if (tagData == null)
            return false;

        if (!tagData.values.Contains(value))
        {
            tagData.values.Add(value);
            return true;
        }

        return false; // 已存在该值
    }

    /// <summary>
    /// 安全更新标签值（会触发验证）
    /// </summary>
    /// <param name="tagName">标签名称</param>
    /// <param name="oldValue">旧值</param>
    /// <param name="newValue">新值</param>
    /// <returns>更新成功返回true，否则返回false</returns>
    public bool TryUpdateValue(string tagName, string oldValue, string newValue)
    {
        EnsureTagStructure();

        TagData tagData = null;
        if (tagName == "Type")
            tagData = TypeTag;
        else if (tagName == "Material")
            tagData = MaterialTag;

        if (tagData == null)
            return false;

        int index = tagData.values.IndexOf(oldValue);
        if (index == -1)
            return false;

        // 先更新值
        tagData.values[index] = newValue;

        // 只在修改时触发验证
        ValidateAndUpdateIndices();
        return true;
    }
}

public static class TagVerify
{
    #region 类型Tag

    public static List<string> Type = new List<string>
    {
        //动物
        "Animal", 
        //植物
        "Plant", 
        //建筑
        "Building",
        //装备
        "Equipment",
        //材料
        "Material",
        //工具
        "Tool",
        //武器
        "Weapon",

    };
    #endregion


    #region 材质Tag
    public static List<string> Material = new List<string>
    {
        //木材
        "Wood",
        //水泥
        "Cement",
        //玻璃
        "Glass",
        //金属
        "Metal",
        //陶瓷
        "Ceramic",
        //血肉
        "Blood",
        //石头
        "Stone",
        //水
        "Water",
        //空气
        "Air",
        //肉
        "Meat",
    };
    #endregion

    #region 生物Tag

    public static List<string> Creature = new List<string>
    {
        //食肉动物
        "MeatAnimal",
        //食草动物
        "VegetableAnimal",
         //植物
        "Tree",
    };


    #endregion
    #region 制作Tag

    public static List<string> MakeTag = new List<string>
    {
        //棍子
        "Stick",
        //石头
        "Stone",
        //绳子,
        "Rope",
    };
    #endregion
    #region 功能Tag

    public static List<string> FunctionTag = new List<string>
    {
        //点火
        "Ignition",
    };
    #endregion

}
