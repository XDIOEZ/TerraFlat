using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
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
            default:
                Debug.LogWarning($"未找到{tagName}的验证列表");
                return null;
        }
    }

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
}
