// ս��Ʒ��Ŀ��
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
public class LootEntry
{
    [Tooltip("ս��ƷԤ����")]
    [JsonIgnore] // ����JSON���л����ֶ�
    [UnityEngine.Serialization.FormerlySerializedAs("LootPrefab")]
    public GameObject LootPrefab;

    [Tooltip("ս��ƷԤ��������")]
    [SerializeField]
    [ReadOnly]
    public string LootPrefabName = "";

    [Tooltip("������� (0-1)")]
    [Range(0f, 1f)]
    public float DropChance = 1f;

    [Tooltip("��С��������")]
    public int MinAmount = 1;

    [Tooltip("����������")]
    public int MaxAmount = 1;

    // �༭����֤������ȷ����ֵ��Ч��
    public void OnValidate()
    {
#if UNITY_EDITOR
        // ����Ԥ��������
        if (LootPrefab != null)
        {
            LootPrefabName = LootPrefab.name;
        }
        else
        {
            LootPrefabName = "";
        }
        
        // ȷ������������Χ��Ч
        MinAmount = Mathf.Max(0, MinAmount); // ȷ����С������С��0
        MaxAmount = Mathf.Max(0, MaxAmount); // ȷ�����������С��0
        
        // ȷ����С�������ᳬ������������������ֵ��������Сֵ��
        if (MinAmount > MaxAmount)
        {
            MaxAmount = MinAmount; // �����������Ϊ��С����
        }
#endif
    }

    // ���÷���������������õ���������
    public void ClearPrefabReference()
    {
        LootPrefab = null;
    }
}