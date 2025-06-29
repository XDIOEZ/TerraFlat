using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    public abstract ModuleData Data { get; set; }
    public Item item { get; set; }
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();

    public void Awake()
    {
        item = GetComponentInParent<Item>();
        //��item���ģ��
        item.Mods.Add(Data.ModuleName, this);
    }
    public void Start()
    {
       

        //����ģ������
        if (item?.Item_Data?.ModuleDataDic != null && !string.IsNullOrEmpty(Data?.ModuleName))
        {
            if (item.Item_Data.ModuleDataDic.ContainsKey(Data.ModuleName))
            {
                Debug.LogWarning("ģ�������Ѵ��ڣ�������ԭ������");
                Data = item.Item_Data.ModuleDataDic[Data.ModuleName];
            }
            else
            {
                Debug.LogWarning("ģ�����ݲ����ڣ������������");
                item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
            }
        }
        else
        {
            Debug.LogWarning("�޷�����ģ�����ݣ�item��Item_Data��ModuleDataDic �� ModuleName Ϊ��");
        }
    }



    public void OnDestroy()
    {
        item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
    }

}