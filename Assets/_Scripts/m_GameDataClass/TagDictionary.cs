using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class TagDictionary
{
    #region 数据结构
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
    #endregion

    #region 属性
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
    #endregion

    #region 构造函数
    public TagDictionary()
    {
        keys.Add(new TagData("Type"));
        keys.Add(new TagData("Material"));
    }
    #endregion

    #region 标签结构验证与维护

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
    #endregion

    #region 增删改查功能

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
    /// 检查物品是否具有指定的标签类型和标签名称
    /// </summary>
    /// <param name="tagType">标签类型（如"Type"、"Material"等）</param>
    /// <param name="tagName">标签名称（如"Animal"、"Wood"等）</param>
    /// <returns>如果物品具有该标签则返回true，否则返回false</returns>
    public bool HasType(string tagName)
    {
        return HasTag("Type", tagName);
    }


    #endregion

 
    #region 字符串表示
    /// <summary>
    /// 返回标签字典的字符串表示形式
    /// </summary>
    /// <returns>包含所有标签和值的格式化字符串</returns>
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("TagDictionary:");
        
        foreach (var tagData in keys)
        {
            if (string.IsNullOrEmpty(tagData.tag))
                continue;
                
            sb.AppendLine($"  {tagData.tag}:");
            foreach (var value in tagData.values)
            {
                sb.AppendLine($"    - {value}");
            }
        }
        
        return sb.ToString();
    }
    #endregion
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

    #region 材料Tag
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
        //血液
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
        //木棍
        "Stick",
        //石头
        "Stone",
        //绳子
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