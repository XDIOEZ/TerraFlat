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


    // �����������ڸ���Ԥ�������ƣ����༭��ʹ�ã�
    public void UpdatePrefabName()
    {
#if UNITY_EDITOR
        if (LootPrefab != null)
        {
            LootPrefabName = LootPrefab.name;
        }
#endif
    }

    // ���÷���������������õ���������
    public void ClearPrefabReference()
    {
        LootPrefab = null;
    }
}