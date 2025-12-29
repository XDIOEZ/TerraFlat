using System.Collections.Generic;
using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class ItemTag
{
    public List<string> Item_TypeTag = new List<string> { "None", "None" };
    public List<string> Item_Material = new List<string> { "None" };

    public override string ToString()
    {
        string str =
            $"物品类型：{Item_TypeTag[0]}({Item_TypeTag[1]})," +
            $"物品材质：{Item_Material[0]}";
        return str;
    }

    /// <summary>
    /// 判断是否包含某个类型标签（主类或子类）
    /// </summary>
    public bool HasTypeTag(string tag)
    {
        return Item_TypeTag.Contains(tag);
    }

    /// <summary>
    /// 判断是否包含某个材质
    /// </summary>
    public bool HasMaterial(string material)
    {
        return Item_Material.Contains(material);
    }
}