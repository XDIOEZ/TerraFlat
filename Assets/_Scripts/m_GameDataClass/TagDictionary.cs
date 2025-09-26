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
    #region ���ݽṹ
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

    #region ����
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
    #endregion

    #region ���캯��
    public TagDictionary()
    {
        keys.Add(new TagData("Type"));
        keys.Add(new TagData("Material"));
    }
    #endregion

    #region ��ǩ�ṹ��֤��ά��

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
    #endregion

    #region ��ɾ�Ĳ鹦��

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
    /// �����Ʒ�Ƿ����ָ���ı�ǩ���ͺͱ�ǩ����
    /// </summary>
    /// <param name="tagType">��ǩ���ͣ���"Type"��"Material"�ȣ�</param>
    /// <param name="tagName">��ǩ���ƣ���"Animal"��"Wood"�ȣ�</param>
    /// <returns>�����Ʒ���иñ�ǩ�򷵻�true�����򷵻�false</returns>
    public bool HasType(string tagName)
    {
        return HasTag("Type", tagName);
    }


    #endregion

 
    #region �ַ�����ʾ
    /// <summary>
    /// ���ر�ǩ�ֵ���ַ�����ʾ��ʽ
    /// </summary>
    /// <returns>�������б�ǩ��ֵ�ĸ�ʽ���ַ���</returns>
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
        //ѪҺ
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
        //ľ��
        "Stick",
        //ʯͷ
        "Stone",
        //����
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