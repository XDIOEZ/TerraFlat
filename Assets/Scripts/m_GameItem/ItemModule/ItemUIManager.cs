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
    //手动合成面板
    public Canvas Craft;
    //背包面板
    public Canvas Bag;
    //装备面板
    public Canvas Equip;
    //快捷栏面板
    public Canvas HotBor;
    //手部插槽面板
    public Canvas HandSlotCanvas;
    //设置面板
    public Canvas Setting;
    //右键菜单
    public Canvas RightClickMenu;
}
