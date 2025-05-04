using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class HouseData : ItemData
{
    //---建筑链接的场景名字
    public string sceneName;
    //---建筑的当前耐久度//使用Hp类
    public Hp hp;
    //建筑的防御
    public Defense defense;
    //---是否处于建筑状态
    public bool isBuilding;
}
