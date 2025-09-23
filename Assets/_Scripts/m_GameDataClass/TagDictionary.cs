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
            EnsureTagStructure(); // ֻȷ���ṹ���ڣ�����ֵ֤
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
            EnsureTagStructure(); // ֻȷ���ṹ���ڣ�����ֵ֤
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

    [Button("��Ⲣ�޸���ǩ������ֵ")]
    public void ValidateAndUpdateIndices()
    {
        // ����Type��ǩ
        HandleTag("Type", 0);

        // ����Material��ǩ
        HandleTag("Material", 1);

        // ����MakeTag��ǩ
        HandleTag("MakeTag", 2);
    }

    /// <summary>
    /// ��ȷ����ǩ�ṹ���ڣ�����ֵ֤
    /// </summary>
    public void EnsureTagStructure()
    {
        // ȷ���б�����������Ԫ��
        while (keys.Count < 2)
        {
            keys.Add(new TagData(string.Empty));
        }

        // ȷ��Type��ǩ����
        if (keys.FindIndex(data => data.tag == "Type") == -1)
        {
            keys[0] = new TagData("Type");
        }

        // ȷ��Material��ǩ����
        if (keys.FindIndex(data => data.tag == "Material") == -1)
        {
            keys[1] = new TagData("Material");
        }
    }

    /// <summary>
    /// ����ָ����ǩ��λ�ú�ֵ��֤
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="targetIndex">Ŀ������λ��</param>
    private void HandleTag(string tagName, int targetIndex)
    {
        // �������б�ǩ��λ��
        int currentIndex = keys.FindIndex(data => data.tag == tagName);

        // �����ǩ�����ڣ�������
        if (currentIndex == -1)
        {
            // ���Ŀ��λ�ò���Ŀ���ǩ���滻��
            if (keys[targetIndex].tag != tagName)
            {
                keys[targetIndex] = new TagData(tagName);
            }
            currentIndex = targetIndex;
        }
        // �����ǩ���ڵ�����Ŀ���������ƶ���
        else if (currentIndex != targetIndex)
        {
            var tagData = keys[currentIndex];
            keys.RemoveAt(currentIndex);

            // �������λ�ã������Ƴ�Ԫ�غ�������仯��
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

        // ��֤��ǩֵ�Ƿ���Ч
        ValidateTagValues(keys[targetIndex]);
    }
    
    /// <summary>
    /// ��֤��ǩֵ�Ƿ��������֤�б���
    /// </summary>
    /// <param name="tagData">Ҫ��֤�ı�ǩ����</param>
    private void ValidateTagValues(TagData tagData)
    {
        if (tagData == null || string.IsNullOrEmpty(tagData.tag))
            return;

        List<string> validValues = GetValidValuesForTag(tagData.tag);
        if (validValues == null)
            return;

        bool hasInvalidValues = false;

        // �����Чֵ
        foreach (string value in tagData.values)
        {
            if (!validValues.Contains(value))
            {
                hasInvalidValues = true;
                Debug.LogWarning($"������Ч��{tagData.tag}ֵ: {value} - �����Ƿ�ƴд����");
            }
        }

        // �����ɫ����֤ͨ��������ʹ��Unity����̨��ɫ��ǣ�
        if (!hasInvalidValues)
        {
            Debug.Log("<color=green>���б�ǩֵ��֤ͨ��</color>");
        }
    }

    /// <summary>
    /// ��ȡָ����ǩ����Чֵ�б�
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <returns>��Чֵ�б����޶�Ӧ�б��򷵻�null</returns>
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
                Debug.LogWarning($"δ�ҵ�{tagName}����֤�б�");
                return null;
        }
    }

    #region ��ɾ�Ĳ鹦��

    /// <summary>
    /// ��ӱ�ǩ������
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="index">����λ�ã�-1��ʾ��ӵ�ĩβ</param>
    /// <returns>��ӳɹ�����true�����򷵻�false</returns>
    [Button("��ӱ�ǩ")]
    public bool AddTag(string tagName, int index = -1)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            Debug.LogError("��ǩ���Ʋ���Ϊ��");
            return false;
        }

        // ����ǩ�Ƿ��Ѵ���
        if (keys.Any(data => data.tag == tagName))
        {
            Debug.LogWarning($"��ǩ {tagName} �Ѵ���");
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

        Debug.Log($"�ɹ���ӱ�ǩ {tagName}");
        return true;
    }

    /// <summary>
    /// ɾ����ǩ��ɾ��
    /// </summary>
    /// <param name="tagName">Ҫɾ���ı�ǩ����</param>
    /// <returns>ɾ���ɹ�����true�����򷵻�false</returns>
    [Button("ɾ����ǩ")]
    public bool RemoveTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            Debug.LogError("��ǩ���Ʋ���Ϊ��");
            return false;
        }

        // ����ɾ�����ı�ǩ
        if (tagName == "Type" || tagName == "Material")
        {
            Debug.LogWarning($"����ɾ�����ı�ǩ {tagName}");
            return false;
        }

        int index = keys.FindIndex(data => data.tag == tagName);
        if (index == -1)
        {
            Debug.LogWarning($"δ�ҵ���ǩ {tagName}");
            return false;
        }

        keys.RemoveAt(index);
        Debug.Log($"�ɹ�ɾ����ǩ {tagName}");
        return true;
    }

    /// <summary>
    /// ��������ǩ���ģ�
    /// </summary>
    /// <param name="oldTagName">�ɱ�ǩ����</param>
    /// <param name="newTagName">�±�ǩ����</param>
    /// <returns>�������ɹ�����true�����򷵻�false</returns>
    [Button("��������ǩ")]
    public bool RenameTag(string oldTagName, string newTagName)
    {
        if (string.IsNullOrEmpty(oldTagName) || string.IsNullOrEmpty(newTagName))
        {
            Debug.LogError("��ǩ���Ʋ���Ϊ��");
            return false;
        }

        if (oldTagName == newTagName)
        {
            Debug.LogWarning("�¾ɱ�ǩ������ͬ");
            return false;
        }

        // ����������Ƿ��Ѵ���
        if (keys.Any(data => data.tag == newTagName))
        {
            Debug.LogWarning($"��ǩ {newTagName} �Ѵ���");
            return false;
        }

        int index = keys.FindIndex(data => data.tag == oldTagName);
        if (index == -1)
        {
            Debug.LogWarning($"δ�ҵ���ǩ {oldTagName}");
            return false;
        }

        keys[index].tag = newTagName;
        Debug.Log($"�ɹ�����ǩ {oldTagName} ������Ϊ {newTagName}");
        return true;
    }

    /// <summary>
    /// ��ȡ���б�ǩ���ƣ��飩
    /// </summary>
    /// <returns>��ǩ�����б�</returns>
    public List<string> GetAllTagNames()
    {
        return keys.Where(data => !string.IsNullOrEmpty(data.tag)).Select(data => data.tag).ToList();
    }

    /// <summary>
    /// ��ȡָ����ǩ������ֵ���飩
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <returns>��ǩֵ�б������ǩ�������򷵻�null</returns>
    public List<string> GetTagValues(string tagName)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData?.values;
    }

    /// <summary>
    /// ����ǩ�Ƿ���ڣ��飩
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <returns>���ڷ���true�����򷵻�false</returns>
    public bool HasTag(string tagName)
    {
        return keys.Any(data => data.tag == tagName);
    }
/// <summary>
/// ���ָ����ǩ�������Ƿ����ָ���ı�ǩ����
/// </summary>
/// <param name="tagType">��ǩ���ͣ���"Type"��"Material"�ȣ�</param>
/// <param name="tagName">��ǩ���ƣ���"Animal"��"Wood"�ȣ�</param>
/// <returns>�����ǩ�����а����ñ�ǩ�����򷵻�true�����򷵻�false</returns>
public bool HasTag(string tagType, string tagName)
{
    // ������֤
    if (string.IsNullOrEmpty(tagType) || string.IsNullOrEmpty(tagName))
        return false;

    // �Ȳ���Tag������
    TagData tagData = keys.FirstOrDefault(data => data.tag == tagType);
    
    // ����Ҳ�����Ӧ�ı�ǩ���ͣ�����false
    if (tagData == null)
        return false;

    // �������в��Ҷ�Ӧ��Tag����
    return tagData.values.Contains(tagName);
}

/// <summary>
/// �����Ʒ�Ƿ����ָ���ı�ǩ���ͺͱ�ǩ����
/// </summary>
/// <param name="tagType">��ǩ���ͣ���"Type"��"Material"�ȣ�</param>
/// <param name="tagName">��ǩ���ƣ���"Animal"��"Wood"�ȣ�</param>
/// <returns>�����Ʒ���иñ�ǩ�򷵻�true�����򷵻�false</returns>
public bool HasTypeTag(string tagType, string tagName)
{
    return HasTag(tagType, tagName);
}

    /// <summary>
    /// ��ȡ��ǩ���ݶ��󣨲飩
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <returns>��ǩ���ݶ�������������򷵻�null</returns>
    public TagData GetTagData(string tagName)
    {
        return keys.FirstOrDefault(data => data.tag == tagName);
    }

    /// <summary>
    /// ��ȫ��ӱ�ǩֵ������
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="value">Ҫ��ӵ�ֵ</param>
    /// <returns>��ӳɹ�����true�����򷵻�false</returns>
    [Button("��ӱ�ǩֵ")]
    public bool AddTagValue(string tagName, string value)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(value))
        {
            Debug.LogError("��ǩ���ƺ�ֵ����Ϊ��");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"δ�ҵ���ǩ {tagName}");
            return false;
        }

        if (tagData.values.Contains(value))
        {
            Debug.LogWarning($"��ǩ {tagName} ���Ѵ���ֵ {value}");
            return false;
        }

        tagData.values.Add(value);
        Debug.Log($"�ɹ��ڱ�ǩ {tagName} �����ֵ {value}");
        return true;
    }

    /// <summary>
    /// ɾ����ǩֵ��ɾ��
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="value">Ҫɾ����ֵ</param>
    /// <returns>ɾ���ɹ�����true�����򷵻�false</returns>
    [Button("ɾ����ǩֵ")]
    public bool RemoveTagValue(string tagName, string value)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(value))
        {
            Debug.LogError("��ǩ���ƺ�ֵ����Ϊ��");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"δ�ҵ���ǩ {tagName}");
            return false;
        }

        if (!tagData.values.Remove(value))
        {
            Debug.LogWarning($"��ǩ {tagName} ��δ�ҵ�ֵ {value}");
            return false;
        }

        Debug.Log($"�ɹ��ӱ�ǩ {tagName} ��ɾ��ֵ {value}");
        return true;
    }

    /// <summary>
    /// ��������ǩֵ���ģ�
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="oldValue">��ֵ</param>
    /// <param name="newValue">��ֵ</param>
    /// <returns>�������ɹ�����true�����򷵻�false</returns>
    [Button("��������ǩֵ")]
    public bool RenameTagValue(string tagName, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(oldValue) || string.IsNullOrEmpty(newValue))
        {
            Debug.LogError("��ǩ���ƺ�ֵ����Ϊ��");
            return false;
        }

        if (oldValue == newValue)
        {
            Debug.LogWarning("�¾�ֵ��ͬ");
            return false;
        }

        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        if (tagData == null)
        {
            Debug.LogWarning($"δ�ҵ���ǩ {tagName}");
            return false;
        }

        int index = tagData.values.IndexOf(oldValue);
        if (index == -1)
        {
            Debug.LogWarning($"��ǩ {tagName} ��δ�ҵ�ֵ {oldValue}");
            return false;
        }

        // �����ֵ�Ƿ��Ѵ���
        if (tagData.values.Contains(newValue))
        {
            Debug.LogWarning($"��ǩ {tagName} ���Ѵ���ֵ {newValue}");
            return false;
        }

        tagData.values[index] = newValue;
        Debug.Log($"�ɹ�����ǩ {tagName} �е�ֵ {oldValue} ������Ϊ {newValue}");
        return true;
    }

    /// <summary>
    /// ����ǩֵ�Ƿ���ڣ��飩
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="value">Ҫ����ֵ</param>
    /// <returns>���ڷ���true�����򷵻�false</returns>
    public bool HasTagValue(string tagName, string value)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData != null && tagData.values.Contains(value);
    }

    /// <summary>
    /// ��ȡ��ǩֵ������
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <returns>��ǩֵ�������������ǩ�������򷵻�-1</returns>
    public int GetTagValueCount(string tagName)
    {
        TagData tagData = keys.FirstOrDefault(data => data.tag == tagName);
        return tagData?.values.Count ?? -1;
    }

    #endregion

    /// <summary>
    /// ��ȫ��ӱ�ǩֵ����������֤��
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="value">Ҫ��ӵ�ֵ</param>
    /// <returns>��ӳɹ�����true�����򷵻�false</returns>
    public bool TryAddValue(string tagName, string value)
    {
        EnsureTagStructure(); // ֻȷ���ṹ������ֵ֤

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

        return false; // �Ѵ��ڸ�ֵ
    }

    /// <summary>
    /// ��ȫ���±�ǩֵ���ᴥ����֤��
    /// </summary>
    /// <param name="tagName">��ǩ����</param>
    /// <param name="oldValue">��ֵ</param>
    /// <param name="newValue">��ֵ</param>
    /// <returns>���³ɹ�����true�����򷵻�false</returns>
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

        // �ȸ���ֵ
        tagData.values[index] = newValue;

        // ֻ���޸�ʱ������֤
        ValidateAndUpdateIndices();
        return true;
    }
}

public static class TagVerify
{
    #region ����Tag

    public static List<string> Type = new List<string>
    {
        //����
        "Animal", 
        //ֲ��
        "Plant", 
        //����
        "Building",
        //װ��
        "Equipment",
        //����
        "Material",
        //����
        "Tool",
        //����
        "Weapon",

    };
    #endregion


    #region ����Tag
    public static List<string> Material = new List<string>
    {
        //ľ��
        "Wood",
        //ˮ��
        "Cement",
        //����
        "Glass",
        //����
        "Metal",
        //�մ�
        "Ceramic",
        //Ѫ��
        "Blood",
        //ʯͷ
        "Stone",
        //ˮ
        "Water",
        //����
        "Air",
        //��
        "Meat",
    };
    #endregion

    #region ����Tag

    public static List<string> Creature = new List<string>
    {
        //ʳ�⶯��
        "MeatAnimal",
        //ʳ�ݶ���
        "VegetableAnimal",
         //ֲ��
        "Tree",
    };


    #endregion
    #region ����Tag

    public static List<string> MakeTag = new List<string>
    {
        //����
        "Stick",
        //ʯͷ
        "Stone",
        //����,
        "Rope",
    };
    #endregion
    #region ����Tag

    public static List<string> FunctionTag = new List<string>
    {
        //���
        "Ignition",
    };
    #endregion

}
