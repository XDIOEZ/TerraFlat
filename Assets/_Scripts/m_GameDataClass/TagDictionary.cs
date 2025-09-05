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
            default:
                Debug.LogWarning($"δ�ҵ�{tagName}����֤�б�");
                return null;
        }
    }

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
}
