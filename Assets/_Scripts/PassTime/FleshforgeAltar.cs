using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FleshForgeAltar : Organ,IFun_FleshForgeAltar
{
    // ��Ʒ�б�
    public List<Item> Sacrifice;
    // ��¯���������
    public int MaxCapacity = 2;
    // ����Ʒ�б�����ʱ���µļ�Ʒ������ӵ�����б���
    public List<Item> trashBin = new List<Item>();

    // ��ʼ����¯
    public void Start()
    {
        // ��ʼ����Ʒ�б�������¯���������
        Sacrifice = new List<Item>(MaxCapacity);
    }

    // ��Ӽ�Ʒ����¯
public void PushSacrifice(Item item)
{
    // ��� Item �Ƿ�Ϊ��
    if (item == null)
    {
        Debug.LogWarning("�޷���ӿյļ�Ʒ����¯��");
        return; // ��� item Ϊ�գ��򲻽����κδ���
    }

    Debug.Log("��Ӽ�Ʒ����¯");
    // todo �ж���¯�Ƿ�����
    if (Sacrifice.Count >= MaxCapacity)
    {
        // �� Sacrifice �����һ��Ԫ���Ƶ� trashBin ��
        trashBin.Add(Sacrifice[Sacrifice.Count - 1]);
        Sacrifice.RemoveAt(Sacrifice.Count - 1);
        // ���µļ�Ʒ��ӵ� Sacrifice ��
        Sacrifice.Add(item);
    }
    else
    {
        Sacrifice.Add(item);
    }
}


    public bool Composite()
    {
        // �����¯�Ƿ�����Ʒ���Ժϳ�
        if (Sacrifice.Count > 0)
        {
            // ����¯�е�������Ʒ�ƶ�������Ͱ��
            foreach (Item item in Sacrifice)
            {
                trashBin.Add(item);
            }
            // �����¯
            Sacrifice.Clear();

            Debug.Log("��¯�е���Ʒ�Ѻϳɲ���������Ͱ��");
            return true;
        }
        else
        {
            Debug.LogWarning("��¯Ϊ�գ��޷��ϳɡ�");
            return false;
        }
    }
}
public interface IFun_FleshForgeAltar
{
    void PushSacrifice(Item item); // ��Ʒ����

    public bool Composite(); // �ϳ�

}
