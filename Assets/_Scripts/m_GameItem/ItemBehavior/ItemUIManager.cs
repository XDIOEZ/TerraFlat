using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using static UnityEditor.Experimental.GraphView.GraphView;
#endif

public class ItemUIManager : MonoBehaviour
{
    public Item CanvasBelong_Item;
    //�ֶ��ϳ����
    public Canvas Craft;
    //�������
    public Canvas Bag;
    //װ�����
    public Canvas Equip;
    //��������
    public Canvas HotBor;
    //�ֲ�������
    public Canvas HandSlotCanvas;
    //�������
    public Canvas Setting;
    //�Ҽ��˵�
    public Canvas RightClickMenu;
}
